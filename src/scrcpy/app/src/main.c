#include "common.h"

#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#ifdef HAVE_V4L2
# include <libavdevice/avdevice.h>
#endif
#include <SDL3/SDL.h>

#include "cli.h"
#include "events.h"
#ifdef _WIN32
# include "ipc/machine.h"
# include "ipc/machine_exit.h"
#endif
#include "options.h"
#include "scrcpy.h"
#ifdef HAVE_USB
# include "usb/scrcpy_otg.h"
#endif
#include "util/log.h"
#include "util/net.h"
#include "util/launcher_stop.h"
#include "version.h"

#ifdef _WIN32
#include <windows.h>
#include "util/str.h"
#endif

#ifdef _WIN32
/** Translate a machine Stop or terminal pipe failure to native quit. */
static void
sc_machine_request_stop(void *userdata) {
    (void) userdata;
    sc_push_event(SDL_EVENT_QUIT);
}
#endif

static int
main_scrcpy(int argc, char *argv[]) {
#ifdef _WIN32
    // Select the binary stream before the banner, logging or argument errors.
    bool machine_selected = false;
    for (int index = 1; index < argc; ++index) {
        if (!strncmp(argv[index], "--seamless-machine", 18)) {
            machine_selected = true;
            break;
        }
    }
    if (machine_selected && !sc_machine_prepare_stdio()) {
        return SCRCPY_EXIT_FAILURE;
    }
    // disable buffering, we want logs immediately
    // even line buffering (setvbuf() with mode _IOLBF) is not sufficient
    setbuf(stdout, NULL);
    setbuf(stderr, NULL);
#endif

    printf("scrcpy " SCRCPY_VERSION
           " <https://github.com/Genymobile/scrcpy>\n");

    struct scrcpy_cli_args args = {
        .opts = scrcpy_options_default,
        .help = false,
        .version = false,
        .pause_on_exit = SC_PAUSE_ON_EXIT_UNDEFINED,
    };

#ifndef NDEBUG
    args.opts.log_level = SC_LOG_LEVEL_DEBUG;
#endif

    enum scrcpy_exit_code ret;

    if (!scrcpy_parse_args(&args, argc, argv)) {
        ret = SCRCPY_EXIT_FAILURE;
        goto end;
    }

    sc_set_log_level(args.opts.log_level);

    if (args.help) {
        scrcpy_print_usage(argv[0]);
        ret = SCRCPY_EXIT_SUCCESS;
        goto end;
    }

    if (args.version) {
        scrcpy_print_version();
        ret = SCRCPY_EXIT_SUCCESS;
        goto end;
    }

#ifdef SCRCPY_LAVF_REQUIRES_REGISTER_ALL
    av_register_all();
#endif

#ifdef HAVE_V4L2
    if (args.opts.v4l2_device) {
        avdevice_register_all();
    }
#endif

#ifdef _WIN32
    bool machine_active = false;
    if (args.machine_mode) {
        struct sc_machine_config config = {
            .session_id = args.machine_session_id,
            .parent_pid = args.machine_parent_pid,
            .parent_created = args.machine_parent_created,
        };
        if (!sc_machine_init(&config)) {
            ret = SCRCPY_EXIT_FAILURE;
            goto end;
        }
        machine_active = true;
        if (!sc_machine_handshake()) {
            ret = SCRCPY_EXIT_FAILURE;
            goto machine_cleanup;
        }
        sc_machine_exit_reset();
    }
#endif

    if (!net_init()) {
        ret = SCRCPY_EXIT_FAILURE;
        goto machine_cleanup;
    }

    sc_log_configure();

    if (!sc_main_thread_init()) {
        ret = SCRCPY_EXIT_FAILURE;
        goto net_cleanup;
    }

    if (!SDL_Init(SDL_INIT_EVENTS)) {
        LOGE("Could not initialize SDL events: %s", SDL_GetError());
        ret = SCRCPY_EXIT_FAILURE;
        goto main_thread_cleanup;
    }
    atexit(SDL_Quit);

    struct sc_launcher_stop launcher_stop = {0};
#ifdef _WIN32
    if (machine_active) {
        if (!sc_machine_start(sc_machine_request_stop, NULL) ||
                !sc_machine_emit_lifecycle("NativeReady", "native", NULL,
                                           "none", "none")) {
            ret = SCRCPY_EXIT_FAILURE;
            goto main_thread_cleanup;
        }
    } else
#endif
    if (!sc_launcher_stop_init(&launcher_stop)) {
        ret = SCRCPY_EXIT_FAILURE;
        goto main_thread_cleanup;
    }

#ifdef HAVE_USB
    ret = args.opts.otg ? scrcpy_otg(&args.opts) :
#ifdef _WIN32
          machine_active && sc_machine_stop_requested() ? SCRCPY_EXIT_SUCCESS :
#endif
          scrcpy(&args.opts);
#else
    ret =
#ifdef _WIN32
          machine_active && sc_machine_stop_requested() ? SCRCPY_EXIT_SUCCESS :
#endif
          scrcpy(&args.opts);
#endif

#ifdef _WIN32
    if (machine_active) {
        sc_machine_close_commands();
        if (ret != SCRCPY_EXIT_SUCCESS) {
            sc_machine_emit_lifecycle("FatalError", "native", NULL,
                                      "nativeFailure", "internalFailure");
        }
        sc_machine_emit_lifecycle("SessionStopped", "native", NULL,
                                  sc_machine_exit_reason(
                                      ret == SCRCPY_EXIT_SUCCESS,
                                      sc_machine_stop_requested(),
                                      sc_machine_stop_reason()), "none");
    } else
#endif
    sc_launcher_stop_destroy(&launcher_stop);

main_thread_cleanup:
#ifdef _WIN32
    if (machine_active) {
        sc_machine_close_commands();
    }
#endif
    sc_main_thread_destroy();

net_cleanup:
    net_cleanup();

machine_cleanup:
#ifdef _WIN32
    if (machine_active) {
        sc_machine_destroy();
    }
#endif
end:
    if (
#ifdef _WIN32
        !machine_selected &&
#endif
        (args.pause_on_exit == SC_PAUSE_ON_EXIT_TRUE ||
            (args.pause_on_exit == SC_PAUSE_ON_EXIT_IF_ERROR &&
                ret != SCRCPY_EXIT_SUCCESS))) {
        printf("Press Enter to continue...\n");
        getchar();
    }

    return ret;
}

int
main(int argc, char *argv[]) {
#ifndef _WIN32
    return main_scrcpy(argc, argv);
#else
    (void) argc;
    (void) argv;
    int wargc;
    wchar_t **wargv = CommandLineToArgvW(GetCommandLineW(), &wargc);
    if (!wargv) {
        LOG_OOM();
        return SCRCPY_EXIT_FAILURE;
    }

    char **argv_utf8 = malloc((wargc + 1) * sizeof(*argv_utf8));
    if (!argv_utf8) {
        LOG_OOM();
        LocalFree(wargv);
        return SCRCPY_EXIT_FAILURE;
    }

    argv_utf8[wargc] = NULL;

    for (int i = 0; i < wargc; ++i) {
        argv_utf8[i] = sc_str_from_wchars(wargv[i]);
        if (!argv_utf8[i]) {
            LOG_OOM();
            for (int j = 0; j < i; ++j) {
                free(argv_utf8[j]);
            }
            LocalFree(wargv);
            free(argv_utf8);
            return SCRCPY_EXIT_FAILURE;
        }
    }

    LocalFree(wargv);

    int ret = main_scrcpy(wargc, argv_utf8);

    for (int i = 0; i < wargc; ++i) {
        free(argv_utf8[i]);
    }
    free(argv_utf8);

    return ret;
#endif
}
