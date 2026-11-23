#include "recovery_adapter.h"

#include <string.h>

void
sc_recovery_adapter_init(struct sc_recovery_adapter *adapter,
                         const char *network_serial,
                         const char *android_serial) {
    *adapter = (struct sc_recovery_adapter) {
        .network_serial = network_serial,
        .android_serial = android_serial,
    };
}

bool
sc_recovery_adapter_enabled(const struct sc_recovery_adapter *adapter) {
    return adapter->network_serial && *adapter->network_serial;
}

bool
sc_recovery_adapter_resolve(const struct sc_recovery_adapter *adapter,
                            const struct scrcpy_options *options, bool retrying,
                            struct sc_recovery_target *target) {

    if (retrying && !sc_recovery_adapter_enabled(adapter)) {
        return false;
    }

    struct sc_recovery_target resolved = {
        .req_serial = retrying ? adapter->network_serial : options->serial,
        .tcpip_dst = retrying ? NULL : options->tcpip_dst,
        .select_usb = !retrying && options->select_usb,
        .select_tcpip = !retrying && options->select_tcpip,
        .tcpip = !retrying && options->tcpip,
    };

    // Preserve pre-connect selection before the historical ANDROID_SERIAL
    // fallback: that fallback selects a route but does not request adb connect.
    resolved.connect_requested_serial = adapter->network_serial
        && resolved.req_serial
        && !strcmp(adapter->network_serial, resolved.req_serial);

    bool use_environment_selector = !resolved.req_serial && !resolved.select_usb
        && !resolved.select_tcpip && !resolved.tcpip_dst;

    if (use_environment_selector) {
        resolved.req_serial = adapter->android_serial;
    }

    *target = resolved;
    return true;
}
