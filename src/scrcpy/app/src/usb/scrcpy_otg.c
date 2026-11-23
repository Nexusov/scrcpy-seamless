#include "scrcpy_otg.h"

#include <assert.h>
#include <stdbool.h>
#include <stdlib.h>
#include <SDL3/SDL.h>

#ifdef _WIN32
# include "adb/adb.h"
#endif
#include "events.h"
#include "dispatcher.h"
#include "generation_targets.h"
#include "screen.h"
#include "session_notifications.h"
#include "sdl_hints.h"
#include "usb/aoa_hid.h"
#include "usb/gamepad_aoa.h"
#include "usb/keyboard_aoa.h"
#include "usb/mouse_aoa.h"
#include "util/log.h"

struct scrcpy_otg {
    struct sc_usb usb;
    struct sc_aoa aoa;
    struct sc_keyboard_aoa keyboard;
    struct sc_mouse_aoa mouse;
    struct sc_gamepad_aoa gamepad;

    struct sc_screen screen;
    struct sc_session_notifications notifications;
    struct sc_generation_targets generation_targets;
};

/** Publish USB completion while the retained OTG owner still exists. */
static void
sc_usb_on_disconnected(struct sc_usb *usb, void *userdata) {
    (void) usb;
    struct scrcpy_otg *owner = userdata;
    sc_session_notifications_publish(&owner->notifications,
                                      SC_EVENT_DEVICE_DISCONNECTED);
}

/** Publish AOA failure through the same bounded generation handoff. */
static void
sc_otg_on_aoa_error(void *userdata) {
    struct scrcpy_otg *owner = userdata;
    sc_session_notifications_publish(&owner->notifications,
                                      SC_EVENT_AOA_OPEN_ERROR);
}

/** Consume scoped outcomes after draining, including failed wakeup admission. */
static enum scrcpy_exit_code
event_loop(struct scrcpy_otg *s, struct sc_dispatcher *dispatcher) {
    const int outcome_poll_ms = 50;
    SDL_Event event;
    for (;;) {
        uint32_t outcome;

        if (sc_session_notifications_take(&s->notifications, &outcome)) {
            event = (SDL_Event) {.type = outcome};
        } else {

            if (!SDL_WaitEventTimeout(&event, outcome_poll_ms)) {
                continue;
            }

            if (event.type == SC_EVENT_SESSION_NOTIFICATION) {

                if (!sc_session_notifications_handle_event(&s->notifications,
                                                            &event, &outcome)) {
                    continue;
                }
                event.type = outcome;
            }

            if (sc_dispatcher_handle_event(dispatcher, &event)) {
                // The next iteration evaluates facts before blocking again.
                continue;
            }
        }

        switch (event.type) {
            case SC_EVENT_DEVICE_DISCONNECTED:
                LOGW("Device disconnected");
                sc_screen_handle_event(&s->screen, &event);
                return SCRCPY_EXIT_DISCONNECTED;
            case SC_EVENT_AOA_OPEN_ERROR:
                LOGE("AOA open error");
                return SCRCPY_EXIT_FAILURE;
            case SDL_EVENT_QUIT:
                LOGD("User requested to quit");
                return SCRCPY_EXIT_SUCCESS;
            default:
                sc_screen_handle_event(&s->screen, &event);
                break;
        }
    }
}

/** Preserve the OTG path while borrowing the application-owned dispatcher. */
enum scrcpy_exit_code
scrcpy_otg(struct scrcpy_options *options, struct sc_dispatcher *dispatcher) {
    static struct scrcpy_otg scrcpy_otg;
    struct scrcpy_otg *s = &scrcpy_otg;

    const char *serial = options->serial;

    sc_sdl_set_hints(options->render_driver, options->disable_screensaver);

    if (options->gamepad_input_mode != SC_GAMEPAD_INPUT_MODE_DISABLED) {
        if (!SDL_Init(SDL_INIT_GAMEPAD)) {
            LOGE("Could not initialize SDL gamepad: %s", SDL_GetError());
            // Not fatal, keyboard/mouse should still work
        }
    }

    enum scrcpy_exit_code ret = SCRCPY_EXIT_FAILURE;

    struct sc_key_processor *kp = NULL;
    struct sc_mouse_processor *mp = NULL;
    struct sc_gamepad_processor *gp = NULL;
    bool usb_device_initialized = false;
    bool usb_connected = false;
    bool aoa_started = false;
    bool aoa_initialized = false;
    bool screen_initialized = false;
    bool disconnected = false;

#ifdef _WIN32
    // On Windows, only one process could open a USB device
    // <https://github.com/Genymobile/scrcpy/issues/2773>
    LOGI("Killing adb server (if any)...");
    if (sc_adb_init()) {
        unsigned flags = SC_ADB_NO_STDOUT | SC_ADB_NO_STDERR | SC_ADB_NO_LOGERR;
        // uninterruptible (intr == NULL), but in practice it's very quick
        sc_adb_kill_server(NULL, flags);
        sc_adb_destroy();
    } else {
        LOGW("Could not call adb executable, adb server not killed");
    }
#endif

    static const struct sc_usb_callbacks cbs = {
        .on_disconnected = sc_usb_on_disconnected,
    };
    bool ok = sc_usb_init(&s->usb);
    if (!ok) {
        return SCRCPY_EXIT_FAILURE;
    }

    // P7.3: keep the notification latch until both external producers settle.
    if (!sc_session_notifications_init(&s->notifications, dispatcher)) {
        sc_usb_destroy(&s->usb);
        return SCRCPY_EXIT_FAILURE;
    }
    s->generation_targets = (struct sc_generation_targets) {
        .screen = &s->screen,
        .notifications = &s->notifications,
    };
    sc_dispatcher_generation generation;

    if (!sc_dispatcher_generation_begin(dispatcher, &s->generation_targets,
                                        &generation)) {
        sc_session_notifications_revoke(&s->notifications);
        sc_session_notifications_destroy(&s->notifications);
        sc_usb_destroy(&s->usb);
        return SCRCPY_EXIT_FAILURE;
    }
    bool bound = sc_session_notifications_bind(&s->notifications, generation);
    assert(bound);
    (void) bound;

    struct sc_usb_device usb_device;
    ok = sc_usb_select_device(&s->usb, serial, &usb_device);
    if (!ok) {
        goto end;
    }

    usb_device_initialized = true;

    ok = sc_usb_connect(&s->usb, usb_device.device, &cbs, s);
    if (!ok) {
        goto end;
    }
    usb_connected = true;

    ok = sc_aoa_init(&s->aoa, &s->usb, NULL);
    if (!ok) {
        goto end;
    }
    aoa_initialized = true;
    sc_aoa_set_error_callback(&s->aoa, sc_otg_on_aoa_error, s);

    assert(options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA
        || options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_DISABLED);
    assert(options->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA
        || options->mouse_input_mode == SC_MOUSE_INPUT_MODE_DISABLED);
    assert(options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_AOA
        || options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_DISABLED);

    bool enable_keyboard =
        options->keyboard_input_mode == SC_KEYBOARD_INPUT_MODE_AOA;
    bool enable_mouse =
        options->mouse_input_mode == SC_MOUSE_INPUT_MODE_AOA;
    bool enable_gamepad =
        options->gamepad_input_mode == SC_GAMEPAD_INPUT_MODE_AOA;

    if (enable_keyboard) {
        ok = sc_keyboard_aoa_init(&s->keyboard, &s->aoa);
        if (!ok) {
            goto end;
        }
        kp = &s->keyboard.key_processor;
    }

    if (enable_mouse) {
        ok = sc_mouse_aoa_init(&s->mouse, &s->aoa);
        if (!ok) {
            goto end;
        }
        mp = &s->mouse.mouse_processor;
    }

    if (enable_gamepad) {
        sc_gamepad_aoa_init(&s->gamepad, &s->aoa);
        gp = &s->gamepad.gamepad_processor;
    }

    ok = sc_aoa_start(&s->aoa);
    if (!ok) {
        goto end;
    }
    aoa_started = true;

    const char *window_title = options->window_title;
    if (!window_title) {
        window_title = usb_device.product ? usb_device.product : "scrcpy";
    }

    struct sc_screen_params params = {
        .dispatcher = dispatcher,
        .generation = generation,
        .video = false,
        .camera = false,
        .controller = false,
        .fp = NULL,
        .kp = kp,
        .mp = mp,
        .gp = gp,
        .mouse_bindings = options->mouse_bindings,
        .legacy_paste = false,
        .clipboard_autosync = false,
        .shortcut_mods = options->shortcut_mods,
        .window_title = window_title,
        .always_on_top = options->always_on_top,
        .window_x = options->window_x,
        .window_y = options->window_y,
        .window_width = options->window_width,
        .window_height = options->window_height,
        .window_borderless = options->window_borderless,
        .orientation = SC_ORIENTATION_0,
        .mipmaps = options->mipmaps,
        .fullscreen = false,
        .start_fps_counter = false,
    };

    ok = sc_screen_init(&s->screen, &params);
    if (!ok) {
        goto end;
    }
    screen_initialized = true;

    // usb_device not needed anymore
    sc_usb_device_destroy(&usb_device);
    usb_device_initialized = false;

    ret = event_loop(s, dispatcher);
    disconnected = ret == SCRCPY_EXIT_DISCONNECTED;

end:
    // Detach every borrowed processor before AOA stop or destruction.
    if (screen_initialized) {
        sc_screen_detach(&s->screen);
    }

    sc_session_notifications_revoke(&s->notifications);
    bool revoked = sc_dispatcher_generation_revoke(dispatcher, generation);
    assert(revoked);
    (void) revoked;

    if (aoa_started) {
        sc_aoa_stop(&s->aoa);
    }
    if (usb_connected) {
        sc_usb_stop(&s->usb);
    }

    if (screen_initialized) {
        sc_screen_interrupt(&s->screen);

        if (disconnected) {
            sc_screen_handle_disconnection(&s->screen);
        }
        LOGD("Quit...");

        // Close the window immediately
        sc_screen_hide_window(&s->screen);
    }

    if (mp) {
        sc_mouse_aoa_destroy(&s->mouse);
    }
    if (kp) {
        sc_keyboard_aoa_destroy(&s->keyboard);
    }
    if (gp) {
        sc_gamepad_aoa_destroy(&s->gamepad);
    }

    // Initialization does not acquire a worker when thread creation fails.
    if (aoa_started) {
        sc_aoa_join(&s->aoa);
    }
    if (aoa_initialized) {
        sc_aoa_destroy(&s->aoa);
    }

    if (usb_connected) {
        sc_usb_join(&s->usb);
        sc_usb_disconnect(&s->usb);
    }

    if (usb_device_initialized) {
        sc_usb_device_destroy(&usb_device);
    }

    sc_usb_destroy(&s->usb);
    sc_session_notifications_destroy(&s->notifications);

    if (screen_initialized) {
        sc_screen_join(&s->screen);
        sc_screen_destroy(&s->screen);
    }

    return ret;
}
