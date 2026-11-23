#include "common.h"

#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "adb/adb.h"
#include "util/env.h"

static void query_observe_free(void *pointer);

/* Link the shipping query/refcount paths; only external process effects and
 * environment acquisition are controlled. Unused ADB commands are discarded. */
#define free query_observe_free
#include "../src/adb/adb.c"
#undef free

enum query_case {
    QUERY_SUCCESS,
    QUERY_TRUNCATED,
    QUERY_READ_ERROR,
    QUERY_NONZERO_EXIT,
    QUERY_PRE_CANCELLED,
    QUERY_CANCEL_AFTER_SPAWN,
    QUERY_CANCEL_DURING_READ,
    QUERY_CANCEL_DURING_WAIT,
    QUERY_SPAWN_FAILURE,
};

enum observed_effect {
    EFFECT_SPAWN,
    EFFECT_READ,
    EFFECT_TERMINATE,
    EFFECT_PIPE_CLOSE,
    EFFECT_WAIT,
    EFFECT_PROCESS_CLOSE,
};

static enum query_case current_case;
static struct sc_intr *current_interrupt;
static enum observed_effect effects[16];
static unsigned effect_count;
static bool process_owned;
static bool pipe_owned;
static bool process_waited;
static unsigned process_terminations;
static unsigned process_releases;
static unsigned pipe_releases;
static unsigned environment_reads;
static unsigned executable_releases;
static void *owned_executable;

static sc_pid
owned_process(void) {
    return (sc_pid) (uintptr_t) 0x1234;
}

static sc_pipe
owned_pipe(void) {
    return (sc_pipe) (uintptr_t) 0x2345;
}

static void
observe_effect(enum observed_effect effect) {
    assert(effect_count < ARRAY_LEN(effects));
    effects[effect_count++] = effect;
}

static void
query_observe_free(void *pointer) {
    if (pointer && pointer == owned_executable) {
        ++executable_releases;
        owned_executable = NULL;
    }

    free(pointer);
}

char *
sc_get_env(const char *name) {
    assert(!strcmp(name, "ADB"));
    ++environment_reads;
    assert(!owned_executable);
    owned_executable = strdup("synthetic-adb-path");
    assert(owned_executable);
    return owned_executable;
}

enum sc_process_result
sc_process_execute_p(const char *const argv[], sc_pid *pid, unsigned flags,
                      sc_pipe *input, sc_pipe *output, sc_pipe *error) {
    assert(argv && !strcmp(argv[0], "synthetic-adb-path"));
    assert(!strcmp(argv[1], "synthetic-query") && !argv[2]);
    assert(flags == SC_PROCESS_NO_STDERR);
    assert(!input && output && !error);
    assert(!process_owned && !pipe_owned);
    observe_effect(EFFECT_SPAWN);

    if (current_case == QUERY_SPAWN_FAILURE) {
        return SC_PROCESS_ERROR_GENERIC;
    }

    *pid = owned_process();
    *output = owned_pipe();
    process_owned = true;
    pipe_owned = true;

    if (current_case == QUERY_CANCEL_AFTER_SPAWN) {
        sc_intr_interrupt(current_interrupt);
    }

    return SC_PROCESS_SUCCESS;
}

ssize_t
sc_pipe_read(sc_pipe pipe, char *output, size_t capacity) {
    (void) pipe;
    (void) output;
    (void) capacity;
    /* Only the shipping query's bounded all-read path is authorized here. */
    assert(false);
    return -1;
}

ssize_t
sc_pipe_read_all(sc_pipe pipe, char *output, size_t capacity) {
    assert(pipe == owned_pipe() && pipe_owned && process_owned);
    assert(current_interrupt->process == owned_process());
    assert(capacity == 7);
    observe_effect(EFFECT_READ);

    if (current_case == QUERY_READ_ERROR) {
        return -1;
    }

    if (current_case == QUERY_CANCEL_DURING_READ) {
        sc_intr_interrupt(current_interrupt);
    }

    const char *data = current_case == QUERY_TRUNCATED ? "ABCDEFGH" : "ready";
    size_t length = MIN(strlen(data), capacity);
    memcpy(output, data, length);
    return (ssize_t) length;
}

bool
sc_process_terminate(sc_pid pid) {
    assert(pid == owned_process() && process_owned && !process_waited);
    ++process_terminations;
    assert(process_terminations == 1);
    observe_effect(EFFECT_TERMINATE);
    return true;
}

void
sc_pipe_close(sc_pipe pipe) {
    assert(pipe == owned_pipe() && pipe_owned && process_owned);
    pipe_owned = false;
    ++pipe_releases;
    observe_effect(EFFECT_PIPE_CLOSE);
}

sc_exit_code
sc_process_wait(sc_pid pid, bool close) {
    assert(pid == owned_process() && process_owned && !pipe_owned);
    assert(!close && !process_waited);
    observe_effect(EFFECT_WAIT);

    if (current_case == QUERY_CANCEL_DURING_WAIT) {
        sc_intr_interrupt(current_interrupt);
    }

    process_waited = true;
    return current_case == QUERY_NONZERO_EXIT ? 7 : 0;
}

void
sc_process_close(sc_pid pid) {
    assert(pid == owned_process() && process_owned && process_waited);
    assert(current_interrupt->process == SC_PROCESS_NONE);
    process_owned = false;
    ++process_releases;
    observe_effect(EFFECT_PROCESS_CLOSE);
}

/* Real interrupt cleanup also references the socket branch; no socket exists. */
bool
net_interrupt(sc_socket socket) {
    (void) socket;
    assert(false);
    return false;
}

static void
test_query(enum query_case scenario) {
    current_case = scenario;
    effect_count = 0;
    process_terminations = 0;
    process_releases = 0;
    pipe_releases = 0;
    process_waited = false;
    struct sc_intr interrupt;
    bool initialized = sc_intr_init(&interrupt);
    assert(initialized);
    current_interrupt = &interrupt;

    if (scenario == QUERY_PRE_CANCELLED) {
        sc_intr_interrupt(&interrupt);
    }

    const char *const argv[] = {"synthetic-adb-path", "synthetic-query", NULL};
    char output[8];
    memset(output, 'X', sizeof(output));
    bool success = sc_adb_query(&interrupt, argv, output, sizeof(output));
    assert(success == (scenario == QUERY_SUCCESS));
    assert(!process_owned && !pipe_owned);
    assert(interrupt.process == SC_PROCESS_NONE);
    assert(interrupt.socket == SC_SOCKET_NONE);

    if (scenario == QUERY_SPAWN_FAILURE || scenario == QUERY_PRE_CANCELLED) {
        assert(effect_count == (scenario == QUERY_SPAWN_FAILURE ? 1u : 0u));

        if (scenario == QUERY_SPAWN_FAILURE) {
            assert(effects[0] == EFFECT_SPAWN);
        }

        assert(!process_releases && !pipe_releases && !process_terminations);
        assert(output[0] == (scenario == QUERY_PRE_CANCELLED ? '\0' : 'X'));
    } else {
        assert(process_releases == 1 && pipe_releases == 1);
        bool terminated = scenario == QUERY_TRUNCATED || scenario == QUERY_READ_ERROR
            || scenario == QUERY_CANCEL_AFTER_SPAWN || scenario == QUERY_CANCEL_DURING_READ
            || scenario == QUERY_CANCEL_DURING_WAIT;
        assert(process_terminations == (unsigned) terminated);
        assert(effects[0] == EFFECT_SPAWN);
        assert(effects[effect_count - 1] == EFFECT_PROCESS_CLOSE);

        if (scenario == QUERY_SUCCESS) {
            assert(!strcmp(output, "ready"));
        } else {
            assert(output[0] == '\0');
        }

        if (scenario == QUERY_CANCEL_AFTER_SPAWN) {
            assert(effect_count == 5);
            assert(effects[1] == EFFECT_TERMINATE);
        }

        if (scenario == QUERY_TRUNCATED || scenario == QUERY_READ_ERROR) {
            assert(effect_count == 6);
            assert(effects[1] == EFFECT_READ);
            assert(effects[2] == EFFECT_TERMINATE);
            assert(effects[3] == EFFECT_PIPE_CLOSE);
            assert(effects[4] == EFFECT_WAIT);
        }
    }

    sc_intr_destroy(&interrupt);
    current_interrupt = NULL;
}

static void
test_ordinary_effect_registration(void) {
    current_case = QUERY_CANCEL_AFTER_SPAWN;
    effect_count = process_terminations = process_releases = pipe_releases = 0;
    process_waited = false;
    struct sc_intr interrupt;
    bool initialized = sc_intr_init(&interrupt);
    assert(initialized);
    current_interrupt = &interrupt;
    const char *const argv[] = {"synthetic-adb-path", "synthetic-query", NULL};
    sc_pipe pipe;
    sc_pid pid = sc_adb_execute_p(argv, SC_ADB_NO_STDERR, &pipe);
    assert(pid == owned_process());
    sc_pipe_close(pipe); // Fixture owns this pipe; ordinary helper owns the child.
    bool success = process_check_success_intr(&interrupt, pid, "synthetic-query", SC_ADB_SILENT);
    assert(!success);
    assert(!process_owned && !pipe_owned);
    assert(process_terminations == 1 && process_releases == 1 && pipe_releases == 1);
    assert(interrupt.process == SC_PROCESS_NONE);
    sc_intr_destroy(&interrupt);
    current_interrupt = NULL;
}

static void
test_ordinary_pre_cancelled(void) {
    effect_count = process_terminations = process_releases = pipe_releases = 0;
    struct sc_intr interrupt;
    bool initialized = sc_intr_init(&interrupt);
    assert(initialized);
    current_interrupt = &interrupt;
    sc_intr_interrupt(&interrupt);
    bool success = sc_adb_start_server(&interrupt, SC_ADB_SILENT);
    assert(!success);
    assert(!effect_count && !process_owned && !pipe_owned);
    assert(!process_terminations && !process_releases && !pipe_releases);
    sc_intr_destroy(&interrupt);
    current_interrupt = NULL;
}

static void
test_ordinary_missing_process(void) {
    effect_count = process_terminations = process_releases = pipe_releases = 0;
    struct sc_intr interrupt;
    bool initialized = sc_intr_init(&interrupt);
    assert(initialized);
    current_interrupt = &interrupt;
    bool success = process_check_success_intr(&interrupt, SC_PROCESS_NONE,
                                              "synthetic-never-spawned", SC_ADB_SILENT);
    assert(!success);
    assert(!effect_count && !process_owned && !pipe_owned);
    sc_intr_destroy(&interrupt);
    current_interrupt = NULL;
}

static void
test_adb_reference_scope(void) {
    bool first = sc_adb_init();
    assert(first);
    const char *executable = sc_adb_get_executable();
    assert(executable && !strcmp(executable, "synthetic-adb-path"));
    bool second = sc_adb_init();
    assert(second);
    assert(environment_reads == 1);
    sc_adb_destroy();
    assert(!executable_releases);
    assert(sc_adb_get_executable() == executable);
    sc_adb_destroy();
    assert(executable_releases == 1);
    assert(!sc_adb_get_executable() && !owned_executable);
}

int
main(int argc, char *argv[]) {
    if (argc == 2 && !strcmp(argv[1], "--ordinary-registration")) {
        test_ordinary_effect_registration();
        return 0;
    }

    for (unsigned scenario = QUERY_SUCCESS; scenario <= QUERY_SPAWN_FAILURE; ++scenario) {
        test_query((enum query_case) scenario);
    }

    test_adb_reference_scope();
    test_ordinary_effect_registration();
    test_ordinary_pre_cancelled();
    test_ordinary_missing_process();
    puts("ADB lifetime: nine query, three ordinary-effect cases and one nested reference scope");
    return 0;
}
