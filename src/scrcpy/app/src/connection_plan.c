#include "connection_plan.h"

#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
# include <winsock2.h>
# include <ws2tcpip.h>
#else
# include <arpa/inet.h>
#endif

#include "../vendor/yyjson/yyjson.h"

#define SC_CONNECTION_PLAN_PARSE_MEMORY (256 * 1024)

static bool
ascii_alphanumeric(unsigned char value) {
    return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z')
        || (value >= '0' && value <= '9');
}

bool
sc_connection_endpoint_valid(const char *endpoint) {
    if (!endpoint || !*endpoint || strlen(endpoint) >= SC_CONNECTION_PLAN_ENDPOINT_CAPACITY) {
        return false;
    }

    const char *port;

    if (endpoint[0] == '[') {
        const char *end = strchr(endpoint, ']');

        if (!end || end == endpoint + 1 || end[1] != ':' || strchr(endpoint, '%')) {
            return false;
        }

        size_t length = (size_t) (end - endpoint - 1);
        char address[SC_CONNECTION_PLAN_ENDPOINT_CAPACITY];
        memcpy(address, endpoint + 1, length);
        address[length] = '\0';
        struct in6_addr parsed;

        if (inet_pton(AF_INET6, address, &parsed) != 1) {
            return false;
        }

        port = end + 2;
    } else {
        const char *delimiter = strchr(endpoint, ':');

        if (!delimiter || delimiter == endpoint || strchr(delimiter + 1, ':')) {
            return false;
        }

        size_t host_length = (size_t) (delimiter - endpoint);

        if (host_length > 253) {
            return false;
        }

        bool dotted_numeric = memchr(endpoint, '.', host_length) != NULL;
        for (size_t index = 0; index < host_length; ++index) {
            unsigned char value = (unsigned char) endpoint[index];

            if (value != '.' && (value < '0' || value > '9')) {
                dotted_numeric = false;
            }
        }

        const char *label = endpoint;
        unsigned labels = 0;
        while (label < delimiter) {
            const char *end = memchr(label, '.', (size_t) (delimiter - label));

            if (!end) {
                end = delimiter;
            }

            size_t length = (size_t) (end - label);

            if (!length || length > 63 || !ascii_alphanumeric((unsigned char) label[0]) ||
                    !ascii_alphanumeric((unsigned char) end[-1])) {
                return false;
            }

            unsigned octet = 0;
            for (const char *character = label; character < end; ++character) {
                unsigned char value = (unsigned char) *character;

                if (!ascii_alphanumeric(value) && value != '-') {
                    return false;
                }

                if (dotted_numeric) {
                    octet = octet * 10 + (value - '0');
                }
            }

            if (dotted_numeric && (length > 3 || (length > 1 && label[0] == '0') || octet > 255)) {
                return false;
            }

            ++labels;
            label = end + 1;
        }

        if (delimiter[-1] == '.' || (dotted_numeric && labels != 4)) {
            return false;
        }

        port = delimiter + 1;
    }

    size_t port_length = strlen(port);

    if (!port_length || port_length > 5) {
        return false;
    }

    unsigned number = 0;
    for (size_t index = 0; index < port_length; ++index) {
        unsigned digit = (unsigned char) port[index] - '0';

        if (digit > 9) {
            return false;
        }

        number = number * 10 + digit;
    }

    return number > 0 && number <= UINT16_MAX;
}

static bool
object_fields(yyjson_val *object, const char *const *names, size_t count) {
    if (!yyjson_is_obj(object)) {
        return false;
    }

    uint32_t seen = 0;
    yyjson_obj_iter iterator = yyjson_obj_iter_with(object);
    yyjson_val *key;
    while ((key = yyjson_obj_iter_next(&iterator))) {
        const char *name = yyjson_get_str(key);
        size_t index;
        for (index = 0; index < count; ++index) {
            if (strlen(names[index]) == yyjson_get_len(key)
                    && !strcmp(name, names[index])) {
                break;
            }
        }

        if (index == count || (seen & (UINT32_C(1) << index))) {
            return false;
        }

        seen |= UINT32_C(1) << index;
    }

    return true;
}

static bool
bounded_uint(yyjson_val *value, uint64_t minimum, uint64_t maximum,
             uint64_t *result) {
    if (!yyjson_is_uint(value)) {
        return false;
    }

    uint64_t number = yyjson_get_uint(value);

    if (number < minimum || number > maximum) {
        return false;
    }

    *result = number;
    return true;
}

static bool
delay_us(yyjson_val *value, sc_tick *result) {
    const char *text = yyjson_get_str(value);
    size_t length = yyjson_get_len(value);

    if (!text || !length || (length > 1 && text[0] == '0')) {
        return false;
    }

    uint64_t number = 0;
    for (size_t index = 0; index < length; ++index) {
        unsigned digit = (unsigned char) text[index] - '0';

        if (digit > 9 || number > (SC_CONNECTION_PLAN_MAX_DELAY_US - digit) / 10) {
            return false;
        }

        number = number * 10 + digit;
    }

    *result = (sc_tick) number;
    return true;
}

static bool
unicode_whitespace(uint32_t codepoint) {
    return codepoint == 0x20 || codepoint == 0x85 || codepoint == 0xa0
        || codepoint == 0x1680 || (codepoint >= 0x2000 && codepoint <= 0x200a)
        || codepoint == 0x2028 || codepoint == 0x2029 || codepoint == 0x202f
        || codepoint == 0x205f || codepoint == 0x3000;
}

static bool
copy_identifier(yyjson_val *value, char *destination, size_t capacity,
                 bool optional) {
    if (!value) {
        return optional;
    }

    const char *text = yyjson_get_str(value);
    size_t length = yyjson_get_len(value);

    if (!text || !length || length >= capacity) {
        return false;
    }

    bool non_whitespace = false;
    for (size_t index = 0; index < length; ++index) {
        unsigned char byte = (unsigned char) text[index];
        uint32_t codepoint = byte;

        if (byte >= 0xc0) {
            unsigned continuation = byte < 0xe0 ? 1 : byte < 0xf0 ? 2 : 3;
            codepoint &= (UINT32_C(1) << (6 - continuation)) - 1;
            for (unsigned remaining = 0; remaining < continuation; ++remaining) {
                codepoint = (codepoint << 6) | ((unsigned char) text[++index] & 0x3f);
            }
        }

        if (codepoint < 0x20 || (codepoint >= 0x7f && codepoint <= 0x9f)) {
            return false;
        }

        non_whitespace |= !unicode_whitespace(codepoint);
    }

    if (!non_whitespace) {
        return false;
    }

    memcpy(destination, text, length);
    destination[length] = '\0';
    return true;
}

static bool
parse_target(yyjson_val *value, struct sc_connection_target *target) {
    static const char *const fields[] = {"transport", "serial", "service", "endpoint"};

    if (!object_fields(value, fields, 4)) {
        return false;
    }

    yyjson_val *transport_value = yyjson_obj_get(value, "transport");
    const char *transport = yyjson_get_str(transport_value);

    if (!transport) {
        return false;
    }

    yyjson_val *serial = yyjson_obj_get(value, "serial");
    yyjson_val *service = yyjson_obj_get(value, "service");
    yyjson_val *endpoint = yyjson_obj_get(value, "endpoint");

    if (yyjson_get_len(transport_value) == 3 && !strcmp(transport, "usb")) {
        target->transport = SC_CONNECTION_USB;
        return !service && !endpoint
            && copy_identifier(serial, target->serial, SC_CONNECTION_PLAN_ID_CAPACITY, false);
    }

    if (yyjson_get_len(transport_value) != 7 || strcmp(transport, "network")) {
        return false;
    }

    target->transport = SC_CONNECTION_NETWORK;

    if ((!service && !endpoint)
            || !copy_identifier(serial, target->serial, sizeof(target->serial), true)
            || !copy_identifier(service, target->service, sizeof(target->service), true)
            || !copy_identifier(endpoint, target->endpoint, sizeof(target->endpoint), true)) {
        return false;
    }

    static const char service_suffix[] = "._adb-tls-connect._tcp";
    size_t length = strlen(target->service);

    while (length && target->service[length - 1] == '.') {
        --length;
    }

    bool typed_service = strstr(target->service, "._") != NULL;
    bool valid_service = !service || (length && (!typed_service ||
        (length >= sizeof(service_suffix) - 1 &&
         !memcmp(target->service + length - (sizeof(service_suffix) - 1),
                 service_suffix, sizeof(service_suffix) - 1))));
    bool valid_serial = strlen(target->serial) < SC_CONNECTION_PLAN_ID_CAPACITY
        || sc_connection_endpoint_valid(target->serial);
    return valid_service && valid_serial && (!endpoint || sc_connection_endpoint_valid(target->endpoint));
}

static bool
parse_document(yyjson_val *root, struct sc_connection_launch_plan *plan) {
    static const char *const fields[] = {"version", "reconnect", "selected",
        "requiredCapabilities", "desiredCapabilities", "maximumAttempts",
        "initialBackoffUs", "maximumBackoffUs", "candidates"};

    if (!object_fields(root, fields, 9) || yyjson_obj_size(root) != 9) {
        return false;
    }

    uint64_t version, selected, required, desired, attempts;
    yyjson_val *reconnect = yyjson_obj_get(root, "reconnect");

    if (!bounded_uint(yyjson_obj_get(root, "version"), 1, 1, &version)
            || !bounded_uint(yyjson_obj_get(root, "selected"), 0, 0, &selected)
            || !bounded_uint(yyjson_obj_get(root, "requiredCapabilities"), 1, 7, &required)
            || !bounded_uint(yyjson_obj_get(root, "desiredCapabilities"), 1, 7, &desired)
            || (required & ~desired)
            || !bounded_uint(yyjson_obj_get(root, "maximumAttempts"), 1,
                             SC_CONNECTION_PLAN_MAX_ATTEMPTS, &attempts)
            || !yyjson_is_bool(reconnect)
            || !delay_us(yyjson_obj_get(root, "initialBackoffUs"), &plan->policy.initial_backoff)
            || !delay_us(yyjson_obj_get(root, "maximumBackoffUs"), &plan->policy.maximum_backoff)
            || plan->policy.maximum_backoff < plan->policy.initial_backoff) {
        return false;
    }

    yyjson_val *candidates = yyjson_obj_get(root, "candidates");
    size_t count = yyjson_arr_size(candidates);

    if (!yyjson_is_arr(candidates) || !count || count > SC_CONNECTION_MAX_CANDIDATES) {
        return false;
    }

    for (size_t index = 0; index < count; ++index) {
        if (!parse_target(yyjson_arr_get(candidates, index), &plan->targets[index])) {
            return false;
        }

        plan->policy.candidates[index] = plan->targets[index].transport;
    }

    if (count == 2 && plan->policy.candidates[0] == plan->policy.candidates[1]) {
        return false;
    }

    plan->reconnect = yyjson_get_bool(reconnect);
    plan->required_capabilities = (uint8_t) required;
    plan->desired_capabilities = (uint8_t) desired;
    plan->policy.candidate_count = (unsigned) count;
    plan->policy.selected_candidate = 0;
    plan->policy.allow_fallback = count == 2;
    plan->policy.retry_enabled = plan->reconnect;
    plan->policy.maximum_attempts = (unsigned) attempts;
    return true;
}

bool
sc_connection_launch_plan_parse(const char *json, struct sc_connection_launch_plan *plan) {
    if (!json || !plan) {
        return false;
    }

    size_t length = 0;
    while (length <= SC_CONNECTION_PLAN_MAX_BYTES && json[length]) {
        ++length;
    }

    if (!length || length > SC_CONNECTION_PLAN_MAX_BYTES) {
        return false;
    }

    void *memory = malloc(SC_CONNECTION_PLAN_PARSE_MEMORY);

    if (!memory) {
        return false;
    }

    yyjson_alc allocator;
    yyjson_alc_pool_init(&allocator, memory, SC_CONNECTION_PLAN_PARSE_MEMORY);
    yyjson_doc *document = yyjson_read_opts((char *) json, length, 0, &allocator, NULL);
    struct sc_connection_launch_plan candidate = {0};
    bool valid = document && parse_document(yyjson_doc_get_root(document), &candidate);

    if (valid) {
        *plan = candidate;
    }

    yyjson_doc_free(document);
    free(memory);
    return valid;
}
