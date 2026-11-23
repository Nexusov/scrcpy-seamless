#ifndef SC_CONNECTION_LAUNCH_PLAN_H
#define SC_CONNECTION_LAUNCH_PLAN_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>

#include "connection_manager.h"

#define SC_CONNECTION_PLAN_MAX_BYTES 4096
#define SC_CONNECTION_PLAN_ID_CAPACITY 257
#define SC_CONNECTION_PLAN_ENDPOINT_CAPACITY 260
#define SC_CONNECTION_PLAN_MAX_ATTEMPTS 100
#define SC_CONNECTION_PLAN_MAX_DELAY_US UINT64_C(86400000000)

/* Owned invocation storage; no argv/JSON/session pointers survive parsing. */
struct sc_connection_target {
    enum sc_connection_transport transport;
    char serial[SC_CONNECTION_PLAN_ENDPOINT_CAPACITY];
    char service[SC_CONNECTION_PLAN_ID_CAPACITY];
    char endpoint[SC_CONNECTION_PLAN_ENDPOINT_CAPACITY];
};

struct sc_connection_launch_plan {
    struct sc_connection_plan policy;
    struct sc_connection_target targets[SC_CONNECTION_MAX_CANDIDATES];
    bool reconnect;
    uint8_t required_capabilities;
    uint8_t desired_capabilities;
};

/* Strict machine-bootstrap JSON v1, separate from the unchanged IPC codec. */
bool
sc_connection_launch_plan_parse(const char *json,
                                 struct sc_connection_launch_plan *plan);

bool
sc_connection_endpoint_valid(const char *endpoint);

#endif
