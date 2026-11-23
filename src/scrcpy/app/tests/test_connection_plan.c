#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "connection_plan.h"

#define VALID_PLAN "{\"version\":1,\"reconnect\":true,\"selected\":0," \
    "\"requiredCapabilities\":5,\"desiredCapabilities\":7,\"maximumAttempts\":3," \
    "\"initialBackoffUs\":\"1000000\",\"maximumBackoffUs\":\"8000000\"," \
    "\"candidates\":[{\"transport\":\"usb\",\"serial\":\"SYNTHETIC_USB\"}," \
    "{\"transport\":\"network\",\"endpoint\":\"synthetic.example:37123\"}]}"

static const char valid_plan[] = VALID_PLAN;

static void
reject_in_document(const char *document, const char *old, const char *replacement) {
    const char *position = strstr(document, old);
    assert(position);
    char input[SC_CONNECTION_PLAN_MAX_BYTES + 1];
    int written = snprintf(input, sizeof(input), "%.*s%s%s", (int) (position - document),
                           document, replacement, position + strlen(old));
    assert(written >= 0 && (size_t) written < sizeof(input));
    struct sc_connection_launch_plan plan;
    memset(&plan, 0x5a, sizeof(plan));
    struct sc_connection_launch_plan before = plan;
    bool accepted = sc_connection_launch_plan_parse(input, &plan);
    assert(!accepted);
    assert(!memcmp(&plan, &before, sizeof(plan)));
}

static void
reject_replacement(const char *old, const char *replacement) {
    reject_in_document(valid_plan, old, replacement);
}

static void
check_managed_plans(const char *directory) {
    const char *names[] = {"usb-network", "network-no-fallback", "unicode-boundary", "selected-network", "maximum-endpoint"};
    for (size_t index = 0; index < ARRAY_LEN(names); ++index) {
        char path[4096];
        int written = snprintf(path, sizeof(path), "%s/%s.json", directory, names[index]);
        assert(written >= 0 && (size_t) written < sizeof(path));
        FILE *file = fopen(path, "rb");
        assert(file);
        char input[SC_CONNECTION_PLAN_MAX_BYTES + 2];
        size_t count = fread(input, 1, sizeof(input) - 1, file);
        bool read_error = ferror(file);
        int close_result = fclose(file);
        assert(!read_error && !close_result && count && count <= SC_CONNECTION_PLAN_MAX_BYTES);
        input[count] = '\0';
        struct sc_connection_launch_plan plan;
        bool accepted = sc_connection_launch_plan_parse(input, &plan);
        assert(accepted);
        bool maximum_endpoint = index == 4;
        bool network_only = index == 1 || maximum_endpoint;
        bool custom_policy = index == 1;
        bool unicode = index == 2;
        bool network_first = network_only || index == 3;
        assert(plan.reconnect == !unicode);
        assert(plan.policy.retry_enabled == !unicode);
        assert(plan.policy.selected_candidate == 0);
        assert(plan.policy.candidate_count == (network_only || unicode ? 1u : 2u));
        assert(plan.policy.allow_fallback == (!network_only && !unicode));
        assert(plan.required_capabilities == (custom_policy ? 1 : 5));
        assert(plan.desired_capabilities == (custom_policy ? 3 : 7));
        assert(plan.policy.maximum_attempts == (custom_policy ? 5u : 3u));
        assert(plan.policy.initial_backoff == (custom_policy ? 500000 : 1000000));
        assert(plan.policy.maximum_backoff == (custom_policy ? 1000000 : 8000000));
        assert(plan.targets[0].transport == (network_first ? SC_CONNECTION_NETWORK : SC_CONNECTION_USB));

        if (maximum_endpoint) {
            char expected[260];
            memset(expected, 'a', 253);
            expected[63] = expected[127] = expected[191] = '.';
            memcpy(expected + 253, ":65535", 7);
            assert(strlen(plan.targets[0].serial) == 259);
            assert(!strcmp(plan.targets[0].serial, expected));
            assert(!strcmp(plan.targets[0].endpoint, expected));
            assert(!plan.targets[0].service[0]);
        } else if (unicode) {
            assert(strlen(plan.targets[0].serial) == 256);
            for (size_t byte = 0; byte < 256; byte += 2) {
                assert((unsigned char) plan.targets[0].serial[byte] == 0xc3);
                assert((unsigned char) plan.targets[0].serial[byte + 1] == 0xa9);
            }
        } else {
            unsigned network_index = network_first ? 0 : 1;
            assert(!strcmp(plan.targets[network_index].service, "adb-synthetic._adb-tls-connect._tcp"));
            assert(!strcmp(plan.targets[network_index].endpoint, "synthetic.example:37123"));

            if (network_first) {
                assert(!strcmp(plan.targets[0].serial, "synthetic.example:37123"));
            }

            if (!network_only) {
                assert(!strcmp(plan.targets[network_first ? 1 : 0].serial, "SYNTHETIC_USB"));
            }
        }

        reject_in_document(input, "\"version\":1", "\"version\":1,\"version\":1");
        reject_in_document(input, "\"version\":1", "\"version\":1,\"unknown\":false");
        reject_in_document(input, custom_policy ? "\"requiredCapabilities\":1" : "\"requiredCapabilities\":5",
                           "\"requiredCapabilities\":8");
    }

    puts("connection plan C# -> C: five independently checked committed plans and 15 rejection mutations");
}

static void
check_field_order_and_ownership(void) {
    static const char *const fields[] = {
        "\"version\": 1",
        "\"reconnect\": true",
        "\"selected\": 0",
        "\"requiredCapabilities\": 5",
        "\"desiredCapabilities\": 7",
        "\"maximumAttempts\": 3",
        "\"initialBackoffUs\": \"1000000\"",
        "\"maximumBackoffUs\": \"8000000\"",
        "\"candidates\": ["
            "{\"serial\":\"SYNTHETIC\\u005fUSB\",\"transport\":\"usb\"},"
            "{\"endpoint\":\"synthetic.example:37123\",\"transport\":\"network\"}]",
    };

    for (size_t first = 0; first < ARRAY_LEN(fields); ++first) {
        char input[SC_CONNECTION_PLAN_MAX_BYTES + 1] = "{";
        size_t length = strlen(input);

        for (size_t index = 0; index < ARRAY_LEN(fields); ++index) {
            int written = snprintf(input + length, sizeof(input) - length,
                                   "%s\n  %s", index ? "," : "",
                                   fields[(first + index) % ARRAY_LEN(fields)]);
            assert(written > 0 && (size_t) written < sizeof(input) - length);
            length += written;
        }
        assert(sizeof(input) - length >= sizeof("\n}"));
        memcpy(input + length, "\n}", sizeof("\n}"));

        struct sc_connection_launch_plan plan;
        assert(sc_connection_launch_plan_parse(input, &plan));
        memset(input, 0, sizeof(input));
        assert(plan.reconnect && plan.policy.retry_enabled && plan.policy.allow_fallback);
        assert(plan.required_capabilities == 5 && plan.desired_capabilities == 7);
        assert(plan.policy.candidate_count == 2 && plan.policy.selected_candidate == 0);
        assert(plan.policy.maximum_attempts == 3);
        assert(plan.policy.initial_backoff == SC_TICK_FROM_SEC(1));
        assert(plan.policy.maximum_backoff == SC_TICK_FROM_SEC(8));
        assert(plan.policy.candidates[0] == SC_CONNECTION_USB);
        assert(plan.policy.candidates[1] == SC_CONNECTION_NETWORK);
        assert(!strcmp(plan.targets[0].serial, "SYNTHETIC_USB"));
        assert(!strcmp(plan.targets[1].endpoint, "synthetic.example:37123"));
    }
}

int
main(int argc, char *argv[]) {
    if (argc == 3 && !strcmp(argv[1], "--managed")) {
        check_managed_plans(argv[2]);
        return 0;
    }

    assert(argc == 1);
    struct sc_connection_launch_plan plan;
    bool accepted = sc_connection_launch_plan_parse(VALID_PLAN, &plan);
    assert(accepted);
    assert(plan.policy.candidate_count == 2);
    assert(plan.policy.selected_candidate == 0);
    assert(plan.policy.allow_fallback && plan.policy.retry_enabled);
    assert(plan.policy.maximum_attempts == 3);
    assert(plan.policy.initial_backoff == SC_TICK_FROM_SEC(1));
    assert(plan.policy.maximum_backoff == SC_TICK_FROM_SEC(8));
    assert(!strcmp(plan.targets[0].serial, "SYNTHETIC_USB"));
    assert(!strcmp(plan.targets[1].endpoint, "synthetic.example:37123"));
    reject_replacement("\"version\":1", "\"version\":2");
    reject_replacement("\"version\":1", "\"version\":1,\"version\":1");
    reject_replacement("\"version\":1", "\"version\":1,\"unknown\":0");
    reject_replacement("\"selected\":0", "\"selected\":1");
    reject_replacement("\"maximumAttempts\":3", "\"maximumAttempts\":101");
    reject_replacement("\"maximumAttempts\":3", "\"maximumAttempts\":0");
    reject_replacement("\"initialBackoffUs\":\"1000000\"", "\"initialBackoffUs\":\"01\"");
    reject_replacement("\"initialBackoffUs\":\"1000000\"", "\"initialBackoffUs\":1000000");
    reject_replacement("\"maximumBackoffUs\":\"8000000\"", "\"maximumBackoffUs\":\"86400000001\"");
    reject_replacement("\"maximumBackoffUs\":\"8000000\"", "\"maximumBackoffUs\":\"18446744073709551616\"");
    reject_replacement("\"maximumBackoffUs\":\"8000000\"", "\"maximumBackoffUs\":\"0\"");
    reject_replacement("\"requiredCapabilities\":5", "\"requiredCapabilities\":8");
    reject_replacement("\"desiredCapabilities\":7", "\"desiredCapabilities\":1");
    reject_replacement("\"serial\":\"SYNTHETIC_USB\"", "\"serial\":\"bad\\u0000tail\"");
    reject_replacement("\"serial\":\"SYNTHETIC_USB\"", "\"serial\":\"bad\\ntail\"");
    reject_replacement("\"reconnect\":true", "\"reconnect\":null");
    reject_replacement("\"transport\":\"usb\"", "\"transport\":\"usb\",\"service\":\"wrong\"");
    reject_replacement("\"transport\":\"usb\"", "\"transport\":\"usb\\u0000other\"");
    reject_replacement("\"endpoint\":\"synthetic.example:37123\"", "\"endpoint\":\"synthetic.example:0\"");
    reject_replacement("\"endpoint\":\"synthetic.example:37123\"", "\"endpoint\":\"synthetic.example:37123\",\"service\":\"bad_adb-tls-connect._tcp.extra\"");
    reject_replacement("\"endpoint\":\"synthetic.example:37123\"", "\"endpoint\":\"synthetic.example:37123\",\"service\":\"adb-synthetic._adb-tls-pairing._tcp\"");
    reject_replacement("\"endpoint\":\"synthetic.example:37123\"", "\"endpoint\":\"synthetic.example:37123\",\"service\":\"adb-synthetic._ssh._tcp\"");
    reject_replacement("\"endpoint\":\"synthetic.example:37123\"", "\"endpoint\":\"synthetic.example:37123\",\"service\":\".\"");
    const char *accepted_services[] = {"adb-synthetic", "adb-synthetic.",
        "adb-synthetic._adb-tls-connect._tcp", "adb-synthetic._adb-tls-connect._tcp."};
    for (size_t index = 0; index < ARRAY_LEN(accepted_services); ++index) {
        char input[SC_CONNECTION_PLAN_MAX_BYTES + 1];
        const char *endpoint = strstr(valid_plan, "\"endpoint\"");
        int written = snprintf(input, sizeof(input), "%.*s\"service\":\"%s\",%s",
            (int) (endpoint - valid_plan), valid_plan, accepted_services[index], endpoint);
        assert(written > 0 && (size_t) written < sizeof(input));
        bool parsed = sc_connection_launch_plan_parse(input, &plan);
        assert(parsed);
        assert(!strcmp(plan.targets[1].service, accepted_services[index]));
    }
    const char *accepted_endpoints[] = {"synthetic.example:37123", "192.0.2.1:1",
        "[2001:db8::1]:65535", "[::ffff:192.0.2.1]:37123", "a:00001"};
    const char *rejected_endpoints[] = {"", "hostname", "a:0", "a:65536", "a:-1", "a:000001",
        "[::1%2]:1234", "[::1]1234", "::1:1234", "[not-ipv6]:1234", "a.:1234", ".a:1234",
        "-a:1234", "a-:1234", "192.00.2.1:1234", "999.1.1.1:1234", "1.2.3:1234", "a b:1234"};
    for (size_t index = 0; index < ARRAY_LEN(accepted_endpoints); ++index) {
        assert(sc_connection_endpoint_valid(accepted_endpoints[index]));
    }
    for (size_t index = 0; index < ARRAY_LEN(rejected_endpoints); ++index) {
        assert(!sc_connection_endpoint_valid(rejected_endpoints[index]));
    }
    char identifier[258];
    memset(identifier, 'x', sizeof(identifier) - 1);
    identifier[sizeof(identifier) - 1] = '\0';
    const char *transports[] = {"usb", "network"};
    for (unsigned index = 0; index < ARRAY_LEN(transports); ++index) {
        char input[SC_CONNECTION_PLAN_MAX_BYTES + 1];
        int written = snprintf(input, sizeof(input),
            "{\"version\":1,\"reconnect\":true,\"selected\":0,\"requiredCapabilities\":5,"
            "\"desiredCapabilities\":7,\"maximumAttempts\":3,\"initialBackoffUs\":\"0\","
            "\"maximumBackoffUs\":\"0\",\"candidates\":[{\"transport\":\"%s\",\"serial\":\"%s\"%s}]}",
            transports[index], identifier, index ? ",\"endpoint\":\"synthetic.example:37123\"" : "");
        assert(written > 0 && (size_t) written < sizeof(input));
        bool oversized = sc_connection_launch_plan_parse(input, &plan);
        assert(!oversized);
    }
    check_field_order_and_ownership();
    puts("connection launch plan: accepted owned plan, 25 isolated rejections, "
         "23 endpoint cases, four service cases and nine field orders");
    return 0;
}
