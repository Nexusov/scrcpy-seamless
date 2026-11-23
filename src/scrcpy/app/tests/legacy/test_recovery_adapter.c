#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "recovery_adapter.h"

static const char network_serial[] = "synthetic-device._adb-tls-connect._tcp";
static const char usb_serial[] = "synthetic-usb";
static const char environment_serial[] = "synthetic-environment";

static struct sc_recovery_target
resolve(const char *network, const char *android,
         const struct scrcpy_options *options, bool retrying) {
    struct sc_recovery_adapter adapter;
    sc_recovery_adapter_init(&adapter, network, android);
    struct sc_recovery_target target;
    bool resolved = sc_recovery_adapter_resolve(&adapter, options, retrying,
                                                &target);
    assert(resolved);
    return target;
}

static void
test_initial_selection(void) {
    struct scrcpy_options options = {0};
    struct sc_recovery_target target = resolve(NULL, NULL, &options, false);
    assert(!target.req_serial && !target.select_usb && !target.select_tcpip);
    assert(!target.tcpip && !target.tcpip_dst && !target.connect_requested_serial);

    target = resolve(network_serial, environment_serial, &options, false);
    assert(target.req_serial == environment_serial);
    assert(!target.connect_requested_serial);

    target = resolve(network_serial, "", &options, false);
    assert(target.req_serial && !*target.req_serial);
    assert(!target.connect_requested_serial);

    options.select_usb = true;
    target = resolve(network_serial, environment_serial, &options, false);
    assert(!target.req_serial && target.select_usb && !target.select_tcpip);

    options.select_usb = false;
    options.select_tcpip = true;
    target = resolve(network_serial, environment_serial, &options, false);
    assert(!target.req_serial && !target.select_usb && target.select_tcpip);

    options.select_tcpip = false;
    options.serial = usb_serial;
    target = resolve(network_serial, environment_serial, &options, false);
    assert(target.req_serial == usb_serial && !target.connect_requested_serial);
}

/** Only an explicit matching requested route carried the old pre-connect flag. */
static void
test_initial_network_connect(void) {
    struct scrcpy_options options = {.serial = network_serial};
    char same_network[sizeof(network_serial)];
    memcpy(same_network, network_serial, sizeof(same_network));
    struct sc_recovery_target target = resolve(same_network, environment_serial,
                                               &options, false);
    assert(target.req_serial == network_serial);
    assert(target.connect_requested_serial);

    options.serial = NULL;
    target = resolve(network_serial, network_serial, &options, false);
    assert(target.req_serial == network_serial);
    assert(!target.connect_requested_serial);

    options.serial = "";
    target = resolve("", environment_serial, &options, false);
    assert(target.req_serial && !*target.req_serial);
    assert(target.connect_requested_serial);
}

static void
test_tcpip_destination(void) {
    struct scrcpy_options options = {.tcpip = true, .tcpip_dst = "+synthetic:1234"};
    struct sc_recovery_target target = resolve(network_serial, environment_serial,
                                               &options, false);
    assert(!target.req_serial && target.tcpip);
    assert(target.tcpip_dst == options.tcpip_dst);
    assert(!target.connect_requested_serial);

    options.tcpip_dst = NULL;
    target = resolve(network_serial, environment_serial, &options, false);
    assert(target.req_serial == environment_serial && target.tcpip);
    assert(!target.tcpip_dst && !target.connect_requested_serial);
}

static void
test_retry_route(void) {
    struct scrcpy_options options = {
        .serial = usb_serial,
        .select_usb = true,
        .select_tcpip = true,
        .tcpip = true,
        .tcpip_dst = "+synthetic:1234",
    };
    struct sc_recovery_target target = resolve(network_serial, environment_serial,
                                               &options, true);
    assert(target.req_serial == network_serial && target.connect_requested_serial);
    assert(!target.select_usb && !target.select_tcpip);
    assert(!target.tcpip && !target.tcpip_dst);
    assert(options.serial == usb_serial && options.select_usb && options.select_tcpip);
    assert(options.tcpip && options.tcpip_dst);
}

static void
test_disabled_retry(void) {
    const char *disabled[] = {NULL, ""};
    struct scrcpy_options options = {0};

    for (size_t index = 0; index < sizeof(disabled) / sizeof(disabled[0]); ++index) {
        struct sc_recovery_adapter adapter;
        sc_recovery_adapter_init(&adapter, disabled[index], environment_serial);
        assert(!sc_recovery_adapter_enabled(&adapter));
        struct sc_recovery_target target = {.req_serial = usb_serial};
        bool resolved = sc_recovery_adapter_resolve(&adapter, &options, true,
                                                    &target);
        assert(!resolved && target.req_serial == usb_serial);
    }
    struct sc_recovery_adapter adapter;
    sc_recovery_adapter_init(&adapter, network_serial, NULL);
    assert(sc_recovery_adapter_enabled(&adapter));
}

int
main(void) {
    test_initial_selection();
    test_initial_network_connect();
    test_tcpip_destination();
    test_retry_route();
    test_disabled_retry();
    puts("Recovery adapter: 5 target-selection groups passed");
    return 0;
}
