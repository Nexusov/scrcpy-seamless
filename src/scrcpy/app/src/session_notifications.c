#include "session_notifications.h"

#include <assert.h>
#include <string.h>

#include "events.h"
#include "generation_targets.h"

#define SC_SESSION_GENERATION_HIGH_SHIFT 32

/** Decode the captured value without resolving any generation-owned pointer. */
static sc_dispatcher_generation
sc_session_notifications_event_generation(const SDL_Event *event) {
    uint32_t high;
    memcpy(&high, &event->user.code, sizeof(high));
    return ((uint64_t) high << SC_SESSION_GENERATION_HIGH_SHIFT)
         | event->user.windowID;
}

/** Validate authority before a current owner can observe a queued old value. */
static bool
sc_session_notifications_is_current(
        const struct sc_session_notifications *notifications,
        sc_dispatcher_generation generation) {
    struct sc_dispatcher *dispatcher = notifications->dispatcher;

    if (!sc_thread_is_main() || dispatcher->draining) {
        return false;
    }

    sc_mutex_lock(&dispatcher->mutex);
    bool current_generation = !dispatcher->closed && generation
                           && dispatcher->generation == generation;
    const struct sc_generation_targets *targets =
        current_generation ? dispatcher->binding : NULL;
    bool current = targets && targets->notifications == notifications;
    sc_mutex_unlock(&dispatcher->mutex);
    return current;
}

/** Take only a FIFO head whose normal wake was seen or whose wake failed. */
static bool
sc_session_notifications_take_locked(
        struct sc_session_notifications *notifications, uint32_t *event) {
    sc_mutex_assert(&notifications->mutex);

    if (!notifications->count) {
        return false;
    }

    struct sc_session_notification *head =
        &notifications->pending[notifications->head];
    bool eligible = head->wake_observed || head->wake_failed;

    if (!eligible) {
        return false;
    }

    *event = head->event;
    *head = (struct sc_session_notification) {0};
    notifications->head = (notifications->head + 1)
                        % SC_SESSION_NOTIFICATIONS_CAPACITY;
    --notifications->count;

    if (*event != SC_EVENT_SERVER_CONNECTED) {
        notifications->open = false;
    }

    return true;
}

/** Initialize bounded outcome storage before any producer can borrow it. */
bool
sc_session_notifications_init(struct sc_session_notifications *notifications,
                              struct sc_dispatcher *dispatcher) {
    *notifications = (struct sc_session_notifications) {
        .dispatcher = dispatcher,
    };
    return sc_mutex_init(&notifications->mutex);
}

/** Bind this stable owner to one generation before publisher startup. */
bool
sc_session_notifications_bind(struct sc_session_notifications *notifications,
                              sc_dispatcher_generation generation) {
    sc_mutex_lock(&notifications->mutex);
    bool valid = generation && !notifications->bound;

    if (valid) {
        notifications->generation = generation;
        notifications->bound = true;
        notifications->open = true;
    }

    sc_mutex_unlock(&notifications->mutex);
    return valid;
}

/** Retain a fact, then publish its exact value token in the ordinary SDL FIFO. */
bool
sc_session_notifications_publish(struct sc_session_notifications *notifications,
                                 uint32_t event) {
    sc_mutex_lock(&notifications->mutex);
    bool connected = event == SC_EVENT_SERVER_CONNECTED;
    bool duplicate = notifications->connected && connected;
    bool rejected = !event || !notifications->open || notifications->terminal
                 || duplicate;

    if (rejected) {
        sc_mutex_unlock(&notifications->mutex);
        return false;
    }

    assert(notifications->count < SC_SESSION_NOTIFICATIONS_CAPACITY);
    unsigned tail = (notifications->head + notifications->count)
                  % SC_SESSION_NOTIFICATIONS_CAPACITY;
    notifications->pending[tail] = (struct sc_session_notification) {
        .event = event,
    };
    ++notifications->count;
    notifications->connected |= connected;
    notifications->terminal = !connected;
    sc_dispatcher_generation generation = notifications->generation;
    sc_mutex_unlock(&notifications->mutex);

    uint32_t high = generation >> SC_SESSION_GENERATION_HIGH_SHIFT;
    SDL_Event wake = {
        .user = {
            .type = SC_EVENT_SESSION_NOTIFICATION,
            .windowID = (uint32_t) generation,
            .data1 = notifications->dispatcher,
            .data2 = (void *) (uintptr_t) event,
        },
    };
    _Static_assert(sizeof(wake.user.code) == sizeof(high),
                   "SDL user event must retain the high generation bits");
    memcpy(&wake.user.code, &high, sizeof(high));
    struct sc_dispatcher *dispatcher = notifications->dispatcher;
    bool queued = dispatcher->wakeup(dispatcher->wakeup_userdata, &wake);

    sc_mutex_lock(&notifications->mutex);
    struct sc_session_notification *retained = &notifications->pending[tail];

    // A fast main consumer or revocation may have already retired this slot.
    if (retained->event == event) {
        retained->wake_failed = !queued;
    }

    sc_mutex_unlock(&notifications->mutex);
    return true;
}

/** Poll only settled failed wakes and normal wakes already observed in FIFO. */
bool
sc_session_notifications_take(struct sc_session_notifications *notifications,
                              uint32_t *event) {

    if (!sc_session_notifications_is_current(notifications,
                                             notifications->generation)) {
        return false;
    }

    sc_mutex_lock(&notifications->mutex);
    bool taken = sc_session_notifications_take_locked(notifications, event);
    sc_mutex_unlock(&notifications->mutex);
    return taken;
}

/** Record a matching wake without allowing old work to resolve a new target. */
bool
sc_session_notifications_handle_event(
        struct sc_session_notifications *notifications, const SDL_Event *event,
        uint32_t *outcome) {
    bool exact_application = event->type == SC_EVENT_SESSION_NOTIFICATION
        && event->user.data1 == notifications->dispatcher;

    if (!exact_application) {
        return false;
    }

    sc_dispatcher_generation generation =
        sc_session_notifications_event_generation(event);

    if (!sc_session_notifications_is_current(notifications, generation)) {
        return false;
    }

    sc_mutex_lock(&notifications->mutex);

    if (generation != notifications->generation || !notifications->open) {
        sc_mutex_unlock(&notifications->mutex);
        return false;
    }

    uintptr_t encoded_kind = (uintptr_t) event->user.data2;

    if (encoded_kind > UINT32_MAX) {
        sc_mutex_unlock(&notifications->mutex);
        return false;
    }

    uint32_t kind = encoded_kind;
    bool matched = false;

    for (unsigned offset = 0; offset < notifications->count; ++offset) {
        unsigned index = (notifications->head + offset)
                       % SC_SESSION_NOTIFICATIONS_CAPACITY;

        if (notifications->pending[index].event == kind) {
            notifications->pending[index].wake_observed = true;
            matched = true;
            break;
        }
    }

    bool taken = matched
        && sc_session_notifications_take_locked(notifications, outcome);
    sc_mutex_unlock(&notifications->mutex);
    return taken;
}

/** End publication before the main owner revokes dispatcher resolution. */
void
sc_session_notifications_revoke(struct sc_session_notifications *notifications) {
    sc_mutex_lock(&notifications->mutex);
    notifications->open = false;
    notifications->count = 0;
    memset(notifications->pending, 0, sizeof(notifications->pending));
    sc_mutex_unlock(&notifications->mutex);
}

/** Remove only this application's captured generation after publishers join. */
static bool SDLCALL
sc_session_notifications_keep_unrelated(void *userdata, SDL_Event *event) {
    const struct sc_session_notifications *notifications = userdata;
    bool owned = event->type == SC_EVENT_SESSION_NOTIFICATION
        && event->user.data1 == notifications->dispatcher
        && sc_session_notifications_event_generation(event)
            == notifications->generation;
    return !owned;
}

/** Destroy synchronization only after all borrowers and wake publishers settle. */
void
sc_session_notifications_destroy(struct sc_session_notifications *notifications) {
    assert(!notifications->open);
    SDL_FilterEvents(sc_session_notifications_keep_unrelated, notifications);
    sc_mutex_destroy(&notifications->mutex);
}
