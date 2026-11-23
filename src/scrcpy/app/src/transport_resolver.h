#ifndef SC_TRANSPORT_RESOLVER_H
#define SC_TRANSPORT_RESOLVER_H

#include "common.h"

#include <stdatomic.h>
#include "connection_plan.h"
#include "util/intr.h"
#include "util/thread.h"

#define SC_TRANSPORT_IDENTITY_MAX (SC_CONNECTION_PLAN_ENDPOINT_CAPACITY - 1)
#define SC_TRANSPORT_QUERY_BYTES 65536

struct sc_transport_result {
    sc_connection_attempt attempt;
    enum sc_connection_failure failure;
    char serial[SC_TRANSPORT_IDENTITY_MAX + 1];
};

/* One retained operation. Cancel never substitutes for join/take. */
struct sc_transport_resolver {
    struct sc_intr intr;
    sc_thread thread;
    struct sc_connection_target candidate;
    struct sc_transport_result result;
    atomic_bool completed;
    bool started;
};

bool
sc_transport_resolver_start(struct sc_transport_resolver *resolver,
                             sc_connection_attempt attempt,
                             const struct sc_connection_target *candidate);

void
sc_transport_resolver_cancel(struct sc_transport_resolver *resolver);

/* Nonblocking until completion; then joins the completed publisher and releases
 * the interrupt context before transferring a value-only result. */
bool
sc_transport_resolver_take(struct sc_transport_resolver *resolver,
                            struct sc_transport_result *result);

/* Exact connection-service match; pairing services and ambiguous rows reject. */
bool
sc_transport_parse_mdns(const char *output, const char *service,
                         char endpoint[SC_TRANSPORT_IDENTITY_MAX + 1]);

#endif
