#ifndef SCRCPY_H
#define SCRCPY_H

#include "common.h"

#include "options.h"

struct sc_dispatcher;

enum scrcpy_exit_code {
    // Normal program termination
    SCRCPY_EXIT_SUCCESS,

    // No connection could be established
    SCRCPY_EXIT_FAILURE,

    // Device was disconnected while running
    SCRCPY_EXIT_DISCONNECTED,
};

enum scrcpy_exit_code
scrcpy(struct scrcpy_options *options, struct sc_dispatcher *dispatcher);

#endif
