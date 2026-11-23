#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <SDL3/SDL.h>

#include "dispatcher.h"
#include "events.h"
#include "generation_targets.h"
#include "session_notifications.h"

struct test_gate {
    sc_mutex mutex;
    sc_cond condition;
    bool open;
};

struct test_wakeup {
    struct sc_generation_targets targets;
    struct sc_session_notifications *notifications;
    struct sc_dispatcher *dispatcher;
    struct test_gate *entered;
    struct test_gate *release;
    unsigned calls;
    bool fail;
    bool gate_after_post;
};

struct test_producer {
    struct sc_session_notifications *notifications;
    uint32_t event;
    bool retained;
};

/** Initialize a controlled publication gate without scheduling assumptions. */
static void
test_gate_init(struct test_gate *gate) {
    gate->open = false;
    assert(sc_mutex_init(&gate->mutex));
    assert(sc_cond_init(&gate->condition));
}

/** Release a worker waiting at an explicitly observed publication stage. */
static void
test_gate_open(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);
    gate->open = true;
    sc_cond_broadcast(&gate->condition);
    sc_mutex_unlock(&gate->mutex);
}

/** Wait for the exact controlled stage, without polling or sleeps. */
static void
test_gate_wait(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);

    while (!gate->open) {
        sc_cond_wait(&gate->condition, &gate->mutex);
    }

    sc_mutex_unlock(&gate->mutex);
}

/** Release gate synchronization only after participating workers join. */
static void
test_gate_destroy(struct test_gate *gate) {
    sc_cond_destroy(&gate->condition);
    sc_mutex_destroy(&gate->mutex);
}

/** Observe outside-lock wake publication, optionally failing or gating it. */
static bool
test_wake(void *userdata, SDL_Event *event) {
    struct test_wakeup *wake = userdata;
    assert(!sc_mutex_held(&wake->dispatcher->mutex));
    assert(!sc_mutex_held(&wake->notifications->mutex));
    ++wake->calls;

    if (wake->entered && !wake->gate_after_post) {
        test_gate_open(wake->entered);
        test_gate_wait(wake->release);
    }

    bool queued = !wake->fail && SDL_PushEvent(event);

    if (wake->entered && wake->gate_after_post) {
        test_gate_open(wake->entered);
        test_gate_wait(wake->release);
    }

    return queued;
}

/** Initialize the actual dispatcher and a single stable generation latch. */
static sc_dispatcher_generation
test_init_seed(struct sc_dispatcher *dispatcher,
          struct sc_session_notifications *notifications,
          struct test_wakeup *wake, sc_dispatcher_generation seed) {
    *wake = (struct test_wakeup) {
        .notifications = notifications,
        .dispatcher = dispatcher,
    };
    assert(sc_dispatcher_init(dispatcher, SC_EVENT_DISPATCHER_WAKEUP, test_wake,
                               wake));
    assert(sc_session_notifications_init(notifications, dispatcher));
    dispatcher->last_generation = seed;
    wake->targets.notifications = notifications;
    sc_dispatcher_generation generation;
    assert(sc_dispatcher_generation_begin(dispatcher, &wake->targets,
                                           &generation));
    assert(sc_session_notifications_bind(notifications, generation));
    return generation;
}

/** Use the ordinary monotonic allocator unless an exact-width boundary is tested. */
static sc_dispatcher_generation
test_init(struct sc_dispatcher *dispatcher,
          struct sc_session_notifications *notifications,
          struct test_wakeup *wake) {
    return test_init_seed(dispatcher, notifications, wake, 0);
}

/** Retire each owner only after publication has quiesced. */
static void
test_finish(struct sc_dispatcher *dispatcher,
            struct sc_session_notifications *notifications,
            sc_dispatcher_generation generation) {
    sc_session_notifications_revoke(notifications);
    assert(sc_dispatcher_generation_revoke(dispatcher, generation));
    sc_session_notifications_destroy(notifications);
    assert(sc_dispatcher_shutdown(dispatcher));
    assert(sc_dispatcher_destroy(dispatcher));
}

/** Read the actual FIFO without adding unrelated OS-pump sentinel events. */
static bool
test_poll(SDL_Event *event) {
    return SDL_PeepEvents(event, 1, SDL_GETEVENT, SDL_EVENT_FIRST,
                          SDL_EVENT_LAST) == 1;
}

/** Consume production tokens through the actual SDL FIFO, with failed-wake fallback. */
static bool
test_take(struct sc_session_notifications *notifications, uint32_t *outcome) {

    if (sc_session_notifications_take(notifications, outcome)) {
        return true;
    }

    SDL_Event event;

    while (test_poll(&event)) {

        if (sc_session_notifications_handle_event(notifications, &event,
                                                   outcome)) {
            return true;
        }

        (void) sc_dispatcher_handle_event(notifications->dispatcher, &event);
    }

    return false;
}

/** Verify first connected/terminal ordering and suppression of duplicates. */
static void
test_connected_then_terminal(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(!sc_session_notifications_bind(&notifications, generation));
    assert(!sc_session_notifications_publish(&notifications, 0));
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_SERVER_CONNECTED));
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_SERVER_CONNECTED));
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_DEMUXER_ERROR));
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_CONTROLLER_ERROR));
    uint32_t event;
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_SERVER_CONNECTED);
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_DEMUXER_ERROR);
    assert(!test_take(&notifications, &event));
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_SERVER_CONNECTED));
    assert(sc_dispatcher_get_stats(&dispatcher).retained_payload_bytes == 0);
    test_finish(&dispatcher, &notifications, generation);
}

/** A first terminal outcome excludes late connection and duplicate outcomes. */
static void
test_terminal_first(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_SERVER_CONNECTION_FAILED));
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_SERVER_CONNECTED));
    uint32_t event;
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_SERVER_CONNECTION_FAILED);
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_CONTROLLER_ERROR));
    assert(!test_take(&notifications, &event));
    test_finish(&dispatcher, &notifications, generation);
}

/** Consuming connection does not reset its deduplication or terminal admission. */
static void
test_consumed_connection(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_SERVER_CONNECTED));
    uint32_t event;
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_SERVER_CONNECTED);
    assert(!test_take(&notifications, &event));
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_SERVER_CONNECTED));
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_TIME_LIMIT_REACHED));
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_TIME_LIMIT_REACHED);
    test_finish(&dispatcher, &notifications, generation);
}

/** A fixed-slot pressure task has no external target or allocated payload. */
static void
test_noop(void *binding, void *payload) {
    (void) binding;
    (void) payload;
}

/** Release the empty pressure task without allocating or touching a target. */
static void
test_release(void *payload) {
    (void) payload;
}

/** Full dispatcher admission must not lose critical current-generation work. */
static void
test_capacity_rejection(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    const struct sc_dispatcher_task task = {
        .run = test_noop,
        .destroy = test_release,
    };

    for (unsigned index = 0; index < SC_DISPATCHER_MAX_ITEMS; ++index) {
        assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
                == SC_DISPATCHER_ACCEPTED);
    }

    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_SERVER_CONNECTED));
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_RECORDER_ERROR));
    struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(&dispatcher);
    assert(stats.retained_count == SC_DISPATCHER_MAX_ITEMS);
    assert(stats.retained_payload_bytes == 0);
    uint32_t event;
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_SERVER_CONNECTED);
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_RECORDER_ERROR);
    test_finish(&dispatcher, &notifications, generation);
}

/** Wake failure retires only the doorbell, never the allocation-free outcome. */
static void
test_wakeup_failure(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    wake.fail = true;
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_CONTROLLER_ERROR));
    assert(wake.calls == 1);
    assert(sc_dispatcher_get_stats(&dispatcher).retained_count == 0);
    uint32_t event;
    assert(test_take(&notifications, &event));
    assert(event == SC_EVENT_CONTROLLER_ERROR);
    test_finish(&dispatcher, &notifications, generation);
}

/** A worker publishes through the production latch before the controlled wake. */
static int
test_publish(void *userdata) {
    struct test_producer *producer = userdata;
    producer->retained = sc_session_notifications_publish(
        producer->notifications,
        producer->event ? producer->event : SC_EVENT_CONTROLLER_ERROR);
    return 0;
}

/** Revoke can close a publication while its outside-lock wake is still held. */
static void
test_publish_revoke_replacement(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications previous;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &previous,
                                                    &wake);
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    wake.entered = &entered;
    wake.release = &release;
    struct test_producer producer = {.notifications = &previous};
    sc_thread thread;
    assert(sc_thread_create(&thread, test_publish, "notify-test", &producer));
    test_gate_wait(&entered);
    uint32_t event;
    // An in-flight wake has no settlement yet and cannot bypass prior app work.
    assert(!sc_session_notifications_take(&previous, &event));
    sc_session_notifications_revoke(&previous);
    assert(sc_dispatcher_generation_revoke(&dispatcher, generation));

    struct sc_session_notifications replacement;
    assert(sc_session_notifications_init(&replacement, &dispatcher));
    struct sc_generation_targets replacement_targets = {
        .notifications = &replacement,
    };
    sc_dispatcher_generation next_generation;
    assert(sc_dispatcher_generation_begin(&dispatcher, &replacement_targets,
                                           &next_generation));
    assert(next_generation > generation);
    assert(sc_session_notifications_bind(&replacement, next_generation));
    assert(!sc_session_notifications_publish(&previous,
                                              SC_EVENT_SERVER_CONNECTED));
    assert(!test_take(&replacement, &event));
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(producer.retained);
    sc_session_notifications_destroy(&previous);
    wake.entered = NULL;
    wake.release = NULL;
    wake.notifications = &replacement;
    assert(sc_session_notifications_publish(&replacement,
                                             SC_EVENT_SERVER_CONNECTED));
    assert(test_take(&replacement, &event));
    assert(event == SC_EVENT_SERVER_CONNECTED);
    assert(!test_take(&replacement, &event));
    test_finish(&dispatcher, &replacement, next_generation);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
}

/** Revocation before publication prevents both a retained event and a doorbell. */
static void
test_revoke_first(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    sc_session_notifications_revoke(&notifications);
    assert(!sc_session_notifications_publish(&notifications,
                                              SC_EVENT_SERVER_CONNECTION_FAILED));
    assert(!wake.calls);
    uint32_t event;
    assert(!test_take(&notifications, &event));
    test_finish(&dispatcher, &notifications, generation);
}

/** Preserve an ordinary app Quit queued before a critical generation outcome. */
static void
test_app_fifo_before_outcome(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    SDL_Event quit = {.type = SDL_EVENT_QUIT};
    assert(SDL_PushEvent(&quit));
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_CONTROLLER_ERROR));
    uint32_t outcome;
    assert(!sc_session_notifications_take(&notifications, &outcome));
    SDL_Event event;
    assert(test_poll(&event) && event.type == SDL_EVENT_QUIT);
    assert(!sc_session_notifications_handle_event(&notifications, &event,
                                                   &outcome));
    assert(test_poll(&event));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_CONTROLLER_ERROR);
    test_finish(&dispatcher, &notifications, generation);
}

/** Preserve a critical outcome already queued before a later ordinary Quit. */
static void
test_outcome_fifo_before_app(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_DEMUXER_ERROR));
    SDL_Event quit = {.type = SDL_EVENT_QUIT};
    assert(SDL_PushEvent(&quit));
    SDL_Event event;
    uint32_t outcome;
    assert(test_poll(&event));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_DEMUXER_ERROR);
    assert(test_poll(&event) && event.type == SDL_EVENT_QUIT);
    test_finish(&dispatcher, &notifications, generation);
}

/** Remember an early terminal token without bypassing its preceding ready fact. */
static void
test_reversed_wake_publication(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    wake.entered = &entered;
    wake.release = &release;
    struct test_producer producer = {
        .notifications = &notifications,
        .event = SC_EVENT_SERVER_CONNECTED,
    };
    sc_thread thread;
    assert(sc_thread_create(&thread, test_publish, "notify-ready", &producer));
    test_gate_wait(&entered);
    wake.entered = NULL;
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_CONTROLLER_ERROR));
    SDL_Event event;
    uint32_t outcome;
    assert(test_poll(&event));
    assert(!sc_session_notifications_handle_event(&notifications, &event,
                                                   &outcome));
    assert(!sc_session_notifications_take(&notifications, &outcome));
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(producer.retained);
    assert(test_poll(&event));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_SERVER_CONNECTED);
    assert(sc_session_notifications_take(&notifications, &outcome));
    assert(outcome == SC_EVENT_CONTROLLER_ERROR);
    assert(!sc_session_notifications_take(&notifications, &outcome));
    test_finish(&dispatcher, &notifications, generation);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
}

/** Do not expose a failed-wake fallback until its outside-lock attempt settles. */
static void
test_inflight_failed_wake(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    wake.entered = &entered;
    wake.release = &release;
    wake.fail = true;
    struct test_producer producer = {.notifications = &notifications};
    sc_thread thread;
    assert(sc_thread_create(&thread, test_publish, "notify-failed", &producer));
    test_gate_wait(&entered);
    uint32_t outcome;
    assert(!sc_session_notifications_take(&notifications, &outcome));
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(producer.retained);
    assert(sc_session_notifications_take(&notifications, &outcome));
    assert(outcome == SC_EVENT_CONTROLLER_ERROR);
    test_finish(&dispatcher, &notifications, generation);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
}

/** Validate full-width generation and exact application before consuming a fact. */
static void
test_exact_wake_identity(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    const uint64_t seed = UINT64_C(0xfedcba9800000000);
    sc_dispatcher_generation generation = test_init_seed(&dispatcher,
        &notifications, &wake, seed);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_RECORDER_ERROR));
    SDL_Event event;
    assert(test_poll(&event));
    uint32_t outcome;
    SDL_Event altered = event;
    altered.user.windowID ^= 1;
    assert(!sc_session_notifications_handle_event(&notifications, &altered,
                                                   &outcome));
    altered = event;
    altered.user.code ^= 1;
    assert(!sc_session_notifications_handle_event(&notifications, &altered,
                                                   &outcome));
    altered = event;
    altered.user.data1 = &notifications;
    assert(!sc_session_notifications_handle_event(&notifications, &altered,
                                                   &outcome));
    altered = event;
    altered.user.data2 = (void *) (uintptr_t) SC_EVENT_DEMUXER_ERROR;
    assert(!sc_session_notifications_handle_event(&notifications, &altered,
                                                   &outcome));
#if UINTPTR_MAX > UINT32_MAX
    altered = event;
    altered.user.data2 = (void *) (UINT64_C(1) << 32
                                  | (uintptr_t) event.user.data2);
    assert(!sc_session_notifications_handle_event(&notifications, &altered,
                                                   &outcome));
#endif
    assert(!sc_session_notifications_take(&notifications, &outcome));
    struct sc_generation_targets unrelated_targets = {0};
    assert(sc_dispatcher_generation_bind(&dispatcher, generation,
                                          &unrelated_targets));
    assert(!sc_session_notifications_handle_event(&notifications, &event,
                                                   &outcome));
    assert(!sc_session_notifications_take(&notifications, &outcome));
    assert(sc_dispatcher_generation_bind(&dispatcher, generation,
                                          &wake.targets));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_RECORDER_ERROR);
    assert(!sc_session_notifications_handle_event(&notifications, &event,
                                                   &outcome));
    test_finish(&dispatcher, &notifications, generation);
}

/** A main consumer may settle a token before its outside-lock publisher returns. */
static void
test_early_wake_consumption(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    struct test_gate entered, release;
    test_gate_init(&entered);
    test_gate_init(&release);
    wake.entered = &entered;
    wake.release = &release;
    wake.gate_after_post = true;
    struct test_producer producer = {.notifications = &notifications};
    sc_thread thread;
    assert(sc_thread_create(&thread, test_publish, "notify-early", &producer));
    test_gate_wait(&entered);
    SDL_Event event;
    uint32_t outcome;
    assert(test_poll(&event));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_CONTROLLER_ERROR);
    assert(!notifications.count);
    assert(!sc_session_notifications_take(&notifications, &outcome));
    test_gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(producer.retained);
    assert(!notifications.count && !notifications.open);
    assert(!sc_session_notifications_take(&notifications, &outcome));
    test_finish(&dispatcher, &notifications, generation);
    test_gate_destroy(&entered);
    test_gate_destroy(&release);
}

/** Remove only retired exact-generation tokens, preserving unrelated app work. */
static void
test_exact_wake_retirement(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_CONTROLLER_ERROR));
    SDL_Event event;
    assert(test_poll(&event));
    SDL_Event unrelated = event;
    unrelated.user.data1 = &wake;
    assert(SDL_PushEvent(&unrelated));
    assert(SDL_PushEvent(&event));
    test_finish(&dispatcher, &notifications, generation);
    assert(test_poll(&event));
    assert(event.type == SC_EVENT_SESSION_NOTIFICATION);
    assert(event.user.data1 == &wake);
    assert(!test_poll(&event));
}

static bool drain_guard_observed;

/** Resolve only the live typed binding while verifying the non-reentrant guard. */
static void
test_observe_inside_drain(void *binding, void *payload) {
    struct sc_generation_targets *targets = binding;
    const SDL_Event *event = payload;
    uint32_t outcome;
    assert(!sc_session_notifications_take(targets->notifications, &outcome));
    assert(!sc_session_notifications_handle_event(targets->notifications, event,
                                                   &outcome));
    drain_guard_observed = true;
}

/** Release the owned event copy after the actual dispatcher callback finishes. */
static void
test_release_event(void *payload) {
    free(payload);
}

/** Leave transition evaluation to the outer loop after its active drain ends. */
static void
test_nonreentrant_observation(void) {
    struct sc_dispatcher dispatcher;
    struct sc_session_notifications notifications;
    struct test_wakeup wake;
    sc_dispatcher_generation generation = test_init(&dispatcher, &notifications,
                                                    &wake);
    assert(sc_session_notifications_publish(&notifications,
                                             SC_EVENT_TIME_LIMIT_REACHED));
    SDL_Event event;
    assert(test_poll(&event));
    SDL_Event *owned_copy = malloc(sizeof(*owned_copy));
    assert(owned_copy);
    *owned_copy = event;
    const struct sc_dispatcher_task task = {
        .run = test_observe_inside_drain,
        .destroy = test_release_event,
        .payload = owned_copy,
        .payload_bytes = sizeof(*owned_copy),
    };
    drain_guard_observed = false;
    assert(sc_dispatcher_post(&dispatcher, generation, &task, NULL)
            == SC_DISPATCHER_ACCEPTED);
    assert(sc_dispatcher_drain(&dispatcher) == 1);
    assert(drain_guard_observed);
    uint32_t outcome;
    assert(!sc_session_notifications_take(&notifications, &outcome));
    assert(sc_session_notifications_handle_event(&notifications, &event,
                                                  &outcome));
    assert(outcome == SC_EVENT_TIME_LIMIT_REACHED);
    test_finish(&dispatcher, &notifications, generation);
}

/** Exercise production bounded handoff without a device, window or payload I/O. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    test_connected_then_terminal();
    test_terminal_first();
    test_consumed_connection();
    test_capacity_rejection();
    test_wakeup_failure();
    test_publish_revoke_replacement();
    test_revoke_first();
    test_app_fifo_before_outcome();
    test_outcome_fifo_before_app();
    test_reversed_wake_publication();
    test_inflight_failed_wake();
    test_exact_wake_identity();
    test_early_wake_consumption();
    test_exact_wake_retirement();
    test_nonreentrant_observation();
    puts("session notifications: 15 contract groups passed");
    SDL_Quit();
    return 0;
}
