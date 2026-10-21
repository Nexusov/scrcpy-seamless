#include "scrcpy.h"

#include <assert.h>
#include <inttypes.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#ifdef _WIN32
// not needed here, but winsock2.h must never be included AFTER windows.h
# include <winsock2.h>
# include <windows.h>
#endif

#include "dispatcher.h"
#include "events.h"
#ifdef _WIN32
# include "ipc/machine.h"
# include "ipc/machine_exit.h"
#endif
#include "screen.h"
#include "sdl_hints.h"
#include "server.h"
#include "util/log.h"
#include "util/rand.h"
#include "session.h"

/* Persistent presentation and one stable, sequentially retired generation. */
struct scrcpy {
    struct sc_screen screen;
    struct sc_session *current;
};

#ifdef _WIN32
static BOOL WINAPI windows_ctrl_handler(DWORD ctrl_type) {
    if (ctrl_type == CTRL_C_EVENT || ctrl_type == CTRL_BREAK_EVENT) {
        sc_push_event(SDL_EVENT_QUIT);
        return TRUE;
    }
    return FALSE;
}

static void
sdl_configure_ctrl_c_windows(void) {
    // Clean up properly on Ctrl+C on Windows
    bool ok = SetConsoleCtrlHandler(windows_ctrl_handler, TRUE);
    if (!ok) {
        LOGW("Could not set Ctrl+C handler");
    }
}

/** Record only an SDL quit that was not caused by a machine Stop. */
static void
sc_machine_observe_quit(Uint32 event_type) {
    if (sc_machine_is_active()) {
        sc_machine_exit_observe_event(event_type,
                                      sc_machine_stop_requested());
    }
}
#endif // _WIN32

/** Service application wakeups without changing legacy session exit policy. */
static enum scrcpy_exit_code
event_loop(struct scrcpy *s, bool has_screen, bool reconnect,
           struct sc_dispatcher *dispatcher) {
    SDL_Event event;
    for (;;) {
        uint32_t outcome;

        if (sc_session_take_event(s->current, &outcome)) {
            event = (SDL_Event) {.type = outcome};
        } else {

            if (!SDL_WaitEventTimeout(&event, SC_SESSION_EVENT_WAIT_MS)) {
                continue;
            }

            if (event.type == SC_EVENT_SESSION_NOTIFICATION) {

                if (!sc_session_handle_event(s->current, &event, &outcome)) {
                    continue;
                }
                event.type = outcome;
            }

            if (sc_dispatcher_handle_event(dispatcher, &event)) {
                // Re-evaluate only after the non-reentrant drain lease ends.
                continue;
            }
        }

        switch (event.type) {
            case SC_EVENT_DEVICE_DISCONNECTED:
                LOGW("Device disconnected");
                if (has_screen && !reconnect) {
                    sc_screen_handle_event(&s->screen, &event);
                }
                return SCRCPY_EXIT_DISCONNECTED;
            case SC_EVENT_DEMUXER_ERROR:
                LOGE("Demuxer error");
                return reconnect ? SCRCPY_EXIT_DISCONNECTED : SCRCPY_EXIT_FAILURE;
            case SC_EVENT_CONTROLLER_ERROR:
                LOGE("Controller error");
                return reconnect ? SCRCPY_EXIT_DISCONNECTED : SCRCPY_EXIT_FAILURE;
            case SC_EVENT_RECORDER_ERROR:
                LOGE("Recorder error");
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_AOA_OPEN_ERROR:
                LOGE("AOA open error");
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_TIME_LIMIT_REACHED:
                LOGI("Time limit reached");
                return SCRCPY_EXIT_SUCCESS;
            case SDL_EVENT_QUIT:
                LOGD("User requested to quit");
#ifdef _WIN32
                sc_machine_observe_quit(event.type);
#endif
                return SCRCPY_EXIT_SUCCESS;
            default:
                if (has_screen) {
                    sc_screen_handle_event(&s->screen, &event);
                }
                break;
        }
    }
    return SCRCPY_EXIT_FAILURE;
}

/** Wait for the server while servicing app-scoped work and existing events. */
static bool
await_for_server(struct sc_session *session, bool *connected, struct sc_screen *screen,
                 struct sc_dispatcher *dispatcher) {
    SDL_Event event;
    for (;;) {
        uint32_t outcome;

        if (sc_session_take_event(session, &outcome)) {
            event = (SDL_Event) {.type = outcome};
        } else {

            if (!SDL_WaitEventTimeout(&event, SC_SESSION_EVENT_WAIT_MS)) {
                continue;
            }

            if (event.type == SC_EVENT_SESSION_NOTIFICATION) {

                if (!sc_session_handle_event(session, &event, &outcome)) {
                    continue;
                }
                event.type = outcome;
            }

            if (sc_dispatcher_handle_event(dispatcher, &event)) {
                // Re-evaluate only after the non-reentrant drain lease ends.
                continue;
            }
        }

        switch (event.type) {
            case SDL_EVENT_QUIT:
#ifdef _WIN32
                sc_machine_observe_quit(event.type);
#endif
                if (connected) {
                    *connected = false;
                }
                return true;
            case SC_EVENT_SERVER_CONNECTION_FAILED:
                return false;
            case SC_EVENT_SERVER_CONNECTED:
                if (connected) {
                    *connected = true;
                }
                return true;
            default:

                if (screen) {
                    sc_screen_handle_event(screen, &event);
                }
                break;
        }
    }

    LOGE("SDL_WaitEvent() error: %s", SDL_GetError());
    return false;
}

/** Wait between attempts while processing close and repaint events. */
static bool
sc_wait_reconnect(struct sc_screen *screen, struct sc_dispatcher *dispatcher) {
    const Uint64 retry_delay_ms = 1000;
    const int event_poll_ms = 50;
    Uint64 deadline = SDL_GetTicks() + retry_delay_ms;
    SDL_Event event;

    while (SDL_GetTicks() < deadline) {
        if (!SDL_WaitEventTimeout(&event, event_poll_ms)) {
            continue;
        }

        if (sc_dispatcher_handle_event(dispatcher, &event)) {
            continue;
        }

        if (event.type == SDL_EVENT_QUIT) {
#ifdef _WIN32
            sc_machine_observe_quit(event.type);
#endif
            return false;
        }

        if (screen) {
            sc_screen_handle_event(screen, &event);
        }
    }

    return true;
}

// Generate a scrcpy id to differentiate multiple running scrcpy instances
static uint32_t
scrcpy_generate_scid(void) {
    struct sc_rand rand;
    sc_rand_init(&rand);
    // Only use 31 bits to avoid issues with signed values on the Java-side
    return sc_rand_u32(&rand) & 0x7FFFFFFF;
}

/** Retain sequential legacy attempts with explicitly scoped receiver work. */
enum scrcpy_exit_code
scrcpy(struct scrcpy_options *options, struct sc_dispatcher *dispatcher) {
    static struct scrcpy scrcpy;
#ifndef NDEBUG
    // Detect missing initializations
    memset(&scrcpy, 42, sizeof(scrcpy));
#endif
    struct scrcpy *s = &scrcpy;

    const char *reconnect_serial = getenv("SCRCPY_RECONNECT_SERIAL");
    bool reconnect = reconnect_serial && *reconnect_serial;
    bool incompatible = !options->window || !options->video_playback
                     || options->record_filename || options->time_limit
                     || options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA
                     || options->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA
                     || options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_AOA;

    if (reconnect && incompatible) {
        LOGE("Reconnect requires video playback without recording, time limit or AOA");
        return SCRCPY_EXIT_FAILURE;
    }
    bool screen_initialized = false;
    bool retrying = false;
#ifdef _WIN32
    char machine_attempt_id[37] = {0};
#endif

restart_session:;
    enum scrcpy_exit_code ret = SCRCPY_EXIT_FAILURE;
    bool retry_session = false;
    bool disconnected = false;
    bool screen_interrupted = false;
#ifdef _WIN32
    bool reconnect_scheduled = false;

    if (sc_machine_is_active() && sc_machine_stop_requested()) {
        ret = SCRCPY_EXIT_SUCCESS;
        goto finish_screen;
    }
#endif
    s->current = sc_session_create(options, dispatcher, &s->screen,
                                    &screen_initialized);

    if (!s->current) {
        goto finish_screen;
    }
    uint32_t scid = scrcpy_generate_scid();

    struct sc_server_params params = {
        .scid = scid,
        .req_serial = retrying ? reconnect_serial : options->serial,
        .select_usb = !retrying && options->select_usb,
        .select_tcpip = !retrying && options->select_tcpip,
        .log_level = options->log_level,
        .video_codec = options->video_codec,
        .audio_codec = options->audio_codec,
        .video_source = options->video_source,
        .audio_source = options->audio_source,
        .camera_facing = options->camera_facing,
        .crop = options->crop,
        .port_range = options->port_range,
        .tunnel_host = options->tunnel_host,
        .tunnel_port = options->tunnel_port,
        .min_size_alignment = options->min_size_alignment,
        .max_size = options->max_size,
        .video_bit_rate = options->video_bit_rate,
        .audio_bit_rate = options->audio_bit_rate,
        .max_fps = options->max_fps,
        .angle = options->angle,
        .screen_off_timeout = options->screen_off_timeout,
        .capture_orientation = options->capture_orientation,
        .capture_orientation_lock = options->capture_orientation_lock,
        .control = options->control,
        .display_id = options->display_id,
        .new_display = options->new_display,
        .display_ime_policy = options->display_ime_policy,
        .video = options->video,
        .audio = options->audio,
        .audio_dup = options->audio_dup,
        .show_touches = options->show_touches,
        .stay_awake = options->stay_awake,
        .video_codec_options = options->video_codec_options,
        .audio_codec_options = options->audio_codec_options,
        .video_encoder = options->video_encoder,
        .audio_encoder = options->audio_encoder,
        .camera_id = options->camera_id,
        .camera_size = options->camera_size,
        .camera_ar = options->camera_ar,
        .camera_fps = options->camera_fps,
        .force_adb_forward = options->force_adb_forward,
        .power_off_on_close = options->power_off_on_close,
        .clipboard_autosync = options->clipboard_autosync,
        .downsize_on_error = options->downsize_on_error,
        .tcpip = !retrying && options->tcpip,
        .tcpip_dst = retrying ? NULL : options->tcpip_dst,
        .cleanup = options->cleanup,
        .power_on = options->power_on,
        .kill_adb_on_close = options->kill_adb_on_close,
        .camera_high_speed = options->camera_high_speed,
        .camera_torch = options->camera_torch,
        .camera_zoom = options->camera_zoom,
        .vd_destroy_content = options->vd_destroy_content,
        .vd_system_decorations = options->vd_system_decorations,
        .keep_active = options->keep_active,
        .flex_display = options->flex_display,
        .list = options->list,
    };

#ifdef _WIN32
    sdl_configure_ctrl_c_windows();
#endif

    // Set hints before starting the server thread to avoid race conditions in
    // SDL
    sc_sdl_set_hints(options->render_driver, options->disable_screensaver);

#ifdef _WIN32
    if (sc_machine_is_active()) {
        if (!sc_machine_new_attempt_id(machine_attempt_id)) {
            goto end;
        }
        sc_machine_set_attempt_id(machine_attempt_id, retrying);
        if (sc_machine_stop_requested()) {
            ret = SCRCPY_EXIT_SUCCESS;
            goto end;
        }
    }
#endif
    if (!sc_session_start_server(s->current, &params)) {
        ret = sc_session_startup_interrupted(s->current)
            ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
        goto end;
    }

#ifdef _WIN32
    if (sc_machine_is_active() &&
            !sc_machine_emit_lifecycle(retrying ? "Reconnecting" :
                                       "Connecting", "connection",
                                       machine_attempt_id, "none", "none")) {
        goto end;
    }
#endif

    if (options->list) {
        bool ok = await_for_server(s->current, NULL, NULL, dispatcher);
        ret = ok ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
        goto end;
    }

    // playback implies capture
    assert(!options->video_playback || options->video);
    assert(!options->audio_playback || options->audio);

    if (!retrying && (options->window ||
            (options->control && options->clipboard_autosync))) {
        // Initialize the video subsystem even if --no-video or
        // --no-video-playback is passed so that clipboard synchronization
        // still works.
        // <https://github.com/Genymobile/scrcpy/issues/4418>
        if (!SDL_Init(SDL_INIT_VIDEO)) {
            // If it fails, it is an error only if video playback is enabled
            if (options->video_playback) {
                LOGE("Could not initialize SDL video: %s", SDL_GetError());
                goto end;
            } else {
                LOGW("Could not initialize SDL video: %s", SDL_GetError());
            }
        }
    }

    if (!retrying && options->audio_playback) {
        if (!SDL_Init(SDL_INIT_AUDIO)) {
            LOGE("Could not initialize SDL audio: %s", SDL_GetError());
            goto end;
        }
    }

    if (!retrying && options->gamepad_input_mode != SC_GAMEPAD_INPUT_MODE_DISABLED) {
        if (!SDL_Init(SDL_INIT_GAMEPAD)) {
            LOGE("Could not initialize SDL gamepad: %s", SDL_GetError());
            goto end;
        }
    }

    // Await for server without blocking Ctrl+C handling
    bool connected;
    if (!await_for_server(s->current, &connected, screen_initialized ? &s->screen : NULL,
                           dispatcher)) {
        LOGE("Server connection failed");
        retry_session = reconnect;
        goto end;
    }

    if (!connected) {
        // This is not an error, user requested to quit
        LOGD("User requested to quit");
        ret = SCRCPY_EXIT_SUCCESS;
        goto end;
    }

    LOGD("Server connected");

    if (!sc_session_start(s->current)) {
        ret = sc_session_startup_interrupted(s->current)
            ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
        goto end;
    }

    ret = event_loop(s, options->window, reconnect, dispatcher);
    retry_session = reconnect && ret == SCRCPY_EXIT_DISCONNECTED;

    disconnected = ret == SCRCPY_EXIT_DISCONNECTED;
#ifdef _WIN32
    if (sc_machine_is_active() && disconnected) {
        sc_machine_emit_lifecycle("TransportLost", "connection",
                                  machine_attempt_id, "transportLost", "none");
        if (retry_session) {
            sc_machine_emit_lifecycle("ReconnectScheduled", "connection",
                                      NULL, "transportLost", "none");
            reconnect_scheduled = true;
        }
    }
#endif

end:
#ifdef _WIN32
    if (sc_machine_is_active() && retry_session && !reconnect_scheduled) {
        sc_machine_emit_lifecycle("ReconnectScheduled", "connection", NULL,
                                  "unknown", "unknown");
    }
#endif
    if (retry_session && screen_initialized) {
        sc_screen_prepare_reconnect(&s->screen);
    }
    sc_session_request_stop(s->current);

    if (screen_initialized && !retry_session) {
        sc_screen_interrupt(&s->screen);
        screen_interrupted = true;

        if (disconnected) {
            sc_screen_handle_disconnection(&s->screen);
        }
        sc_screen_hide_window(&s->screen);
    }
    sc_session_join(s->current);
    sc_session_destroy(s->current);
    s->current = NULL;

finish_screen:
    if (screen_initialized && !retry_session) {

        if (!screen_interrupted) {
            sc_screen_interrupt(&s->screen);
        }
        sc_screen_join(&s->screen);
#ifdef _WIN32
        sc_machine_set_window(NULL);
#endif
        sc_screen_destroy(&s->screen);
    }

    if (retry_session) {
        if (screen_initialized) {
            sc_screen_clear_pending_frames(&s->screen);
        }

        if (sc_wait_reconnect(screen_initialized ? &s->screen : NULL,
                               dispatcher)) {
            retrying = true;
            LOGI("Reconnecting to %s", reconnect_serial);
            goto restart_session;
        }

        if (screen_initialized) {
            sc_screen_interrupt(&s->screen);
            sc_screen_join(&s->screen);
#ifdef _WIN32
            sc_machine_set_window(NULL);
#endif
            sc_screen_destroy(&s->screen);
        }
        ret = SCRCPY_EXIT_SUCCESS;
    }

    return ret;
}
