#include "ipc_json.h"
#include "ipc_frame.h"
#include "../../vendor/yyjson/yyjson.h"

#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define SC_IPC_MAX_JSON_PROPERTIES 32
#define SC_IPC_MAX_JSON_MEMORY (32u * 1024u * 1024u)

static const char *const known_fields[] = {
    "messageType", "product", "protocolMajor", "protocolMinor",
    "requiredCapabilities", "supportedCapabilities", "capabilities",
    "status", "requestId", "sessionId", "command", "sequence", "utc",
    "monotonicMicroseconds", "connectionAttemptId", "subsystem",
    "eventType", "reason", "error",
};
static const char *const type_names[] = {
    "hello", "helloResult", "command", "commandResult", "lifecycle",
};
static const char *const type_fields[][11] = {
    [SC_IPC_HELLO] = {"messageType", "product", "protocolMajor",
        "protocolMinor", "requiredCapabilities", "supportedCapabilities"},
    [SC_IPC_HELLO_RESULT] = {"messageType", "product", "protocolMajor",
        "protocolMinor", "status", "capabilities"},
    [SC_IPC_COMMAND] = {"messageType", "requestId", "sessionId", "command"},
    [SC_IPC_COMMAND_RESULT] = {"messageType", "requestId", "sessionId",
        "command", "status"},
    [SC_IPC_LIFECYCLE] = {"messageType", "sequence", "utc",
        "monotonicMicroseconds", "sessionId", "connectionAttemptId",
        "subsystem", "eventType", "reason", "error"},
};
static const char *const hello_statuses[] = {
    "accepted", "productMismatch", "majorMismatch",
    "requiredCapabilityMissing",
};
static const char *const command_statuses[] = {
    "accepted", "applied", "unsupportedCommand", "invalidState",
    "noWindow", "failed",
};
static const char *const subsystems[] = {
    "protocol", "native", "connection", "video", "audio", "control",
};
static const char *const events[] = {
    "NativeReady", "Connecting", "StreamStarted", "TransportLost",
    "ReconnectScheduled", "Reconnecting", "StreamResumed",
    "CapabilityDegraded", "SessionStopped", "FatalError",
};
static const char *const attempt_events[] = {
    "Connecting", "StreamStarted", "TransportLost", "Reconnecting",
    "StreamResumed",
};
static const char *const reasons[] = {
    "none", "userStop", "windowClosed", "transportLost", "protocolError",
    "nativeFailure", "unknown",
};
static const char *const errors[] = {
    "none", "unknown", "invalidMessage", "unsupported", "internalFailure",
};

/** Checks a token against a fixed vocabulary. */
static bool
in_set(const char *value, const char *const *set, size_t count) {
    for (size_t index = 0; index < count; ++index) {
        if (set[index] && !strcmp(value, set[index])) {
            return true;
        }
    }
    return false;
}

#define IN_SET(value, set) \
    in_set((value), (set), sizeof(set) / sizeof((set)[0]))

/** Copies a JSON string only after length and NUL validation. */
static bool
copy_string(yyjson_val *value, char *target, size_t capacity) {
    if (!yyjson_is_str(value)) {
        return false;
    }
    size_t length = yyjson_get_len(value);
    const char *source = yyjson_get_str(value);
    if (!length || length >= capacity || memchr(source, '\0', length)) {
        return false;
    }
    memcpy(target, source, length);
    target[length] = '\0';
    return true;
}

/** Reads a required bounded string property. */
static bool
get_string(yyjson_val *root, const char *name, char *target, size_t capacity) {
    return copy_string(yyjson_obj_get(root, name), target, capacity);
}

/** Parses an exact uint64 decimal string. */
static bool
get_decimal(yyjson_val *root, const char *name, bool allow_zero,
            uint64_t *result) {
    yyjson_val *value = yyjson_obj_get(root, name);
    if (!yyjson_is_str(value)) {
        return false;
    }
    size_t length = yyjson_get_len(value);
    const char *digits = yyjson_get_str(value);
    if (!length || length > 20 || (length > 1 && digits[0] == '0')) {
        return false;
    }
    uint64_t number = 0;
    for (size_t index = 0; index < length; ++index) {
        unsigned char digit = (unsigned char) digits[index];
        if (digit < '0' || digit > '9') {
            return false;
        }
        uint64_t decimal = digit - '0';
        if (number > (UINT64_MAX - decimal) / 10) {
            return false;
        }
        number = number * 10 + decimal;
    }
    if (!allow_zero && !number) {
        return false;
    }
    *result = number;
    return true;
}

/** Reads a version field without floating-point conversion. */
static bool
get_version(yyjson_val *root, const char *name, uint16_t *version) {
    yyjson_val *value = yyjson_obj_get(root, name);
    if (!yyjson_is_uint(value) || yyjson_get_uint(value) > UINT16_MAX) {
        return false;
    }
    *version = (uint16_t) yyjson_get_uint(value);
    return true;
}

/** Validates a lowercase, nonzero GUID D string. */
static bool
valid_guid(const char *value) {
    if (strlen(value) != 36) {
        return false;
    }
    bool nonzero = false;
    for (size_t index = 0; index < 36; ++index) {
        char character = value[index];
        if (index == 8 || index == 13 || index == 18 || index == 23) {
            if (character != '-') {
                return false;
            }
        } else if ((character < '0' || character > '9') &&
                   (character < 'a' || character > 'f')) {
            return false;
        } else if (character != '0') {
            nonzero = true;
        }
    }
    return nonzero;
}

/** Reads one exact identity. */
static bool
get_guid(yyjson_val *root, const char *name, char target[37]) {
    return get_string(root, name, target, 37) && valid_guid(target);
}

/** Parses a fixed-width UTC timestamp component. */
static int
utc_part(const char *value, size_t start, size_t count) {
    int number = 0;
    for (size_t index = 0; index < count; ++index) {
        char digit = value[start + index];
        if (digit < '0' || digit > '9') {
            return -1;
        }
        number = number * 10 + digit - '0';
    }
    return number;
}

/** Checks exact UTC millisecond syntax and calendar ranges. */
static bool
valid_utc(const char *value) {
    if (strlen(value) != 24 || value[4] != '-' || value[7] != '-' ||
        value[10] != 'T' || value[13] != ':' || value[16] != ':' ||
        value[19] != '.' || value[23] != 'Z') {
        return false;
    }
    int year = utc_part(value, 0, 4);
    int month = utc_part(value, 5, 2);
    int day = utc_part(value, 8, 2);
    int hour = utc_part(value, 11, 2);
    int minute = utc_part(value, 14, 2);
    int second = utc_part(value, 17, 2);
    int millisecond = utc_part(value, 20, 3);
    static const int days[] = {0, 31, 28, 31, 30, 31, 30, 31,
                               31, 30, 31, 30, 31};
    if (year < 1 || month < 1 || month > 12 || hour < 0 || hour > 23 ||
        minute < 0 || minute > 59 || second < 0 || second > 59 ||
        millisecond < 0 || millisecond > 999) {
        return false;
    }
    int maximum = days[month];
    if (month == 2 && !(year % 4) && (year % 100 || !(year % 400))) {
        maximum = 29;
    }
    return day >= 1 && day <= maximum;
}

/** Reads a bounded set of distinct capability strings. */
static bool
get_capabilities(yyjson_val *root, const char *name,
                  struct sc_ipc_capabilities *capabilities) {
    yyjson_val *array = yyjson_obj_get(root, name);
    if (!yyjson_is_arr(array) || yyjson_arr_size(array) > SC_IPC_CAPABILITY_LIMIT) {
        return false;
    }
    size_t index, maximum;
    yyjson_val *value;
    yyjson_arr_foreach(array, index, maximum, value) {
        char *target = capabilities->values[capabilities->count];
        if (!copy_string(value, target, SC_IPC_CAPABILITY_CAPACITY)) {
            return false;
        }
        for (size_t previous = 0; previous < capabilities->count; ++previous) {
            if (!strcmp(target, capabilities->values[previous])) {
                return false;
            }
        }
        ++capabilities->count;
    }
    return true;
}

/** Checks that every required capability is also advertised by the caller. */
static bool
required_capabilities_are_supported(const struct sc_ipc_message *message) {
    for (size_t required = 0; required < message->required_capabilities.count;
         ++required) {
        bool advertised = false;
        for (size_t supported = 0;
             supported < message->supported_capabilities.count; ++supported) {
            if (!strcmp(message->required_capabilities.values[required],
                        message->supported_capabilities.values[supported])) {
                advertised = true;
                break;
            }
        }
        if (!advertised) {
            return false;
        }
    }
    return true;
}

/** Rejects duplicates and invalid known or unknown fields. */
static bool
check_fields(yyjson_val *root, enum sc_ipc_message_type type) {
    if (yyjson_obj_size(root) > SC_IPC_MAX_JSON_PROPERTIES) {
        return false;
    }
    const char *seen[SC_IPC_MAX_JSON_PROPERTIES];
    size_t seen_lengths[SC_IPC_MAX_JSON_PROPERTIES];
    size_t seen_count = 0;
    yyjson_obj_iter iterator = yyjson_obj_iter_with(root);
    yyjson_val *key;
    while ((key = yyjson_obj_iter_next(&iterator))) {
        size_t length = yyjson_get_len(key);
        const char *name = yyjson_get_str(key);
        if (!length || length > 256 || memchr(name, '\0', length)) {
            return false;
        }
        for (size_t previous = 0; previous < seen_count; ++previous) {
            if (length == seen_lengths[previous] &&
                !memcmp(name, seen[previous], length)) {
                return false;
            }
        }
        seen[seen_count] = name;
        seen_lengths[seen_count++] = length;

        bool known = IN_SET(name, known_fields);
        if (known && !IN_SET(name, type_fields[type])) {
            return false;
        }
        if (!known) {
            yyjson_val *value = yyjson_obj_iter_get_val(key);
            if (yyjson_is_arr(value) || yyjson_is_obj(value) ||
                (yyjson_is_str(value) &&
                 (yyjson_get_len(value) > 256 ||
                  memchr(yyjson_get_str(value), '\0', yyjson_get_len(value))))) {
                return false;
            }
        }
    }
    return true;
}

/** Reads a bounded vocabulary token. */
static bool
get_enum(yyjson_val *root, const char *name, char *target,
         const char *const *values, size_t count) {
    return get_string(root, name, target, SC_IPC_STRING_CAPACITY) &&
           in_set(target, values, count);
}

#define GET_ENUM(root, name, target, values) \
    get_enum((root), (name), (target), (values), \
             sizeof(values) / sizeof((values)[0]))

/** Reads shared request correlation fields. */
static bool
decode_request(yyjson_val *root, struct sc_ipc_message *message) {
    return get_decimal(root, "requestId", false, &message->request_id) &&
           get_guid(root, "sessionId", message->session_id) &&
           get_string(root, "command", message->command,
                      sizeof(message->command));
}

/** Decodes and validates one bounded UTF-8 JSON payload. */
enum sc_ipc_json_status
sc_ipc_json_decode(const uint8_t *payload, size_t length,
                   struct sc_ipc_message *message) {
    if (!payload || !message || !length || length > SC_IPC_MAX_PAYLOAD) {
        return SC_IPC_JSON_INVALID_LENGTH;
    }
    size_t memory_size = yyjson_read_max_memory_usage(length, 0);
    if (!memory_size || memory_size > SC_IPC_MAX_JSON_MEMORY) {
        return SC_IPC_JSON_INVALID_LENGTH;
    }
    void *memory = malloc(memory_size);
    if (!memory) {
        return SC_IPC_JSON_NO_MEMORY;
    }
    yyjson_alc allocator;
    yyjson_doc *document = NULL;
    enum sc_ipc_json_status status = SC_IPC_JSON_INVALID_JSON;
    if (!yyjson_alc_pool_init(&allocator, memory, memory_size)) {
        status = SC_IPC_JSON_NO_MEMORY;
        goto cleanup;
    }
    document = yyjson_read_opts((char *) payload, length, 0, &allocator, NULL);
    if (!document) {
        goto cleanup;
    }
    yyjson_val *root = yyjson_doc_get_root(document);
    if (!yyjson_is_obj(root)) {
        status = SC_IPC_JSON_INVALID_MESSAGE;
        goto cleanup;
    }
    struct sc_ipc_message decoded = {0};
    char type[SC_IPC_STRING_CAPACITY];
    if (!get_string(root, "messageType", type, sizeof(type))) {
        status = SC_IPC_JSON_INVALID_MESSAGE;
        goto cleanup;
    }
    bool found = false;
    for (size_t index = 0; index < sizeof(type_names) /
                                    sizeof(type_names[0]); ++index) {
        if (!strcmp(type, type_names[index])) {
            decoded.type = (enum sc_ipc_message_type) index;
            found = true;
            break;
        }
    }
    if (!found) {
        status = SC_IPC_JSON_UNSUPPORTED_MESSAGE;
        goto cleanup;
    }
    status = SC_IPC_JSON_INVALID_MESSAGE;
    if (!check_fields(root, decoded.type)) {
        goto cleanup;
    }

    switch (decoded.type) {
        case SC_IPC_HELLO:
        case SC_IPC_HELLO_RESULT:
            if (!get_string(root, "product", decoded.product,
                            sizeof(decoded.product)) ||
                !get_version(root, "protocolMajor", &decoded.protocol_major) ||
                !get_version(root, "protocolMinor", &decoded.protocol_minor)) {
                goto cleanup;
            }
            if (decoded.type == SC_IPC_HELLO) {
                if (!get_capabilities(root, "requiredCapabilities",
                                      &decoded.required_capabilities) ||
                    !get_capabilities(root, "supportedCapabilities",
                                      &decoded.supported_capabilities) ||
                    !required_capabilities_are_supported(&decoded)) {
                    goto cleanup;
                }
            } else if (!GET_ENUM(root, "status", decoded.status,
                                 hello_statuses) ||
                       !get_capabilities(root, "capabilities",
                                         &decoded.capabilities) ||
                       (strcmp(decoded.status, "accepted") &&
                        decoded.capabilities.count)) {
                goto cleanup;
            }
            break;
        case SC_IPC_COMMAND:
        case SC_IPC_COMMAND_RESULT:
            if (!decode_request(root, &decoded)) {
                goto cleanup;
            }
            if (decoded.type == SC_IPC_COMMAND_RESULT &&
                !GET_ENUM(root, "status", decoded.status, command_statuses)) {
                goto cleanup;
            }
            break;
        case SC_IPC_LIFECYCLE: {
            yyjson_val *attempt = yyjson_obj_get(root, "connectionAttemptId");
            if (!get_decimal(root, "sequence", false, &decoded.sequence) ||
                !get_string(root, "utc", decoded.utc, sizeof(decoded.utc)) ||
                !valid_utc(decoded.utc) ||
                !get_decimal(root, "monotonicMicroseconds", true,
                             &decoded.monotonic_microseconds) ||
                !get_guid(root, "sessionId", decoded.session_id) ||
                !attempt ||
                !GET_ENUM(root, "subsystem", decoded.subsystem, subsystems) ||
                !GET_ENUM(root, "eventType", decoded.event_type, events) ||
                !GET_ENUM(root, "reason", decoded.reason, reasons) ||
                !GET_ENUM(root, "error", decoded.error, errors)) {
                goto cleanup;
            }
            decoded.connection_attempt_null = yyjson_is_null(attempt);
            if (!decoded.connection_attempt_null &&
                !get_guid(root, "connectionAttemptId",
                          decoded.connection_attempt_id)) {
                goto cleanup;
            }
            if (decoded.connection_attempt_null &&
                IN_SET(decoded.event_type, attempt_events)) {
                goto cleanup;
            }
            break;
        }
    }
    *message = decoded;
    status = SC_IPC_JSON_OK;

cleanup:
    if (document) {
        yyjson_doc_free(document);
    }
    free(memory);
    return status;
}

/** Adds an exact decimal string without losing uint64 bits. */
static bool
add_decimal(yyjson_mut_doc *document, yyjson_mut_val *root,
            const char *name, uint64_t value) {
    char digits[21];
    int count = snprintf(digits, sizeof(digits), "%" PRIu64, value);
    return count > 0 && count < (int) sizeof(digits) &&
           yyjson_mut_obj_add_strcpy(document, root, name, digits);
}

/** Adds capability strings in canonical ordinal order. */
static bool
add_capabilities(yyjson_mut_doc *document, yyjson_mut_val *root,
                 const char *name,
                 const struct sc_ipc_capabilities *capabilities) {
    if (capabilities->count > SC_IPC_CAPABILITY_LIMIT) {
        return false;
    }
    yyjson_mut_val *array = yyjson_mut_obj_add_arr(document, root, name);
    if (!array) {
        return false;
    }
    const char *sorted[SC_IPC_CAPABILITY_LIMIT];
    for (size_t index = 0; index < capabilities->count; ++index) {
        sorted[index] = capabilities->values[index];
    }
    for (size_t index = 1; index < capabilities->count; ++index) {
        const char *value = sorted[index];
        size_t previous = index;
        while (previous && strcmp(sorted[previous - 1], value) > 0) {
            sorted[previous] = sorted[previous - 1];
            --previous;
        }
        sorted[previous] = value;
    }
    for (size_t index = 0; index < capabilities->count; ++index) {
        if (!yyjson_mut_arr_add_str(document, array, sorted[index])) {
            return false;
        }
    }
    return true;
}

/** Encodes one valid message with canonical field order. Caller frees output. */
enum sc_ipc_json_status
sc_ipc_json_encode(const struct sc_ipc_message *message,
                   uint8_t **payload, size_t *length) {
    if (!message || !payload || !length ||
        message->type < SC_IPC_HELLO || message->type > SC_IPC_LIFECYCLE) {
        return SC_IPC_JSON_INVALID_MESSAGE;
    }
    *payload = NULL;
    *length = 0;
    yyjson_mut_doc *document = yyjson_mut_doc_new(NULL);
    if (!document) {
        return SC_IPC_JSON_NO_MEMORY;
    }
    yyjson_mut_val *root = yyjson_mut_obj(document);
    if (!root) {
        yyjson_mut_doc_free(document);
        return SC_IPC_JSON_NO_MEMORY;
    }
    yyjson_mut_doc_set_root(document, root);
    bool success = yyjson_mut_obj_add_str(document, root, "messageType",
                                          type_names[message->type]);
    switch (message->type) {
        case SC_IPC_HELLO:
        case SC_IPC_HELLO_RESULT:
            success = success &&
                yyjson_mut_obj_add_str(document, root, "product",
                                        message->product) &&
                yyjson_mut_obj_add_uint(document, root, "protocolMajor",
                                         message->protocol_major) &&
                yyjson_mut_obj_add_uint(document, root, "protocolMinor",
                                         message->protocol_minor);
            if (message->type == SC_IPC_HELLO) {
                success = success && add_capabilities(document, root,
                    "requiredCapabilities", &message->required_capabilities) &&
                    add_capabilities(document, root, "supportedCapabilities",
                                     &message->supported_capabilities);
            } else {
                success = success && yyjson_mut_obj_add_str(document, root,
                    "status", message->status) && add_capabilities(document,
                    root, "capabilities", &message->capabilities);
            }
            break;
        case SC_IPC_COMMAND:
        case SC_IPC_COMMAND_RESULT:
            success = success && add_decimal(document, root, "requestId",
                message->request_id) && yyjson_mut_obj_add_str(document, root,
                "sessionId", message->session_id) &&
                yyjson_mut_obj_add_str(document, root, "command",
                                       message->command);
            if (message->type == SC_IPC_COMMAND_RESULT) {
                success = success && yyjson_mut_obj_add_str(document, root,
                    "status", message->status);
            }
            break;
        case SC_IPC_LIFECYCLE:
            success = success && add_decimal(document, root, "sequence",
                message->sequence) && yyjson_mut_obj_add_str(document, root,
                "utc", message->utc) && add_decimal(document, root,
                "monotonicMicroseconds", message->monotonic_microseconds) &&
                yyjson_mut_obj_add_str(document, root, "sessionId",
                                        message->session_id);
            if (message->connection_attempt_null) {
                success = success && yyjson_mut_obj_add_null(document, root,
                    "connectionAttemptId");
            } else {
                success = success && yyjson_mut_obj_add_str(document, root,
                    "connectionAttemptId", message->connection_attempt_id);
            }
            success = success && yyjson_mut_obj_add_str(document, root,
                "subsystem", message->subsystem) &&
                yyjson_mut_obj_add_str(document, root, "eventType",
                                       message->event_type) &&
                yyjson_mut_obj_add_str(document, root, "reason",
                                       message->reason) &&
                yyjson_mut_obj_add_str(document, root, "error",
                                       message->error);
            break;
    }
    enum sc_ipc_json_status status = SC_IPC_JSON_NO_MEMORY;
    size_t bytes = 0;
    char *encoded = success ? yyjson_mut_write(document, 0, &bytes) : NULL;
    if (encoded) {
        struct sc_ipc_message checked;
        status = sc_ipc_json_decode((const uint8_t *) encoded, bytes, &checked);
        if (status == SC_IPC_JSON_OK) {
            *payload = (uint8_t *) encoded;
            *length = bytes;
            encoded = NULL;
        }
    }
    free(encoded);
    yyjson_mut_doc_free(document);
    return status;
}

/** Evaluates the pure v1 handshake without process effects. */
enum sc_ipc_json_status
sc_ipc_hello_evaluate(const struct sc_ipc_message *hello,
                      struct sc_ipc_message *result) {
    if (!hello || !result || hello->type != SC_IPC_HELLO ||
        !required_capabilities_are_supported(hello)) {
        return SC_IPC_JSON_INVALID_MESSAGE;
    }
    static const char *const supported[] = {
        "stop", "focus-window", "lifecycle-v1",
    };
    memset(result, 0, sizeof(*result));
    result->type = SC_IPC_HELLO_RESULT;
    strcpy(result->product, "scrcpy-seamless");
    result->protocol_major = 1;
    result->protocol_minor = 0;
    const char *status = "accepted";
    if (strcmp(hello->product, result->product)) {
        status = "productMismatch";
    } else if (hello->protocol_major != 1) {
        status = "majorMismatch";
    } else {
        for (size_t index = 0; index < hello->required_capabilities.count;
             ++index) {
            if (!IN_SET(hello->required_capabilities.values[index],
                        supported)) {
                status = "requiredCapabilityMissing";
                break;
            }
        }
    }
    strcpy(result->status, status);
    if (!strcmp(status, "accepted")) {
        for (size_t index = 0; index < hello->supported_capabilities.count;
             ++index) {
            const char *capability = hello->supported_capabilities.values[index];
            if (IN_SET(capability, supported)) {
                strcpy(result->capabilities.values[result->capabilities.count++],
                       capability);
            }
        }
    }
    return SC_IPC_JSON_OK;
}
