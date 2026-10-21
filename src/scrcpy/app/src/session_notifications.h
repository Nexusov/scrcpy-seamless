#ifndef SC_SESSION_NOTIFICATIONS_H
#define SC_SESSION_NOTIFICATIONS_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>

#include "dispatcher.h"
#include "util/thread.h"

#define SC_SESSION_NOTIFICATIONS_CAPACITY 2

struct sc_session_notification {
    uint32_t event;
    bool wake_observed;
    bool wake_failed;
};

/* A stable, generation-owned latch; publishers borrow it until their joins. */
struct sc_session_notifications {
    sc_mutex mutex;
    struct sc_dispatcher *dispatcher;
    sc_dispatcher_generation generation;
    struct sc_session_notification pending[SC_SESSION_NOTIFICATIONS_CAPACITY];
    unsigned head;
    unsigned count;
    bool connected;
    bool terminal;
    bool bound;
    bool open;
};

bool
sc_session_notifications_init(struct sc_session_notifications *notifications,
                              struct sc_dispatcher *dispatcher);

/* Main binds once before starting publishers; replacement needs a new owner. */
bool
sc_session_notifications_bind(struct sc_session_notifications *notifications,
                              sc_dispatcher_generation generation);

/* True means retained, independently of dispatcher admission or wake success.
 * SERVER_CONNECTED may precede the first terminal event; all later work is
 * ignored. Publication never allocates or queues a pointer to this owner. */
bool
sc_session_notifications_publish(struct sc_session_notifications *notifications,
                                 uint32_t event);

/* Main takes only an already-observed wake or a settled failed-wake fallback.
 * In-flight and normally queued wakes cannot overtake earlier app events. */
bool
sc_session_notifications_take(struct sc_session_notifications *notifications,
                              uint32_t *event);

/* Validate an exact application/generation token before recording its receipt.
 * Reordered wakes remain observed until their publication-ordered head is ready. */
bool
sc_session_notifications_handle_event(
        struct sc_session_notifications *notifications, const SDL_Event *event,
        uint32_t *outcome);

/* Close publication before dispatcher revocation and producer stop/join. */
void
sc_session_notifications_revoke(struct sc_session_notifications *notifications);

/* All publisher calls, including their outside-lock doorbells, must be joined. */
void
sc_session_notifications_destroy(struct sc_session_notifications *notifications);

#endif
