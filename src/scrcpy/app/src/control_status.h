#ifndef SC_CONTROL_STATUS_H
#define SC_CONTROL_STATUS_H

/* Per-callback values: sender and receiver may finish concurrently. */
enum sc_control_status {
    SC_CONTROL_STATUS_STOPPED,
    SC_CONTROL_STATUS_TRANSPORT_LOST,
    SC_CONTROL_STATUS_PROTOCOL_ERROR,
    SC_CONTROL_STATUS_LOCAL_ERROR,
};

#endif
