#ifndef SC_CONNECTION_MANAGER_H
#define SC_CONNECTION_MANAGER_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>

#include "util/tick.h"

#define SC_CONNECTION_MAX_CANDIDATES 2
#define SC_CONNECTION_STABLE_INTERVAL SC_TICK_FROM_SEC(8)

typedef uint64_t sc_connection_attempt;

enum sc_connection_transport {
    SC_CONNECTION_USB,
    SC_CONNECTION_NETWORK,
};

enum sc_connection_failure {
    SC_CONNECTION_FAILURE_NONE,
    SC_CONNECTION_FAILURE_TRANSPORT_LOST,
    SC_CONNECTION_FAILURE_TRANSIENT_CONNECT,
    SC_CONNECTION_FAILURE_SERVER_START,
    SC_CONNECTION_FAILURE_INVALID_CONFIGURATION,
    SC_CONNECTION_FAILURE_PROTOCOL,
    SC_CONNECTION_FAILURE_DECODER,
    SC_CONNECTION_FAILURE_CONTROLLER,
    SC_CONNECTION_FAILURE_RECORDING,
    SC_CONNECTION_FAILURE_USER_STOP,
    SC_CONNECTION_FAILURE_WINDOW_CLOSED,
    SC_CONNECTION_FAILURE_DEADLINE,
    SC_CONNECTION_FAILURE_APPLICATION_SHUTDOWN,
    SC_CONNECTION_FAILURE_RETRY_EXHAUSTED,
    SC_CONNECTION_FAILURE_ATTEMPT_EXHAUSTED,
};

/* Values are captured by copy; target strings belong to the resolver's plan. */
struct sc_connection_plan {
    enum sc_connection_transport candidates[SC_CONNECTION_MAX_CANDIDATES];
    unsigned candidate_count;
    unsigned selected_candidate;
    bool retry_enabled;
    bool allow_fallback;
    unsigned maximum_attempts; // Includes the first attempt of an episode.
    sc_tick initial_backoff;
    sc_tick maximum_backoff;
    sc_tick time_limit; // One invocation deadline; zero disables it.
};

enum sc_connection_state {
    SC_CONNECTION_RESOLVE_PENDING,
    SC_CONNECTION_RESOLVING,
    SC_CONNECTION_START_PENDING,
    SC_CONNECTION_STARTING,
    SC_CONNECTION_ACTIVE,
    SC_CONNECTION_RETIRE_PENDING,
    SC_CONNECTION_RETIRING,
    SC_CONNECTION_WAITING,
    SC_CONNECTION_FINISHED,
};

enum sc_connection_action_type {
    SC_CONNECTION_ACTION_RESOLVE,
    SC_CONNECTION_ACTION_START,
    SC_CONNECTION_ACTION_RETIRE,
    SC_CONNECTION_ACTION_WAIT,
    SC_CONNECTION_ACTION_FINISH,
};

struct sc_connection_action {
    enum sc_connection_action_type type;
    sc_connection_attempt attempt;
    unsigned candidate;
    sc_tick wake_at; // INT64_MAX means no earlier scheduled observation.
    enum sc_connection_failure failure;
    enum sc_connection_failure last_failure; // Cause retained across budget exhaustion.
};

/* Single application-thread owner; no I/O, allocation or session pointers. */
struct sc_connection_manager {
    struct sc_connection_plan plan;
    enum sc_connection_state state;
    enum sc_connection_failure failure;
    enum sc_connection_failure last_failure;
    sc_connection_attempt attempt;
    unsigned candidate;
    unsigned next_candidate;
    unsigned episode_attempts;
    sc_tick last_now;
    sc_tick deadline;
    sc_tick retry_at;
    sc_tick active_since;
    bool terminal;
    bool deadline_enabled;
    bool stable_reset;
};

bool
sc_connection_manager_init(struct sc_connection_manager *manager,
                            const struct sc_connection_plan *plan, sc_tick now);

/* Emitted effects occur once. WAIT/FINISH may be observed repeatedly. */
struct sc_connection_action
sc_connection_manager_poll(struct sc_connection_manager *manager, sc_tick now);

/* Report only after the resolver's owned operation has actually settled. */
bool
sc_connection_manager_resolved(struct sc_connection_manager *manager,
                                sc_connection_attempt attempt,
                                enum sc_connection_failure failure, sc_tick now);

bool
sc_connection_manager_connected(struct sc_connection_manager *manager,
                                 sc_connection_attempt attempt, sc_tick now);

/* START admits an owner; failures thereafter require a RETIRE receipt. */
bool
sc_connection_manager_failed(struct sc_connection_manager *manager,
                              sc_connection_attempt attempt,
                              enum sc_connection_failure failure, sc_tick now);

/* Receipt means all work for that exact attempt has settled, not cancellation. */
bool
sc_connection_manager_retired(struct sc_connection_manager *manager,
                               sc_connection_attempt attempt, sc_tick now);

/* First terminal outcome wins; Stop never admits another attempt. */
void
sc_connection_manager_stop(struct sc_connection_manager *manager,
                            enum sc_connection_failure reason, sc_tick now);

enum sc_connection_state
sc_connection_manager_get_state(const struct sc_connection_manager *manager);

/* Includes deadline, retry and stable-health observations; no wall clock. */
sc_tick
sc_connection_manager_next_deadline(const struct sc_connection_manager *manager);

#endif
