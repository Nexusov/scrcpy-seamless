#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

#include "cli.h"
#include "options.h"

#include "../../../../tests/fixtures/options/native-cli-inventory-baseline.inc"

static void test_flag_version(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
        .help = false,
        .version = false,
    };

    char *argv[] = {"scrcpy", "-v"};

    bool ok = scrcpy_parse_args(&args, 2, argv);
    assert(ok);
    assert(!args.help);
    assert(args.version);
}

static void test_flag_help(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
        .help = false,
        .version = false,
    };

    char *argv[] = {"scrcpy", "--help"};

    bool ok = scrcpy_parse_args(&args, 2, argv);
    assert(ok);
    assert(args.help);
    assert(!args.version);
}

// Check the option declarations compiled into the native parser.
static void test_generated_option_table(void) {
    enum {
        EXPECTED_OPTIONS = 109,
        EXPECTED_LONG_OPTIONS = 106,
        EXPECTED_SHORT_OPTIONS = 20,
        EXPECTED_OPTIONAL_ARGUMENTS = 3,
    };

    assert(sc_cli_option_count() == EXPECTED_OPTIONS);
    assert(sc_cli_option_count() == ARRAY_LEN(phase3_cli_inventory));

    size_t long_count = 0;
    size_t short_count = 0;
    size_t optional_count = 0;
    for (size_t index = 0; index < sc_cli_option_count(); ++index) {
        struct sc_cli_option_test_info option;
        assert(sc_cli_option_get_test_info(index, &option));
        const struct sc_cli_option_test_info *baseline =
            &phase3_cli_inventory[index];
        assert(!!option.longopt == !!baseline->longopt);
        if (baseline->longopt) {
            assert(!strcmp(option.longopt, baseline->longopt));
        }
        assert(option.shortopt == baseline->shortopt);
        assert(option.has_arg == baseline->has_arg);
        assert(option.optional_arg == baseline->optional_arg);
        assert(option.documented == baseline->documented);
        assert(option.longopt || option.shortopt);
        assert(option.documented);
        assert(!option.optional_arg || option.has_arg);
        long_count += !!option.longopt;
        short_count += !!option.shortopt;
        optional_count += option.optional_arg;

        if (index == 0) {
            assert(!strcmp(option.longopt, "always-on-top"));
        }
        if (index == sc_cli_option_count() - 1) {
            assert(!strcmp(option.longopt, "flex-display"));
            assert(option.shortopt == 'x');
        }
    }

    assert(long_count == EXPECTED_LONG_OPTIONS);
    assert(short_count == EXPECTED_SHORT_OPTIONS);
    assert(optional_count == EXPECTED_OPTIONAL_ARGUMENTS);

    struct sc_cli_option_test_info out_of_range;
    assert(!sc_cli_option_get_test_info(sc_cli_option_count(), &out_of_range));
}

// Check representative long, short and context-sensitive short-only aliases.
static void test_generated_option_parser(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
    };

    char *argv[] = {
        "scrcpy", "--always-on-top", "-b", "5M", "-G", "-K", "-M",
    };
    assert(scrcpy_parse_args(&args, ARRAY_LEN(argv), argv));
    assert(args.opts.always_on_top);
    assert(args.opts.video_bit_rate == 5000000);
    assert(args.opts.gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_UHID);
    assert(args.opts.keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_UHID);
    assert(args.opts.mouse_input_mode == SC_MOUSE_INPUT_MODE_UHID);
}

// Check all three optional-argument declarations through getopt.
static void test_generated_optional_arguments(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
    };
    char *argv[] = {
        "scrcpy", "--new-display", "--pause-on-exit", "--tcpip",
    };
    assert(scrcpy_parse_args(&args, ARRAY_LEN(argv), argv));
    assert(args.opts.new_display);
    assert(!strcmp(args.opts.new_display, ""));
    assert(args.pause_on_exit == SC_PAUSE_ON_EXIT_TRUE);
    assert(args.opts.tcpip);
    assert(!args.opts.tcpip_dst);

    struct scrcpy_cli_args with_values = {
        .opts = scrcpy_options_default,
    };
    char *values[] = {
        "scrcpy", "--new-display=1920x1080/420", "--pause-on-exit=if-error",
        "--tcpip=192.0.2.10:5555",
    };
    assert(scrcpy_parse_args(&with_values, ARRAY_LEN(values), values));
    assert(!strcmp(with_values.opts.new_display, "1920x1080/420"));
    assert(with_values.pause_on_exit == SC_PAUSE_ON_EXIT_IF_ERROR);
    assert(!strcmp(with_values.opts.tcpip_dst, "192.0.2.10:5555"));
}

// Check that the generated declarations still produce native help output.
static void test_generated_option_help(void) {
    FILE *help_file = tmpfile();
    assert(help_file);

    fflush(stdout);
    int saved_stdout = dup(fileno(stdout));
    assert(saved_stdout >= 0);
    assert(dup2(fileno(help_file), fileno(stdout)) >= 0);
    scrcpy_print_usage("scrcpy");
    fflush(stdout);
    assert(dup2(saved_stdout, fileno(stdout)) >= 0);
    close(saved_stdout);

    assert(fseek(help_file, 0, SEEK_END) == 0);
    long help_size = ftell(help_file);
    assert(help_size > 0);
    rewind(help_file);

    char *help = malloc((size_t) help_size + 1);
    assert(help);
    assert(fread(help, 1, (size_t) help_size, help_file) == (size_t) help_size);
    help[help_size] = '\0';
    assert(strstr(help, "Usage: scrcpy [options]"));
    assert(strstr(help, "-h, --help"));
    assert(strstr(help, "--new-display"));
    assert(strstr(help, "--port=port[:port]"));
    assert(strstr(help, "-G"));

    // Frozen rendered --help fingerprint from the unchanged Phase 3 CLI.
    const uint64_t phase3_help_fnv1a64 = UINT64_C(0x8da481d232d1d42d);
    const uint64_t fnv_offset_basis = UINT64_C(0xcbf29ce484222325);
    const uint64_t fnv_prime = UINT64_C(0x100000001b3);
    uint64_t help_hash = fnv_offset_basis;
    for (const unsigned char *cursor = (const unsigned char *) help;
         *cursor; ++cursor) {
        if (*cursor != '\r') {
            help_hash = (help_hash ^ *cursor) * fnv_prime;
        }
    }
    assert(help_hash == phase3_help_fnv1a64);

    free(help);
    fclose(help_file);
}

static void test_options(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
        .help = false,
        .version = false,
    };

    char *argv[] = {
        "scrcpy",
        "--always-on-top",
        "--video-bit-rate", "5M",
        "--crop", "100:200:300:400",
        "--fullscreen",
        "--max-fps", "30",
        "--max-size", "1024",
        // "--no-control" is not compatible with "--turn-screen-off"
        // "--no-playback" is not compatible with "--fulscreen"
        "--port", "1234:1236",
        "--push-target", "/sdcard/Movies",
        "--record", "file",
        "--record-format", "mkv",
        "--serial", "0123456789abcdef",
        "--show-touches",
        "--turn-screen-off",
        "--prefer-text",
        "--window-title", "my device",
        "--window-x", "100",
        "--window-y", "-1",
        "--window-width", "600",
        "--window-height", "0",
        "--window-borderless",
    };

    bool ok = scrcpy_parse_args(&args, ARRAY_LEN(argv), argv);
    assert(ok);

    const struct scrcpy_options *opts = &args.opts;
    assert(opts->always_on_top);
    assert(opts->video_bit_rate == 5000000);
    assert(!strcmp(opts->crop, "100:200:300:400"));
    assert(opts->fullscreen);
    assert(!strcmp(opts->max_fps, "30"));
    assert(opts->max_size == 1024);
    assert(opts->port_range.first == 1234);
    assert(opts->port_range.last == 1236);
    assert(!strcmp(opts->push_target, "/sdcard/Movies"));
    assert(!strcmp(opts->record_filename, "file"));
    assert(opts->record_format == SC_RECORD_FORMAT_MKV);
    assert(!strcmp(opts->serial, "0123456789abcdef"));
    assert(opts->show_touches);
    assert(opts->turn_screen_off);
    assert(opts->key_inject_mode == SC_KEY_INJECT_MODE_TEXT);
    assert(!strcmp(opts->window_title, "my device"));
    assert(opts->window_x == 100);
    assert(opts->window_y == -1);
    assert(opts->window_width == 600);
    assert(opts->window_height == 0);
    assert(opts->window_borderless);
}

static void test_options2(void) {
    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
        .help = false,
        .version = false,
    };

    char *argv[] = {
        "scrcpy",
        "--no-control",
        "--no-playback",
        "--record", "file.mp4", // cannot enable --no-playback without recording
    };

    bool ok = scrcpy_parse_args(&args, ARRAY_LEN(argv), argv);
    assert(ok);

    const struct scrcpy_options *opts = &args.opts;
    assert(!opts->control);
    assert(!opts->video_playback);
    assert(!opts->audio_playback);
    assert(!strcmp(opts->record_filename, "file.mp4"));
    assert(opts->record_format == SC_RECORD_FORMAT_MP4);
}

// Check that flex display conflicts with effective, nonzero window dimensions.
static void test_flex_display_window_dimensions(void) {
    struct {
        int argc;
        char *argv[7];
        bool accepted;
        uint16_t width;
        uint16_t height;
    } cases[] = {
        {3, {"scrcpy", "--new-display", "--flex-display"}, true, 0, 0},
        {5, {"scrcpy", "--new-display", "--flex-display",
             "--window-width", "0"}, true, 0, 0},
        {5, {"scrcpy", "--new-display", "--flex-display",
             "--window-height", "0"}, true, 0, 0},
        {7, {"scrcpy", "--new-display", "--flex-display",
             "--window-width", "0", "--window-height", "0"},
             true, 0, 0},
        {5, {"scrcpy", "--new-display", "--flex-display",
             "--window-width", "1"}, false, 0, 0},
        {5, {"scrcpy", "--new-display", "--flex-display",
             "--window-height", "1"}, false, 0, 0},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {
        struct scrcpy_cli_args args = {
            .opts = scrcpy_options_default,
        };
        bool accepted = scrcpy_parse_args(&args, cases[index].argc,
                                          cases[index].argv);
        assert(accepted == cases[index].accepted);

        if (accepted) {
            assert(args.opts.new_display);
            assert(args.opts.flex_display);
            assert(args.opts.window_width == cases[index].width);
            assert(args.opts.window_height == cases[index].height);
        }
    }
}

// Parse one numeric option through the native CLI and its final validation.
static bool parse_numeric_option(char *option, char *value,
                                 struct scrcpy_cli_args *args) {
    char *argv[] = {"scrcpy", option, value};
    return scrcpy_parse_args(args, ARRAY_LEN(argv), argv);
}

// Check the native audio output buffer boundaries and numeric spelling.
static void test_audio_output_buffer_boundaries(void) {
    struct {
        char *value;
        bool accepted;
        unsigned milliseconds;
    } cases[] = {
        {"0", true, 0},
        {"010", true, 8},
        {"08", false, 0},
        {"0x10", true, 16},
        {"1000", true, 1000},
        {"-1", false, 0},
        {"1001", false, 0},
        {"invalid", false, 0},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {
        struct scrcpy_cli_args args = {
            .opts = scrcpy_options_default,
        };
        bool accepted = parse_numeric_option("--audio-output-buffer",
                                             cases[index].value, &args);
        assert(accepted == cases[index].accepted);

        if (accepted) {
            assert(args.opts.audio_output_buffer
                   == SC_TICK_FROM_MS(cases[index].milliseconds));
        }
    }
}

// Check the native maximum size boundaries and base-zero numeric spelling.
static void test_max_size_boundaries(void) {
    struct {
        char *value;
        bool accepted;
        uint16_t size;
    } cases[] = {
        {"0", true, 0},
        {"00", true, 0},
        {"010", true, 8},
        {"08", false, 0},
        {"0x10", true, 16},
        {"0X10", true, 16},
        {"+010", true, 8},
        {"-0", true, 0},
        {"65535", true, 65535},
        {"-1", false, 0},
        {"65536", false, 0},
        {"0x", false, 0},
        {"0xG", false, 0},
        {"10x", false, 0},
        {"999999999999999999999999", false, 0},
        {"invalid", false, 0},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {
        struct scrcpy_cli_args args = {
            .opts = scrcpy_options_default,
        };
        bool accepted = parse_numeric_option("--max-size",
                                             cases[index].value, &args);
        assert(accepted == cases[index].accepted);

        if (accepted) {
            assert(args.opts.max_size == cases[index].size);
        }
    }
}

// Check the native power-of-two alignment domain and numeric spelling.
static void test_min_size_alignment_values(void) {
    struct {
        char *value;
        bool accepted;
        uint8_t alignment;
    } cases[] = {
        {"1", true, 1},
        {"2", true, 2},
        {"4", true, 4},
        {"8", true, 8},
        {"010", true, 8},
        {"+010", true, 8},
        {"16", true, 16},
        {"020", true, 16},
        {"0x10", true, 16},
        {"08", false, 0},
        {"0x", false, 0},
        {"0", false, 0},
        {"3", false, 0},
        {"17", false, 0},
        {"invalid", false, 0},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {
        struct scrcpy_cli_args args = {
            .opts = scrcpy_options_default,
        };
        bool accepted = parse_numeric_option("--min-size-alignment",
                                             cases[index].value, &args);
        assert(accepted == cases[index].accepted);

        if (accepted) {
            assert(args.opts.min_size_alignment == cases[index].alignment);
        }
    }
}

// Check base-zero bitrate values before and after native K/M multiplication.
static void test_bit_rate_numeric_spelling(void) {
    struct {
        char *value;
        bool accepted;
        uint32_t bits_per_second;
    } cases[] = {
        {"010", true, 8},
        {"010K", true, 8000},
        {"0x10K", true, 16000},
        {"0X10m", true, 16000000},
        {"08K", false, 0},
        {"0xK", false, 0},
        {"2M", true, 2000000},
        {"2147483647", true, 2147483647},
        {"2147483648", false, 0},
        {"2147M", true, 2147000000},
        {"2148M", false, 0},
        {"1KK", false, 0},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {
        struct scrcpy_cli_args args = {
            .opts = scrcpy_options_default,
        };
        bool accepted = parse_numeric_option("--video-bit-rate",
                                             cases[index].value, &args);
        assert(accepted == cases[index].accepted);

        if (accepted) {
            assert(args.opts.video_bit_rate == cases[index].bits_per_second);
        }
    }
}

static void test_parse_shortcut_mods(void) {
    uint8_t mods;
    bool ok;

    ok = sc_parse_shortcut_mods("lctrl", &mods);
    assert(ok);
    assert(mods == SC_SHORTCUT_MOD_LCTRL);

    ok = sc_parse_shortcut_mods("rctrl,lalt", &mods);
    assert(ok);
    assert(mods == (SC_SHORTCUT_MOD_RCTRL | SC_SHORTCUT_MOD_LALT));

    ok = sc_parse_shortcut_mods("lsuper,rsuper,lctrl", &mods);
    assert(ok);
    assert(mods == (SC_SHORTCUT_MOD_LSUPER
                  | SC_SHORTCUT_MOD_RSUPER
                  | SC_SHORTCUT_MOD_LCTRL));

    ok = sc_parse_shortcut_mods("", &mods);
    assert(!ok);

    ok = sc_parse_shortcut_mods("lctrl+", &mods);
    assert(!ok);

    ok = sc_parse_shortcut_mods("lctrl,", &mods);
    assert(!ok);
}

int main(int argc, char *argv[]) {
    (void) argc;
    (void) argv;

    test_flag_version();
    test_flag_help();
    test_generated_option_table();
    test_generated_option_parser();
    test_generated_optional_arguments();
    test_generated_option_help();
    test_options();
    test_options2();
    test_flex_display_window_dimensions();
    test_audio_output_buffer_boundaries();
    test_max_size_boundaries();
    test_min_size_alignment_values();
    test_bit_rate_numeric_spelling();
    test_parse_shortcut_mods();
    return 0;
}
