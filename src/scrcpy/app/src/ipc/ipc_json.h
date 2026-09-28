#ifndef SC_IPC_JSON_H
#define SC_IPC_JSON_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define SC_IPC_STRING_CAPACITY 257
#define SC_IPC_CAPABILITY_CAPACITY 65
#define SC_IPC_CAPABILITY_LIMIT 16

enum sc_ipc_message_type {
    SC_IPC_HELLO,
    SC_IPC_HELLO_RESULT,
    SC_IPC_COMMAND,
    SC_IPC_COMMAND_RESULT,
    SC_IPC_LIFECYCLE,
};

enum sc_ipc_json_status {
    SC_IPC_JSON_OK,
    SC_IPC_JSON_INVALID_LENGTH,
    SC_IPC_JSON_INVALID_JSON,
    SC_IPC_JSON_INVALID_MESSAGE,
    SC_IPC_JSON_UNSUPPORTED_MESSAGE,
    SC_IPC_JSON_NO_MEMORY,
};

struct sc_ipc_capabilities {
    char values[SC_IPC_CAPABILITY_LIMIT][SC_IPC_CAPABILITY_CAPACITY];
    size_t count;
};

struct sc_ipc_message {
    enum sc_ipc_message_type type;
    char product[SC_IPC_STRING_CAPACITY];
    uint16_t protocol_major;
    uint16_t protocol_minor;
    struct sc_ipc_capabilities required_capabilities;
    struct sc_ipc_capabilities supported_capabilities;
    struct sc_ipc_capabilities capabilities;
    char status[SC_IPC_STRING_CAPACITY];
    uint64_t request_id;
    char session_id[37];
    char command[SC_IPC_STRING_CAPACITY];
    uint64_t sequence;
    char utc[25];
    uint64_t monotonic_microseconds;
    char connection_attempt_id[37];
    bool connection_attempt_null;
    char subsystem[SC_IPC_STRING_CAPACITY];
    char event_type[SC_IPC_STRING_CAPACITY];
    char reason[SC_IPC_STRING_CAPACITY];
    char error[SC_IPC_STRING_CAPACITY];
};

/** Decodes and validates one bounded UTF-8 JSON payload. */
enum sc_ipc_json_status
sc_ipc_json_decode(const uint8_t *payload, size_t length,
                   struct sc_ipc_message *message);

/** Encodes one valid message with canonical field order. Caller frees output. */
enum sc_ipc_json_status
sc_ipc_json_encode(const struct sc_ipc_message *message,
                   uint8_t **payload, size_t *length);

/** Evaluates the pure v1 handshake; no process or session side effects. */
enum sc_ipc_json_status
sc_ipc_hello_evaluate(const struct sc_ipc_message *hello,
                      struct sc_ipc_message *result);

#endif
