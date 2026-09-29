#include "ipc/machine_exit.h"

#include <string.h>
#include <SDL3/SDL_events.h>

static bool window_close_observed;

/** Initialize the process-local observation for a machine run. */
void
sc_machine_exit_reset(void) {
    window_close_observed = false;
}

/** Observe an SDL event reached by a native quit path. */
void
sc_machine_exit_observe_event(uint32_t event_type, bool stop_requested) {
    if (event_type == SDL_EVENT_QUIT && !stop_requested) {
        window_close_observed = true;
    }
}

/** Preserve protocol and fatal causes before classifying an observed close. */
const char *
sc_machine_exit_reason(bool success, bool stop_requested,
                       const char *stop_reason) {
    if (stop_requested && !strcmp(stop_reason, "protocolError")) {
        return "protocolError";
    }

    if (!success) {
        return "nativeFailure";
    }

    if (stop_requested) {
        return stop_reason;
    }

    return window_close_observed ? "windowClosed" : "unknown";
}
