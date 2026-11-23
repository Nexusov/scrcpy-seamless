#include "session_retirement.h"

#include <assert.h>
#include <SDL3/SDL_thread.h>

/** Execute one borrowed join at a time without holding the admission mutex. */
static int
sc_session_retirement_run(void *userdata) {
    struct sc_session_retirement *retirement = userdata;
    sc_mutex_lock(&retirement->mutex);

    for (;;) {
        while (!retirement->pending && !retirement->stopping) {
            sc_cond_wait(&retirement->condition, &retirement->mutex);
        }

        if (retirement->stopping) {
            break;
        }

        sc_session_retirement_join_fn join = retirement->join;
        void *context = retirement->userdata;
        retirement->join = NULL;
        retirement->userdata = NULL;
        sc_mutex_unlock(&retirement->mutex);
        join(context);
        sc_mutex_lock(&retirement->mutex);
        retirement->pending = false;
        // No later worker access to the borrowed owner follows this release.
        atomic_store_explicit(&retirement->completed, true, memory_order_release);
    }

    sc_mutex_unlock(&retirement->mutex);
    return 0;
}

bool
sc_session_retirement_init(struct sc_session_retirement *retirement) {
    assert(sc_thread_is_main());
    retirement->join = NULL;
    retirement->userdata = NULL;
    retirement->pending = false;
    retirement->stopping = false;
    retirement->started = false;
    retirement->active = false;
    retirement->shutdown_requested = false;
    atomic_init(&retirement->completed, false);

    if (!sc_mutex_init(&retirement->mutex)) {
        return false;
    }

    if (!sc_cond_init(&retirement->condition)) {
        sc_mutex_destroy(&retirement->mutex);
        return false;
    }

    if (!sc_thread_create(&retirement->thread, sc_session_retirement_run,
                          "sc-retirement", retirement)) {
        sc_cond_destroy(&retirement->condition);
        sc_mutex_destroy(&retirement->mutex);
        return false;
    }

    retirement->started = true;
    return true;
}

bool
sc_session_retirement_request(struct sc_session_retirement *retirement,
                              sc_session_retirement_join_fn join,
                              void *userdata) {
    assert(sc_thread_is_main());

    if (!retirement->started || retirement->active ||
            retirement->shutdown_requested || !join) {
        return false;
    }

    sc_mutex_lock(&retirement->mutex);
    retirement->active = true;
    retirement->join = join;
    retirement->userdata = userdata;
    retirement->pending = true;
    atomic_store_explicit(&retirement->completed, false, memory_order_relaxed);
    sc_cond_signal(&retirement->condition);
    sc_mutex_unlock(&retirement->mutex);
    return true;
}

bool
sc_session_retirement_take_completed(struct sc_session_retirement *retirement) {
    assert(sc_thread_is_main());

    if (!retirement->active ||
            !atomic_load_explicit(&retirement->completed, memory_order_acquire)) {
        return false;
    }

    retirement->active = false;
    return true;
}

bool
sc_session_retirement_request_shutdown(struct sc_session_retirement *retirement) {
    assert(sc_thread_is_main());

    if (!retirement->started || retirement->active) {
        return false;
    }

    sc_mutex_lock(&retirement->mutex);
    retirement->shutdown_requested = true;
    retirement->stopping = true;
    sc_cond_signal(&retirement->condition);
    sc_mutex_unlock(&retirement->mutex);
    return true;
}

bool
sc_session_retirement_is_stopped(const struct sc_session_retirement *retirement) {
    assert(sc_thread_is_main());
    return retirement->started && retirement->shutdown_requested &&
           SDL_GetThreadState(retirement->thread.thread) == SDL_THREAD_COMPLETE;
}

bool
sc_session_retirement_destroy(struct sc_session_retirement *retirement) {
    assert(sc_thread_is_main());

    if (!sc_session_retirement_is_stopped(retirement)) {
        return false;
    }

    sc_thread_join(&retirement->thread, NULL);
    sc_cond_destroy(&retirement->condition);
    sc_mutex_destroy(&retirement->mutex);
    retirement->started = false;
    return true;
}
