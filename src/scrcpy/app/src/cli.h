#ifndef SCRCPY_CLI_H
#define SCRCPY_CLI_H

#include "common.h"

#include <stdbool.h>
#include <stddef.h>

#include "options.h"

enum sc_pause_on_exit {
    SC_PAUSE_ON_EXIT_UNDEFINED,
    SC_PAUSE_ON_EXIT_TRUE,
    SC_PAUSE_ON_EXIT_FALSE,
    SC_PAUSE_ON_EXIT_IF_ERROR,
};

struct scrcpy_cli_args {
    struct scrcpy_options opts;
    bool help;
    bool version;
    enum sc_pause_on_exit pause_on_exit;
};

void
scrcpy_print_usage(const char *arg0);

bool
scrcpy_parse_args(struct scrcpy_cli_args *args, int argc, char *argv[]);

#ifdef SC_TEST
struct sc_cli_option_test_info {
    const char *longopt;
    char shortopt;
    bool has_arg;
    bool optional_arg;
    bool documented;
};

// Expose compiled generated declarations to native parity tests.
size_t
sc_cli_option_count(void);

bool
sc_cli_option_get_test_info(size_t index, struct sc_cli_option_test_info *info);

bool
sc_parse_shortcut_mods(const char *s, uint8_t *shortcut_mods);
#endif

#endif
