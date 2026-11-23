#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "util/timeout.h"

static bool test_mutex_init(sc_mutex *mutex);
static void test_mutex_destroy(sc_mutex *mutex);
static bool test_condition_init(sc_cond *condition);
static bool test_wait(sc_cond *condition, sc_mutex *mutex, sc_tick deadline);
static bool test_thread_create(sc_thread *thread, sc_thread_fn function,
                                const char *name, void *userdata);
static void test_thread_join(sc_thread *thread, int *status);

/* Only external thread admission/wait and acquisition failure are controlled;
 * all timeout initialization, worker, callbacks and release are production. */
#define sc_mutex_init test_mutex_init
#define sc_mutex_destroy test_mutex_destroy
#define sc_cond_init test_condition_init
#define sc_cond_timedwait test_wait
#define sc_thread_create test_thread_create
#define sc_thread_join test_thread_join
#include "../src/util/timeout.c"
#undef sc_thread_join
#undef sc_thread_create
#undef sc_cond_timedwait
#undef sc_cond_init
#undef sc_mutex_destroy
#undef sc_mutex_init

static const sc_tick TEST_DEADLINE = SC_TICK_FROM_SEC(12);
static unsigned mutex_acquisitions;
static unsigned mutex_releases;
static unsigned callback_count;
static unsigned join_count;
static bool fail_condition_init;
static int callback_token;

/** Reset observations between distinct lifecycle scenarios, never retry a case. */
static void
test_reset(void) {
    mutex_acquisitions = 0;
    mutex_releases = 0;
    callback_count = 0;
    join_count = 0;
    fail_condition_init = false;
}

/** Count actual mutex acquisition at the production partial-init boundary. */
static bool
test_mutex_init(sc_mutex *mutex) {
    bool initialized = sc_mutex_init(mutex);
    mutex_acquisitions += initialized;
    return initialized;
}

/** Count and perform real release, including condition-init unwind. */
static void
test_mutex_destroy(sc_mutex *mutex) {
    ++mutex_releases;
    sc_mutex_destroy(mutex);
}

/** Fail only the selected condition acquisition, retaining real normal setup. */
static bool
test_condition_init(sc_cond *condition) {

    if (fail_condition_init) {
        return false;
    }

    return sc_cond_init(condition);
}

/** Finish the worker deadline without sleeping and inspect published inputs. */
static bool
test_wait(sc_cond *condition, sc_mutex *mutex, sc_tick deadline) {
    (void) condition;
    assert(sc_mutex_held(mutex));
    assert(deadline == TEST_DEADLINE);
    return false;
}

/** Run the real worker before thread-create returns, a permitted schedule. */
static bool
test_thread_create(sc_thread *thread, sc_thread_fn function,
                    const char *name, void *userdata) {
    assert(sc_thread_create(thread, function, name, userdata));
    sc_thread_join(thread, NULL);
    return true;
}

/** Observe the shipping join call after the admission seam already joined it. */
static void
test_thread_join(sc_thread *thread, int *status) {
    (void) thread;
    (void) status;
    ++join_count;
}

/** Verify the real worker observes the intended callback and stable userdata. */
static void
test_on_timeout(struct sc_timeout *timeout, void *userdata) {
    assert(timeout->deadline == TEST_DEADLINE);
    assert(userdata == &callback_token);
    ++callback_count;
}

/** Preserve the existing callback-on-stop outcome while proving publication. */
static void
test_callback_publication(bool stopped) {
    struct sc_timeout timeout = {0};
    assert(sc_timeout_init(&timeout));

    if (stopped) {
        sc_timeout_stop(&timeout);
    }

    static const struct sc_timeout_callbacks callbacks = {
        .on_timeout = test_on_timeout,
    };
    assert(sc_timeout_start(&timeout, TEST_DEADLINE, &callbacks, &callback_token));
    assert(callback_count == 1);
    sc_timeout_join(&timeout);
    assert(join_count == 1);
    sc_timeout_destroy(&timeout);
    assert(mutex_acquisitions == 1);
    assert(mutex_releases == 1);
}

/** A failed condition acquisition must release its already acquired mutex. */
static void
test_partial_init(void) {
    struct sc_timeout timeout = {0};
    fail_condition_init = true;
    assert(!sc_timeout_init(&timeout));
    assert(mutex_acquisitions == 1);
    assert(mutex_releases == 1);
}

/** Select each independent regression so both original failures are retained. */
int
main(int argument_count, char **arguments) {
    assert(SDL_Init(0));

    if (argument_count > 1 && !strcmp(arguments[1], "--init-failure")) {
        test_partial_init();
        SDL_Quit();
        return 0;
    }

    if (argument_count > 1 && !strcmp(arguments[1], "--already-stopped")) {
        test_callback_publication(true);
        SDL_Quit();
        return 0;
    }

    test_callback_publication(false);
    test_reset();
    test_callback_publication(true);
    test_reset();
    test_partial_init();
    puts("timeout lifetime: 3 production-path cases passed");
    SDL_Quit();
    return 0;
}
