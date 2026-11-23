#ifndef SC_RECOVERY_ADAPTER_H
#define SC_RECOVERY_ADAPTER_H

#include "common.h"

#include <stdbool.h>

#include "options.h"

/* Invocation-owned strings must outlive all server attempts and retirement. */
struct sc_recovery_adapter {
    const char *network_serial;
    const char *android_serial;
};

struct sc_recovery_target {
    const char *req_serial;
    const char *tcpip_dst;
    bool select_usb;
    bool select_tcpip;
    bool tcpip;
    bool connect_requested_serial;
};

void
sc_recovery_adapter_init(struct sc_recovery_adapter *adapter,
                         const char *network_serial,
                         const char *android_serial);

bool
sc_recovery_adapter_enabled(const struct sc_recovery_adapter *adapter);

/* Resolve the characterized route without reading process environment or I/O. */
bool
sc_recovery_adapter_resolve(const struct sc_recovery_adapter *adapter,
                            const struct scrcpy_options *options, bool retrying,
                            struct sc_recovery_target *target);

#endif
