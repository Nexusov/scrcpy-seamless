#ifndef SC_SESSION_RETIREMENT_H
#define SC_SESSION_RETIREMENT_H

#include "common.h"

#include <stdatomic.h>
#include <stdbool.h>

#include "util/thread.h"

typedef void (*sc_session_retirement_join_fn)(void *userdata);

/* Main owns this stable storage. Only the worker invokes the join callback. */
struct sc_session_retirement {
    sc_thread thread;
    sc_mutex mutex;
    sc_cond condition;
    sc_session_retirement_join_fn join;
    void *userdata;
    atomic_bool completed;
    bool pending; // protected by mutex
    bool stopping; // protected by mutex
    bool started; // main only
    bool active; // main only, includes an unconsumed completion
    bool shutdown_requested; // main only
};

/* Start a parked worker before acquiring any connection-generation resources. */
bool
sc_session_retirement_init(struct sc_session_retirement *retirement);

/* Admission borrows userdata until take_completed succeeds; no allocation. */
bool
sc_session_retirement_request(struct sc_session_retirement *retirement,
                              sc_session_retirement_join_fn join,
                              void *userdata);

/* Main may release the retired owner only after consuming this receipt. */
bool
sc_session_retirement_take_completed(struct sc_session_retirement *retirement);

/* An active or unconsumed operation must settle before worker shutdown. */
bool
sc_session_retirement_request_shutdown(struct sc_session_retirement *retirement);

bool
sc_session_retirement_is_stopped(const struct sc_session_retirement *retirement);

/* Reap only a completed thread; never wait for a live worker from main. */
bool
sc_session_retirement_destroy(struct sc_session_retirement *retirement);

#endif
