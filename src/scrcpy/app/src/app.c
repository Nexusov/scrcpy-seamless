#include "app.h"

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
#include "adb/adb.h"
#include "connection_plan.h"
#include "transport_resolver.h"
#include "recording_segments.h"
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
#include "session_retirement.h"
#include "recovery_adapter.h"

#define SC_APP_STARTUP_TIMEOUT SC_TICK_FROM_SEC(30)

enum sc_app_state {
    SC_APP_CONNECTING,
    SC_APP_STREAMING,
    SC_APP_RETIRING,
    SC_APP_WAITING,
    SC_APP_STOPPED,
};

/* Persistent presentation and one stable, sequentially retired generation. */
struct sc_app {
    enum sc_app_state state;
    struct sc_screen screen;
    struct sc_session *current;
    struct sc_session_retirement retirement;
    struct sc_connection_manager connection;
    enum sc_connection_failure failure;
    sc_tick startup_started;
    bool stopped;
    bool recording_output_opened;
    bool has_recording_result;
    enum sc_recorder_result recording_result;
    bool video_initialized;
    bool audio_initialized;
    bool gamepad_initialized;
    bool retirement_quit_is_terminal;
};

/** Observe sticky process intent independently of SDL wake delivery. */
static void
sc_app_observe_stop(struct sc_app *s) {
#ifdef _WIN32

    if (sc_machine_is_active() && sc_machine_stop_requested()) {
        s->stopped = true;
        enum sc_connection_failure reason =
            !strcmp(sc_machine_stop_reason(), "protocolError")
                ? SC_CONNECTION_FAILURE_PROTOCOL
                : SC_CONNECTION_FAILURE_USER_STOP;
        sc_connection_manager_stop(&s->connection, reason, sc_tick_now());
    }
#else
    (void) s;
#endif
}

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

/** Keep only application/window work live while destinations are detached. */
static void
sc_retirement_pump(struct sc_app *s, bool has_screen,
                   struct sc_dispatcher *dispatcher) {
    sc_app_observe_stop(s);
    SDL_Event event;

    if (!SDL_WaitEventTimeout(&event, SC_SESSION_EVENT_WAIT_MS)) {
        return;
    }

    if (sc_dispatcher_handle_event(dispatcher, &event)) {
        return;
    }

    if (event.type == SDL_EVENT_QUIT ||
            (has_screen && event.type == SDL_EVENT_WINDOW_CLOSE_REQUESTED &&
             event.window.windowID == SDL_GetWindowID(s->screen.window))) {
        s->stopped = true;
#ifdef _WIN32
        if (s->retirement_quit_is_terminal) {
            sc_machine_observe_quit(SDL_EVENT_QUIT);
        }
#endif
        return;
    }

    if (!has_screen) {
        return;
    }

    switch (event.type) {
        case SDL_EVENT_WINDOW_EXPOSED:
        case SDL_EVENT_WINDOW_RESIZED:
        case SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
        case SDL_EVENT_WINDOW_RESTORED:
            sc_screen_handle_event(&s->screen, &event);
            break;
        default:
            break;
    }
}

/** No SDL presentation teardown or replacement is allowed in this worker. */
static void
sc_retire_generation(void *userdata) {
    struct sc_app *s = userdata;
    sc_session_request_stop(s->current);
    sc_session_join(s->current);
    s->recording_output_opened =
        sc_session_recording_output_opened(s->current);
    s->has_recording_result =
        sc_session_get_recording_result(s->current, &s->recording_result);
    sc_session_release_resources(s->current);

}

static void
sc_join_presentation(void *userdata) {
    sc_screen_join(userdata);
}

/** Preserve presentation until its application-owned helpers actually join. */
static void
sc_retire_presentation(struct sc_app *s, struct sc_dispatcher *dispatcher) {
    sc_screen_interrupt_helpers(&s->screen);
    bool accepted = sc_session_retirement_request(&s->retirement,
                                                  sc_join_presentation,
                                                  &s->screen);
    assert(accepted);
    (void) accepted;

    while (!sc_session_retirement_take_completed(&s->retirement)) {
        sc_retirement_pump(s, true, dispatcher);
    }
}

/** Borrow stable application/owner storage until the worker releases it. */
static void
sc_retire_current(struct sc_app *s, bool has_screen,
                   struct sc_dispatcher *dispatcher) {
    sc_session_prepare_retirement(s->current);
    bool accepted = sc_session_retirement_request(&s->retirement,
                                                  sc_retire_generation, s);
    assert(accepted);
    (void) accepted;

    while (!sc_session_retirement_take_completed(&s->retirement)) {
        sc_retirement_pump(s, has_screen, dispatcher);
    }
    sc_session_destroy(s->current);
    s->current = NULL;
}

/** One event source preserves scoped outcomes and the dispatcher drain lease. */
static bool
sc_app_next_event(struct sc_app *s, struct sc_dispatcher *dispatcher,
                   SDL_Event *event) {
    uint32_t outcome;

    if (sc_session_take_event(s->current, &outcome)) {
        *event = (SDL_Event) {.type = outcome};
        return true;
    }

    if (!SDL_WaitEventTimeout(event, SC_SESSION_EVENT_WAIT_MS)) {
        return false;
    }

    if (event->type == SC_EVENT_SESSION_NOTIFICATION) {

        if (!sc_session_handle_event(s->current, event, &outcome)) {
            return false;
        }
        event->type = outcome;
    }

    return !sc_dispatcher_handle_event(dispatcher, event);
}

/** The coordinator interprets events according to its current live phase. */
static enum scrcpy_exit_code
sc_app_wait(struct sc_app *s, bool has_screen, bool reconnect,
             struct sc_dispatcher *dispatcher, bool *connected) {
    SDL_Event event;

    for (;;) {
        sc_app_observe_stop(s);
        struct sc_connection_action action =
            sc_connection_manager_poll(&s->connection, sc_tick_now());

        if (action.type == SC_CONNECTION_ACTION_RETIRE
                || action.type == SC_CONNECTION_ACTION_FINISH) {
            s->failure = action.failure;

            if (connected) {
                *connected = false;
            }
            return action.failure == SC_CONNECTION_FAILURE_DEADLINE
                || action.failure == SC_CONNECTION_FAILURE_USER_STOP
                ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
        }

        if (s->state == SC_APP_CONNECTING
                && sc_tick_now() - s->startup_started >= SC_APP_STARTUP_TIMEOUT) {
            s->failure = SC_CONNECTION_FAILURE_TRANSIENT_CONNECT;

            if (connected) {
                *connected = false;
            }
            return SCRCPY_EXIT_FAILURE;
        }
        uint8_t degraded = sc_session_take_degraded(s->current);
#ifdef _WIN32

        if (sc_machine_is_active() && degraded) {
            static const struct {
                uint8_t bit;
                const char *name;
            } channels[] = {
                {SC_SESSION_CAPABILITY_VIDEO, "video"},
                {SC_SESSION_CAPABILITY_AUDIO, "audio"},
                {SC_SESSION_CAPABILITY_CONTROL, "control"},
            };

            for (unsigned index = 0; index < ARRAY_LEN(channels); ++index) {

                if ((degraded & channels[index].bit)
                        && !sc_machine_emit_lifecycle("CapabilityDegraded",
                            channels[index].name, NULL, "unknown", "unknown")) {
                    s->failure = SC_CONNECTION_FAILURE_PROTOCOL;
                    return SCRCPY_EXIT_FAILURE;
                }
            }
        }
#else
        (void) degraded;
#endif

        if (!sc_app_next_event(s, dispatcher, &event)) {
            continue;
        }

        if (event.type == SDL_EVENT_QUIT) {
#ifdef _WIN32
            sc_machine_observe_quit(event.type);
#endif
            s->stopped = true;
            s->failure = SC_CONNECTION_FAILURE_WINDOW_CLOSED;

            if (connected) {
                *connected = false;
            }
            return SCRCPY_EXIT_SUCCESS;
        }

        if (s->state == SC_APP_CONNECTING) {

            if (event.type == SC_EVENT_SERVER_TRANSIENT_FAILURE) {
                s->failure = SC_CONNECTION_FAILURE_TRANSIENT_CONNECT;
                return SCRCPY_EXIT_FAILURE;
            }

            if (event.type == SC_EVENT_SERVER_CONNECTION_FAILED) {
                s->failure = SC_CONNECTION_FAILURE_SERVER_START;
                return SCRCPY_EXIT_FAILURE;
            }

            if (event.type == SC_EVENT_SERVER_CONNECTED) {

                if (connected) {
                    *connected = true;
                }
                return SCRCPY_EXIT_SUCCESS;
            }

            if (has_screen) {
                sc_screen_handle_event(&s->screen, &event);
            }
            continue;
        }

        switch (event.type) {
            case SC_EVENT_DEVICE_DISCONNECTED:
                s->failure = SC_CONNECTION_FAILURE_TRANSPORT_LOST;

                if (has_screen && !reconnect) {
                    sc_screen_handle_event(&s->screen, &event);
                }
                return SCRCPY_EXIT_DISCONNECTED;
            case SC_EVENT_DEMUXER_ERROR:
                s->failure = SC_CONNECTION_FAILURE_DECODER;
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_PROTOCOL_ERROR:
                s->failure = SC_CONNECTION_FAILURE_PROTOCOL;
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_CONTROLLER_ERROR:
                s->failure = SC_CONNECTION_FAILURE_CONTROLLER;
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_RECORDER_ERROR:
                s->failure = SC_CONNECTION_FAILURE_RECORDING;
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_AOA_OPEN_ERROR:
                s->failure = SC_CONNECTION_FAILURE_SERVER_START;
                return SCRCPY_EXIT_FAILURE;
            case SC_EVENT_TIME_LIMIT_REACHED:
                s->failure = SC_CONNECTION_FAILURE_DEADLINE;
                return SCRCPY_EXIT_SUCCESS;
            default:

                if (has_screen) {
                    sc_screen_handle_event(&s->screen, &event);
                }
                break;
        }
    }
}

/** Poll value-only policy while keeping application events responsive. */
static bool
sc_app_acquire_target(struct sc_app *s, struct sc_dispatcher *dispatcher,
                       bool has_screen,
                       const struct sc_connection_launch_plan *plan,
                       const struct sc_recovery_adapter *legacy,
                       const struct scrcpy_options *options,
                       struct sc_recovery_target *target,
                       struct sc_transport_result *resolved) {
    struct sc_transport_resolver resolver = {0};
    bool cancelled = false;
    sc_tick started = 0;

    for (;;) {
        sc_app_observe_stop(s);

        if (s->stopped) {
            sc_connection_manager_stop(&s->connection,
                                       SC_CONNECTION_FAILURE_WINDOW_CLOSED,
                                       sc_tick_now());
        }
        struct sc_connection_action action =
            sc_connection_manager_poll(&s->connection, sc_tick_now());

        switch (action.type) {
            case SC_CONNECTION_ACTION_RESOLVE:

                if (!plan) {
                    bool retrying = action.attempt > 1;
                    bool success = sc_recovery_adapter_resolve(legacy, options,
                                                               retrying, target);
                    sc_connection_manager_resolved(&s->connection, action.attempt,
                        success ? SC_CONNECTION_FAILURE_NONE
                                : SC_CONNECTION_FAILURE_INVALID_CONFIGURATION,
                        sc_tick_now());
                    break;
                }

                if (!sc_transport_resolver_start(&resolver, action.attempt,
                                                  &plan->targets[action.candidate])) {
                    sc_connection_manager_resolved(&s->connection, action.attempt,
                        SC_CONNECTION_FAILURE_SERVER_START, sc_tick_now());
                    break;
                }
                cancelled = false;
                started = sc_tick_now();
                break;
            case SC_CONNECTION_ACTION_START:

                if (plan) {
                    *target = (struct sc_recovery_target) {
                        .req_serial = resolved->serial,
                    };
                }
                return true;
            case SC_CONNECTION_ACTION_RETIRE:

                if (resolver.started && !cancelled) {
                    sc_transport_resolver_cancel(&resolver);
                    cancelled = true;
                }

                if (!resolver.started) {
                    sc_connection_manager_retired(&s->connection, action.attempt,
                                                   sc_tick_now());
                }
                break;
            case SC_CONNECTION_ACTION_FINISH:
                s->failure = action.failure;
                return false;
            case SC_CONNECTION_ACTION_WAIT:
                break;
        }

        if (resolver.started) {

            if (!cancelled && sc_tick_now() - started >= SC_TICK_FROM_SEC(8)) {
                sc_transport_resolver_cancel(&resolver);
                cancelled = true;
            }

            if (sc_transport_resolver_take(&resolver, resolved)) {

                if (sc_connection_manager_get_state(&s->connection)
                        == SC_CONNECTION_RETIRING) {
                    sc_connection_manager_retired(&s->connection,
                                                   resolved->attempt, sc_tick_now());
                } else {
                    sc_connection_manager_resolved(&s->connection,
                        resolved->attempt, resolved->failure, sc_tick_now());
                }
            }
        }
        sc_retirement_pump(s, has_screen, dispatcher);
    }
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
sc_app_run(const struct scrcpy_options *options, struct sc_dispatcher *dispatcher) {
    static struct sc_app scrcpy;
#ifndef NDEBUG
    // Detect missing initializations
    memset(&scrcpy, 42, sizeof(scrcpy));
#endif
    struct sc_app *s = &scrcpy;

    const char *reconnect_serial = getenv("SCRCPY_RECONNECT_SERIAL");
    struct sc_recovery_adapter recovery;
    sc_recovery_adapter_init(&recovery, reconnect_serial, getenv("ANDROID_SERIAL"));
    const struct sc_connection_launch_plan *plan = options->connection_plan;
    bool reconnect = plan ? plan->reconnect
                          : sc_recovery_adapter_enabled(&recovery);
    bool incompatible = options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA
                     || options->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA
                     || options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_AOA;

    if (reconnect && incompatible) {
        LOGE("Network recovery is unavailable for AOA input");
        return SCRCPY_EXIT_FAILURE;
    }
    struct sc_connection_plan policy = plan ? plan->policy
        : (struct sc_connection_plan) {
            .candidates = {SC_CONNECTION_USB, SC_CONNECTION_NETWORK},
            .candidate_count = reconnect ? 2 : 1,
            .allow_fallback = reconnect,
            .retry_enabled = reconnect,
            .maximum_attempts = 3,
            .initial_backoff = SC_TICK_FROM_SEC(1),
            .maximum_backoff = SC_TICK_FROM_SEC(8),
        };
    policy.time_limit = options->time_limit;

    if (!sc_connection_manager_init(&s->connection, &policy, sc_tick_now())) {
        return SCRCPY_EXIT_FAILURE;
    }
    bool segmented_recording = reconnect && options->record_filename;
    struct sc_recording_segments segments;

    if (segmented_recording &&
            !sc_recording_segments_init(&segments, options->record_filename)) {
        return SCRCPY_EXIT_FAILURE;
    }
    /* One application clock includes resolve/start/backoff/retirement. */
    struct scrcpy_options generation_options = *options;
    generation_options.time_limit = 0;

    if (!sc_adb_init()) {
        return SCRCPY_EXIT_FAILURE;
    }
    bool screen_initialized = false;
    bool retrying = false;
    s->stopped = false;
    s->video_initialized = false;
    s->audio_initialized = false;
    s->gamepad_initialized = false;
    s->current = NULL;
    s->retirement_quit_is_terminal = false;

    if (!sc_session_retirement_init(&s->retirement)) {
        sc_adb_destroy();
        return SCRCPY_EXIT_FAILURE;
    }
#ifdef _WIN32
    char machine_attempt_id[37] = {0};
#endif

    enum scrcpy_exit_code ret = SCRCPY_EXIT_FAILURE;

    for (;;) {
        s->state = SC_APP_CONNECTING;
        s->failure = SC_CONNECTION_FAILURE_SERVER_START;
        ret = SCRCPY_EXIT_FAILURE;
        bool retry_session = false;
        bool disconnected = false;
        bool screen_interrupted = false;
        struct sc_recording_segment segment = {0};
        s->recording_output_opened = false;
        s->has_recording_result = false;
#ifdef _WIN32
        bool reconnect_scheduled = false;

        if (sc_machine_is_active() && sc_machine_stop_requested()) {
            ret = SCRCPY_EXIT_SUCCESS;
            goto finish_screen;
        }
#endif
        struct sc_recovery_target target;
        struct sc_transport_result resolved;

        if (!sc_app_acquire_target(s, dispatcher, screen_initialized, plan,
                                    &recovery, options, &target, &resolved)) {
            ret = s->failure == SC_CONNECTION_FAILURE_USER_STOP
                || s->failure == SC_CONNECTION_FAILURE_WINDOW_CLOSED
                || s->failure == SC_CONNECTION_FAILURE_DEADLINE
                ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
            goto finish_screen;
        }
        retrying = s->connection.attempt > 1;

        if (segmented_recording) {

            if (!sc_recording_segments_prepare(&segments, &segment)) {
                sc_connection_manager_failed(&s->connection, s->connection.attempt,
                    SC_CONNECTION_FAILURE_RECORDING, sc_tick_now());
                sc_connection_manager_poll(&s->connection, sc_tick_now());
                sc_connection_manager_retired(&s->connection, s->connection.attempt,
                                               sc_tick_now());
                goto finish_screen;
            }
            generation_options.record_filename = segment.filename;
            generation_options.record_exclusive = true;
        }
        s->current = sc_session_create(&generation_options, dispatcher, &s->screen,
                                        &screen_initialized);

        if (!s->current) {
            sc_connection_manager_failed(&s->connection, s->connection.attempt,
                                          SC_CONNECTION_FAILURE_SERVER_START,
                                          sc_tick_now());
            sc_connection_manager_poll(&s->connection, sc_tick_now());
            sc_connection_manager_retired(&s->connection, s->connection.attempt,
                                           sc_tick_now());
            goto finish_screen;
        }
        uint32_t scid = scrcpy_generate_scid();

        struct sc_server_params params = {
            .scid = scid,
            .req_serial = target.req_serial,
            .connect_requested_serial = target.connect_requested_serial,
            .select_usb = target.select_usb,
            .select_tcpip = target.select_tcpip,
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
            .tcpip = target.tcpip,
            .tcpip_dst = target.tcpip_dst,
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
        s->startup_started = sc_tick_now();

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
            ret = sc_app_wait(s, false, reconnect, dispatcher, NULL);
            goto end;
        }

        // playback implies capture
        assert(!options->video_playback || options->video);
        assert(!options->audio_playback || options->audio);

        if (!s->video_initialized && (options->window ||
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
            } else {
                s->video_initialized = true;
            }
        }

        if (!s->audio_initialized && options->audio_playback) {
            if (!SDL_Init(SDL_INIT_AUDIO)) {
                LOGE("Could not initialize SDL audio: %s", SDL_GetError());
                goto end;
            }
            s->audio_initialized = true;
        }

        if (!s->gamepad_initialized && options->gamepad_input_mode != SC_GAMEPAD_INPUT_MODE_DISABLED) {
            if (!SDL_Init(SDL_INIT_GAMEPAD)) {
                LOGE("Could not initialize SDL gamepad: %s", SDL_GetError());
                goto end;
            }
            s->gamepad_initialized = true;
        }

        // Await for server without blocking Ctrl+C handling
        bool connected;
        if (sc_app_wait(s, screen_initialized, reconnect, dispatcher,
                          &connected) != SCRCPY_EXIT_SUCCESS) {
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

        sc_connection_manager_connected(&s->connection, s->connection.attempt,
                                          sc_tick_now());
        s->state = SC_APP_STREAMING;
        ret = sc_app_wait(s, options->window, reconnect, dispatcher, NULL);
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
        s->state = SC_APP_RETIRING;

        if (ret == SCRCPY_EXIT_SUCCESS) {
            sc_app_observe_stop(s);
        }

        if (s->stopped || options->list || ret == SCRCPY_EXIT_SUCCESS) {
            sc_connection_manager_stop(&s->connection,
                s->failure == SC_CONNECTION_FAILURE_DEADLINE
                    ? s->failure : s->stopped
                    ? SC_CONNECTION_FAILURE_WINDOW_CLOSED
                    : SC_CONNECTION_FAILURE_APPLICATION_SHUTDOWN,
                sc_tick_now());
        } else {
            sc_connection_manager_failed(&s->connection, s->connection.attempt,
                                          s->failure, sc_tick_now());
        }
        sc_connection_manager_poll(&s->connection, sc_tick_now());
        retry_session = !s->connection.terminal;
#ifdef _WIN32
        if (sc_machine_is_active() && retry_session && !reconnect_scheduled) {
            sc_machine_emit_lifecycle("ReconnectScheduled", "connection", NULL,
                                      "unknown", "unknown");
        }
#endif
        if (retry_session && screen_initialized) {
            sc_screen_prepare_reconnect(&s->screen);
        }
        s->retirement_quit_is_terminal = retry_session;
        sc_session_prepare_retirement(s->current);

        if (screen_initialized && !retry_session) {
            sc_screen_interrupt(&s->screen);
            screen_interrupted = true;

            if (disconnected) {
                sc_screen_handle_disconnection(&s->screen);
            }
            sc_screen_hide_window(&s->screen);
        }
        sc_retire_current(s, screen_initialized, dispatcher);
        bool recording_failed = s->has_recording_result
            && s->recording_result == SC_RECORDER_RESULT_FAILED;

        if (recording_failed) {
            /* Revocation ends publication, not finalization. Keep this joined
             * failure separate from already recorded user/deadline intent. */
            sc_connection_manager_stop(&s->connection,
                SC_CONNECTION_FAILURE_RECORDING, sc_tick_now());
            ret = SCRCPY_EXIT_FAILURE;
        }

        if (segmented_recording && s->recording_output_opened
                && !sc_recording_segments_commit(&segments,
                                      &segment, s->recording_output_opened)) {
            sc_connection_manager_stop(&s->connection,
                SC_CONNECTION_FAILURE_RECORDING, sc_tick_now());
            ret = SCRCPY_EXIT_FAILURE;
        }

        if (s->stopped) {
            sc_connection_manager_stop(&s->connection,
                SC_CONNECTION_FAILURE_WINDOW_CLOSED, sc_tick_now());
        }
        sc_connection_manager_retired(&s->connection, s->connection.attempt,
                                       sc_tick_now());

        if (!recording_failed
                && s->connection.failure == SC_CONNECTION_FAILURE_DEADLINE) {
            ret = SCRCPY_EXIT_SUCCESS;
        }
        retry_session = !s->connection.terminal;

        if (!recording_failed && s->stopped && s->retirement_quit_is_terminal) {
            retry_session = false;
            ret = SCRCPY_EXIT_SUCCESS;
        }
        s->retirement_quit_is_terminal = false;

    finish_screen:
        sc_recording_segment_destroy(&segment);

        if (screen_initialized && !retry_session) {

            if (!screen_interrupted) {
                sc_screen_interrupt(&s->screen);
            }
            sc_retire_presentation(s, dispatcher);
#ifdef _WIN32
            sc_machine_set_window(NULL);
#endif
            sc_screen_destroy(&s->screen);
        }

        if (retry_session) {

            if (screen_initialized) {
                sc_screen_clear_pending_frames(&s->screen);
            }
            s->state = SC_APP_WAITING;
            continue;
        }

        break;
    }
    s->state = SC_APP_STOPPED;

    bool shutdown = sc_session_retirement_request_shutdown(&s->retirement);
    assert(shutdown);
    (void) shutdown;

    while (!sc_session_retirement_is_stopped(&s->retirement)) {
        sc_retirement_pump(s, false, dispatcher);
    }
    bool destroyed = sc_session_retirement_destroy(&s->retirement);
    assert(destroyed);
    (void) destroyed;
    sc_adb_destroy();
    return ret;
}
