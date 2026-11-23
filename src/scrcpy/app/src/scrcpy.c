#include "scrcpy.h"

#include "app.h"

enum scrcpy_exit_code
scrcpy(struct scrcpy_options *options, struct sc_dispatcher *dispatcher) {
    return sc_app_run(options, dispatcher);
}
