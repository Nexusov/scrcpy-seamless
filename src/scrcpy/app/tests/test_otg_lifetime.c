#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "adb/adb.h"
#include "dispatcher.h"
#include "events.h"
#include "screen.h"
#include "sdl_hints.h"
#include "usb/aoa_hid.h"
#include "usb/gamepad_aoa.h"
#include "usb/keyboard_aoa.h"
#include "usb/mouse_aoa.h"
#include "usb/scrcpy_otg.h"

static unsigned aoa_initializations;
static unsigned aoa_start_attempts;
static unsigned aoa_joins;
static unsigned aoa_destructions;
static unsigned usb_destructions;

/** Replace only external ADB ownership; the test must never invoke ADB. */
bool
sc_adb_init(void) {
    return false;
}

/** Forbid accidental continuation into an actual ADB operation. */
bool
sc_adb_kill_server(struct sc_intr *interrupt, unsigned flags) {
    (void) interrupt;
    (void) flags;
    assert(!"OTG lifetime test must not execute ADB");
    return false;
}

/** Match the unreachable ADB teardown boundary without external effects. */
void
sc_adb_destroy(void) {
    assert(!"No ADB ownership was acquired");
}

/** Avoid changing global SDL preferences in the ownership test. */
void
sc_sdl_set_hints(const char *render_driver, bool disable_screensaver) {
    (void) render_driver;
    (void) disable_screensaver;
}

/** Acquire only a synthetic external USB handle. */
bool
sc_usb_init(struct sc_usb *usb) {
    memset(usb, 0, sizeof(*usb));
    return true;
}

/** Report one independent synthetic device without enumerating hardware. */
bool
sc_usb_select_device(struct sc_usb *usb, const char *serial,
                     struct sc_usb_device *device) {
    (void) usb;
    (void) serial;
    memset(device, 0, sizeof(*device));
    return true;
}

/** Keep connection acquisition independent from real libusb calls. */
bool
sc_usb_connect(struct sc_usb *usb, libusb_device *device,
               const struct sc_usb_callbacks *callbacks, void *userdata) {
    (void) usb;
    (void) device;
    (void) callbacks;
    (void) userdata;
    return true;
}

/** No synthetic device descriptor owns an external reference. */
void
sc_usb_device_destroy(struct sc_usb_device *device) {
    (void) device;
}

/** Observe USB stop without touching a physical device. */
void
sc_usb_stop(struct sc_usb *usb) {
    (void) usb;
}

/** No external USB event thread was started by the synthetic boundary. */
void
sc_usb_join(struct sc_usb *usb) {
    (void) usb;
}

/** Release only the synthetic connection handle. */
void
sc_usb_disconnect(struct sc_usb *usb) {
    (void) usb;
}

/** Count the actual production caller's external USB release. */
void
sc_usb_destroy(struct sc_usb *usb) {
    (void) usb;
    ++usb_destructions;
}

/** Model successful AOA resource acquisition, independently of worker startup. */
bool
sc_aoa_init(struct sc_aoa *aoa, struct sc_usb *usb,
            struct sc_acksync *acksync) {
    (void) aoa;
    (void) usb;
    (void) acksync;
    ++aoa_initializations;
    return true;
}

/** Accept only the retained production owner's pre-start callback binding. */
void
sc_aoa_set_error_callback(struct sc_aoa *aoa,
                          void (*on_open_error)(void *userdata),
                          void *userdata) {
    assert(aoa);
    assert(on_open_error);
    assert(userdata);
}

/** Fail precisely the external worker creation boundary. */
bool
sc_aoa_start(struct sc_aoa *aoa) {
    (void) aoa;
    ++aoa_start_attempts;
    return false;
}

/** A failed worker acquisition must not receive stop. */
void
sc_aoa_stop(struct sc_aoa *aoa) {
    (void) aoa;
    assert(!"An unstarted AOA worker cannot be stopped");
}

/** Detect an invalid production join before dereferencing an invalid thread. */
void
sc_aoa_join(struct sc_aoa *aoa) {
    (void) aoa;
    ++aoa_joins;
    assert(!"An unstarted AOA worker cannot be joined");
}

/** Resource acquisition still requires one destruction after startup failure. */
void
sc_aoa_destroy(struct sc_aoa *aoa) {
    (void) aoa;
    ++aoa_destructions;
}

/** Disabled processors must not acquire any HID state. */
bool
sc_keyboard_aoa_init(struct sc_keyboard_aoa *keyboard, struct sc_aoa *aoa) {
    (void) keyboard;
    (void) aoa;
    assert(!"The test disables keyboard acquisition");
    return false;
}

/** Disabled keyboard state must not be destroyed. */
void
sc_keyboard_aoa_destroy(struct sc_keyboard_aoa *keyboard) {
    (void) keyboard;
    assert(!"No keyboard ownership was acquired");
}

/** Disabled processors must not acquire any mouse state. */
bool
sc_mouse_aoa_init(struct sc_mouse_aoa *mouse, struct sc_aoa *aoa) {
    (void) mouse;
    (void) aoa;
    assert(!"The test disables mouse acquisition");
    return false;
}

/** Disabled mouse state must not be destroyed. */
void
sc_mouse_aoa_destroy(struct sc_mouse_aoa *mouse) {
    (void) mouse;
    assert(!"No mouse ownership was acquired");
}

/** Disabled processors must not acquire any gamepad state. */
void
sc_gamepad_aoa_init(struct sc_gamepad_aoa *gamepad, struct sc_aoa *aoa) {
    (void) gamepad;
    (void) aoa;
    assert(!"The test disables gamepad acquisition");
}

/** Disabled gamepad state must not be destroyed. */
void
sc_gamepad_aoa_destroy(struct sc_gamepad_aoa *gamepad) {
    (void) gamepad;
    assert(!"No gamepad ownership was acquired");
}

/** Startup failure must not construct or show a native window. */
bool
sc_screen_init(struct sc_screen *screen, const struct sc_screen_params *params) {
    (void) screen;
    (void) params;
    assert(!"AOA startup failed before screen acquisition");
    return false;
}

/** Unacquired screen boundaries are linked but forbidden in this scenario. */
void
sc_screen_detach(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_interrupt(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_handle_disconnection(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_hide_window(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_join(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_destroy(struct sc_screen *screen) {
    (void) screen;
    assert(!"No screen ownership was acquired");
}

/** Reject a presentation call after failed startup. */
void
sc_screen_handle_event(struct sc_screen *screen, const SDL_Event *event) {
    (void) screen;
    (void) event;
    assert(!"No screen ownership was acquired");
}

/** Exercise actual OTG unwind with successful init and failed thread creation. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    struct scrcpy_options options = {
        .keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_DISABLED,
        .mouse_input_mode = SC_MOUSE_INPUT_MODE_DISABLED,
        .gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_DISABLED,
    };
    assert(scrcpy_otg(&options, &dispatcher) == SCRCPY_EXIT_FAILURE);
    assert(aoa_initializations == 1);
    assert(aoa_start_attempts == 1);
    assert(!aoa_joins);
    assert(aoa_destructions == 1);
    assert(usb_destructions == 1);
    sc_dispatcher_destroy(&dispatcher);
    SDL_Quit();
    puts("OTG lifetime: 1 production-linked startup-failure case passed");
    return 0;
}
