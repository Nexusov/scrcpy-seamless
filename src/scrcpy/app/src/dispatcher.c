#include "dispatcher.h"

#include <assert.h>
#include <string.h>

/** Publish an application wakeup without holding an admission lock. */
static bool
sc_dispatcher_sdl_wakeup(void *userdata, SDL_Event *event) {
    (void) userdata;
    return SDL_PushEvent(event);
}

/** Initialize the bounded slots and their independent completion locks. */
bool
sc_dispatcher_init(struct sc_dispatcher *dispatcher, uint32_t wake_event_type,
                   sc_dispatcher_wakeup_fn wakeup, void *wakeup_userdata) {
    assert(sc_thread_is_main());
    memset(dispatcher, 0, sizeof(*dispatcher));

    if (!sc_mutex_init(&dispatcher->mutex)) {
        return false;
    }

    size_t initialized = 0;
    for (; initialized < SC_DISPATCHER_MAX_ITEMS; ++initialized) {
        struct sc_dispatcher_completion *completion =
            &dispatcher->items[initialized].completion;

        if (!sc_mutex_init(&completion->mutex)) {
            break;
        }

        if (!sc_cond_init(&completion->condition)) {
            sc_mutex_destroy(&completion->mutex);
            break;
        }

        completion->owner = dispatcher;
        completion->index = initialized;
    }

    if (initialized != SC_DISPATCHER_MAX_ITEMS) {
        while (initialized) {
            struct sc_dispatcher_completion *completion =
                &dispatcher->items[--initialized].completion;
            sc_cond_destroy(&completion->condition);
            sc_mutex_destroy(&completion->mutex);
        }
        sc_mutex_destroy(&dispatcher->mutex);
        return false;
    }

    dispatcher->wake_event_type = wake_event_type;
    dispatcher->wakeup = wakeup ? wakeup : sc_dispatcher_sdl_wakeup;
    dispatcher->wakeup_userdata = wakeup_userdata;
    return true;
}

/** Guard target changes with the actual non-reentrant main-thread lease. */
static bool
sc_dispatcher_can_transition(struct sc_dispatcher *dispatcher) {
    return sc_thread_is_main() && !dispatcher->draining;
}

/** Allocate a fresh value before any generation producer starts. */
bool
sc_dispatcher_generation_begin(struct sc_dispatcher *dispatcher, void *binding,
                               sc_dispatcher_generation *generation) {

    if (!sc_dispatcher_can_transition(dispatcher)) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool unavailable = dispatcher->closed || dispatcher->generation ||
                       dispatcher->last_generation == UINT64_MAX;

    if (unavailable) {
        sc_mutex_unlock(&dispatcher->mutex);
        return false;
    }

    dispatcher->generation = ++dispatcher->last_generation;
    dispatcher->binding = binding;
    *generation = dispatcher->generation;
    sc_mutex_unlock(&dispatcher->mutex);
    return true;
}

/** Bind a live target while no callback can borrow its predecessor. */
bool
sc_dispatcher_generation_bind(struct sc_dispatcher *dispatcher,
                              sc_dispatcher_generation generation,
                              void *binding) {

    if (!sc_dispatcher_can_transition(dispatcher)) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool current = !dispatcher->closed && generation &&
                   generation == dispatcher->generation;

    if (current) {
        dispatcher->binding = binding;
    }

    sc_mutex_unlock(&dispatcher->mutex);
    return current;
}

/** Settle execution separately from a possibly already cancelled caller. */
static void
sc_dispatcher_settle(struct sc_dispatcher_completion *completion,
                     enum sc_dispatcher_result result) {
    sc_mutex_lock(&completion->mutex);
    assert(completion->execution_result == SC_DISPATCHER_PENDING);
    completion->execution_result = result;

    if (completion->caller_result == SC_DISPATCHER_PENDING) {
        completion->caller_result = result;
    }

    sc_cond_broadcast(&completion->condition);
    sc_mutex_unlock(&completion->mutex);
}

/** Release payload outside locks, retaining its budget through destruction. */
static void
sc_dispatcher_retire(struct sc_dispatcher *dispatcher, size_t index,
                     enum sc_dispatcher_result result) {
    struct sc_dispatcher_item *item = &dispatcher->items[index];
    sc_dispatcher_settle(&item->completion, result);
    item->task.destroy(item->task.payload);

    sc_mutex_lock(&dispatcher->mutex);
    dispatcher->stats.retained_payload_bytes -= item->task.payload_bytes;
    item->task = (struct sc_dispatcher_task) {0};

    if (item->completion.caller_owned) {
        item->state = SC_DISPATCHER_ITEM_RETIRED;
    } else {
        item->state = SC_DISPATCHER_ITEM_FREE;
        --dispatcher->stats.retained_count;
    }

    sc_mutex_unlock(&dispatcher->mutex);
}

/** Detach queued work under the gate, then settle/destroy it outside the gate. */
static size_t
sc_dispatcher_detach_pending(struct sc_dispatcher *dispatcher,
                             size_t detached[SC_DISPATCHER_MAX_ITEMS]) {
    sc_mutex_assert(&dispatcher->mutex);
    size_t count = dispatcher->stats.queued_count;
    for (size_t item_index = 0; item_index < count; ++item_index) {
        size_t index = dispatcher->queue[dispatcher->head];
        dispatcher->head = (dispatcher->head + 1) % SC_DISPATCHER_MAX_ITEMS;
        dispatcher->items[index].state = SC_DISPATCHER_ITEM_RETIRING;
        detached[item_index] = index;
    }
    dispatcher->stats.queued_count = 0;
    return count;
}

/** Settle detached ownership without admission-lock side effects. */
static void
sc_dispatcher_retire_batch(struct sc_dispatcher *dispatcher,
                           const size_t *detached, size_t count,
                           enum sc_dispatcher_result result) {
    for (size_t item_index = 0; item_index < count; ++item_index) {
        sc_dispatcher_retire(dispatcher, detached[item_index], result);
    }
}

/** Collect queued ownership under the gate and retire it after unlocking. */
static void
sc_dispatcher_discard_pending(struct sc_dispatcher *dispatcher,
                              enum sc_dispatcher_result result) {
    size_t detached[SC_DISPATCHER_MAX_ITEMS];
    sc_mutex_lock(&dispatcher->mutex);
    size_t count = sc_dispatcher_detach_pending(dispatcher, detached);
    sc_mutex_unlock(&dispatcher->mutex);
    sc_dispatcher_retire_batch(dispatcher, detached, count, result);
}

/** Revoke admission before any target release and settle queued borrowers. */
bool
sc_dispatcher_generation_revoke(struct sc_dispatcher *dispatcher,
                                sc_dispatcher_generation generation) {

    if (!sc_dispatcher_can_transition(dispatcher)) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool current = generation && generation == dispatcher->generation;

    if (current) {
        dispatcher->generation = 0;
        dispatcher->binding = NULL;
    }

    sc_mutex_unlock(&dispatcher->mutex);

    if (current) {
        sc_dispatcher_discard_pending(dispatcher, SC_DISPATCHER_REVOKED);
    }

    return current;
}

/** Complete a reserved publication, including all coalesced wake dependants. */
static void
sc_dispatcher_publish_wakeup(struct sc_dispatcher *dispatcher, uintptr_t ticket) {
    SDL_Event event = {
        .user = {
            .type = dispatcher->wake_event_type,
            .data1 = dispatcher,
            .data2 = (void *) ticket,
        },
    };
    bool published = dispatcher->wakeup(dispatcher->wakeup_userdata, &event);

    if (!published) {
        size_t detached[SC_DISPATCHER_MAX_ITEMS];
        sc_mutex_lock(&dispatcher->mutex);
        bool still_pending = dispatcher->wake_pending &&
                             ticket == dispatcher->wake_ticket;
        size_t count = 0;

        if (still_pending) {
            dispatcher->wake_pending = false;
            count = sc_dispatcher_detach_pending(dispatcher, detached);
        }

        sc_mutex_unlock(&dispatcher->mutex);
        sc_dispatcher_retire_batch(dispatcher, detached, count,
                                   SC_DISPATCHER_WAKE_FAILED);
    }

    // The publisher owns a lifetime reference through failure-side destruction.
    sc_mutex_lock(&dispatcher->mutex);
    assert(dispatcher->stats.publishers);
    --dispatcher->stats.publishers;
    sc_mutex_unlock(&dispatcher->mutex);
}

struct sc_dispatcher_wakeup_reservation {
    uintptr_t ticket;
    bool exhausted;
};

/** Reserve a coalesced wake while the caller holds the admission mutex. */
static struct sc_dispatcher_wakeup_reservation
sc_dispatcher_reserve_wakeup(struct sc_dispatcher *dispatcher) {
    sc_mutex_assert(&dispatcher->mutex);
    bool required = dispatcher->stats.queued_count && !dispatcher->wake_pending;

    if (required) {
        // Never alias an outstanding notification after counter exhaustion.
        if (dispatcher->wake_ticket == UINTPTR_MAX) {
            // Accepted failure settlement itself is a live publisher operation.
            ++dispatcher->stats.publishers;
            return (struct sc_dispatcher_wakeup_reservation) {.exhausted = true};
        }
        dispatcher->wake_pending = true;
        ++dispatcher->wake_ticket;
        ++dispatcher->stats.publishers;
        return (struct sc_dispatcher_wakeup_reservation) {
            .ticket = dispatcher->wake_ticket,
        };
    }

    return (struct sc_dispatcher_wakeup_reservation) {0};
}

/** Settle counter exhaustion while retaining the same publication lifetime. */
static void
sc_dispatcher_complete_wakeup(struct sc_dispatcher *dispatcher,
                              struct sc_dispatcher_wakeup_reservation wake) {

    if (wake.ticket) {
        sc_dispatcher_publish_wakeup(dispatcher, wake.ticket);
        return;
    }

    if (wake.exhausted) {
        sc_dispatcher_discard_pending(dispatcher, SC_DISPATCHER_WAKE_FAILED);
        sc_mutex_lock(&dispatcher->mutex);
        --dispatcher->stats.publishers;
        sc_mutex_unlock(&dispatcher->mutex);
    }
}

/** Transfer payload ownership atomically with publication of one bounded slot. */
enum sc_dispatcher_admission
sc_dispatcher_post(struct sc_dispatcher *dispatcher,
                   sc_dispatcher_generation generation,
                   const struct sc_dispatcher_task *task,
                   struct sc_dispatcher_completion **completion) {

    if (completion) {
        *completion = NULL;
    }

    if (!task || !task->run || !task->destroy) {
        return SC_DISPATCHER_REJECTED_INVALID;
    }

    sc_mutex_lock(&dispatcher->mutex);
    enum sc_dispatcher_admission admission = SC_DISPATCHER_ACCEPTED;

    if (dispatcher->closed) {
        admission = SC_DISPATCHER_REJECTED_CLOSED;
    } else if (!generation || generation != dispatcher->generation) {
        admission = SC_DISPATCHER_REJECTED_GENERATION;
    } else if (dispatcher->stats.retained_count == SC_DISPATCHER_MAX_ITEMS) {
        admission = SC_DISPATCHER_REJECTED_COUNT;
    } else if (task->payload_bytes > SC_DISPATCHER_MAX_PAYLOAD_BYTES -
                                  dispatcher->stats.retained_payload_bytes) {
        admission = SC_DISPATCHER_REJECTED_BYTES;
    }

    if (admission != SC_DISPATCHER_ACCEPTED) {
        sc_mutex_unlock(&dispatcher->mutex);
        return admission;
    }

    size_t index = 0;
    while (dispatcher->items[index].state != SC_DISPATCHER_ITEM_FREE) {
        ++index;
        assert(index < SC_DISPATCHER_MAX_ITEMS);
    }
    struct sc_dispatcher_item *item = &dispatcher->items[index];
    item->task = *task;
    item->generation = generation;
    item->completion.caller_result = SC_DISPATCHER_PENDING;
    item->completion.execution_result = SC_DISPATCHER_PENDING;
    item->completion.caller_owned = completion != NULL;
    item->state = SC_DISPATCHER_ITEM_QUEUED;
    size_t tail = (dispatcher->head + dispatcher->stats.queued_count)
                % SC_DISPATCHER_MAX_ITEMS;
    dispatcher->queue[tail] = index;
    ++dispatcher->stats.queued_count;
    ++dispatcher->stats.retained_count;
    dispatcher->stats.retained_payload_bytes += task->payload_bytes;

    if (completion) {
        *completion = &item->completion;
    }

    struct sc_dispatcher_wakeup_reservation wake =
        sc_dispatcher_reserve_wakeup(dispatcher);
    // The slot is now published: even scheduling failure remains ACCEPTED.
    sc_mutex_unlock(&dispatcher->mutex);

    sc_dispatcher_complete_wakeup(dispatcher, wake);

    return SC_DISPATCHER_ACCEPTED;
}

/** Check caller cancellation before crossing the callback-entry boundary. */
static bool
sc_dispatcher_may_execute(struct sc_dispatcher_completion *completion) {
    sc_mutex_lock(&completion->mutex);
    bool execute = completion->caller_result != SC_DISPATCHER_CANCELLED;
    sc_mutex_unlock(&completion->mutex);
    return execute;
}

/** Drain a finite batch with validation before resolving the live binding. */
size_t
sc_dispatcher_drain(struct sc_dispatcher *dispatcher) {
    assert(sc_thread_is_main());
    sc_mutex_lock(&dispatcher->mutex);
    // A consumed wake must be rearmed even when a nested pump cannot drain.
    dispatcher->wake_pending = false;
    sc_mutex_unlock(&dispatcher->mutex);

    if (dispatcher->draining) {
        return 0;
    }

    dispatcher->draining = true;
    size_t drained = 0;
    while (drained < SC_DISPATCHER_DRAIN_LIMIT) {
        sc_mutex_lock(&dispatcher->mutex);

        if (!dispatcher->stats.queued_count) {
            sc_mutex_unlock(&dispatcher->mutex);
            break;
        }

        size_t index = dispatcher->queue[dispatcher->head];
        dispatcher->head = (dispatcher->head + 1) % SC_DISPATCHER_MAX_ITEMS;
        --dispatcher->stats.queued_count;
        struct sc_dispatcher_item *item = &dispatcher->items[index];
        item->state = SC_DISPATCHER_ITEM_EXECUTING;
        bool current = !dispatcher->closed && item->generation &&
                       item->generation == dispatcher->generation;
        // No retired target is dereferenced to validate the captured value.
        void *binding = current ? dispatcher->binding : NULL;
        sc_mutex_unlock(&dispatcher->mutex);

        enum sc_dispatcher_result result = SC_DISPATCHER_REVOKED;

        if (current) {
            result = SC_DISPATCHER_CANCELLED;

            if (sc_dispatcher_may_execute(&item->completion)) {
                item->task.run(binding, item->task.payload);
                result = SC_DISPATCHER_EXECUTED;
            }
        }

        sc_dispatcher_retire(dispatcher, index, result);
        ++drained;
    }

    sc_mutex_lock(&dispatcher->mutex);
    struct sc_dispatcher_wakeup_reservation wake =
        sc_dispatcher_reserve_wakeup(dispatcher);
    sc_mutex_unlock(&dispatcher->mutex);
    dispatcher->draining = false;

    sc_dispatcher_complete_wakeup(dispatcher, wake);

    return drained;
}

/** Keep this application event separate from legacy generation flushes. */
bool
sc_dispatcher_handle_event(struct sc_dispatcher *dispatcher,
                           const SDL_Event *event) {

    if (event->type != dispatcher->wake_event_type ||
            event->user.data1 != dispatcher) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool current_wake = (uintptr_t) event->user.data2 == dispatcher->wake_ticket;
    sc_mutex_unlock(&dispatcher->mutex);

    if (!current_wake) {
        return true;
    }

    sc_dispatcher_drain(dispatcher);
    return true;
}

/** Close admission without waiting for workers or invoking effects under locks. */
bool
sc_dispatcher_shutdown(struct sc_dispatcher *dispatcher) {

    if (!sc_dispatcher_can_transition(dispatcher)) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    dispatcher->closed = true;
    dispatcher->generation = 0;
    dispatcher->binding = NULL;
    sc_mutex_unlock(&dispatcher->mutex);
    sc_dispatcher_discard_pending(dispatcher, SC_DISPATCHER_SHUTDOWN);
    return true;
}

/** Remove only pending wakeups that borrow this dispatcher instance. */
static bool SDLCALL
sc_dispatcher_keep_other_events(void *userdata, SDL_Event *event) {
    const struct sc_dispatcher *dispatcher = userdata;
    return event->type != dispatcher->wake_event_type ||
           event->user.data1 != dispatcher;
}

/** Free synchronization only after every retained operation and caller settles. */
bool
sc_dispatcher_destroy(struct sc_dispatcher *dispatcher) {

    if (!sc_dispatcher_can_transition(dispatcher)) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool settled = dispatcher->closed && !dispatcher->stats.publishers &&
                   !dispatcher->stats.retained_count;
    sc_mutex_unlock(&dispatcher->mutex);

    if (!settled) {
        return false;
    }

    // External producer/caller quiescence is an ownership precondition here.
    SDL_FilterEvents(sc_dispatcher_keep_other_events, dispatcher);
    for (size_t index = 0; index < SC_DISPATCHER_MAX_ITEMS; ++index) {
        struct sc_dispatcher_completion *completion =
            &dispatcher->items[index].completion;
        sc_cond_destroy(&completion->condition);
        sc_mutex_destroy(&completion->mutex);
    }
    sc_mutex_destroy(&dispatcher->mutex);
    return true;
}

/** Wait with a completion-local condition, never the admission mutex. */
enum sc_dispatcher_result
sc_dispatcher_completion_wait(struct sc_dispatcher_completion *completion) {

    if (sc_thread_is_main()) {
        return SC_DISPATCHER_MAIN_THREAD_REJECTED;
    }

    sc_mutex_lock(&completion->mutex);
    while (completion->caller_result == SC_DISPATCHER_PENDING) {
        sc_cond_wait(&completion->condition, &completion->mutex);
    }
    enum sc_dispatcher_result result = completion->caller_result;
    sc_mutex_unlock(&completion->mutex);
    return result;
}

/** Let a caller depart without freeing data still used by its operation. */
void
sc_dispatcher_completion_cancel(struct sc_dispatcher_completion *completion) {
    sc_mutex_lock(&completion->mutex);

    if (completion->caller_result == SC_DISPATCHER_PENDING) {
        completion->caller_result = SC_DISPATCHER_CANCELLED;
        sc_cond_broadcast(&completion->condition);
    }

    sc_mutex_unlock(&completion->mutex);
}

/** Release the caller reference independently of callback/destructor retirement. */
void
sc_dispatcher_completion_release(struct sc_dispatcher_completion *completion) {
    struct sc_dispatcher *dispatcher = completion->owner;
    sc_mutex_lock(&dispatcher->mutex);
    assert(completion->caller_owned);
    completion->caller_owned = false;
    struct sc_dispatcher_item *item = &dispatcher->items[completion->index];

    if (item->state == SC_DISPATCHER_ITEM_RETIRED) {
        item->state = SC_DISPATCHER_ITEM_FREE;
        --dispatcher->stats.retained_count;
    }

    sc_mutex_unlock(&dispatcher->mutex);
}

/** Inspect actual execution separately from early caller cancellation. */
enum sc_dispatcher_result
sc_dispatcher_completion_execution_result(
        struct sc_dispatcher_completion *completion) {
    sc_mutex_lock(&completion->mutex);
    enum sc_dispatcher_result result = completion->execution_result;
    sc_mutex_unlock(&completion->mutex);
    return result;
}

/** Return a bounded payload-free accounting snapshot for tests and diagnostics. */
struct sc_dispatcher_stats
sc_dispatcher_get_stats(struct sc_dispatcher *dispatcher) {
    sc_mutex_lock(&dispatcher->mutex);
    struct sc_dispatcher_stats stats = dispatcher->stats;
    sc_mutex_unlock(&dispatcher->mutex);
    return stats;
}
