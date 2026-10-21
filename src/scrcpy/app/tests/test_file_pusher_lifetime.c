#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "adb/adb.h"
#include "file_pusher.h"

#define TEST_REQUEST_COUNT 3

static bool test_thread_create(sc_thread *thread, sc_thread_fn function,
                                const char *name, void *userdata);
static void test_thread_join(sc_thread *thread, int *status);
static void test_free(void *pointer);

/* Keep production initialization, request admission, worker and destruction;
 * hold only worker entry until Stop, and observe real destructor releases. */
#define sc_thread_create test_thread_create
#define sc_thread_join test_thread_join
#define free test_free
#include "../src/file_pusher.c"
#undef free
#undef sc_thread_join
#undef sc_thread_create

static sc_mutex worker_mutex;
static sc_cond worker_condition;
static bool worker_released;
static sc_thread_fn *worker_function;
static void *worker_userdata;
static void *queue_storage;
static char *request_paths[TEST_REQUEST_COUNT];
static unsigned queue_releases;
static unsigned request_releases;
static unsigned worker_starts;
static unsigned worker_joins;

/** Observe actual release of admitted payloads and separately retained storage. */
static void
test_free(void *pointer) {

    if (pointer && pointer == queue_storage) {
        ++queue_releases;
    }

    for (unsigned index = 0; index < TEST_REQUEST_COUNT; ++index) {

        if (pointer && pointer == request_paths[index]) {
            ++request_releases;
        }
    }

    free(pointer);
}

/** Hold a real worker before production entry until its owner requests Stop. */
static int
test_worker(void *userdata) {
    assert(userdata == worker_userdata);
    sc_mutex_lock(&worker_mutex);

    while (!worker_released) {
        sc_cond_wait(&worker_condition, &worker_mutex);
    }

    sc_mutex_unlock(&worker_mutex);
    return worker_function(userdata);
}

/** Interpose only worker scheduling, without starting any external operation. */
static bool
test_thread_create(sc_thread *thread, sc_thread_fn function,
                    const char *name, void *userdata) {
    ++worker_starts;
    worker_function = function;
    worker_userdata = userdata;
    return sc_thread_create(thread, test_worker, name, userdata);
}

/** Release the gated worker after Stop and retain the real join dependency. */
static void
test_thread_join(sc_thread *thread, int *status) {
    struct sc_file_pusher *pusher = worker_userdata;
    assert(pusher->stopped);
    sc_mutex_lock(&worker_mutex);
    worker_released = true;
    sc_cond_signal(&worker_condition);
    sc_mutex_unlock(&worker_mutex);
    sc_thread_join(thread, status);
    ++worker_joins;
}

/** Any accidental ADB install is a test failure, never an external operation. */
bool
sc_adb_install(struct sc_intr *intr, const char *serial, const char *local,
                unsigned flags) {
    (void) intr;
    (void) serial;
    (void) local;
    (void) flags;
    assert(!"ADB install must not run in a lifetime test");
    return false;
}

/** Any accidental ADB push is a test failure, never an external operation. */
bool
sc_adb_push(struct sc_intr *intr, const char *serial, const char *local,
             const char *remote, unsigned flags) {
    (void) intr;
    (void) serial;
    (void) local;
    (void) remote;
    (void) flags;
    assert(!"ADB push must not run in a lifetime test");
    return false;
}

/** The real interruptor owns no socket in this controlled stopped worker. */
bool
net_interrupt(sc_socket socket) {
    (void) socket;
    assert(!"No socket is acquired by this lifetime test");
    return false;
}

/** The real interruptor owns no process in this controlled stopped worker. */
bool
sc_process_terminate(sc_pid pid) {
    (void) pid;
    assert(!"No child process is acquired by this lifetime test");
    return false;
}

/** An unused owner must not invent a lazy worker or an allocated queue. */
static void
test_unused_owner(void) {
    struct sc_file_pusher pusher;
    assert(sc_file_pusher_init(&pusher, "synthetic-test-only", NULL));
    sc_file_pusher_stop(&pusher);
    sc_file_pusher_join(&pusher);
    sc_file_pusher_destroy(&pusher);
    assert(!worker_starts);
    assert(!worker_joins);
    assert(!queue_releases);
}

/** Stop/join must settle the worker and release queue backing plus each item. */
static void
test_admitted_queue_release(void) {
    assert(sc_mutex_init(&worker_mutex));
    assert(sc_cond_init(&worker_condition));
    struct sc_file_pusher pusher;
    assert(sc_file_pusher_init(&pusher, "synthetic-test-only", NULL));

    for (unsigned index = 0; index < TEST_REQUEST_COUNT; ++index) {
        request_paths[index] = strdup("synthetic-file-not-opened");
        assert(request_paths[index]);
        assert(sc_file_pusher_request(&pusher, SC_FILE_PUSHER_ACTION_PUSH_FILE,
                                      request_paths[index]));
    }

    queue_storage = pusher.queue.data;
    assert(queue_storage);
    sc_file_pusher_stop(&pusher);
    sc_file_pusher_join(&pusher);
    sc_file_pusher_destroy(&pusher);
    assert(worker_starts == 1);
    assert(worker_joins == 1);
    assert(request_releases == TEST_REQUEST_COUNT);
    assert(queue_releases == 1);
    sc_cond_destroy(&worker_condition);
    sc_mutex_destroy(&worker_mutex);
}

/** Check actual lazy-worker and queue release without file or ADB access. */
int
main(void) {
    assert(SDL_Init(0));
    test_unused_owner();
    test_admitted_queue_release();
    puts("file pusher lifetime: 2 production-path cases passed");
    SDL_Quit();
    return 0;
}
