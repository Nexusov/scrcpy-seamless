#ifndef SC_GENERATION_TARGETS_H
#define SC_GENERATION_TARGETS_H

struct sc_screen;
struct sc_uhid_devices;
struct sc_session_notifications;

/* Dispatcher callbacks resolve under their execution lease. The main owner may
 * also validate a value wake and its retained current slot outside a drain. */
struct sc_generation_targets {
    struct sc_screen *screen;
    struct sc_uhid_devices *uhid_devices;
    struct sc_session_notifications *notifications;
};

#endif
