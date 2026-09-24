#include "cli.h"

#include <assert.h>
#include <getopt.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "options.h"
#include "util/log.h"
#include "util/net.h"
#include "util/str.h"
#include "util/strbuf.h"
#include "util/term.h"
#include "util/tick.h"

#define STR_IMPL_(x) #x
#define STR(x) STR_IMPL_(x)

struct sc_option {
    char shortopt;
    int longopt_id; // either shortopt or longopt_id is non-zero
    const char *longopt;
    // no argument:       argdesc == NULL && !optional_arg
    // optional argument: argdesc != NULL && optional_arg
    // required argument: argdesc != NULL && !optional_arg
    const char *argdesc;
    bool optional_arg;
    const char *text; // if NULL, the option does not appear in the help
};

#define MAX_EQUIVALENT_SHORTCUTS 3
struct sc_shortcut {
    const char *shortcuts[MAX_EQUIVALENT_SHORTCUTS + 1];
    const char *text;
};

struct sc_envvar {
    const char *name;
    const char *text;
};

struct sc_exit_status {
    unsigned value;
    const char *text;
};

struct sc_getopt_adapter {
    char *optstring;
    struct option *longopts;
};

#include "cli_options.generated.inc"

#ifdef SC_TEST
size_t
sc_cli_option_count(void) {
    return ARRAY_LEN(options);
}

bool
sc_cli_option_get_test_info(size_t index, struct sc_cli_option_test_info *info) {
    if (index >= ARRAY_LEN(options)) {
        return false;
    }

    const struct sc_option *option = &options[index];
    *info = (struct sc_cli_option_test_info) {
        .longopt = option->longopt,
        .shortopt = option->shortopt,
        .has_arg = !!option->argdesc,
        .optional_arg = option->optional_arg,
        .documented = !!option->text,
    };
    return true;
}
#endif

static const struct sc_shortcut shortcuts[] = {
    {
        .shortcuts = { "MOD+q" },
        .text = "Quit",
    },
    {
        .shortcuts = { "MOD+f", "F11" },
        .text = "Switch fullscreen mode",
    },
    {
        .shortcuts = { "MOD+Left" },
        .text = "Rotate display left",
    },
    {
        .shortcuts = { "MOD+Right" },
        .text = "Rotate display right",
    },
    {
        .shortcuts = { "MOD+Shift+Left", "MOD+Shift+Right" },
        .text = "Flip display horizontally",
    },
    {
        .shortcuts = { "MOD+Shift+Up", "MOD+Shift+Down" },
        .text = "Flip display vertically",
    },
    {
        .shortcuts = { "MOD+z" },
        .text = "Pause or re-pause display",
    },
    {
        .shortcuts = { "MOD+Shift+z" },
        .text = "Unpause display",
    },
    {
        .shortcuts = { "MOD+Shift+r" },
        .text = "Reset video capture/encoding",
    },
    {
        .shortcuts = { "MOD+g" },
        .text = "Resize window to 1:1 (pixel-perfect)",
    },
    {
        .shortcuts = { "MOD+w", "Double-click on black borders" },
        .text = "Resize window to remove black borders",
    },
    {
        .shortcuts = { "MOD+h", "Middle-click" },
        .text = "Click on HOME",
    },
    {
        .shortcuts = {
            "MOD+b",
            "MOD+Backspace",
            "Right-click (when screen is on)",
        },
        .text = "Click on BACK",
    },
    {
        .shortcuts = { "MOD+s", "4th-click" },
        .text = "Click on APP_SWITCH",
    },
    {
        .shortcuts = { "MOD+m" },
        .text = "Click on MENU",
    },
    {
        .shortcuts = { "MOD+Up" },
        .text = "Click on VOLUME_UP",
    },
    {
        .shortcuts = { "MOD+Down" },
        .text = "Click on VOLUME_DOWN",
    },
    {
        .shortcuts = { "MOD+p" },
        .text = "Click on POWER (turn screen on/off)",
    },
    {
        .shortcuts = { "Right-click (when screen is off)" },
        .text = "Power on",
    },
    {
        .shortcuts = { "MOD+o" },
        .text = "Turn device screen off (keep mirroring)",
    },
    {
        .shortcuts = { "MOD+Shift+o" },
        .text = "Turn device screen on",
    },
    {
        .shortcuts = { "MOD+r" },
        .text = "Rotate device screen",
    },
    {
        .shortcuts = { "MOD+n", "5th-click" },
        .text = "Expand notification panel",
    },
    {
        .shortcuts = { "MOD+Shift+n" },
        .text = "Collapse notification panel",
    },
    {
        .shortcuts = { "MOD+c" },
        .text = "Copy to clipboard (inject COPY keycode, Android >= 7 only)",
    },
    {
        .shortcuts = { "MOD+x" },
        .text = "Cut to clipboard (inject CUT keycode, Android >= 7 only)",
    },
    {
        .shortcuts = { "MOD+v" },
        .text = "Copy computer clipboard to device, then paste (inject PASTE "
                "keycode, Android >= 7 only)",
    },
    {
        .shortcuts = { "MOD+Shift+v" },
        .text = "Inject computer clipboard text as a sequence of key events",
    },
    {
        .shortcuts = { "MOD+k" },
        .text = "Open keyboard settings on the device (for HID keyboard only)",
    },
    {
        .shortcuts = { "MOD+i" },
        .text = "Enable/disable FPS counter (print frames/second in logs)",
    },
    {
        .shortcuts = { "Ctrl+click-and-move" },
        .text = "Pinch-to-zoom and rotate from the center of the screen",
    },
    {
        .shortcuts = { "Shift+click-and-move" },
        .text = "Tilt vertically (slide with 2 fingers)",
    },
    {
        .shortcuts = { "Ctrl+Shift+click-and-move" },
        .text = "Tilt horizontally (slide with 2 fingers)",
    },
    {
        .shortcuts = { "Drag & drop APK file" },
        .text = "Install APK from computer",
    },
    {
        .shortcuts = { "Drag & drop non-APK file" },
        .text = "Push file to device (see --push-target)",
    },
    {
        .shortcuts = { "MOD+t" },
        .text = "Turn on the camera torch (camera mode only)",
    },
    {
        .shortcuts = { "MOD+Shift+t" },
        .text = "Turn off the camera torch (camera mode only)",
    },
    {
        .shortcuts = { "MOD+Up" },
        .text = "Zoom camera in (camera mode only)",
    },
    {
        .shortcuts = { "MOD+Down" },
        .text = "Zoom camera out (camera mode only)",
    },
};

static const struct sc_envvar envvars[] = {
    {
        .name = "ADB",
        .text = "Path to adb executable",
    },
    {
        .name = "ANDROID_SERIAL",
        .text = "Device serial to use if no selector (-s, -d, -e or "
                "--tcpip=<addr>) is specified",
    },
    {
        .name = "SCRCPY_ICON_DIR",
        .text = "Path to the icon directory",
    },
    {
        .name = "SCRCPY_SERVER_PATH",
        .text = "Path to the server binary",
    },
};

static const struct sc_exit_status exit_statuses[] = {
    {
        .value = 0,
        .text = "Normal program termination",
    },
    {
        .value = 1,
        .text = "Start failure",
    },
    {
        .value = 2,
        .text = "Device disconnected while running",
    },
};

static char *
sc_getopt_adapter_create_optstring(void) {
    struct sc_strbuf buf;
    if (!sc_strbuf_init(&buf, 64)) {
        return false;
    }

    for (size_t i = 0; i < ARRAY_LEN(options); ++i) {
        const struct sc_option *opt = &options[i];
        if (opt->shortopt) {
            if (!sc_strbuf_append_char(&buf, opt->shortopt)) {
                goto error;
            }
            // If there is an argument, add ':'
            if (opt->argdesc) {
                if (!sc_strbuf_append_char(&buf, ':')) {
                    goto error;
                }
                // If the argument is optional, add another ':'
                if (opt->optional_arg && !sc_strbuf_append_char(&buf, ':')) {
                    goto error;
                }
            }
        }
    }

    return buf.s;

error:
    free(buf.s);
    return NULL;
}

static struct option *
sc_getopt_adapter_create_longopts(void) {
    struct option *longopts =
        malloc((ARRAY_LEN(options) + 1) * sizeof(*longopts));
    if (!longopts) {
        LOG_OOM();
        return NULL;
    }

    size_t out_idx = 0;
    for (size_t i = 0; i < ARRAY_LEN(options); ++i) {
        const struct sc_option *in = &options[i];

        // If longopt_id is set, then longopt must be set
        assert(!in->longopt_id || in->longopt);

        if (!in->longopt) {
            // The longopts array must only contain long options
            continue;
        }
        struct option *out = &longopts[out_idx++];

        out->name = in->longopt;

        if (!in->argdesc) {
            assert(!in->optional_arg);
            out->has_arg = no_argument;
        } else if (in->optional_arg) {
            out->has_arg = optional_argument;
        } else {
            out->has_arg = required_argument;
        }

        out->flag = NULL;

        // Either shortopt or longopt_id is set, but not both
        assert(!!in->shortopt ^ !!in->longopt_id);
        out->val = in->shortopt ? in->shortopt : in->longopt_id;
    }

    // The array must be terminated by a NULL item
    longopts[out_idx] = (struct option) {0};

    return longopts;
}

static bool
sc_getopt_adapter_init(struct sc_getopt_adapter *adapter) {
    adapter->optstring = sc_getopt_adapter_create_optstring();
    if (!adapter->optstring) {
        return false;
    }

    adapter->longopts = sc_getopt_adapter_create_longopts();
    if (!adapter->longopts) {
        free(adapter->optstring);
        return false;
    }

    return true;
}

static void
sc_getopt_adapter_destroy(struct sc_getopt_adapter *adapter) {
    free(adapter->optstring);
    free(adapter->longopts);
}

static void
print_option_usage_header(const struct sc_option *opt) {
    struct sc_strbuf buf;
    if (!sc_strbuf_init(&buf, 64)) {
        goto error;
    }

    bool ok = true;
    (void) ok; // only used for assertions

    if (opt->shortopt) {
        ok = sc_strbuf_append_char(&buf, '-');
        assert(ok);

        ok = sc_strbuf_append_char(&buf, opt->shortopt);
        assert(ok);

        if (opt->longopt) {
            ok = sc_strbuf_append_staticstr(&buf, ", ");
            assert(ok);
        }
    }

    if (opt->longopt) {
        ok = sc_strbuf_append_staticstr(&buf, "--");
        assert(ok);

        if (!sc_strbuf_append_str(&buf, opt->longopt)) {
            goto error;
        }
    }

    if (opt->argdesc) {
        if (opt->optional_arg && !sc_strbuf_append_char(&buf, '[')) {
            goto error;
        }

        if (!sc_strbuf_append_char(&buf, '=')) {
            goto error;
        }

        if (!sc_strbuf_append_str(&buf, opt->argdesc)) {
            goto error;
        }

        if (opt->optional_arg && !sc_strbuf_append_char(&buf, ']')) {
            goto error;
        }
    }

    printf("\n    %s\n", buf.s);
    free(buf.s);
    return;

error:
    printf("<ERROR>\n");
}

static void
print_option_usage(const struct sc_option *opt, unsigned cols) {
    assert(cols > 8); // sc_str_wrap_lines() requires indent < columns

    if (!opt->text) {
        // Option not documented in help (for example because it is deprecated)
        return;
    }

    print_option_usage_header(opt);

    char *text = sc_str_wrap_lines(opt->text, cols, 8);
    if (!text) {
        printf("<ERROR>\n");
        return;
    }

    printf("%s\n", text);
    free(text);
}

static void
print_shortcuts_intro(unsigned cols) {
    char *intro = sc_str_wrap_lines(
        "In the following list, MOD is the shortcut modifier. By default, it's "
        "(left) Alt or (left) Super, but it can be configured by "
        "--shortcut-mod (see above).", cols, 4);
    if (!intro) {
        printf("<ERROR>\n");
        return;
    }

    printf("\n%s\n", intro);
    free(intro);
}

static void
print_shortcut(const struct sc_shortcut *shortcut, unsigned cols) {
    assert(cols > 8); // sc_str_wrap_lines() requires indent < columns
    assert(shortcut->shortcuts[0]); // At least one shortcut
    assert(shortcut->text);

    printf("\n");

    unsigned i = 0;
    while (shortcut->shortcuts[i]) {
        printf("    %s\n", shortcut->shortcuts[i]);
        ++i;
    }

    char *text = sc_str_wrap_lines(shortcut->text, cols, 8);
    if (!text) {
        printf("<ERROR>\n");
        return;
    }

    printf("%s\n", text);
    free(text);
}

static void
print_envvar(const struct sc_envvar *envvar, unsigned cols) {
    assert(cols > 8); // sc_str_wrap_lines() requires indent < columns
    assert(envvar->name);
    assert(envvar->text);

    printf("\n    %s\n", envvar->name);
    char *text = sc_str_wrap_lines(envvar->text, cols, 8);
    if (!text) {
        printf("<ERROR>\n");
        return;
    }

    printf("%s\n", text);
    free(text);
}

static void
print_exit_status(const struct sc_exit_status *status, unsigned cols) {
    assert(cols > 8); // sc_str_wrap_lines() requires indent < columns
    assert(status->text);

    // The text starts at 9: 4 ident spaces, 3 chars for numeric value, 2 spaces
    char *text = sc_str_wrap_lines(status->text, cols, 9);
    if (!text) {
        printf("<ERROR>\n");
        return;
    }

    assert(strlen(text) >= 9); // Contains at least the initial indentation

    // text + 9 to remove the initial indentation
    printf("    %3d  %s\n", status->value, text + 9);
    free(text);
}

void
scrcpy_print_usage(const char *arg0) {
#define SC_TERM_COLS_DEFAULT 80
    unsigned cols;

    if (!isatty(STDERR_FILENO)) {
        // Not a tty
        cols = SC_TERM_COLS_DEFAULT;
    } else {
        bool ok = sc_term_get_size(NULL, &cols);
        if (!ok) {
            // Could not get the terminal size
            cols = SC_TERM_COLS_DEFAULT;
        }
        if (cols < 20) {
            // Do not accept a too small value
            cols = 20;
        }
    }

    printf("Usage: %s [options]\n\n"
            "Options:\n", arg0);
    for (size_t i = 0; i < ARRAY_LEN(options); ++i) {
        print_option_usage(&options[i], cols);
    }

    // Print shortcuts section
    printf("\nShortcuts:\n");
    print_shortcuts_intro(cols);
    for (size_t i = 0; i < ARRAY_LEN(shortcuts); ++i) {
        print_shortcut(&shortcuts[i], cols);
    }

    // Print environment variables section
    printf("\nEnvironment variables:\n");
    for (size_t i = 0; i < ARRAY_LEN(envvars); ++i) {
        print_envvar(&envvars[i], cols);
    }

    printf("\nExit status:\n\n");
    for (size_t i = 0; i < ARRAY_LEN(exit_statuses); ++i) {
        print_exit_status(&exit_statuses[i], cols);
    }
}

static bool
parse_integer_arg(const char *s, long *out, bool accept_suffix, long min,
                  long max, const char *name) {
    long value;
    bool ok;
    if (accept_suffix) {
        ok = sc_str_parse_integer_with_suffix(s, &value);
    } else {
        ok = sc_str_parse_integer(s, &value);
    }
    if (!ok) {
        LOGE("Could not parse %s: %s", name, s);
        return false;
    }

    if (value < min || value > max) {
        LOGE("Could not parse %s: value (%ld) out-of-range (%ld; %ld)",
             name, value, min, max);
        return false;
    }

    *out = value;
    return true;
}

static size_t
parse_integers_arg(const char *s, const char sep, size_t max_items, long *out,
                   long min, long max, const char *name) {
    size_t count = sc_str_parse_integers(s, sep, max_items, out);
    if (!count) {
        LOGE("Could not parse %s: %s", name, s);
        return 0;
    }

    for (size_t i = 0; i < count; ++i) {
        long value = out[i];
        if (value < min || value > max) {
            LOGE("Could not parse %s: value (%ld) out-of-range (%ld; %ld)",
                 name, value, min, max);
            return 0;
        }
    }

    return count;
}

static bool
parse_bit_rate(const char *s, uint32_t *bit_rate) {
    long value;
    // long may be 32 bits (it is the case on mingw), so do not use more than
    // 31 bits (long is signed)
    bool ok = parse_integer_arg(s, &value, true, 0, 0x7FFFFFFF, "bit-rate");
    if (!ok) {
        return false;
    }

    *bit_rate = (uint32_t) value;
    return true;
}

static bool
parse_max_size(const char *s, uint16_t *max_size) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 0xFFFF, "max size");
    if (!ok) {
        return false;
    }

    *max_size = (uint16_t) value;
    return true;
}

static bool
parse_min_size_alignment(const char *s, uint8_t *min_size_alignment) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 1, 16, "min size alignment");
    if (!ok) {
        return false;
    }
    if (value & (value - 1)) {
        LOGE("The minimum size alignment (%ld) must be a power-of-2", value);
        return false;
    }

    *min_size_alignment = (uint8_t) value;
    return true;
}

static bool
parse_buffering_time(const char *s, sc_tick *tick) {
    long value;
    // In practice, buffering time should not exceed a few seconds.
    // Limit it to some arbitrary value (1 hour) to prevent 32-bit overflow
    // when multiplied by the audio sample size and the number of samples per
    // millisecond.
    bool ok = parse_integer_arg(s, &value, false, 0, 60 * 60 * 1000,
                                "buffering time");
    if (!ok) {
        return false;
    }

    *tick = SC_TICK_FROM_MS(value);
    return true;
}

static bool
parse_audio_output_buffer(const char *s, sc_tick *tick) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 1000,
                                "audio output buffer");
    if (!ok) {
        return false;
    }

    *tick = SC_TICK_FROM_MS(value);
    return true;
}

static bool
parse_display_ime_policy(const char *s, enum sc_display_ime_policy *policy) {
    if (!strcmp(s, "local")) {
        *policy = SC_DISPLAY_IME_POLICY_LOCAL;
        return true;
    }
    if (!strcmp(s, "fallback")) {
        *policy = SC_DISPLAY_IME_POLICY_FALLBACK;
        return true;
    }
    if (!strcmp(s, "hide")) {
        *policy = SC_DISPLAY_IME_POLICY_HIDE;
        return true;
    }
    LOGE("Unsupported display IME policy: %s (expected local, fallback or "
         "hide)", s);
    return false;
}

static bool
parse_orientation(const char *s, enum sc_orientation *orientation) {
    if (!strcmp(s, "0")) {
        *orientation = SC_ORIENTATION_0;
        return true;
    }
    if (!strcmp(s, "90")) {
        *orientation = SC_ORIENTATION_90;
        return true;
    }
    if (!strcmp(s, "180")) {
        *orientation = SC_ORIENTATION_180;
        return true;
    }
    if (!strcmp(s, "270")) {
        *orientation = SC_ORIENTATION_270;
        return true;
    }
    if (!strcmp(s, "flip0")) {
        *orientation = SC_ORIENTATION_FLIP_0;
        return true;
    }
    if (!strcmp(s, "flip90")) {
        *orientation = SC_ORIENTATION_FLIP_90;
        return true;
    }
    if (!strcmp(s, "flip180")) {
        *orientation = SC_ORIENTATION_FLIP_180;
        return true;
    }
    if (!strcmp(s, "flip270")) {
        *orientation = SC_ORIENTATION_FLIP_270;
        return true;
    }
    LOGE("Unsupported orientation: %s (expected 0, 90, 180, 270, flip0, "
         "flip90, flip180 or flip270)", s);
    return false;
}

static bool
parse_capture_orientation(const char *s, enum sc_orientation *orientation,
                          enum sc_orientation_lock *lock) {
    if (*s == '\0') {
        LOGE("Capture orientation may not be empty (expected 0, 90, 180, 270, "
             "flip0, flip90, flip180 or flip270, possibly prefixed by '@')");
        return false;
    }

    // Lock the orientation by a leading '@'
    if (s[0] == '@') {
        // Consume '@'
        ++s;
        if (*s == '\0') {
            // Only '@': lock to the initial orientation (orientation is unused)
            *lock = SC_ORIENTATION_LOCKED_INITIAL;
            return true;
        }
        *lock = SC_ORIENTATION_LOCKED_VALUE;
    } else {
        *lock = SC_ORIENTATION_UNLOCKED;
    }

    return parse_orientation(s, orientation);
}

static bool
parse_window_position(const char *s, int16_t *position) {
    // special value for "auto"
    static_assert(SC_WINDOW_POSITION_UNDEFINED == -0x8000, "unexpected value");

    if (!strcmp(s, "auto")) {
        *position = SC_WINDOW_POSITION_UNDEFINED;
        return true;
    }

    long value;
    bool ok = parse_integer_arg(s, &value, false, -0x7FFF, 0x7FFF,
                                "window position");
    if (!ok) {
        return false;
    }

    *position = (int16_t) value;
    return true;
}

static bool
parse_window_dimension(const char *s, uint16_t *dimension) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 0xFFFF,
                                "window dimension");
    if (!ok) {
        return false;
    }

    *dimension = (uint16_t) value;
    return true;
}

static bool
parse_port_range(const char *s, struct sc_port_range *port_range) {
    long values[2];
    size_t count = parse_integers_arg(s, ':', 2, values, 0, 0xFFFF, "port");
    if (!count) {
        return false;
    }

    uint16_t v0 = (uint16_t) values[0];
    if (count == 1) {
        port_range->first = v0;
        port_range->last = v0;
        return true;
    }

    assert(count == 2);
    uint16_t v1 = (uint16_t) values[1];
    if (v0 < v1) {
        port_range->first = v0;
        port_range->last = v1;
    } else {
        port_range->first = v1;
        port_range->last = v0;
    }

    return true;
}

static bool
parse_display_id(const char *s, uint32_t *display_id) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 0x7FFFFFFF, "display id");
    if (!ok) {
        return false;
    }

    *display_id = (uint32_t) value;
    return true;
}

static bool
parse_log_level(const char *s, enum sc_log_level *log_level) {
    if (!strcmp(s, "verbose")) {
        *log_level = SC_LOG_LEVEL_VERBOSE;
        return true;
    }

    if (!strcmp(s, "debug")) {
        *log_level = SC_LOG_LEVEL_DEBUG;
        return true;
    }

    if (!strcmp(s, "info")) {
        *log_level = SC_LOG_LEVEL_INFO;
        return true;
    }

    if (!strcmp(s, "warn")) {
        *log_level = SC_LOG_LEVEL_WARN;
        return true;
    }

    if (!strcmp(s, "error")) {
        *log_level = SC_LOG_LEVEL_ERROR;
        return true;
    }

    LOGE("Could not parse log level: %s", s);
    return false;
}

static enum sc_shortcut_mod
parse_shortcut_mods_item(const char *item, size_t len) {
#define STREQ(literal, s, len) \
    ((sizeof(literal)-1 == len) && !memcmp(literal, s, len))

    if (STREQ("lctrl", item, len)) {
        return SC_SHORTCUT_MOD_LCTRL;
    }
    if (STREQ("rctrl", item, len)) {
        return SC_SHORTCUT_MOD_RCTRL;
    }
    if (STREQ("lalt", item, len)) {
        return SC_SHORTCUT_MOD_LALT;
    }
    if (STREQ("ralt", item, len)) {
        return SC_SHORTCUT_MOD_RALT;
    }
    if (STREQ("lsuper", item, len)) {
        return SC_SHORTCUT_MOD_LSUPER;
    }
    if (STREQ("rsuper", item, len)) {
        return SC_SHORTCUT_MOD_RSUPER;
    }
#undef STREQ

    bool has_plus = strchr(item, '+');
    if (has_plus) {
        LOGE("Shortcut mod combination with '+' is not supported anymore: "
             "'%.*s' (see #4741)", (int) len, item);
        return 0;
    }

    LOGE("Unknown modifier key: %.*s "
         "(must be one of: lctrl, rctrl, lalt, ralt, lsuper, rsuper)",
         (int) len, item);

    return 0;
}

static bool
parse_shortcut_mods(const char *s, uint8_t *shortcut_mods) {
    uint8_t mods = 0;

    // A list of shortcut modifiers, for example "lctrl,rctrl,rsuper"

    for (;;) {
        char *comma = strchr(s, ',');
        assert(!comma || comma > s);
        size_t limit = comma ? (size_t) (comma - s) : strlen(s);

        enum sc_shortcut_mod mod = parse_shortcut_mods_item(s, limit);
        if (!mod) {
            return false;
        }

        mods |= mod;

        if (!comma) {
            break;
        }

        s = comma + 1;
    }

    *shortcut_mods = mods;

    return true;
}

#ifdef SC_TEST
// expose the function to unit-tests
bool
sc_parse_shortcut_mods(const char *s, uint8_t *mods) {
    return parse_shortcut_mods(s, mods);
}
#endif

static enum sc_record_format
get_record_format(const char *name) {
    if (!strcmp(name, "mp4")) {
        return SC_RECORD_FORMAT_MP4;
    }
    if (!strcmp(name, "mkv")) {
        return SC_RECORD_FORMAT_MKV;
    }
    if (!strcmp(name, "m4a")) {
        return SC_RECORD_FORMAT_M4A;
    }
    if (!strcmp(name, "mka")) {
        return SC_RECORD_FORMAT_MKA;
    }
    if (!strcmp(name, "opus")) {
        return SC_RECORD_FORMAT_OPUS;
    }
    if (!strcmp(name, "aac")) {
        return SC_RECORD_FORMAT_AAC;
    }
    if (!strcmp(name, "flac")) {
        return SC_RECORD_FORMAT_FLAC;
    }
    if (!strcmp(name, "wav")) {
        return SC_RECORD_FORMAT_WAV;
    }
    return 0;
}

static bool
parse_record_format(const char *optarg, enum sc_record_format *format) {
    enum sc_record_format fmt = get_record_format(optarg);
    if (!fmt) {
        LOGE("Unsupported record format: %s (expected mp4, mkv, m4a, mka, "
             "opus, aac, flac or wav)", optarg);
        return false;
    }

    *format = fmt;
    return true;
}

static bool
parse_ip(const char *optarg, uint32_t *ipv4) {
    return net_parse_ipv4(optarg, ipv4);
}

static bool
parse_port(const char *optarg, uint16_t *port) {
    long value;
    if (!parse_integer_arg(optarg, &value, false, 0, 0xFFFF, "port")) {
        return false;
    }
    *port = (uint16_t) value;
    return true;
}

static enum sc_record_format
guess_record_format(const char *filename) {
    const char *dot = strrchr(filename, '.');
    if (!dot) {
        return 0;
    }

    const char *ext = dot + 1;
    return get_record_format(ext);
}

static bool
parse_video_codec(const char *optarg, enum sc_codec *codec) {
    if (!strcmp(optarg, "h264")) {
        *codec = SC_CODEC_H264;
        return true;
    }
    if (!strcmp(optarg, "h265")) {
        *codec = SC_CODEC_H265;
        return true;
    }
    if (!strcmp(optarg, "av1")) {
        *codec = SC_CODEC_AV1;
        return true;
    }
    LOGE("Unsupported video codec: %s (expected h264, h265 or av1)", optarg);
    return false;
}

static bool
parse_audio_codec(const char *optarg, enum sc_codec *codec) {
    if (!strcmp(optarg, "opus")) {
        *codec = SC_CODEC_OPUS;
        return true;
    }
    if (!strcmp(optarg, "aac")) {
        *codec = SC_CODEC_AAC;
        return true;
    }
    if (!strcmp(optarg, "flac")) {
        *codec = SC_CODEC_FLAC;
        return true;
    }
    if (!strcmp(optarg, "raw")) {
        *codec = SC_CODEC_RAW;
        return true;
    }
    LOGE("Unsupported audio codec: %s (expected opus, aac, flac or raw)",
         optarg);
    return false;
}

static bool
parse_video_source(const char *optarg, enum sc_video_source *source) {
    if (!strcmp(optarg, "display")) {
        *source = SC_VIDEO_SOURCE_DISPLAY;
        return true;
    }

    if (!strcmp(optarg, "camera")) {
        *source = SC_VIDEO_SOURCE_CAMERA;
        return true;
    }

    LOGE("Unsupported video source: %s (expected display or camera)", optarg);
    return false;
}

static bool
parse_audio_source(const char *optarg, enum sc_audio_source *source) {
    if (!strcmp(optarg, "mic")) {
        *source = SC_AUDIO_SOURCE_MIC;
        return true;
    }

    if (!strcmp(optarg, "output")) {
        *source = SC_AUDIO_SOURCE_OUTPUT;
        return true;
    }

    if (!strcmp(optarg, "playback")) {
        *source = SC_AUDIO_SOURCE_PLAYBACK;
        return true;
    }

    if (!strcmp(optarg, "mic-unprocessed")) {
        *source = SC_AUDIO_SOURCE_MIC_UNPROCESSED;
        return true;
    }

    if (!strcmp(optarg, "mic-camcorder")) {
        *source = SC_AUDIO_SOURCE_MIC_CAMCORDER;
        return true;
    }

    if (!strcmp(optarg, "mic-voice-recognition")) {
        *source = SC_AUDIO_SOURCE_MIC_VOICE_RECOGNITION;
        return true;
    }

    if (!strcmp(optarg, "mic-voice-communication")) {
        *source = SC_AUDIO_SOURCE_MIC_VOICE_COMMUNICATION;
        return true;
    }

    if (!strcmp(optarg, "voice-call")) {
        *source = SC_AUDIO_SOURCE_VOICE_CALL;
        return true;
    }

    if (!strcmp(optarg, "voice-call-uplink")) {
        *source = SC_AUDIO_SOURCE_VOICE_CALL_UPLINK;
        return true;
    }

    if (!strcmp(optarg, "voice-call-downlink")) {
        *source = SC_AUDIO_SOURCE_VOICE_CALL_DOWNLINK;
        return true;
    }

    if (!strcmp(optarg, "voice-performance")) {
        *source = SC_AUDIO_SOURCE_VOICE_PERFORMANCE;
        return true;
    }

    LOGE("Unsupported audio source: %s (expected output, mic, playback, "
         "mic-unprocessed, mic-camcorder, mic-voice-recognition, "
         "mic-voice-communication, voice-call, voice-call-uplink, "
         "voice-call-downlink, voice-performance)", optarg);
    return false;
}

static bool
parse_camera_facing(const char *optarg, enum sc_camera_facing *facing) {
    if (!strcmp(optarg, "front")) {
        *facing = SC_CAMERA_FACING_FRONT;
        return true;
    }

    if (!strcmp(optarg, "back")) {
        *facing = SC_CAMERA_FACING_BACK;
        return true;
    }

    if (!strcmp(optarg, "external")) {
        *facing = SC_CAMERA_FACING_EXTERNAL;
        return true;
    }

    if (*optarg == '\0') {
        // Empty string is a valid value (equivalent to not passing the option)
        *facing = SC_CAMERA_FACING_ANY;
        return true;
    }

    LOGE("Unsupported camera facing: %s (expected front, back or external)",
         optarg);
    return false;
}

static bool
parse_camera_fps(const char *s, uint16_t *camera_fps) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 0xFFFF, "camera fps");
    if (!ok) {
        return false;
    }

    *camera_fps = (uint16_t) value;
    return true;
}

static bool
parse_keyboard(const char *optarg, enum sc_keyboard_input_mode *mode) {
    if (!strcmp(optarg, "disabled")) {
        *mode = SC_KEYBOARD_INPUT_MODE_DISABLED;
        return true;
    }

    if (!strcmp(optarg, "sdk")) {
        *mode = SC_KEYBOARD_INPUT_MODE_SDK;
        return true;
    }

    if (!strcmp(optarg, "uhid")) {
        *mode = SC_KEYBOARD_INPUT_MODE_UHID;
        return true;
    }

    if (!strcmp(optarg, "aoa")) {
#ifdef HAVE_USB
        *mode = SC_KEYBOARD_INPUT_MODE_AOA;
        return true;
#else
        LOGE("--keyboard=aoa is disabled.");
        return false;
#endif
    }

    LOGE("Unsupported keyboard: %s (expected disabled, sdk, uhid or aoa)",
         optarg);
    return false;
}

static bool
parse_mouse(const char *optarg, enum sc_mouse_input_mode *mode) {
    if (!strcmp(optarg, "disabled")) {
        *mode = SC_MOUSE_INPUT_MODE_DISABLED;
        return true;
    }

    if (!strcmp(optarg, "sdk")) {
        *mode = SC_MOUSE_INPUT_MODE_SDK;
        return true;
    }

    if (!strcmp(optarg, "uhid")) {
        *mode = SC_MOUSE_INPUT_MODE_UHID;
        return true;
    }

    if (!strcmp(optarg, "aoa")) {
#ifdef HAVE_USB
        *mode = SC_MOUSE_INPUT_MODE_AOA;
        return true;
#else
        LOGE("--mouse=aoa is disabled.");
        return false;
#endif
    }

    LOGE("Unsupported mouse: %s (expected disabled, sdk, uhid or aoa)", optarg);
    return false;
}

static bool
parse_gamepad(const char *optarg, enum sc_gamepad_input_mode *mode) {
    if (!strcmp(optarg, "disabled")) {
        *mode = SC_GAMEPAD_INPUT_MODE_DISABLED;
        return true;
    }

    if (!strcmp(optarg, "uhid")) {
        *mode = SC_GAMEPAD_INPUT_MODE_UHID;
        return true;
    }

    if (!strcmp(optarg, "aoa")) {
#ifdef HAVE_USB
        *mode = SC_GAMEPAD_INPUT_MODE_AOA;
        return true;
#else
        LOGE("--gamepad=aoa is disabled.");
        return false;
#endif
    }

    LOGE("Unsupported gamepad: %s (expected disabled or aoa)", optarg);
    return false;
}

static bool
parse_time_limit(const char *s, sc_tick *tick) {
    long value;
    bool ok = parse_integer_arg(s, &value, false, 0, 0x7FFFFFFF, "time limit");
    if (!ok) {
        return false;
    }

    *tick = SC_TICK_FROM_SEC(value);
    return true;
}

static bool
parse_screen_off_timeout(const char *s, sc_tick *tick) {
    long value;
    // value in seconds, but must fit in 31 bits in milliseconds
    bool ok = parse_integer_arg(s, &value, false, 0, 0x7FFFFFFF / 1000,
                                "screen off timeout");
    if (!ok) {
        return false;
    }

    *tick = SC_TICK_FROM_SEC(value);
    return true;
}

static bool
parse_pause_on_exit(const char *s, enum sc_pause_on_exit *pause_on_exit) {
    if (!s || !strcmp(s, "true")) {
        *pause_on_exit = SC_PAUSE_ON_EXIT_TRUE;
        return true;
    }

    if (!strcmp(s, "false")) {
        *pause_on_exit = SC_PAUSE_ON_EXIT_FALSE;
        return true;
    }

    if (!strcmp(s, "if-error")) {
        *pause_on_exit = SC_PAUSE_ON_EXIT_IF_ERROR;
        return true;
    }

    LOGE("Unsupported pause on exit mode: %s "
         "(expected true, false or if-error)", s);
    return false;

}

static bool
parse_mouse_binding(char c, enum sc_mouse_binding *b) {
    switch (c) {
        case '+':
            *b = SC_MOUSE_BINDING_CLICK;
            return true;
        case '-':
            *b = SC_MOUSE_BINDING_DISABLED;
            return true;
        case 'b':
            *b = SC_MOUSE_BINDING_BACK;
            return true;
        case 'h':
            *b = SC_MOUSE_BINDING_HOME;
            return true;
        case 's':
            *b = SC_MOUSE_BINDING_APP_SWITCH;
            return true;
        case 'n':
            *b = SC_MOUSE_BINDING_EXPAND_NOTIFICATION_PANEL;
            return true;
        default:
            LOGE("Invalid mouse binding: '%c' "
                 "(expected '+', '-', 'b', 'h', 's' or 'n')", c);
            return false;
    }
}

static bool
parse_mouse_binding_set(const char *s, struct sc_mouse_binding_set *mbs) {
    assert(strlen(s) >= 4);

    if (!parse_mouse_binding(s[0], &mbs->right_click)) {
        return false;
    }
    if (!parse_mouse_binding(s[1], &mbs->middle_click)) {
        return false;
    }
    if (!parse_mouse_binding(s[2], &mbs->click4)) {
        return false;
    }
    if (!parse_mouse_binding(s[3], &mbs->click5)) {
        return false;
    }

    return true;
}

static bool
parse_mouse_bindings(const char *s, struct sc_mouse_bindings *mb) {
    size_t len = strlen(s);
    // either "xxxx" or "xxxx:xxxx"
    if (len != 4 && (len != 9 || s[4] != ':')) {
        LOGE("Invalid mouse bindings: '%s' (expected 'xxxx' or 'xxxx:xxxx', "
             "with each 'x' being in {'+', '-', 'b', 'h', 's', 'n'})", s);
        return false;
    }

    if (!parse_mouse_binding_set(s, &mb->pri)) {
        return false;
    }

    if (len == 9) {
        if (!parse_mouse_binding_set(s + 5, &mb->sec)) {
            return false;
        }
    } else {
        // use the same bindings for Shift+click
        mb->sec = mb->pri;
    }

    return true;
}

static bool
parse_hex_char(char c, uint8_t *value) {
    if (c >= '0' && c <= '9') {
        *value = c - '0';
        return true;
    }
    if (c >= 'a' && c <= 'f') {
        *value = c - 'a' + 10;
        return true;
    }
    if (c >= 'A' && c <= 'F') {
        *value = c - 'A' + 10;
        return true;
    }
    return false;
}

static bool
parse_hex_byte(const char *s, uint8_t *value) {
    uint8_t left, right;
    bool ok = parse_hex_char(s[0], &left)
           && parse_hex_char(s[1], &right);
    if (!ok) {
        return false;
    }
    *value = left << 4 | right;
    return true;
}

static bool
parse_hex_color(const char *s, uint32_t *color) {
    if (s[0] == '#') {
        // Accept with and without a leading '#'
        ++s;
    }

    size_t len = strlen(s);
    if (len != 3 && len != 6) {
        LOGE("Invalid hexadecimal color code (expected #RGB or #RRGGBB): "
             "%s", s);
        return false;
    }

    uint8_t rgb[3];
    if (len == 3) {
        for (size_t i = 0; i < 3; ++i) {
            bool ok = parse_hex_char(s[i], &rgb[i]);
            if (!ok) {
                LOGE("Invalid hexadecimal color code: %s", s);
                return false;
            }
            rgb[i] *= 0x11;
        }
    } else {
        assert(len == 6);
        for (size_t i = 0; i < 3; ++i) {
            bool ok = parse_hex_byte(&s[2*i], &rgb[i]);
            if (!ok) {
                LOGE("Invalid hexadecimal color code: %s", s);
                return false;
            }
        }
    }

    *color = rgb[0] << 16 | rgb[1] << 8 | rgb[2];
    return true;
}

static bool
parse_render_fit(const char *optarg, enum sc_render_fit *mode) {
    if (!strcmp(optarg, "letterbox")) {
        *mode = SC_RENDER_FIT_LETTERBOX;
        return true;
    }

    if (!strcmp(optarg, "stretched")) {
        *mode = SC_RENDER_FIT_STRETCHED;
        return true;
    }

    if (!strcmp(optarg, "unscaled")) {
        *mode = SC_RENDER_FIT_UNSCALED;
        return true;
    }

    LOGE("Unsupported render-fit: %s (expected letterbox, stretched or "
         "unscaled)", optarg);
    return false;
}

static bool
parse_args_with_getopt(struct scrcpy_cli_args *args, int argc, char *argv[],
                       const char *optstring, const struct option *longopts) {
    struct scrcpy_options *opts = &args->opts;

    optind = 0; // reset to start from the first argument in tests

    int c;
    while ((c = getopt_long(argc, argv, optstring, longopts, NULL)) != -1) {
        switch (c) {
            case 'b':
                if (!parse_bit_rate(optarg, &opts->video_bit_rate)) {
                    return false;
                }
                break;
            case OPT_AUDIO_BIT_RATE:
                if (!parse_bit_rate(optarg, &opts->audio_bit_rate)) {
                    return false;
                }
                break;
            case OPT_CROP:
                opts->crop = optarg;
                break;
            case OPT_DISPLAY_ID:
                if (!parse_display_id(optarg, &opts->display_id)) {
                    return false;
                }
                break;
            case 'd':
                opts->select_usb = true;
                break;
            case 'e':
                opts->select_tcpip = true;
                break;
            case 'f':
                opts->fullscreen = true;
                break;
            case OPT_RECORD_FORMAT:
                if (!parse_record_format(optarg, &opts->record_format)) {
                    return false;
                }
                break;
            case 'h':
                args->help = true;
                break;
            case 'K':
                opts->keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_UHID_OR_AOA;
                break;
            case OPT_KEYBOARD:
                if (!parse_keyboard(optarg, &opts->keyboard_input_mode)) {
                    return false;
                }
                break;
            case OPT_MAX_FPS:
                opts->max_fps = optarg;
                break;
            case 'm':
                if (!parse_max_size(optarg, &opts->max_size)) {
                    return false;
                }
                break;
            case 'M':
                opts->mouse_input_mode = SC_MOUSE_INPUT_MODE_UHID_OR_AOA;
                break;
            case OPT_MOUSE:
                if (!parse_mouse(optarg, &opts->mouse_input_mode)) {
                    return false;
                }
                break;
            case OPT_MOUSE_BIND:
                if (!parse_mouse_bindings(optarg, &opts->mouse_bindings)) {
                    return false;
                }
                break;
            case OPT_NO_MOUSE_HOVER:
                opts->mouse_hover = false;
                break;
            case OPT_CAPTURE_ORIENTATION:
                if (!parse_capture_orientation(optarg,
                                          &opts->capture_orientation,
                                          &opts->capture_orientation_lock)) {
                    return false;
                }
                break;
            case OPT_TUNNEL_HOST:
                if (!parse_ip(optarg, &opts->tunnel_host)) {
                    return false;
                }
                break;
            case OPT_TUNNEL_PORT:
                if (!parse_port(optarg, &opts->tunnel_port)) {
                    return false;
                }
                break;
            case 'n':
                opts->control = false;
                break;
            case 'N':
                opts->video_playback = false;
                opts->audio_playback = false;
                break;
            case OPT_NO_VIDEO_PLAYBACK:
                opts->video_playback = false;
                break;
            case OPT_NO_AUDIO_PLAYBACK:
                opts->audio_playback = false;
                break;
            case 'p':
                if (!parse_port_range(optarg, &opts->port_range)) {
                    return false;
                }
                break;
            case 'r':
                opts->record_filename = optarg;
                break;
            case 's':
                opts->serial = optarg;
                break;
            case 'S':
                opts->turn_screen_off = true;
                break;
            case 't':
                opts->show_touches = true;
                break;
            case OPT_ALWAYS_ON_TOP:
                opts->always_on_top = true;
                break;
            case 'v':
                args->version = true;
                break;
            case 'V':
                if (!parse_log_level(optarg, &opts->log_level)) {
                    return false;
                }
                break;
            case 'w':
                opts->stay_awake = true;
                break;
            case OPT_WINDOW_TITLE:
                opts->window_title = optarg;
                break;
            case OPT_WINDOW_X:
                if (!parse_window_position(optarg, &opts->window_x)) {
                    return false;
                }
                break;
            case OPT_WINDOW_Y:
                if (!parse_window_position(optarg, &opts->window_y)) {
                    return false;
                }
                break;
            case OPT_WINDOW_WIDTH:
                if (!parse_window_dimension(optarg, &opts->window_width)) {
                    return false;
                }
                break;
            case OPT_WINDOW_HEIGHT:
                if (!parse_window_dimension(optarg, &opts->window_height)) {
                    return false;
                }
                break;
            case OPT_WINDOW_BORDERLESS:
                opts->window_borderless = true;
                break;
            case OPT_PUSH_TARGET:
                opts->push_target = optarg;
                break;
            case OPT_PREFER_TEXT:
                if (opts->key_inject_mode != SC_KEY_INJECT_MODE_MIXED) {
                    LOGE("--prefer-text is incompatible with --raw-key-events");
                    return false;
                }
                opts->key_inject_mode = SC_KEY_INJECT_MODE_TEXT;
                break;
            case OPT_RAW_KEY_EVENTS:
                if (opts->key_inject_mode != SC_KEY_INJECT_MODE_MIXED) {
                    LOGE("--prefer-text is incompatible with --raw-key-events");
                    return false;
                }
                opts->key_inject_mode = SC_KEY_INJECT_MODE_RAW;
                break;
            case OPT_DISPLAY_ORIENTATION:
                if (!parse_orientation(optarg, &opts->display_orientation)) {
                    return false;
                }
                break;
            case OPT_RECORD_ORIENTATION:
                if (!parse_orientation(optarg, &opts->record_orientation)) {
                    return false;
                }
                break;
            case OPT_ORIENTATION: {
                enum sc_orientation orientation;
                if (!parse_orientation(optarg, &orientation)) {
                    return false;
                }
                opts->display_orientation = orientation;
                opts->record_orientation = orientation;
                break;
            }
            case OPT_RENDER_DRIVER:
                opts->render_driver = optarg;
                break;
            case OPT_NO_MIPMAPS:
                opts->mipmaps = false;
                break;
            case OPT_NO_KEY_REPEAT:
                opts->forward_key_repeat = false;
                break;
            case OPT_VIDEO_CODEC_OPTIONS:
                opts->video_codec_options = optarg;
                break;
            case OPT_AUDIO_CODEC_OPTIONS:
                opts->audio_codec_options = optarg;
                break;
            case OPT_VIDEO_ENCODER:
                opts->video_encoder = optarg;
                break;
            case OPT_AUDIO_ENCODER:
                opts->audio_encoder = optarg;
                break;
            case OPT_FORCE_ADB_FORWARD:
                opts->force_adb_forward = true;
                break;
            case OPT_DISABLE_SCREENSAVER:
                opts->disable_screensaver = true;
                break;
            case OPT_SHORTCUT_MOD:
                if (!parse_shortcut_mods(optarg, &opts->shortcut_mods)) {
                    return false;
                }
                break;
            case OPT_LEGACY_PASTE:
                opts->legacy_paste = true;
                break;
            case OPT_POWER_OFF_ON_CLOSE:
                opts->power_off_on_close = true;
                break;
            case OPT_VIDEO_BUFFER:
                if (!parse_buffering_time(optarg, &opts->video_buffer)) {
                    return false;
                }
                break;
            case OPT_NO_CLIPBOARD_AUTOSYNC:
                opts->clipboard_autosync = false;
                break;
            case OPT_TCPIP:
                opts->tcpip = true;
                opts->tcpip_dst = optarg;
                break;
            case OPT_NO_DOWNSIZE_ON_ERROR:
                opts->downsize_on_error = false;
                break;
            case OPT_NO_VIDEO:
                opts->video = false;
                break;
            case OPT_NO_AUDIO:
                opts->audio = false;
                break;
            case OPT_NO_CLEANUP:
                opts->cleanup = false;
                break;
            case OPT_NO_POWER_ON:
                opts->power_on = false;
                break;
            case OPT_PRINT_FPS:
                opts->start_fps_counter = true;
                break;
            case OPT_VIDEO_CODEC:
                if (!parse_video_codec(optarg, &opts->video_codec)) {
                    return false;
                }
                break;
            case OPT_AUDIO_CODEC:
                if (!parse_audio_codec(optarg, &opts->audio_codec)) {
                    return false;
                }
                break;
            case OPT_OTG:
#ifdef HAVE_USB
                opts->otg = true;
                break;
#else
                LOGE("OTG mode (--otg) is disabled.");
                return false;
#endif
            case OPT_V4L2_SINK:
#ifdef HAVE_V4L2
                opts->v4l2_device = optarg;
                break;
#else
                LOGE("V4L2 (--v4l2-sink) is disabled (or unsupported on this "
                     "platform).");
                return false;
#endif
            case OPT_V4L2_BUFFER:
#ifdef HAVE_V4L2
                if (!parse_buffering_time(optarg, &opts->v4l2_buffer)) {
                    return false;
                }
                break;
#else
                LOGE("V4L2 (--v4l2-buffer) is disabled (or unsupported on this "
                     "platform).");
                return false;
#endif
            case OPT_LIST_ENCODERS:
                opts->list |= SC_OPTION_LIST_ENCODERS;
                break;
            case OPT_LIST_DISPLAYS:
                opts->list |= SC_OPTION_LIST_DISPLAYS;
                break;
            case OPT_LIST_CAMERAS:
                opts->list |= SC_OPTION_LIST_CAMERAS;
                break;
            case OPT_LIST_CAMERA_SIZES:
                opts->list |= SC_OPTION_LIST_CAMERA_SIZES;
                break;
            case OPT_LIST_APPS:
                opts->list |= SC_OPTION_LIST_APPS;
                break;
            case OPT_REQUIRE_AUDIO:
                opts->require_audio = true;
                break;
            case OPT_AUDIO_BUFFER:
                if (!parse_buffering_time(optarg, &opts->audio_buffer)) {
                    return false;
                }
                break;
            case OPT_AUDIO_OUTPUT_BUFFER:
                if (!parse_audio_output_buffer(optarg,
                                               &opts->audio_output_buffer)) {
                    return false;
                }
                break;
            case OPT_VIDEO_SOURCE:
                if (!parse_video_source(optarg, &opts->video_source)) {
                    return false;
                }
                break;
            case OPT_AUDIO_SOURCE:
                if (!parse_audio_source(optarg, &opts->audio_source)) {
                    return false;
                }
                break;
            case OPT_KILL_ADB_ON_CLOSE:
                opts->kill_adb_on_close = true;
                break;
            case OPT_TIME_LIMIT:
                if (!parse_time_limit(optarg, &opts->time_limit)) {
                    return false;
                }
                break;
            case OPT_PAUSE_ON_EXIT:
                if (!parse_pause_on_exit(optarg, &args->pause_on_exit)) {
                    return false;
                }
                break;
            case OPT_CAMERA_AR:
                opts->camera_ar = optarg;
                break;
            case OPT_CAMERA_ID:
                opts->camera_id = optarg;
                break;
            case OPT_CAMERA_SIZE:
                opts->camera_size = optarg;
                break;
            case OPT_CAMERA_FACING:
                if (!parse_camera_facing(optarg, &opts->camera_facing)) {
                    return false;
                }
                break;
            case OPT_CAMERA_FPS:
                if (!parse_camera_fps(optarg, &opts->camera_fps)) {
                    return false;
                }
                break;
            case OPT_CAMERA_HIGH_SPEED:
                opts->camera_high_speed = true;
                break;
            case OPT_CAMERA_TORCH:
                opts->camera_torch = true;
                break;
            case OPT_CAMERA_ZOOM:
                opts->camera_zoom = optarg;
                break;
            case OPT_NO_WINDOW:
                opts->window = false;
                break;
            case OPT_AUDIO_DUP:
                opts->audio_dup = true;
                break;
            case 'G':
                opts->gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_UHID_OR_AOA;
                break;
            case OPT_GAMEPAD:
                if (!parse_gamepad(optarg, &opts->gamepad_input_mode)) {
                    return false;
                }
                break;
            case OPT_NEW_DISPLAY:
                opts->new_display = optarg ? optarg : "";
                break;
            case OPT_START_APP:
                opts->start_app = optarg;
                break;
            case OPT_SCREEN_OFF_TIMEOUT:
                if (!parse_screen_off_timeout(optarg,
                                              &opts->screen_off_timeout)) {
                    return false;
                }
                break;
            case OPT_ANGLE:
                opts->angle = optarg;
                break;
            case OPT_NO_VD_DESTROY_CONTENT:
                opts->vd_destroy_content = false;
                break;
            case OPT_NO_VD_SYSTEM_DECORATIONS:
                opts->vd_system_decorations = false;
                break;
            case OPT_DISPLAY_IME_POLICY:
                if (!parse_display_ime_policy(optarg,
                                              &opts->display_ime_policy)) {
                    return false;
                }
                break;
            case OPT_MIN_SIZE_ALIGNMENT:
                if (!parse_min_size_alignment(optarg,
                                              &opts->min_size_alignment)) {
                    return false;
                }
                break;
            case OPT_NO_WINDOW_ASPECT_RATIO_LOCK:
                opts->window_aspect_ratio_lock = false;
                break;
            case OPT_KEEP_ACTIVE:
                opts->keep_active = true;
                break;
            case OPT_BACKGROUND_COLOR:
                if (!parse_hex_color(optarg, &opts->background_color)) {
                    return false;
                }
                break;
            case OPT_RENDER_FIT:
                if (!parse_render_fit(optarg, &opts->render_fit)) {
                    return false;
                }
                break;
            case 'x':
                opts->flex_display = true;
                break;
            default:
                // getopt prints the error message on stderr
                return false;
        }
    }

    int index = optind;
    if (index < argc) {
        LOGE("Unexpected additional argument: %s", argv[index]);
        return false;
    }

    // If a TCP/IP address is provided, then tcpip must be enabled
    assert(opts->tcpip || !opts->tcpip_dst);

    unsigned selectors = !!opts->serial
                       + !!opts->tcpip_dst
                       + opts->select_tcpip
                       + opts->select_usb;
    if (selectors > 1) {
        LOGE("At most one device selector option may be passed, among:\n"
             "  --serial (-s)\n"
             "  --select-usb (-d)\n"
             "  --select-tcpip (-e)\n"
             "  --tcpip=<addr> (with an argument)");
        return false;
    }

    bool otg = false;
    bool v4l2 = false;
#ifdef HAVE_USB
    otg = opts->otg;
#endif
#ifdef HAVE_V4L2
    v4l2 = !!opts->v4l2_device;
#endif

    if (!opts->window) {
        // Without window, there cannot be any video playback
        opts->video_playback = false;
        // Controls are still possible, allowing for options like
        // --turn-screen-off
    }

    if (!opts->video) {
        opts->video_playback = false;
        // Do not power on the device on start if video capture is disabled
        opts->power_on = false;
    }

    if (!opts->audio) {
        opts->audio_playback = false;
    }

    if (opts->video && !opts->video_playback && !opts->record_filename
            && !v4l2) {
        LOGI("No video playback, no recording, no V4L2 sink: video disabled");
        opts->video = false;
    }

    if (opts->audio && !opts->audio_playback && !opts->record_filename) {
        LOGI("No audio playback, no recording: audio disabled");
        opts->audio = false;
    }

    if (!opts->video && !opts->audio && !opts->control && !otg) {
        LOGE("No video, no audio, no control, no OTG: nothing to do");
        return false;
    }

    if (!opts->video && !otg) {
        // If video is disabled, then scrcpy must exit on audio failure.
        opts->require_audio = true;
    }

    if (opts->audio_playback && opts->audio_buffer == -1) {
        if (opts->audio_codec == SC_CODEC_FLAC) {
            // Use 50 ms audio buffer by default, but use a higher value for
            // FLAC, which is not low latency (the default encoder produces
            // blocks of 4096 samples, which represent ~85.333ms).
            LOGI("FLAC audio: audio buffer increased to 120 ms (use "
                 "--audio-buffer to set a custom value)");
            opts->audio_buffer = SC_TICK_FROM_MS(120);
        } else {
            opts->audio_buffer = SC_TICK_FROM_MS(50);
        }
    }

#ifdef HAVE_V4L2
    if (v4l2) {
        if (!opts->video) {
            LOGE("V4L2 sink requires video capture, but --no-video was set.");
            return false;
        }

        if (opts->flex_display) {
            LOGE("V4L2 is incompatible with -x/--flex-display because it does "
                 "not support resizing");
            return false;
        }

        // V4L2 could not handle size change.
        // Do not log because downsizing on error is the default behavior,
        // not an explicit request from the user.
        opts->downsize_on_error = false;
    }

    if (opts->v4l2_buffer && !opts->v4l2_device) {
        LOGE("V4L2 buffer value without V4L2 sink");
        return false;
    }
#endif

    if (opts->control && opts->video_source == SC_VIDEO_SOURCE_DISPLAY) {
        if (opts->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AUTO) {
            opts->keyboard_input_mode = otg ? SC_KEYBOARD_INPUT_MODE_AOA
                                            : SC_KEYBOARD_INPUT_MODE_SDK;
        } else if (opts->keyboard_input_mode
                == SC_KEYBOARD_INPUT_MODE_UHID_OR_AOA) {
            opts->keyboard_input_mode = otg ? SC_KEYBOARD_INPUT_MODE_AOA
                                            : SC_KEYBOARD_INPUT_MODE_UHID;
        }

        if (opts->mouse_input_mode == SC_MOUSE_INPUT_MODE_AUTO) {
            if (otg) {
                opts->mouse_input_mode = SC_MOUSE_INPUT_MODE_AOA;
            } else if (!opts->video_playback) {
                LOGI("No video mirroring, SDK mouse disabled");
                opts->mouse_input_mode = SC_MOUSE_INPUT_MODE_DISABLED;
            } else {
                opts->mouse_input_mode = SC_MOUSE_INPUT_MODE_SDK;
            }
        } else if (opts->mouse_input_mode == SC_MOUSE_INPUT_MODE_UHID_OR_AOA) {
            opts->mouse_input_mode = otg ? SC_MOUSE_INPUT_MODE_AOA
                                         : SC_MOUSE_INPUT_MODE_UHID;
        } else if (opts->mouse_input_mode == SC_MOUSE_INPUT_MODE_SDK
                    && !opts->video_playback) {
            LOGE("SDK mouse mode requires video playback. Try --mouse=uhid.");
            return false;
        }
        if (opts->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_UHID_OR_AOA) {
            opts->gamepad_input_mode = otg ? SC_GAMEPAD_INPUT_MODE_AOA
                                           : SC_GAMEPAD_INPUT_MODE_UHID;
        }
    }

    // If mouse bindings are not explicitly set, configure default bindings
    if (opts->mouse_bindings.pri.right_click == SC_MOUSE_BINDING_AUTO) {
        assert(opts->mouse_bindings.pri.middle_click == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.pri.click4 == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.pri.click5 == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.sec.right_click == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.sec.middle_click == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.sec.click4 == SC_MOUSE_BINDING_AUTO);
        assert(opts->mouse_bindings.sec.click5 == SC_MOUSE_BINDING_AUTO);

        static struct sc_mouse_binding_set default_shortcuts = {
            .right_click = SC_MOUSE_BINDING_BACK,
            .middle_click = SC_MOUSE_BINDING_HOME,
            .click4 = SC_MOUSE_BINDING_APP_SWITCH,
            .click5 = SC_MOUSE_BINDING_EXPAND_NOTIFICATION_PANEL,
        };

        static struct sc_mouse_binding_set forward = {
            .right_click = SC_MOUSE_BINDING_CLICK,
            .middle_click = SC_MOUSE_BINDING_CLICK,
            .click4 = SC_MOUSE_BINDING_CLICK,
            .click5 = SC_MOUSE_BINDING_CLICK,
        };

        // By default, forward all clicks only for UHID and AOA
        if (opts->mouse_input_mode == SC_MOUSE_INPUT_MODE_SDK) {
            opts->mouse_bindings.pri = default_shortcuts;
            opts->mouse_bindings.sec = forward;
        } else {
            opts->mouse_bindings.pri = forward;
            opts->mouse_bindings.sec = default_shortcuts;
        }
    }

    if (opts->new_display) {
        if (opts->video_source != SC_VIDEO_SOURCE_DISPLAY) {
            LOGE("--new-display is only available with --video-source=display");
            return false;
        }

        if (!opts->video) {
            LOGE("--new-display is incompatible with --no-video");
            return false;
        }
    }

    if (opts->render_fit == SC_RENDER_FIT_AUTO) {
        opts->render_fit = opts->flex_display ? SC_RENDER_FIT_UNSCALED
                                              : SC_RENDER_FIT_LETTERBOX;
    }

    if (otg) {
        if (!opts->control) {
            LOGE("--no-control is not allowed in OTG mode");
            return false;
        }

        enum sc_keyboard_input_mode kmode = opts->keyboard_input_mode;
        if (kmode != SC_KEYBOARD_INPUT_MODE_AOA
                && kmode != SC_KEYBOARD_INPUT_MODE_DISABLED) {
            LOGE("In OTG mode, --keyboard only supports aoa or disabled.");
            return false;
        }

        enum sc_mouse_input_mode mmode = opts->mouse_input_mode;
        if (mmode != SC_MOUSE_INPUT_MODE_AOA
                && mmode != SC_MOUSE_INPUT_MODE_DISABLED) {
            LOGE("In OTG mode, --mouse only supports aoa or disabled.");
            return false;
        }

        enum sc_gamepad_input_mode gmode = opts->gamepad_input_mode;
        if (gmode != SC_GAMEPAD_INPUT_MODE_AOA
                && gmode != SC_GAMEPAD_INPUT_MODE_DISABLED) {
            LOGE("In OTG mode, --gamepad only supports aoa or disabled.");
            return false;
        }

        if (kmode == SC_KEYBOARD_INPUT_MODE_DISABLED
                && mmode == SC_MOUSE_INPUT_MODE_DISABLED
                && gmode == SC_GAMEPAD_INPUT_MODE_DISABLED) {
            LOGE("Cannot not disable all inputs in OTG mode.");
            return false;
        }
    }

    if (opts->keyboard_input_mode != SC_KEYBOARD_INPUT_MODE_SDK) {
        if (opts->key_inject_mode == SC_KEY_INJECT_MODE_TEXT) {
            LOGE("--prefer-text is specific to --keyboard=sdk");
            return false;
        }

        if (opts->key_inject_mode == SC_KEY_INJECT_MODE_RAW) {
            LOGE("--raw-key-events is specific to --keyboard=sdk");
            return false;
        }

        if (!opts->forward_key_repeat) {
            LOGE("--no-key-repeat is specific to --keyboard=sdk");
            return false;
        }
    }

    if (opts->mouse_input_mode != SC_MOUSE_INPUT_MODE_SDK
            && !opts->mouse_hover) {
        LOGE("--no-mouse-over is specific to --mouse=sdk");
        return false;
    }

    if ((opts->tunnel_host || opts->tunnel_port) && !opts->force_adb_forward) {
        LOGI("Tunnel host/port is set, "
             "--force-adb-forward automatically enabled.");
        opts->force_adb_forward = true;
    }

    if (opts->video_source == SC_VIDEO_SOURCE_CAMERA) {
        if (opts->display_id) {
            LOGE("--display-id is only available with --video-source=display");
            return false;
        }

        if (opts->display_ime_policy != SC_DISPLAY_IME_POLICY_UNDEFINED) {
            LOGE("--display-ime-policy is only available with "
                 "--video-source=display");
            return false;
        }

        if (opts->camera_id && opts->camera_facing != SC_CAMERA_FACING_ANY) {
            LOGE("Cannot specify both --camera-id and --camera-facing");
            return false;
        }

        if (opts->camera_size) {
            if (opts->max_size) {
                LOGE("Cannot specify both --camera-size and -m/--max-size");
                return false;
            }

            if (opts->camera_ar) {
                LOGE("Cannot specify both --camera-size and --camera-ar");
                return false;
            }
        }

        if (opts->camera_high_speed && !opts->camera_fps) {
            LOGE("--camera-high-speed requires an explicit --camera-fps value");
            return false;
        }

        if (opts->control) {
            // Disable all inputs for camera
            opts->keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_DISABLED;
            opts->mouse_input_mode = SC_MOUSE_INPUT_MODE_DISABLED;
            opts->gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_DISABLED;
        }
    } else if (opts->camera_id
            || opts->camera_ar
            || opts->camera_facing != SC_CAMERA_FACING_ANY
            || opts->camera_fps
            || opts->camera_high_speed
            || opts->camera_size) {
        LOGE("Camera options are only available with --video-source=camera");
        return false;
    }

    if (opts->display_id != 0 && opts->new_display) {
        LOGE("Cannot specify both --display-id and --new-display");
        return false;
    }

    if (opts->flex_display) {
        if (opts->video_source != SC_VIDEO_SOURCE_DISPLAY
                || !opts->new_display) {
            LOGE("-x/--flex-display can only be applied to displays created "
                 "with --new-display");
            return false;
        }

        if (!opts->control) {
            LOGE("-n/--no-control is not compatible with -x/--flex-display");
            return false;
        }

        if (opts->crop) {
            LOGE("--crop is not compatible with -x/--flex-display");
            return false;
        }

        if (opts->window_width || opts->window_height) {
            LOGE("--window-width and --window-height are disabled when using "
                 "-x/--flex-display; configure the display size with "
                 "--new-display=WxH instead");
            return false;
        }

        // Force free resizing
        opts->window_aspect_ratio_lock = false;
    }

    if (opts->display_ime_policy != SC_DISPLAY_IME_POLICY_UNDEFINED
            && opts->display_id == 0 && !opts->new_display) {
        LOGE("--display-ime-policy is only supported on a secondary display");
        return false;
    }

    if (opts->audio && opts->audio_source == SC_AUDIO_SOURCE_AUTO) {
        // Select the audio source according to the video source
        if (opts->video_source == SC_VIDEO_SOURCE_DISPLAY) {
            if (opts->audio_dup) {
                LOGI("Audio duplication enabled: audio source switched to "
                     "\"playback\"");
                opts->audio_source = SC_AUDIO_SOURCE_PLAYBACK;
            } else {
                opts->audio_source = SC_AUDIO_SOURCE_OUTPUT;
            }
        } else {
            opts->audio_source = SC_AUDIO_SOURCE_MIC;
            LOGI("Camera video source: microphone audio source selected");
        }
    }

    if (opts->audio_dup) {
        if (!opts->audio) {
            LOGE("--audio-dup not supported if audio is disabled");
            return false;
        }

        if (opts->audio_source != SC_AUDIO_SOURCE_PLAYBACK) {
            LOGE("--audio-dup is specific to --audio-source=playback");
            return false;
        }
    }

    if (opts->record_format && !opts->record_filename) {
        LOGE("Record format specified without recording");
        return false;
    }

    if (opts->record_filename) {
        if (!opts->video && !opts->audio) {
            LOGE("Video and audio disabled, nothing to record");
            return false;
        }

        if (!opts->record_format) {
            opts->record_format = guess_record_format(opts->record_filename);
            if (!opts->record_format) {
                LOGE("No format specified for \"%s\" "
                     "(try with --record-format=mkv)",
                     opts->record_filename);
                return false;
            }
        }

        if (opts->record_orientation != SC_ORIENTATION_0) {
            if (sc_orientation_is_mirror(opts->record_orientation)) {
                LOGE("Record orientation only supports rotation, not "
                     "flipping: %s",
                     sc_orientation_get_name(opts->record_orientation));
                return false;
            }
        }

        if (opts->video
                && sc_record_format_is_audio_only(opts->record_format)) {
            LOGE("Audio container does not support video stream");
            return false;
        }

        if (opts->record_format == SC_RECORD_FORMAT_OPUS
                && opts->audio_codec != SC_CODEC_OPUS) {
            LOGE("Recording to OPUS file requires an OPUS audio stream "
                 "(try with --audio-codec=opus)");
            return false;
        }

        if (opts->record_format == SC_RECORD_FORMAT_AAC
                && opts->audio_codec != SC_CODEC_AAC) {
            LOGE("Recording to AAC file requires an AAC audio stream "
                 "(try with --audio-codec=aac)");
            return false;
        }
        if (opts->record_format == SC_RECORD_FORMAT_FLAC
                && opts->audio_codec != SC_CODEC_FLAC) {
            LOGE("Recording to FLAC file requires a FLAC audio stream "
                 "(try with --audio-codec=flac)");
            return false;
        }

        if (opts->record_format == SC_RECORD_FORMAT_WAV
                && opts->audio_codec != SC_CODEC_RAW) {
            LOGE("Recording to WAV file requires a RAW audio stream "
                 "(try with --audio-codec=raw)");
            return false;
        }

        if ((opts->record_format == SC_RECORD_FORMAT_MP4 ||
             opts->record_format == SC_RECORD_FORMAT_M4A)
                && opts->audio_codec == SC_CODEC_RAW) {
            LOGE("Recording to MP4 container does not support RAW audio");
            return false;
        }
    }

    if (opts->audio_codec == SC_CODEC_FLAC && opts->audio_bit_rate) {
        LOGW("--audio-bit-rate is ignored for FLAC audio codec");
    }

    if (opts->audio_codec == SC_CODEC_RAW) {
        if (opts->audio_bit_rate) {
            LOGW("--audio-bit-rate is ignored for raw audio codec");
        }
        if (opts->audio_codec_options) {
            LOGW("--audio-codec-options is ignored for raw audio codec");
        }
        if (opts->audio_encoder) {
            LOGW("--audio-encoder is ignored for raw audio codec");
        }
    }

    if (!opts->control) {
        if (opts->turn_screen_off) {
            LOGE("Cannot request to turn screen off if control is disabled");
            return false;
        }
        if (opts->stay_awake) {
            LOGE("Cannot request to stay awake if control is disabled");
            return false;
        }
        if (opts->show_touches) {
            LOGE("Cannot request to show touches if control is disabled");
            return false;
        }
        if (opts->power_off_on_close) {
            LOGE("Cannot request power off on close if control is disabled");
            return false;
        }
        if (opts->start_app) {
            LOGE("Cannot start an Android app if control is disabled");
            return false;
        }
        if (opts->keep_active) {
            LOGE("Cannot keep device active if control is disabled");
            return false;
        }
    }

# ifdef _WIN32
    if (!otg && (opts->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA
                || opts->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA)) {
        LOGE("On Windows, it is not possible to open a USB device already open "
             "by another process (like adb).");
        LOGE("Therefore, --keyboard=aoa and --mouse=aoa may only work in OTG"
             "mode (--otg).");
        return false;
    }
# endif

    if (opts->start_fps_counter && !opts->video_playback) {
        LOGW("--print-fps has no effect without video playback");
        opts->start_fps_counter = false;
    }

    if (otg) {
        // OTG mode is compatible with only very few options.
        // Only report obvious errors.
        if (opts->record_filename) {
            LOGE("OTG mode: cannot record");
            return false;
        }
        if (opts->turn_screen_off) {
            LOGE("OTG mode: could not turn screen off");
            return false;
        }
        if (opts->stay_awake) {
            LOGE("OTG mode: could not stay awake");
            return false;
        }
        if (opts->show_touches) {
            LOGE("OTG mode: could not request to show touches");
            return false;
        }
        if (opts->power_off_on_close) {
            LOGE("OTG mode: could not request power off on close");
            return false;
        }
        if (opts->display_id) {
            LOGE("OTG mode: could not select display");
            return false;
        }
        if (v4l2) {
            LOGE("OTG mode: could not sink to V4L2 device");
            return false;
        }
    }

    return true;
}

static enum sc_pause_on_exit
sc_get_pause_on_exit(int argc, char *argv[]) {
    // Read arguments backwards so that the last --pause-on-exit is considered
    // (same behavior as getopt())
    for (int i = argc - 1; i >= 1; --i) {
        const char *arg = argv[i];
        // Starts with "--pause-on-exit"
        if (!strncmp("--pause-on-exit", arg, 15)) {
            if (arg[15] == '\0') {
                // No argument
                return SC_PAUSE_ON_EXIT_TRUE;
            }
            if (arg[15] != '=') {
                // Invalid parameter, ignore
                return SC_PAUSE_ON_EXIT_UNDEFINED;
            }
            const char *value = &arg[16];
            if (!strcmp(value, "true")) {
                return SC_PAUSE_ON_EXIT_TRUE;
            }
            if (!strcmp(value, "if-error")) {
                return SC_PAUSE_ON_EXIT_IF_ERROR;
            }
            if (!strcmp(value, "false")) {
                return SC_PAUSE_ON_EXIT_FALSE;
            }
            return SC_PAUSE_ON_EXIT_UNDEFINED;
        }
    }

    return SC_PAUSE_ON_EXIT_UNDEFINED;
}

#ifdef _WIN32
/**
 * Attempt to detect whether the user launched scrcpy by double-clicking
 * scrcpy.exe in Windows Explorer.
 *
 * If so, the console should remain open on error.
 */
static bool
scrcpy_launched_by_double_click(void) {
    // No console window
    if (GetConsoleWindow() == NULL) {
        return false;
    }

    // Must be interactive
    if (!_isatty(_fileno(stdin)) || !_isatty(_fileno(stdout))) {
        return false;
    }

    // Check how many processes share the console
    DWORD dummy;
    DWORD count = GetConsoleProcessList(&dummy, 1);

    // Only this process attached, assume it was started by double-clicking
    return count == 1;
}
#endif

bool
scrcpy_parse_args(struct scrcpy_cli_args *args, int argc, char *argv[]) {
    struct sc_getopt_adapter adapter;
    if (!sc_getopt_adapter_init(&adapter)) {
        LOGW("Could not create getopt adapter");
        return false;
    }

    bool ret = parse_args_with_getopt(args, argc, argv, adapter.optstring,
                                      adapter.longopts);

    sc_getopt_adapter_destroy(&adapter);

    if (!ret && args->pause_on_exit == SC_PAUSE_ON_EXIT_UNDEFINED) {
        // Check if "--pause-on-exit" is present in the arguments list, because
        // it must be taken into account even if command line parsing failed
        args->pause_on_exit = sc_get_pause_on_exit(argc, argv);
    }

    if (args->pause_on_exit == SC_PAUSE_ON_EXIT_UNDEFINED) {
        args->pause_on_exit = SC_PAUSE_ON_EXIT_FALSE;
#ifdef _WIN32
        if (scrcpy_launched_by_double_click()) {
            args->pause_on_exit = SC_PAUSE_ON_EXIT_IF_ERROR;
        }
#endif
    }

    assert(args->pause_on_exit != SC_PAUSE_ON_EXIT_UNDEFINED);

    return ret;
}
