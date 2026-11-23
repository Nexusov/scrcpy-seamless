#include "connection_manager.h"

#include <assert.h>
#include <limits.h>
#include <stddef.h>

static sc_tick
sc_connection_add(sc_tick left, sc_tick right) {
    assert(left >= 0 && right >= 0);
    return left > INT64_MAX - right ? INT64_MAX : left + right;
}

static void
sc_connection_observe_time(struct sc_connection_manager *manager, sc_tick now) {
    assert(now >= manager->last_now);
    manager->last_now = now;
}

bool
sc_connection_manager_init(struct sc_connection_manager *manager,
                            const struct sc_connection_plan *plan, sc_tick now) {
    if (now < 0 || !plan->candidate_count
            || plan->candidate_count > SC_CONNECTION_MAX_CANDIDATES
            || plan->selected_candidate >= plan->candidate_count
            || !plan->maximum_attempts || plan->initial_backoff < 0
            || plan->maximum_backoff < plan->initial_backoff
            || plan->time_limit < 0) {
        return false;
    }

    for (unsigned index = 0; index < plan->candidate_count; ++index) {
        if (plan->candidates[index] != SC_CONNECTION_USB
                && plan->candidates[index] != SC_CONNECTION_NETWORK) {
            return false;
        }
    }

    *manager = (struct sc_connection_manager) {
        .plan = *plan,
        .state = SC_CONNECTION_RESOLVE_PENDING,
        .candidate = plan->selected_candidate,
        .next_candidate = plan->selected_candidate,
        .last_now = now,
        .deadline = sc_connection_add(now, plan->time_limit),
        .deadline_enabled = plan->time_limit != 0,
    };
    return true;
}

static bool
sc_connection_has_effect(enum sc_connection_state state) {
    return state == SC_CONNECTION_RESOLVING || state == SC_CONNECTION_STARTING
        || state == SC_CONNECTION_ACTIVE;
}

void
sc_connection_manager_stop(struct sc_connection_manager *manager,
                            enum sc_connection_failure reason, sc_tick now) {
    sc_connection_observe_time(manager, now);
    assert(reason != SC_CONNECTION_FAILURE_NONE);

    if (manager->terminal) {
        return;
    }

    manager->terminal = true;
    manager->failure = reason;

    if (sc_connection_has_effect(manager->state)) {
        manager->state = SC_CONNECTION_RETIRE_PENDING;
        return;
    }

    if (manager->state != SC_CONNECTION_RETIRE_PENDING
            && manager->state != SC_CONNECTION_RETIRING) {
        manager->state = SC_CONNECTION_FINISHED;
    }
}

static void
sc_connection_update(struct sc_connection_manager *manager, sc_tick now) {
    sc_connection_observe_time(manager, now);

    if (manager->deadline_enabled && now >= manager->deadline) {
        sc_connection_manager_stop(manager, SC_CONNECTION_FAILURE_DEADLINE, now);
    }

    if (!manager->terminal && manager->state == SC_CONNECTION_ACTIVE
            && !manager->stable_reset
            && now - manager->active_since >= SC_CONNECTION_STABLE_INTERVAL) {
        manager->episode_attempts = 0;
        manager->stable_reset = true;
    }
}

static sc_tick
sc_connection_backoff(const struct sc_connection_manager *manager) {
    sc_tick delay = manager->plan.initial_backoff;
    unsigned exponent = manager->episode_attempts > 0
        ? manager->episode_attempts - 1 : 0;

    while (exponent && delay < manager->plan.maximum_backoff) {
        if (delay > manager->plan.maximum_backoff / 2) {
            return manager->plan.maximum_backoff;
        }

        delay *= 2;
        --exponent;

        if (!delay) {
            break;
        }
    }

    return delay;
}

static bool
sc_connection_can_retry(const struct sc_connection_manager *manager,
                         enum sc_connection_failure failure) {
    if (!manager->plan.retry_enabled) {
        return false;
    }

    if (failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT) {
        return true;
    }

    if (failure != SC_CONNECTION_FAILURE_TRANSPORT_LOST) {
        return false;
    }

    return manager->plan.candidates[manager->candidate] == SC_CONNECTION_NETWORK
        || (manager->plan.allow_fallback && manager->plan.candidate_count > 1);
}

static void
sc_connection_schedule_failure(struct sc_connection_manager *manager,
                                enum sc_connection_failure failure,
                                bool owner_live, sc_tick now) {
    manager->failure = failure;
    manager->last_failure = failure;
    bool retry = sc_connection_can_retry(manager, failure);

    if (!retry || manager->episode_attempts >= manager->plan.maximum_attempts) {
        manager->terminal = true;

        if (retry) {
            manager->failure = SC_CONNECTION_FAILURE_RETRY_EXHAUSTED;
        }
    }

    manager->next_candidate = manager->candidate;

    if (manager->plan.allow_fallback && manager->plan.candidate_count > 1) {
        manager->next_candidate = (manager->candidate + 1)
            % manager->plan.candidate_count;
    }

    manager->retry_at = sc_connection_add(now, sc_connection_backoff(manager));
    manager->state = owner_live ? SC_CONNECTION_RETIRE_PENDING
        : manager->terminal ? SC_CONNECTION_FINISHED : SC_CONNECTION_WAITING;
}

sc_tick
sc_connection_manager_next_deadline(const struct sc_connection_manager *manager) {
    sc_tick deadline = manager->deadline_enabled && !manager->terminal
        ? manager->deadline : INT64_MAX;

    if (manager->state == SC_CONNECTION_WAITING && !manager->terminal) {
        deadline = MIN(deadline, manager->retry_at);
    }

    if (manager->state == SC_CONNECTION_ACTIVE && !manager->stable_reset
            && !manager->terminal) {
        deadline = MIN(deadline, sc_connection_add(manager->active_since,
                                                   SC_CONNECTION_STABLE_INTERVAL));
    }

    return deadline;
}

struct sc_connection_action
sc_connection_manager_poll(struct sc_connection_manager *manager, sc_tick now) {
    sc_connection_update(manager, now);

    if (manager->state == SC_CONNECTION_WAITING && now >= manager->retry_at) {
        manager->state = SC_CONNECTION_RESOLVE_PENDING;
        manager->candidate = manager->next_candidate;
    }

    struct sc_connection_action action = {
        .type = SC_CONNECTION_ACTION_WAIT,
        .attempt = manager->attempt,
        .candidate = manager->candidate,
        .wake_at = sc_connection_manager_next_deadline(manager),
        .failure = manager->failure,
        .last_failure = manager->last_failure,
    };

    switch (manager->state) {
        case SC_CONNECTION_RESOLVE_PENDING:
            if (manager->attempt == UINT64_MAX) {
                manager->terminal = true;
                manager->failure = SC_CONNECTION_FAILURE_ATTEMPT_EXHAUSTED;
                manager->state = SC_CONNECTION_FINISHED;
                action.type = SC_CONNECTION_ACTION_FINISH;
                action.failure = manager->failure;
                break;
            }

            ++manager->attempt;
            ++manager->episode_attempts;
            manager->state = SC_CONNECTION_RESOLVING;
            action.type = SC_CONNECTION_ACTION_RESOLVE;
            action.attempt = manager->attempt;
            break;
        case SC_CONNECTION_START_PENDING:
            manager->state = SC_CONNECTION_STARTING;
            action.type = SC_CONNECTION_ACTION_START;
            break;
        case SC_CONNECTION_RETIRE_PENDING:
            manager->state = SC_CONNECTION_RETIRING;
            action.type = SC_CONNECTION_ACTION_RETIRE;
            break;
        case SC_CONNECTION_FINISHED:
            action.type = SC_CONNECTION_ACTION_FINISH;
            break;
        default:
            break;
    }

    return action;
}

bool
sc_connection_manager_resolved(struct sc_connection_manager *manager,
                                sc_connection_attempt attempt,
                                enum sc_connection_failure failure, sc_tick now) {
    sc_connection_update(manager, now);

    if (attempt != manager->attempt || manager->state != SC_CONNECTION_RESOLVING) {
        return false;
    }

    if (failure != SC_CONNECTION_FAILURE_NONE) {
        sc_connection_schedule_failure(manager, failure, false, now);
        return true;
    }

    manager->state = SC_CONNECTION_START_PENDING;
    return true;
}

bool
sc_connection_manager_connected(struct sc_connection_manager *manager,
                                 sc_connection_attempt attempt, sc_tick now) {
    sc_connection_update(manager, now);

    if (attempt != manager->attempt || manager->state != SC_CONNECTION_STARTING) {
        return false;
    }

    manager->state = SC_CONNECTION_ACTIVE;
    manager->active_since = now;
    manager->stable_reset = false;
    return true;
}

bool
sc_connection_manager_failed(struct sc_connection_manager *manager,
                              sc_connection_attempt attempt,
                              enum sc_connection_failure failure, sc_tick now) {
    sc_connection_update(manager, now);

    if (attempt != manager->attempt || (manager->state != SC_CONNECTION_STARTING
            && manager->state != SC_CONNECTION_ACTIVE)
            || failure == SC_CONNECTION_FAILURE_NONE) {
        return false;
    }

    sc_connection_schedule_failure(manager, failure, true, now);
    return true;
}

bool
sc_connection_manager_retired(struct sc_connection_manager *manager,
                               sc_connection_attempt attempt, sc_tick now) {
    sc_connection_update(manager, now);

    if (attempt != manager->attempt || manager->state != SC_CONNECTION_RETIRING) {
        return false;
    }

    manager->state = manager->terminal ? SC_CONNECTION_FINISHED
        : SC_CONNECTION_WAITING;
    return true;
}

enum sc_connection_state
sc_connection_manager_get_state(const struct sc_connection_manager *manager) {
    return manager->state;
}
