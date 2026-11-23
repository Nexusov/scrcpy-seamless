#ifndef SC_APP_H
#define SC_APP_H

#include "scrcpy.h"

/* One application owner retains presentation across sequential generations. */
enum scrcpy_exit_code
sc_app_run(const struct scrcpy_options *options, struct sc_dispatcher *dispatcher);

#endif
