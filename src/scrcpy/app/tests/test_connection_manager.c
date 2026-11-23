#include "common.h"

#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>

#include "connection_manager.h"

static struct sc_connection_plan
test_plan(void) {
    return (struct sc_connection_plan) {
        .candidates = {SC_CONNECTION_USB, SC_CONNECTION_NETWORK},
        .candidate_count = 2,
        .retry_enabled = true,
        .allow_fallback = true,
        .maximum_attempts = 3,
        .initial_backoff = SC_TICK_FROM_SEC(1),
        .maximum_backoff = SC_TICK_FROM_SEC(8),
    };
}

static struct sc_connection_manager
test_manager(struct sc_connection_plan plan, sc_tick now) {
    struct sc_connection_manager manager;
    bool initialized = sc_connection_manager_init(&manager, &plan, now);
    assert(initialized);
    return manager;
}

static struct sc_connection_action
test_poll(struct sc_connection_manager *manager, sc_tick now,
           enum sc_connection_action_type expected) {
    struct sc_connection_action action = sc_connection_manager_poll(manager, now);
    assert(action.type == expected);
    return action;
}

static sc_connection_attempt
test_start(struct sc_connection_manager *manager, sc_tick now) {
    struct sc_connection_action resolve = test_poll(manager, now,
                                                     SC_CONNECTION_ACTION_RESOLVE);
    bool resolved = sc_connection_manager_resolved(manager, resolve.attempt,
                                                   SC_CONNECTION_FAILURE_NONE, now);
    assert(resolved);
    struct sc_connection_action start = test_poll(manager, now,
                                                   SC_CONNECTION_ACTION_START);
    assert(start.attempt == resolve.attempt);
    return start.attempt;
}

static void
test_connect(struct sc_connection_manager *manager, sc_connection_attempt attempt,
              sc_tick now) {
    bool connected = sc_connection_manager_connected(manager, attempt, now);
    assert(connected);
}

static void
test_fail(struct sc_connection_manager *manager, sc_connection_attempt attempt,
           enum sc_connection_failure failure, sc_tick now) {
    bool failed = sc_connection_manager_failed(manager, attempt, failure, now);
    assert(failed);
}

static void
test_retire(struct sc_connection_manager *manager, sc_connection_attempt attempt,
             sc_tick now) {
    struct sc_connection_action retire = test_poll(manager, now,
                                                    SC_CONNECTION_ACTION_RETIRE);
    assert(retire.attempt == attempt);
    test_poll(manager, now, SC_CONNECTION_ACTION_WAIT);
    bool retired = sc_connection_manager_retired(manager, attempt, now);
    assert(retired);
}

static void
test_validation_and_capture(void) {
    struct sc_connection_plan plan = test_plan();
    struct sc_connection_manager manager;
    struct sc_connection_manager original;
    memset(&manager, 0x37, sizeof(manager));
    original = manager;
    plan.maximum_attempts = 0;
    bool valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid && !memcmp(&manager, &original, sizeof(manager)));
    plan = test_plan();
    plan.candidate_count = SC_CONNECTION_MAX_CANDIDATES + 1;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    plan.selected_candidate = plan.candidate_count;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    plan.candidates[0] = (enum sc_connection_transport) 42;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    plan.initial_backoff = -1;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    plan.maximum_backoff = plan.initial_backoff - 1;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    plan.time_limit = -1;
    valid = sc_connection_manager_init(&manager, &plan, 0);
    assert(!valid);
    plan = test_plan();
    valid = sc_connection_manager_init(&manager, &plan, -1);
    assert(!valid);
    plan = test_plan();
    manager = test_manager(plan, 0);
    plan.allow_fallback = false;
    plan.maximum_attempts = 1;
    assert(manager.plan.allow_fallback && manager.plan.maximum_attempts == 3);
}

static void
test_order_and_exact_retirement(void) {
    struct sc_connection_manager manager = test_manager(test_plan(), 0);
    sc_connection_attempt first = test_start(&manager, 0);
    test_poll(&manager, 0, SC_CONNECTION_ACTION_WAIT);
    test_connect(&manager, first, 0);
    assert(sc_connection_manager_get_state(&manager) == SC_CONNECTION_ACTIVE);
    test_fail(&manager, first, SC_CONNECTION_FAILURE_TRANSPORT_LOST, 1);
    bool duplicate = sc_connection_manager_failed(&manager, first,
                           SC_CONNECTION_FAILURE_DECODER, 1);
    assert(!duplicate);
    struct sc_connection_action retire = test_poll(&manager, 1,
                                                     SC_CONNECTION_ACTION_RETIRE);
    assert(retire.candidate == 0);
    test_poll(&manager, SC_TICK_FROM_SEC(20), SC_CONNECTION_ACTION_WAIT);
    bool stale = sc_connection_manager_retired(&manager, first + 1,
                                                SC_TICK_FROM_SEC(20));
    assert(!stale);
    bool retired = sc_connection_manager_retired(&manager, first,
                                                  SC_TICK_FROM_SEC(20));
    assert(retired);
    duplicate = sc_connection_manager_retired(&manager, first,
                                                SC_TICK_FROM_SEC(20));
    assert(!duplicate);
    struct sc_connection_action resolve = test_poll(&manager, SC_TICK_FROM_SEC(20),
                                                      SC_CONNECTION_ACTION_RESOLVE);
    assert(resolve.candidate == 1 && resolve.attempt == first + 1);
    stale = sc_connection_manager_resolved(&manager, first,
                         SC_CONNECTION_FAILURE_NONE, SC_TICK_FROM_SEC(20));
    assert(!stale);
    bool resolved = sc_connection_manager_resolved(&manager, resolve.attempt,
                         SC_CONNECTION_FAILURE_NONE, SC_TICK_FROM_SEC(20));
    assert(resolved);
    duplicate = sc_connection_manager_resolved(&manager, resolve.attempt,
                         SC_CONNECTION_FAILURE_NONE, SC_TICK_FROM_SEC(20));
    assert(!duplicate);
    test_poll(&manager, SC_TICK_FROM_SEC(20), SC_CONNECTION_ACTION_START);
    stale = sc_connection_manager_connected(&manager, first, SC_TICK_FROM_SEC(20));
    assert(!stale);
    test_connect(&manager, resolve.attempt, SC_TICK_FROM_SEC(20));
    duplicate = sc_connection_manager_connected(&manager, resolve.attempt,
                                                  SC_TICK_FROM_SEC(20));
    assert(!duplicate);
}

static void
test_backoff_and_budget(void) {
    struct sc_connection_plan plan = test_plan();
    plan.selected_candidate = 1;
    plan.allow_fallback = false;
    struct sc_connection_manager manager = test_manager(plan, 0);
    sc_tick now = 0;

    for (unsigned index = 0; index < plan.maximum_attempts; ++index) {
        sc_connection_attempt attempt = test_start(&manager, now);
        assert(manager.candidate == 1);
        test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, now);
        test_retire(&manager, attempt, now);

        if (index + 1 == plan.maximum_attempts) {
            struct sc_connection_action finish = test_poll(&manager, now,
                                                      SC_CONNECTION_ACTION_FINISH);
            assert(finish.failure == SC_CONNECTION_FAILURE_RETRY_EXHAUSTED);
            assert(finish.last_failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
            break;
        }

        sc_tick delay = plan.initial_backoff << index;
        assert(sc_connection_manager_next_deadline(&manager) == now + delay);
        test_poll(&manager, now + delay - 1, SC_CONNECTION_ACTION_WAIT);
        now += delay;
    }

    assert(manager.attempt == plan.maximum_attempts);
    plan.maximum_attempts = 1;
    manager = test_manager(plan, 0);
    struct sc_connection_action resolve = test_poll(&manager, 0,
                                                       SC_CONNECTION_ACTION_RESOLVE);
    bool resolved = sc_connection_manager_resolved(&manager, resolve.attempt,
                                          SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, 0);
    assert(resolved);
    test_poll(&manager, 0, SC_CONNECTION_ACTION_FINISH);
}

static void
test_switch_permission_and_usb_transience(void) {
    struct sc_connection_plan plan = test_plan();
    plan.allow_fallback = false;
    struct sc_connection_manager manager = test_manager(plan, 0);
    sc_connection_attempt attempt = test_start(&manager, 0);
    test_connect(&manager, attempt, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSPORT_LOST, 0);
    test_retire(&manager, attempt, 0);
    struct sc_connection_action finish = test_poll(&manager, 0,
                                                     SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_TRANSPORT_LOST);

    manager = test_manager(plan, 0);
    attempt = test_start(&manager, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, 0);
    test_retire(&manager, attempt, 0);
    struct sc_connection_action retry = test_poll(&manager, plan.initial_backoff,
                                                     SC_CONNECTION_ACTION_RESOLVE);
    assert(retry.candidate == 0);

    plan.selected_candidate = 1;
    manager = test_manager(plan, 0);
    attempt = test_start(&manager, 0);
    test_connect(&manager, attempt, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSPORT_LOST, 0);
    test_retire(&manager, attempt, 0);
    retry = test_poll(&manager, plan.initial_backoff, SC_CONNECTION_ACTION_RESOLVE);
    assert(retry.candidate == 1);
}

static void
test_flapping_stability_and_no_failback(void) {
    struct sc_connection_plan plan = test_plan();
    plan.selected_candidate = 1;
    struct sc_connection_manager manager = test_manager(plan, 0);
    sc_connection_attempt attempt = test_start(&manager, 0);
    test_connect(&manager, attempt, 0);
    assert(sc_connection_manager_next_deadline(&manager)
            == SC_CONNECTION_STABLE_INTERVAL);
    test_poll(&manager, SC_CONNECTION_STABLE_INTERVAL - 1, SC_CONNECTION_ACTION_WAIT);
    assert(manager.episode_attempts == 1);
    test_poll(&manager, SC_CONNECTION_STABLE_INTERVAL, SC_CONNECTION_ACTION_WAIT);
    assert(!manager.episode_attempts && manager.candidate == 1);
    test_poll(&manager, SC_TICK_FROM_SEC(100), SC_CONNECTION_ACTION_WAIT);
    assert(manager.candidate == 1 && manager.attempt == attempt);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSPORT_LOST,
                 SC_TICK_FROM_SEC(100));
    test_retire(&manager, attempt, SC_TICK_FROM_SEC(100));
    sc_tick now = SC_TICK_FROM_SEC(101);

    for (unsigned index = 0; index < plan.maximum_attempts; ++index) {
        attempt = test_start(&manager, now);
        test_connect(&manager, attempt, now);
        ++now;
        test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, now);
        test_retire(&manager, attempt, now);

        if (index + 1 < plan.maximum_attempts) {
            now = sc_connection_manager_next_deadline(&manager);
        }
    }

    struct sc_connection_action finish = test_poll(&manager, now,
                                                      SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_RETRY_EXHAUSTED);
    assert(manager.attempt == 1 + plan.maximum_attempts);

    plan.retry_enabled = false;
    manager = test_manager(plan, 0);
    attempt = test_start(&manager, 0);
    test_connect(&manager, attempt, 0);
    test_poll(&manager, SC_CONNECTION_STABLE_INTERVAL, SC_CONNECTION_ACTION_WAIT);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSPORT_LOST,
                 SC_CONNECTION_STABLE_INTERVAL);
    test_retire(&manager, attempt, SC_CONNECTION_STABLE_INTERVAL);
    finish = test_poll(&manager, SC_CONNECTION_STABLE_INTERVAL,
                         SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_TRANSPORT_LOST);
    assert(manager.attempt == 1);
}

static void
test_stop_during_retry_and_retirement(void) {
    for (unsigned phase = 0; phase < 3; ++phase) {
        struct sc_connection_manager manager = test_manager(test_plan(), 0);
        sc_connection_attempt attempt = test_start(&manager, 0);
        test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, 1);

        if (phase) {
            test_poll(&manager, 1, SC_CONNECTION_ACTION_RETIRE);
        }

        if (phase == 2) {
            bool retired = sc_connection_manager_retired(&manager, attempt, 1);
            assert(retired);
        }

        sc_connection_manager_stop(&manager, SC_CONNECTION_FAILURE_USER_STOP, 2);

        if (!phase) {
            test_poll(&manager, 2, SC_CONNECTION_ACTION_RETIRE);
        }

        if (phase != 2) {
            bool retired = sc_connection_manager_retired(&manager, attempt, 2);
            assert(retired);
        }

        struct sc_connection_action finish = test_poll(&manager, SC_TICK_FROM_SEC(50),
                                                       SC_CONNECTION_ACTION_FINISH);
        assert(finish.failure == SC_CONNECTION_FAILURE_USER_STOP);
        assert(manager.attempt == 1);
    }
}

static void
test_sticky_stop_and_deadline(void) {
    for (unsigned stage = 0; stage < 5; ++stage) {
        struct sc_connection_manager manager = test_manager(test_plan(), 0);
        sc_connection_attempt attempt = 0;

        if (stage) {
            struct sc_connection_action resolve = test_poll(&manager, 0,
                                                         SC_CONNECTION_ACTION_RESOLVE);
            attempt = resolve.attempt;
        }

        if (stage > 1) {
            bool resolved = sc_connection_manager_resolved(&manager, attempt,
                                                    SC_CONNECTION_FAILURE_NONE, 0);
            assert(resolved);
        }

        if (stage > 2) {
            test_poll(&manager, 0, SC_CONNECTION_ACTION_START);
        }

        if (stage > 3) {
            test_connect(&manager, attempt, 0);
        }

        sc_connection_manager_stop(&manager, SC_CONNECTION_FAILURE_USER_STOP, 1);
        bool effect_live = stage == 1 || stage > 2;

        if (effect_live) {
            test_retire(&manager, attempt, 1);
        }

        struct sc_connection_action finish = test_poll(&manager, 1,
                                                       SC_CONNECTION_ACTION_FINISH);
        assert(finish.failure == SC_CONNECTION_FAILURE_USER_STOP);
        sc_connection_manager_stop(&manager, SC_CONNECTION_FAILURE_WINDOW_CLOSED, 2);
        finish = test_poll(&manager, 2, SC_CONNECTION_ACTION_FINISH);
        assert(finish.failure == SC_CONNECTION_FAILURE_USER_STOP);
        bool late = sc_connection_manager_connected(&manager, attempt, 2);
        assert(!late);
    }

    struct sc_connection_plan plan = test_plan();
    plan.time_limit = SC_TICK_FROM_SEC(2);
    struct sc_connection_manager manager = test_manager(plan, 0);
    sc_connection_attempt attempt = test_start(&manager, 0);
    test_connect(&manager, attempt, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT,
                 SC_TICK_FROM_SEC(1));
    test_retire(&manager, attempt, SC_TICK_FROM_SEC(1));
    test_poll(&manager, SC_TICK_FROM_SEC(2) - 1, SC_CONNECTION_ACTION_WAIT);
    struct sc_connection_action finish = test_poll(&manager, SC_TICK_FROM_SEC(2),
                                                     SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_DEADLINE && manager.attempt == 1);

    manager = test_manager(plan, 0);
    attempt = test_start(&manager, 0);
    test_poll(&manager, plan.time_limit, SC_CONNECTION_ACTION_RETIRE);
    test_poll(&manager, plan.time_limit + 1, SC_CONNECTION_ACTION_WAIT);
    bool retired = sc_connection_manager_retired(&manager, attempt,
                                                  plan.time_limit + 1);
    assert(retired);
    finish = test_poll(&manager, plan.time_limit + 1, SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_DEADLINE);
}

static void
test_terminal_failures(void) {
    static const enum sc_connection_failure fatal[] = {
        SC_CONNECTION_FAILURE_SERVER_START,
        SC_CONNECTION_FAILURE_INVALID_CONFIGURATION,
        SC_CONNECTION_FAILURE_PROTOCOL,
        SC_CONNECTION_FAILURE_DECODER,
        SC_CONNECTION_FAILURE_CONTROLLER,
        SC_CONNECTION_FAILURE_RECORDING,
    };

    for (unsigned index = 0; index < ARRAY_LEN(fatal); ++index) {
        struct sc_connection_manager manager = test_manager(test_plan(), 0);
        sc_connection_attempt attempt = test_start(&manager, 0);
        test_fail(&manager, attempt, fatal[index], 1);
        sc_connection_manager_stop(&manager, SC_CONNECTION_FAILURE_USER_STOP, 2);
        test_retire(&manager, attempt, 2);
        struct sc_connection_action finish = test_poll(&manager, 2,
                                                       SC_CONNECTION_ACTION_FINISH);
        assert(finish.failure == fatal[index]);
    }
}

static void
test_saturation_and_zero_delay(void) {
    struct sc_connection_plan plan = test_plan();
    plan.time_limit = 10;
    struct sc_connection_manager manager = test_manager(plan, INT64_MAX - 5);
    assert(sc_connection_manager_next_deadline(&manager) == INT64_MAX);
    test_poll(&manager, INT64_MAX - 1, SC_CONNECTION_ACTION_RESOLVE);
    test_poll(&manager, INT64_MAX, SC_CONNECTION_ACTION_RETIRE);

    plan.time_limit = 0;
    plan.initial_backoff = INT64_MAX / 2 + 1;
    plan.maximum_backoff = INT64_MAX;
    plan.maximum_attempts = 4;
    manager = test_manager(plan, 0);
    sc_connection_attempt attempt = test_start(&manager, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, 0);
    test_retire(&manager, attempt, 0);
    assert(sc_connection_manager_next_deadline(&manager) == plan.initial_backoff);
    attempt = test_start(&manager, plan.initial_backoff);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT,
                 plan.initial_backoff);
    test_retire(&manager, attempt, plan.initial_backoff);
    assert(sc_connection_manager_next_deadline(&manager) == INT64_MAX);

    plan = test_plan();
    plan.maximum_attempts = 6;
    manager = test_manager(plan, 0);
    sc_tick now = 0;

    for (unsigned index = 0; index < plan.maximum_attempts - 1; ++index) {
        attempt = test_start(&manager, now);
        test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, now);
        test_retire(&manager, attempt, now);
        sc_tick expected = SC_TICK_FROM_SEC(1) << MIN(index, 3);
        assert(sc_connection_manager_next_deadline(&manager) == now + expected);
        now += expected;
    }

    plan = test_plan();
    plan.initial_backoff = 0;
    plan.maximum_backoff = 0;
    manager = test_manager(plan, 0);
    attempt = test_start(&manager, 0);
    test_fail(&manager, attempt, SC_CONNECTION_FAILURE_TRANSIENT_CONNECT, 0);
    test_retire(&manager, attempt, 0);
    test_poll(&manager, 0, SC_CONNECTION_ACTION_RESOLVE);

    manager = test_manager(test_plan(), 0);
    manager.attempt = UINT64_MAX; // Exercise exhaustion without 2^64 effects.
    struct sc_connection_action finish = test_poll(&manager, 0,
                                                      SC_CONNECTION_ACTION_FINISH);
    assert(finish.failure == SC_CONNECTION_FAILURE_ATTEMPT_EXHAUSTED);
}

int
main(void) {
    test_validation_and_capture();
    test_order_and_exact_retirement();
    test_backoff_and_budget();
    test_switch_permission_and_usb_transience();
    test_flapping_stability_and_no_failback();
    test_stop_during_retry_and_retirement();
    test_sticky_stop_and_deadline();
    test_terminal_failures();
    test_saturation_and_zero_delay();
    puts("Connection manager: 9 fake-clock production policy groups passed");
    return 0;
}
