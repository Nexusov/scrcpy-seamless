#include "common.h"

#include <assert.h>
#include <stdatomic.h>
#include <stdio.h>
#include <SDL3/SDL.h>

#include "session_retirement.h"

enum test_failure {
    TEST_FAILURE_NONE,
    TEST_FAILURE_MUTEX,
    TEST_FAILURE_CONDITION,
    TEST_FAILURE_THREAD,
};

enum {
    TEST_REPLACEMENT_COUNT = 100,
    TEST_PROGRESS_EVENT_CODE = 73,
};

static enum test_failure failure;
static unsigned mutex_releases;
static unsigned condition_releases;
static unsigned thread_attempts;
static unsigned thread_joins;

bool __real_sc_mutex_init(sc_mutex *mutex);
void __real_sc_mutex_destroy(sc_mutex *mutex);
bool __real_sc_cond_init(sc_cond *condition);
void __real_sc_cond_destroy(sc_cond *condition);
bool __real_sc_thread_create(sc_thread *thread, sc_thread_fn function,
                             const char *name, void *userdata);
void __real_sc_thread_join(sc_thread *thread, int *status);

bool
__wrap_sc_mutex_init(sc_mutex *mutex) {
    return failure != TEST_FAILURE_MUTEX && __real_sc_mutex_init(mutex);
}

void
__wrap_sc_mutex_destroy(sc_mutex *mutex) {
    ++mutex_releases;
    __real_sc_mutex_destroy(mutex);
}

bool
__wrap_sc_cond_init(sc_cond *condition) {
    return failure != TEST_FAILURE_CONDITION && __real_sc_cond_init(condition);
}

void
__wrap_sc_cond_destroy(sc_cond *condition) {
    ++condition_releases;
    __real_sc_cond_destroy(condition);
}

bool
__wrap_sc_thread_create(sc_thread *thread, sc_thread_fn function,
                        const char *name, void *userdata) {
    ++thread_attempts;

    if (failure == TEST_FAILURE_THREAD) {
        return false;
    }

    return __real_sc_thread_create(thread, function, name, userdata);
}

void
__wrap_sc_thread_join(sc_thread *thread, int *status) {
    ++thread_joins;
    __real_sc_thread_join(thread, status);
}

struct test_owner {
    SDL_Semaphore *entered;
    SDL_Semaphore *release;
    atomic_uint joins;
};

/** Hold the actual production worker at its external module-join boundary. */
static void
test_owner_join(void *userdata) {
    struct test_owner *owner = userdata;
    assert(!sc_thread_is_main());
    SDL_SignalSemaphore(owner->entered);
    SDL_WaitSemaphore(owner->release);
    atomic_fetch_add_explicit(&owner->joins, 1, memory_order_relaxed);
}

static void
test_reset(enum test_failure requested_failure) {
    failure = requested_failure;
    mutex_releases = 0;
    condition_releases = 0;
    thread_attempts = 0;
    thread_joins = 0;
}

/** Failed admission has no owner callback and releases only acquired stages. */
static void
test_startup_failure(enum test_failure requested_failure) {
    test_reset(requested_failure);
    struct sc_session_retirement retirement;
    bool initialized = sc_session_retirement_init(&retirement);
    assert(!initialized);
    assert(!sc_session_retirement_destroy(&retirement));
    assert(!sc_session_retirement_request_shutdown(&retirement));
    assert(!sc_session_retirement_request(&retirement, test_owner_join, NULL));
    assert(!thread_joins);
    assert(mutex_releases == (requested_failure != TEST_FAILURE_MUTEX));
    assert(condition_releases == (requested_failure == TEST_FAILURE_THREAD));
    assert(thread_attempts == (requested_failure == TEST_FAILURE_THREAD));
}

/** Reap only after actual thread completion, continuing to pump app events. */
static void
test_shutdown(struct sc_session_retirement *retirement) {
    assert(!sc_session_retirement_destroy(retirement));
    bool shutdown = sc_session_retirement_request_shutdown(retirement);
    assert(shutdown);
    assert(!sc_session_retirement_request(retirement, test_owner_join, NULL));

    while (!sc_session_retirement_is_stopped(retirement)) {
        SDL_PumpEvents();
    }

    bool destroyed = sc_session_retirement_destroy(retirement);
    assert(destroyed);
    assert(!sc_session_retirement_destroy(retirement));
    assert(thread_joins == 1);
    assert(mutex_releases == 1);
    assert(condition_releases == 1);
}

/** A blocked owner cannot block main progress or admit a replacement callback. */
static void
test_join_and_reuse(void) {
    test_reset(TEST_FAILURE_NONE);
    struct sc_session_retirement retirement;
    bool initialized = sc_session_retirement_init(&retirement);
    assert(initialized);
    struct test_owner owner = {
        .entered = SDL_CreateSemaphore(0),
        .release = SDL_CreateSemaphore(0),
    };
    assert(owner.entered && owner.release);
    atomic_init(&owner.joins, 0);

    for (unsigned generation = 0; generation < TEST_REPLACEMENT_COUNT;
            ++generation) {
        bool requested = sc_session_retirement_request(&retirement,
                                                        test_owner_join, &owner);
        assert(requested);
        SDL_WaitSemaphore(owner.entered);
        assert(!sc_session_retirement_take_completed(&retirement));
        assert(!sc_session_retirement_request(&retirement, test_owner_join, &owner));
        assert(!sc_session_retirement_request_shutdown(&retirement));
        assert(!sc_session_retirement_destroy(&retirement));
        assert(!thread_joins);

        SDL_Event event = {.type = SDL_EVENT_USER};
        event.user.code = TEST_PROGRESS_EVENT_CODE;
        bool posted = SDL_PushEvent(&event);
        assert(posted);
        SDL_Event observed;
        int received = SDL_PeepEvents(&observed, 1, SDL_GETEVENT,
                                      SDL_EVENT_USER, SDL_EVENT_USER);
        assert(received == 1 && observed.user.code == TEST_PROGRESS_EVENT_CODE);

        SDL_SignalSemaphore(owner.release);

        while (!atomic_load_explicit(&retirement.completed, memory_order_acquire)) {
            SDL_PumpEvents();
        }

        // Completion is still owned until the one main-thread receipt.
        assert(!sc_session_retirement_request(&retirement, test_owner_join, &owner));
        assert(!sc_session_retirement_request_shutdown(&retirement));
        bool completed = sc_session_retirement_take_completed(&retirement);
        assert(completed);
        assert(!sc_session_retirement_take_completed(&retirement));
        assert(atomic_load_explicit(&owner.joins, memory_order_relaxed)
               == generation + 1);
    }

    assert(thread_attempts == 1);
    test_shutdown(&retirement);
    SDL_DestroySemaphore(owner.release);
    SDL_DestroySemaphore(owner.entered);
}

static void
test_idle_shutdown(void) {
    test_reset(TEST_FAILURE_NONE);
    struct sc_session_retirement retirement;
    bool initialized = sc_session_retirement_init(&retirement);
    assert(initialized);
    assert(!sc_session_retirement_take_completed(&retirement));
    test_shutdown(&retirement);
}

int
main(void) {
    bool initialized = SDL_Init(SDL_INIT_EVENTS);
    assert(initialized);
    test_startup_failure(TEST_FAILURE_MUTEX);
    test_startup_failure(TEST_FAILURE_CONDITION);
    test_startup_failure(TEST_FAILURE_THREAD);
    test_idle_shutdown();
    test_join_and_reuse();
    SDL_Quit();
    puts("Session retirement: 5 groups passed, including 100 controlled worker operations");
    return 0;
}
