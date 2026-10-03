#ifndef SC_GENERATION_TARGETS_H
#define SC_GENERATION_TARGETS_H

struct sc_screen;
struct sc_uhid_devices;

/* One generation binding, resolved only under the dispatcher execution lease. */
struct sc_generation_targets {
    struct sc_screen *screen;
    struct sc_uhid_devices *uhid_devices;
};

#endif
