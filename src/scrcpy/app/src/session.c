#include "session.h"

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

#include "audio_player.h"
#include "controller.h"
#include "decoder.h"
#include "delay_buffer.h"
#include "demuxer.h"
#include "dispatcher.h"
#include "events.h"
#include "file_pusher.h"
#include "generation_targets.h"
#ifdef _WIN32
# include "ipc/machine.h"
# include "ipc/machine_exit.h"
#endif
#include "keyboard_sdk.h"
#include "mouse_sdk.h"
#include "recorder.h"
#include "screen.h"
#include "sdl_hints.h"
#include "server.h"
#include "uhid/gamepad_uhid.h"
#include "uhid/keyboard_uhid.h"
#include "uhid/mouse_uhid.h"
#ifdef HAVE_USB
# include "usb/aoa_hid.h"
# include "usb/gamepad_aoa.h"
# include "usb/keyboard_aoa.h"
# include "usb/mouse_aoa.h"
# include "usb/usb.h"
#endif
#include "util/acksync.h"
#include "util/log.h"
#include "util/rand.h"
#include "util/timeout.h"
#include "util/tick.h"
#ifdef HAVE_V4L2
# include "v4l2_sink.h"
#endif

#include "session_notifications.h"

struct sc_session {
    const struct scrcpy_options *options;
    struct sc_dispatcher *dispatcher;
    struct sc_screen *screen;
    bool *screen_initialized; // borrowed application slot, main-thread only
    bool screen_attached;
    sc_dispatcher_generation generation;
    struct sc_session_notifications notifications;
    struct sc_server server;
    struct sc_video_bridge video_bridge;
    struct sc_generation_targets generation_targets;
    struct sc_audio_player audio_player;
    struct sc_demuxer video_demuxer;
    struct sc_demuxer audio_demuxer;
    struct sc_decoder video_decoder;
    struct sc_decoder audio_decoder;
    struct sc_recorder recorder;
    struct sc_delay_buffer video_buffer;
#ifdef HAVE_V4L2
    struct sc_v4l2_sink v4l2_sink;
    struct sc_delay_buffer v4l2_buffer;
#endif
    struct sc_controller controller;
    struct sc_file_pusher file_pusher;
#ifdef HAVE_USB
    struct sc_usb usb;
    struct sc_aoa aoa;
    // sequence/ack helper to synchronize clipboard and Ctrl+v via HID
    struct sc_acksync acksync;
#endif
    struct sc_uhid_devices uhid_devices;
    union {
        struct sc_keyboard_sdk keyboard_sdk;
        struct sc_keyboard_uhid keyboard_uhid;
#ifdef HAVE_USB
        struct sc_keyboard_aoa keyboard_aoa;
#endif
    };
    union {
        struct sc_mouse_sdk mouse_sdk;
        struct sc_mouse_uhid mouse_uhid;
#ifdef HAVE_USB
        struct sc_mouse_aoa mouse_aoa;
#endif
    };
    union {
        struct sc_gamepad_uhid gamepad_uhid;
#ifdef HAVE_USB
        struct sc_gamepad_aoa gamepad_aoa;
#endif
    };
    struct sc_timeout timeout;
    bool server_initialized;
    bool server_started;
    bool file_pusher_initialized;
    bool recorder_initialized;
    bool recorder_started;
    bool video_demuxer_started;
    bool audio_demuxer_started;
    bool controller_initialized;
    bool controller_started;
    bool timeout_initialized;
    bool timeout_started;
#ifdef HAVE_USB
    bool acksync_initialized;
    bool usb_initialized;
    bool usb_connected;
    bool aoa_initialized;
    bool aoa_started;
    bool keyboard_aoa_initialized;
    bool mouse_aoa_initialized;
    bool gamepad_aoa_initialized;
#endif
#ifdef HAVE_V4L2
    bool v4l2_sink_initialized;
#endif
    bool stop_requested;
    bool joined;
    bool startup_interrupted;
};

/** Observe application Stop without reclassifying a preceding module failure. */
static bool
sc_session_start_allowed(struct sc_session *session) {
    bool stopped = session->stop_requested;
#ifdef _WIN32
    stopped |= sc_machine_is_active() && sc_machine_stop_requested();
#endif

    if (stopped) {
        session->startup_interrupted = true;
        return false;
    }

    return true;
}

static void
sc_recorder_on_ended(struct sc_recorder *recorder, bool success,
                     void *userdata) {
    (void) recorder;
    struct sc_session *session = userdata;

    if (!success) {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_RECORDER_ERROR);
    }
}

static void
sc_video_demuxer_on_ended(struct sc_demuxer *demuxer,
                          enum sc_demuxer_status status, void *userdata) {
    (void) demuxer;
    struct sc_session *session = userdata;

    // The device may not decide to disable the video
    assert(status != SC_DEMUXER_STATUS_DISABLED);

    if (status == SC_DEMUXER_STATUS_EOS) {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_DEVICE_DISCONNECTED);
    } else {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_DEMUXER_ERROR);
    }
}

static void
sc_audio_demuxer_on_ended(struct sc_demuxer *demuxer,
                          enum sc_demuxer_status status, void *userdata) {
    (void) demuxer;

    struct sc_session *session = userdata;
    const struct scrcpy_options *options = session->options;

    // Contrary to the video demuxer, keep mirroring if only the audio fails
    // (unless --require-audio is set).
    if (status == SC_DEMUXER_STATUS_EOS) {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_DEVICE_DISCONNECTED);
    } else if (status == SC_DEMUXER_STATUS_ERROR
            || (status == SC_DEMUXER_STATUS_DISABLED
                && options->require_audio)) {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_DEMUXER_ERROR);
    }
}

static void
sc_controller_on_ended(struct sc_controller *controller, bool error,
                       void *userdata) {
    // Note: this function may be called twice, once from the controller thread
    // and once from the receiver thread
    (void) controller;
    struct sc_session *session = userdata;

    if (error) {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_CONTROLLER_ERROR);
    } else {
        sc_session_notifications_publish(&session->notifications, SC_EVENT_DEVICE_DISCONNECTED);
    }
}

static void
sc_server_on_connection_failed(struct sc_server *server, void *userdata) {
    (void) server;
    struct sc_session *session = userdata;

    sc_session_notifications_publish(&session->notifications, SC_EVENT_SERVER_CONNECTION_FAILED);
}

static void
sc_server_on_connected(struct sc_server *server, void *userdata) {
    (void) server;
    struct sc_session *session = userdata;

    sc_session_notifications_publish(&session->notifications, SC_EVENT_SERVER_CONNECTED);
}

static void
sc_server_on_disconnected(struct sc_server *server, void *userdata) {
    (void) server;
    (void) userdata;

    LOGD("Server disconnected");
    // Do nothing, the disconnection will be handled by the "stream stopped"
    // event
}

static void
sc_timeout_on_timeout(struct sc_timeout *timeout, void *userdata) {
    (void) timeout;
    struct sc_session *session = userdata;

    sc_session_notifications_publish(&session->notifications, SC_EVENT_TIME_LIMIT_REACHED);
}


#ifdef HAVE_USB
/** Attribute asynchronous AOA-open failure to its stable generation owner. */
static void
sc_session_on_aoa_error(void *userdata) {
    struct sc_session *session = userdata;
    sc_session_notifications_publish(&session->notifications, SC_EVENT_AOA_OPEN_ERROR);
}
#endif

/** Allocate stable storage before admitting any generation producer. */
struct sc_session *
sc_session_create(const struct scrcpy_options *options,
                  struct sc_dispatcher *dispatcher, struct sc_screen *screen,
                  bool *screen_initialized) {
    struct sc_session *session = calloc(1, sizeof(*session));

    if (!session) {
        LOG_OOM();
        return NULL;
    }
    session->options = options;
    session->dispatcher = dispatcher;
    session->screen = screen;
    session->screen_initialized = screen_initialized;

    if (!sc_session_notifications_init(&session->notifications, dispatcher)) {
        free(session);
        return NULL;
    }
    session->generation_targets.notifications = &session->notifications;

    if (!sc_dispatcher_generation_begin(dispatcher, &session->generation_targets,
                                        &session->generation)) {
        sc_session_notifications_destroy(&session->notifications);
        free(session);
        return NULL;
    }
    bool bound = sc_session_notifications_bind(&session->notifications,
                                               session->generation);
    assert(bound);
    (void) bound;
    LOGD("Session generation=%" PRIu64_ " created", session->generation);
    return session;
}

/** Start only successfully initialized server storage. */
bool
sc_session_start_server(struct sc_session *session,
                        const struct sc_server_params *params) {
    if (!sc_session_start_allowed(session)) {
        return false;
    }
    assert(!session->server_initialized);
    static const struct sc_server_callbacks callbacks = {
        .on_connection_failed = sc_server_on_connection_failed,
        .on_connected = sc_server_on_connected,
        .on_disconnected = sc_server_on_disconnected,
    };

    if (!sc_server_init(&session->server, params, &callbacks, session)) {
        return false;
    }
    session->server_initialized = true;

    if (!sc_session_start_allowed(session) || !sc_server_start(&session->server)) {
        return false;
    }
    session->server_started = true;
    return true;
}

static void
init_sdl_gamepads(void) {
    // Trigger a SDL_EVENT_GAMEPAD_ADDED event for all gamepads already
    // connected
    int count;
    SDL_JoystickID *joysticks = SDL_GetJoysticks(&count);
    if (!joysticks) {
        LOGE("Could not list joysticks: %s", SDL_GetError());
        return;
    }

    for (int i = 0; i < count; ++i) {
        SDL_JoystickID joystick = joysticks[i];
        if (SDL_IsGamepad(joystick)) {
            SDL_Event event;
            event.gdevice.type = SDL_EVENT_GAMEPAD_ADDED;
            event.gdevice.which = i;
            SDL_PushEvent(&event);
        }
    }
    // SDL owns the device list format; the caller owns this returned array.
    SDL_free(joysticks);
}


/** Compose the actual connected graph; each module retains partial-open unwind. */
bool
sc_session_start(struct sc_session *session) {
    const struct scrcpy_options *options = session->options;

    if (!sc_session_start_allowed(session)) {
        return false;
    }
    // It is necessarily initialized here, since the device is connected
    const struct sc_server_info *info = &session->server.info;

    const char *serial = session->server.serial;
    assert(serial);

    struct sc_acksync *acksync = NULL;
    struct sc_file_pusher *fp = NULL;

    if (options->window && options->control) {
        if (!sc_file_pusher_init(&session->file_pusher, serial,
                                 options->push_target)) {
            return false;
        }
        fp = &session->file_pusher;
        session->file_pusher_initialized = true;
    }

    if (options->video) {
        static const struct sc_demuxer_callbacks video_demuxer_cbs = {
            .on_ended = sc_video_demuxer_on_ended,
        };
        sc_demuxer_init(&session->video_demuxer, "video", session->server.video_socket,
                        &video_demuxer_cbs, session);
    }

    if (options->audio) {
        static const struct sc_demuxer_callbacks audio_demuxer_cbs = {
            .on_ended = sc_audio_demuxer_on_ended,
        };
        sc_demuxer_init(&session->audio_demuxer, "audio", session->server.audio_socket,
                        &audio_demuxer_cbs, session);
    }

    bool needs_video_decoder = options->video_playback;
    bool needs_audio_decoder = options->audio_playback;
#ifdef HAVE_V4L2
    needs_video_decoder |= !!options->v4l2_device;
#endif
    if (needs_video_decoder) {
        sc_decoder_init(&session->video_decoder, "video");
        sc_packet_source_add_sink(&session->video_demuxer.packet_source,
                                  &session->video_decoder.packet_sink);
    }
    if (needs_audio_decoder) {
        sc_decoder_init(&session->audio_decoder, "audio");
        sc_packet_source_add_sink(&session->audio_demuxer.packet_source,
                                  &session->audio_decoder.packet_sink);
    }

    if (options->record_filename) {
        static const struct sc_recorder_callbacks recorder_cbs = {
            .on_ended = sc_recorder_on_ended,
        };
        if (!sc_recorder_init(&session->recorder, options->record_filename,
                              options->record_format, options->video,
                              options->audio, options->record_orientation,
                              &recorder_cbs, session)) {
            return false;
        }
        session->recorder_initialized = true;

        if (!sc_session_start_allowed(session) || !sc_recorder_start(&session->recorder)) {
            return false;
        }
        session->recorder_started = true;

        if (options->video) {
            sc_packet_source_add_sink(&session->video_demuxer.packet_source,
                                      &session->recorder.video_packet_sink);
        }
        if (options->audio) {
            sc_packet_source_add_sink(&session->audio_demuxer.packet_source,
                                      &session->recorder.audio_packet_sink);
        }
    }

    struct sc_controller *controller = NULL;
    struct sc_key_processor *kp = NULL;
    struct sc_mouse_processor *mp = NULL;
    struct sc_gamepad_processor *gp = NULL;

    if (options->control) {
        static const struct sc_controller_callbacks controller_cbs = {
            .on_ended = sc_controller_on_ended,
        };

        if (!sc_controller_init(&session->controller, session->server.control_socket,
            &controller_cbs, session)) {
            return false;
        }
        session->controller_initialized = true;

        controller = &session->controller;

#ifdef HAVE_USB
        bool use_keyboard_aoa =
            options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA;
        bool use_mouse_aoa =
            options->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA;
        bool use_gamepad_aoa =
            options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_AOA;
        if (use_keyboard_aoa || use_mouse_aoa || use_gamepad_aoa) {
            if (!sc_acksync_init(&session->acksync)) {
                return false;
            }
            session->acksync_initialized = true;

            if (!sc_usb_init(&session->usb)) {
                return false;
            }
            session->usb_initialized = true;
            struct sc_usb_device usb_device;

            if (!sc_usb_select_device(&session->usb, serial, &usb_device)) {
                return false;
            }
            bool connected = sc_usb_connect(&session->usb, usb_device.device, NULL, NULL);
            sc_usb_device_destroy(&usb_device);

            if (!connected) {
                return false;
            }
            session->usb_connected = true;

            if (!sc_aoa_init(&session->aoa, &session->usb, &session->acksync)) {
                return false;
            }
            session->aoa_initialized = true;
            sc_aoa_set_error_callback(&session->aoa, sc_session_on_aoa_error, session);

            if (use_keyboard_aoa) {

                if (!sc_keyboard_aoa_init(&session->keyboard_aoa, &session->aoa)) {
                    return false;
                }
                session->keyboard_aoa_initialized = true;
                kp = &session->keyboard_aoa.key_processor;
            }

            if (use_mouse_aoa) {

                if (!sc_mouse_aoa_init(&session->mouse_aoa, &session->aoa)) {
                    return false;
                }
                session->mouse_aoa_initialized = true;
                mp = &session->mouse_aoa.mouse_processor;
            }

            if (use_gamepad_aoa) {
                sc_gamepad_aoa_init(&session->gamepad_aoa, &session->aoa);
                gp = &session->gamepad_aoa.gamepad_processor;
                session->gamepad_aoa_initialized = true;
            }

            if (!sc_session_start_allowed(session) || !sc_aoa_start(&session->aoa)) {
                return false;
            }
            session->aoa_started = true;
            acksync = &session->acksync;
        }
#else
        assert(options->keyboard_input_mode != SC_KEYBOARD_INPUT_MODE_AOA);
        assert(options->mouse_input_mode != SC_MOUSE_INPUT_MODE_AOA);
#endif

        struct sc_keyboard_uhid *uhid_keyboard = NULL;

        if (options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_SDK) {
            sc_keyboard_sdk_init(&session->keyboard_sdk, &session->controller,
                                 options->key_inject_mode,
                                 options->forward_key_repeat);
            kp = &session->keyboard_sdk.key_processor;
        } else if (options->keyboard_input_mode
                == SC_KEYBOARD_INPUT_MODE_UHID) {
            bool ok = sc_keyboard_uhid_init(&session->keyboard_uhid, &session->controller);
            if (!ok) {
                return false;
            }
            kp = &session->keyboard_uhid.key_processor;
            uhid_keyboard = &session->keyboard_uhid;
        }

        if (options->mouse_input_mode == SC_MOUSE_INPUT_MODE_SDK) {
            sc_mouse_sdk_init(&session->mouse_sdk, &session->controller,
                              options->mouse_hover);
            mp = &session->mouse_sdk.mouse_processor;
        } else if (options->mouse_input_mode == SC_MOUSE_INPUT_MODE_UHID) {
            bool ok = sc_mouse_uhid_init(&session->mouse_uhid, &session->controller);
            if (!ok) {
                return false;
            }
            mp = &session->mouse_uhid.mouse_processor;
        }

        if (options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_UHID) {
            sc_gamepad_uhid_init(&session->gamepad_uhid, &session->controller);
            gp = &session->gamepad_uhid.gamepad_processor;
        }

        struct sc_uhid_devices *uhid_devices = NULL;
        if (uhid_keyboard) {
            sc_uhid_devices_init(&session->uhid_devices, uhid_keyboard);
            uhid_devices = &session->uhid_devices;
        }

        // Bind only the initialized target; queued work carries no target ptr.
        session->generation_targets.uhid_devices = uhid_devices;
        if (!sc_dispatcher_generation_bind(session->dispatcher, session->generation,
                                            &session->generation_targets)) {
            return false;
        }

        sc_controller_configure(&session->controller, acksync, uhid_devices,
                                 session->dispatcher, session->generation);

        if (!sc_session_start_allowed(session) || !sc_controller_start(&session->controller)) {
            return false;
        }
        session->controller_started = true;
    }

    // There is a controller if and only if control is enabled
    assert(options->control == !!controller);

    if (options->window) {
        const char *window_title =
            options->window_title ? options->window_title : info->device_name;

        struct sc_screen_params screen_params = {
            .dispatcher = session->dispatcher,
            .generation = session->generation,
            .video = options->video_playback,
            .camera = options->video_source == SC_VIDEO_SOURCE_CAMERA,
            .flex_display = options->flex_display,
            .controller = controller,
            .fp = fp,
            .kp = kp,
            .mp = mp,
            .gp = gp,
            .mouse_bindings = options->mouse_bindings,
            .legacy_paste = options->legacy_paste,
            .clipboard_autosync = options->clipboard_autosync,
            .shortcut_mods = options->shortcut_mods,
            .window_title = window_title,
            .always_on_top = options->always_on_top,
            .window_x = options->window_x,
            .window_y = options->window_y,
            .window_width = options->window_width,
            .window_height = options->window_height,
            .background_color = options->background_color,
            .window_aspect_ratio_lock = options->window_aspect_ratio_lock,
            .window_borderless = options->window_borderless,
            .render_fit = options->render_fit,
            .orientation = options->display_orientation,
            .mipmaps = options->mipmaps,
            .fullscreen = options->fullscreen,
            .start_fps_counter = options->start_fps_counter,
        };

        if (*session->screen_initialized) {
            sc_screen_rebind(session->screen, &screen_params);
        } else {
            if (!sc_screen_init(session->screen, &screen_params)) {
                return false;
            }
            *session->screen_initialized = true;
        }
        session->screen_attached = true;
        session->generation_targets.screen = session->screen;
        sc_video_bridge_init(&session->video_bridge, &session->screen->ingress,
                              session->generation);
#ifdef _WIN32
        if (sc_machine_is_active()) {
            sc_machine_set_window(session->screen->window);
        }
#endif

        if (options->video_playback) {
            struct sc_frame_source *src = &session->video_decoder.frame_source;
            if (options->video_buffer) {
                sc_delay_buffer_init(&session->video_buffer,
                                     options->video_buffer, true);
                sc_frame_source_add_sink(src, &session->video_buffer.frame_sink);
                src = &session->video_buffer.frame_source;
            }

            sc_frame_source_add_sink(src, &session->video_bridge.frame_sink);
        }
    }

    if (options->audio_playback) {
        sc_audio_player_init(&session->audio_player, options->audio_buffer,
                             options->audio_output_buffer);
        sc_frame_source_add_sink(&session->audio_decoder.frame_source,
                                 &session->audio_player.frame_sink);
    }

#ifdef HAVE_V4L2
    if (options->v4l2_device) {
        if (!sc_v4l2_sink_init(&session->v4l2_sink, options->v4l2_device)) {
            return false;
        }

        struct sc_frame_source *src = &session->video_decoder.frame_source;
        if (options->v4l2_buffer) {
            sc_delay_buffer_init(&session->v4l2_buffer, options->v4l2_buffer, true);
            sc_frame_source_add_sink(src, &session->v4l2_buffer.frame_sink);
            src = &session->v4l2_buffer.frame_source;
        }

        sc_frame_source_add_sink(src, &session->v4l2_sink.frame_sink);

        session->v4l2_sink_initialized = true;
    }
#endif

    // Now that the header values have been consumed, the socket(s) will
    // receive the stream(s). Start the demuxer(s).

    if (options->video) {
        if (!sc_session_start_allowed(session) || !sc_demuxer_start(&session->video_demuxer)) {
            return false;
        }
        session->video_demuxer_started = true;
    }

    if (options->audio) {
        if (!sc_session_start_allowed(session) || !sc_demuxer_start(&session->audio_demuxer)) {
            return false;
        }
        session->audio_demuxer_started = true;
    }

    // If the device screen is to be turned off, send the control message after
    // everything is set up
    if (options->control && options->turn_screen_off) {
        struct sc_control_msg msg;
        msg.type = SC_CONTROL_MSG_TYPE_SET_DISPLAY_POWER;
        msg.set_display_power.on = false;

        if (!sc_controller_push_msg(&session->controller, &msg)) {
            LOGW("Could not request 'set display power'");
        }
    }

    if (options->time_limit) {
        bool ok = sc_timeout_init(&session->timeout);
        if (!ok) {
            return false;
        }

        session->timeout_initialized = true;

        sc_tick deadline = sc_tick_now() + options->time_limit;
        static const struct sc_timeout_callbacks cbs = {
            .on_timeout = sc_timeout_on_timeout,
        };

        ok = sc_session_start_allowed(session)
          && sc_timeout_start(&session->timeout, deadline, &cbs, session);
        if (!ok) {
            return false;
        }

        session->timeout_started = true;
    }

    if (options->control
            && options->gamepad_input_mode != SC_GAMEPAD_INPUT_MODE_DISABLED) {
        init_sdl_gamepads();
    }

    if (options->control && options->start_app) {
        assert(controller);

        char *name = strdup(options->start_app);
        if (!name) {
            LOG_OOM();
            return false;
        }

        struct sc_control_msg msg;
        msg.type = SC_CONTROL_MSG_TYPE_START_APP;
        msg.start_app.name = name;

        if (!sc_controller_push_msg(controller, &msg)) {
            LOGW("Could not request start app '%s'", name);
            free(name);
        }
    }

    return true;
}

/** Detach and close admission before interrupting any borrowed destination. */
void
sc_session_request_stop(struct sc_session *session) {

    if (session->stop_requested) {
        return;
    }
    session->stop_requested = true;

    if (session->screen_attached) {
        sc_screen_detach(session->screen);
    }
    sc_session_notifications_revoke(&session->notifications);
    bool revoked = sc_dispatcher_generation_revoke(session->dispatcher, session->generation);
    assert(revoked);
    (void) revoked;

    if (session->timeout_started) {
        sc_timeout_stop(&session->timeout);
    }
#ifdef HAVE_USB
    if (session->aoa_started) {
        sc_aoa_stop(&session->aoa);
    }

    if (session->usb_initialized) {
        sc_usb_stop(&session->usb);
    }
#endif
    if (session->controller_started) {
        sc_controller_stop(&session->controller);
    }

    if (session->file_pusher_initialized) {
        sc_file_pusher_stop(&session->file_pusher);
    }

    if (session->recorder_initialized) {
        sc_recorder_stop(&session->recorder);
    }

    if (session->server_started) {
        sc_server_stop(&session->server);
    }
    LOGD("Session generation=%" PRIu64_ " stop requested", session->generation);
}

/** Join every publisher and borrower before freeing any generation context. */
void
sc_session_join(struct sc_session *session) {
    assert(session->stop_requested);

    if (session->joined) {
        return;
    }

    if (session->timeout_started) {
        sc_timeout_join(&session->timeout);
    }

    if (session->video_demuxer_started) {
        sc_demuxer_join(&session->video_demuxer);
    }

    if (session->audio_demuxer_started) {
        sc_demuxer_join(&session->audio_demuxer);
    }
#ifdef HAVE_USB
    if (session->aoa_started) {
        sc_aoa_join(&session->aoa);
    }

    if (session->usb_initialized) {
        sc_usb_join(&session->usb);
    }
#endif
    // Receiver handles ACK directly: its join must precede ACK destruction.
    if (session->controller_started) {
        sc_controller_join(&session->controller);
    }

    if (session->recorder_started) {
        sc_recorder_join(&session->recorder);
    }

    if (session->file_pusher_initialized) {
        sc_file_pusher_join(&session->file_pusher);
    }

    if (session->server_started) {
        sc_server_join(&session->server);
    }
    session->joined = true;
    LOGD("Session generation=%" PRIu64_ " joined", session->generation);
}

/** Release acquired stages after every callback and lazy worker has settled. */
void
sc_session_destroy(struct sc_session *session) {
    assert(session->stop_requested && session->joined);
#ifdef HAVE_V4L2
    if (session->v4l2_sink_initialized) {
        sc_v4l2_sink_destroy(&session->v4l2_sink);
    }
#endif
#ifdef HAVE_USB
    if (session->keyboard_aoa_initialized) {
        sc_keyboard_aoa_destroy(&session->keyboard_aoa);
    }

    if (session->mouse_aoa_initialized) {
        sc_mouse_aoa_destroy(&session->mouse_aoa);
    }

    if (session->gamepad_aoa_initialized) {
        sc_gamepad_aoa_destroy(&session->gamepad_aoa);
    }

    if (session->aoa_initialized) {
        sc_aoa_destroy(&session->aoa);
    }

    if (session->usb_connected) {
        sc_usb_disconnect(&session->usb);
    }

    if (session->usb_initialized) {
        sc_usb_destroy(&session->usb);
    }

    if (session->acksync_initialized) {
        sc_acksync_destroy(&session->acksync);
    }
#endif
    if (session->controller_initialized) {
        sc_controller_destroy(&session->controller);
    }

    if (session->recorder_initialized) {
        sc_recorder_destroy(&session->recorder);
    }

    if (session->file_pusher_initialized) {
        sc_file_pusher_destroy(&session->file_pusher);
    }

    if (session->timeout_initialized) {
        sc_timeout_destroy(&session->timeout);
    }

    if (session->server_initialized) {
        sc_server_destroy(&session->server);
    }
    sc_session_notifications_destroy(&session->notifications);
    LOGD("Session generation=%" PRIu64_ " destroyed", session->generation);
    free(session);
}

/** Take only the current owner's retained, publication-ordered outcome. */
bool
sc_session_take_event(struct sc_session *session, uint32_t *event) {
    return sc_session_notifications_take(&session->notifications, event);
}

/** Validate value-scoped wakes against the live binding before owner delivery. */
bool
sc_session_handle_event(struct sc_session *session, const SDL_Event *event,
                        uint32_t *outcome) {
    return sc_session_notifications_handle_event(&session->notifications,
                                                  event, outcome);
}

/** Keep an observed startup interruption separate from resource failure. */
bool
sc_session_startup_interrupted(const struct sc_session *session) {
    return session->startup_interrupted;
}
