#include "common.h"

#include <assert.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "events.h"
#include "input_manager.h"
#include "screen.h"
#include "util/sdl.h"

static unsigned key_calls;
static unsigned local_calls;
static unsigned text_calls;
static unsigned mouse_calls;
static unsigned gamepad_calls;
static unsigned control_calls;
static unsigned file_calls;
static unsigned resize_calls;
static unsigned opens;
static unsigned closes;
static bool gamepad_connected;
static SDL_Gamepad *const gamepad_handle = (SDL_Gamepad *) (uintptr_t) 1;
static struct sc_controller *expected_controller;
static struct sc_file_pusher *expected_pusher;
static struct sc_key_processor *expected_keyboard;
static struct sc_mouse_processor *expected_mouse;
static struct sc_gamepad_processor *expected_gamepad;
static unsigned clipboard_reads;
static unsigned paste_calls;
static uint64_t last_clipboard_sequence;
static uint64_t last_key_sequence;
static struct sc_controller *accepted_controller;
static struct sc_file_pusher *accepted_pusher;
static struct sc_controller *initial_controller;
static struct sc_file_pusher *initial_pusher;
static char *old_owned_file;
static char *new_owned_file;
static char *old_owned_clipboard;
#define SC_INPUT_TEST_GAMEPAD_ID 42
#define SC_INPUT_TEST_GENERATION 7
#define SC_INPUT_TEST_REPLACEMENT_GENERATION 8

/** Count text delivery through the production keyboard trait. */
static void
process_text(struct sc_key_processor *processor,
             const struct sc_text_event *event) {
    assert(processor == expected_keyboard);
    assert(event->text);
    ++text_calls;
}

/** Count mouse motion delivery. */
static void
process_mouse_motion(struct sc_mouse_processor *processor,
                      const struct sc_mouse_motion_event *event) {
    assert(processor == expected_mouse);
    (void) event;
    ++mouse_calls;
}

/** Count mouse click delivery. */
static void
process_mouse_click(struct sc_mouse_processor *processor,
                     const struct sc_mouse_click_event *event) {
    assert(processor == expected_mouse);
    (void) event;
    ++mouse_calls;
}

/** Count mouse scroll delivery. */
static void
process_mouse_scroll(struct sc_mouse_processor *processor,
                      const struct sc_mouse_scroll_event *event) {
    assert(processor == expected_mouse);
    (void) event;
    ++mouse_calls;
}

/** Count touch delivery. */
static void
process_touch(struct sc_mouse_processor *processor,
               const struct sc_touch_event *event) {
    assert(processor == expected_mouse);
    (void) event;
    ++mouse_calls;
}

/** Count gamepad registration and removal. */
static void
process_gamepad_device(struct sc_gamepad_processor *processor,
                        const struct sc_gamepad_device_event *event) {
    assert(processor == expected_gamepad);
    assert(event->gamepad_id == SC_INPUT_TEST_GAMEPAD_ID);
    ++gamepad_calls;
}

/** Count gamepad axis delivery. */
static void
process_gamepad_axis(struct sc_gamepad_processor *processor,
                      const struct sc_gamepad_axis_event *event) {
    assert(processor == expected_gamepad);
    (void) event;
    ++gamepad_calls;
}

/** Count gamepad button delivery. */
static void
process_gamepad_button(struct sc_gamepad_processor *processor,
                        const struct sc_gamepad_button_event *event) {
    assert(processor == expected_gamepad);
    (void) event;
    ++gamepad_calls;
}

/** Count delivery through the production key processor trait. */
static void
process_key(struct sc_key_processor *processor,
            const struct sc_key_event *event, uint64_t sequence) {
    assert(processor == expected_keyboard);
    (void) event;
    last_key_sequence = sequence;
    ++key_calls;
}

/** Replace device delivery without invoking a controller or transport. */
bool
sc_controller_push_msg(struct sc_controller *controller,
                        const struct sc_control_msg *message) {
    assert(controller == expected_controller);
    accepted_controller = controller;

    if (message->type == SC_CONTROL_MSG_TYPE_SET_CLIPBOARD) {
        last_clipboard_sequence = message->set_clipboard.sequence;
        assert(controller == initial_controller && !old_owned_clipboard);
        old_owned_clipboard = message->set_clipboard.text;
        ++paste_calls;
    }

    if (message->type == SC_CONTROL_MSG_TYPE_INJECT_TEXT) {
        free(message->inject_text.text);
        ++paste_calls;
    }
    ++control_calls;
    return true;
}

/** Replace device display delivery. */
void
sc_controller_resize_display(struct sc_controller *controller, uint16_t width,
                              uint16_t height) {
    assert(controller == expected_controller);
    (void) width;
    (void) height;
    ++resize_calls;
}

/** Replace file enqueue, retaining its actual ownership contract. */
bool
sc_file_pusher_request(struct sc_file_pusher *pusher,
                       enum sc_file_pusher_action action, char *file) {
    assert(pusher == expected_pusher);
    (void) action;
    char **owned_file = pusher == initial_pusher ? &old_owned_file : &new_owned_file;
    assert(!*owned_file);
    *owned_file = file;
    accepted_pusher = pusher;
    ++file_calls;
    return true;
}

/** Replace local window effects with an observable counter. */
void
sc_screen_toggle_fullscreen(struct sc_screen *screen) {
    (void) screen;
    ++local_calls;
}

/** Replace local size changes. */
void
sc_screen_resize_to_fit(struct sc_screen *screen) {
    (void) screen;
    ++local_calls;
}

/** Replace local pixel-size changes. */
void
sc_screen_resize_to_pixel_perfect(struct sc_screen *screen) {
    (void) screen;
    ++local_calls;
}

/** Replace local pause effects. */
void
sc_screen_set_paused(struct sc_screen *screen, bool paused) {
    screen->paused = paused;
}

/** Replace local orientation effects. */
void
sc_screen_set_orientation(struct sc_screen *screen, enum sc_orientation orientation) {
    screen->orientation = orientation;
}

/** Use identity coordinates without a real window. */
struct sc_point
sc_screen_convert_window_to_frame_coords(struct sc_screen *screen, int32_t x, int32_t y) {
    (void) screen;
    return (struct sc_point) {x, y};
}

/** Supply deterministic test geometry. */
struct sc_size
sc_sdl_get_window_size(SDL_Window *window) {
    (void) window;
    return (struct sc_size) {100, 100};
}

/** Replace local FPS effects. */
bool
sc_fps_counter_is_started(struct sc_fps_counter *counter) {
    (void) counter;
    return false;
}

/** Replace local FPS start. */
bool
sc_fps_counter_start(struct sc_fps_counter *counter) {
    (void) counter;
    return true;
}

/** Replace local FPS stop. */
void
sc_fps_counter_stop(struct sc_fps_counter *counter) {
    (void) counter;
}

/** Replace local quit publication. */
bool
sc_push_event_impl(uint32_t type, void *pointer, const char *name) {
    (void) type;
    (void) pointer;
    (void) name;
    return true;
}


/** Never access the maintainer's clipboard. */
char *
SDL_GetClipboardText(void) {
    ++clipboard_reads;
    return strdup("synthetic clipboard");
}

/** Return deterministic modifiers. */
SDL_Keymod
SDL_GetModState(void) {
    return SDL_KMOD_NONE;
}

/** Return deterministic mouse position. */
SDL_MouseButtonFlags
SDL_GetMouseState(float *x, float *y) {
    *x = 0;
    *y = 0;
    return 0;
}

/** Replace SDL memory release. */
void
SDL_free(void *pointer) {
    free(pointer);
}

/** Replace gamepad open without physical input. */
SDL_Gamepad *
SDL_OpenGamepad(SDL_JoystickID id) {
    assert(id == SC_INPUT_TEST_GAMEPAD_ID);
    ++opens;
    return gamepad_handle;
}

/** Replace gamepad close. */
void
SDL_CloseGamepad(SDL_Gamepad *gamepad) {
    assert(gamepad == gamepad_handle);
    ++closes;
}

/** Replace joystick lookup. */
SDL_Joystick *
SDL_GetGamepadJoystick(SDL_Gamepad *gamepad) {
    (void) gamepad;
    return NULL;
}

/** Replace joystick identity. */
SDL_JoystickID
SDL_GetJoystickID(SDL_Joystick *joystick) {
    (void) joystick;
    return 0;
}

/** Replace gamepad lookup. */
SDL_Gamepad *
SDL_GetGamepadFromID(SDL_JoystickID id) {
    (void) id;
    return NULL;
}

/** Main-thread contract is simulated without initializing SDL resources. */
bool
SDL_IsMainThread(void) {
    return true;
}

/** Enumerate a synthetic gamepad without inspecting physical devices. */
SDL_JoystickID *SDL_GetGamepads(int *count) {
    *count = gamepad_connected ? 1 : 0;
    SDL_JoystickID *identifiers = calloc((size_t) *count + 1, sizeof(*identifiers));
    assert(identifiers);
    identifiers[0] = gamepad_connected ? SC_INPUT_TEST_GAMEPAD_ID : 0;
    return identifiers;
}
/** Supply deterministic SDL diagnostics. */
const char *
SDL_GetError(void) {
    return "controlled test effect";
}

/** Replace SDL diagnostics. */
void
SDL_LogWarn(int category, const char *format, ...) {
    (void) category;
    (void) format;
}

/** Replace SDL diagnostics. */
void
SDL_LogError(int category, const char *format, ...) {
    (void) category;
    (void) format;
}


/** Exercise every remote route through actual event handlers. */
static void
send_remote_events(struct sc_input_manager *manager) {
    SDL_Event events[] = {
        {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_A, .scancode = SDL_SCANCODE_A}},
        {.text = {.type = SDL_EVENT_TEXT_INPUT, .text = "synthetic"}},
        {.motion = {.type = SDL_EVENT_MOUSE_MOTION}},
        {.wheel = {.type = SDL_EVENT_MOUSE_WHEEL, .y = 1}},
        {.button = {.type = SDL_EVENT_MOUSE_BUTTON_DOWN, .button = SDL_BUTTON_LEFT}},
        {.tfinger = {.type = SDL_EVENT_FINGER_DOWN}},
        {.gaxis = {.type = SDL_EVENT_GAMEPAD_AXIS_MOTION, .which = SC_INPUT_TEST_GAMEPAD_ID, .axis = SDL_GAMEPAD_AXIS_LEFTX}},
        {.gbutton = {.type = SDL_EVENT_GAMEPAD_BUTTON_DOWN, .which = SC_INPUT_TEST_GAMEPAD_ID, .button = SDL_GAMEPAD_BUTTON_SOUTH}},
        {.drop = {.type = SDL_EVENT_DROP_FILE, .data = "synthetic.apk"}},
        {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_C, .mod = SDL_KMOD_LALT}},
    };
    for (size_t index = 0; index < ARRAY_LEN(events); ++index) {
        sc_input_manager_handle_event(manager, &events[index]);
    }
    sc_input_manager_request_resize(manager, 100, 100);
}

/** Preserve local window shortcuts and border resize while input is gated. */
static void
send_local_events(struct sc_input_manager *manager) {
    SDL_Event fullscreen = {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_F11}};
    SDL_Event border = {.button = {.type = SDL_EVENT_MOUSE_BUTTON_DOWN,
        .button = SDL_BUTTON_LEFT, .clicks = 2, .x = -1, .y = -1}};
    unsigned before = local_calls;
    sc_input_manager_handle_event(manager, &fullscreen);
    sc_input_manager_handle_event(manager, &border);
    assert(local_calls == before + 2);
}

/** Assert no remote effects occur through a detached or gated binding. */
static void
assert_remote_blocked(struct sc_input_manager *manager) {
    unsigned before = key_calls + text_calls + mouse_calls + gamepad_calls
                    + control_calls + file_calls + resize_calls;
    unsigned clipboard_before = clipboard_reads;
    send_remote_events(manager);
    unsigned after = key_calls + text_calls + mouse_calls + gamepad_calls
                   + control_calls + file_calls + resize_calls;
    assert(before == after);
    SDL_Event paste_events[] = {
        {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_V, .mod = SDL_KMOD_LALT}},
        {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_V, .mod = SDL_KMOD_LALT | SDL_KMOD_SHIFT}},
        {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_V, .mod = SDL_KMOD_CTRL}},
    };
    for (size_t index = 0; index < ARRAY_LEN(paste_events); ++index) {
        sc_input_manager_handle_event(manager, &paste_events[index]);
    }
    assert(clipboard_reads == clipboard_before);
}

/** Validate detach, current presentation, replacement and partial input paths. */
int main(void) {
    static const struct sc_key_processor_ops operations = {
        .process_key = process_key,
        .process_text = process_text,
    };
    static const struct sc_mouse_processor_ops mouse_operations = {
        .process_mouse_motion = process_mouse_motion,
        .process_mouse_click = process_mouse_click,
        .process_mouse_scroll = process_mouse_scroll,
        .process_touch = process_touch,
    };
    static const struct sc_gamepad_processor_ops gamepad_operations = {
        .process_gamepad_added = process_gamepad_device,
        .process_gamepad_removed = process_gamepad_device,
        .process_gamepad_axis = process_gamepad_axis,
        .process_gamepad_button = process_gamepad_button,
    };
    struct sc_key_processor keyboard = {.ops = &operations};
    struct sc_mouse_processor mouse = {.ops = &mouse_operations};
    struct sc_gamepad_processor gamepad = {.ops = &gamepad_operations};
    struct sc_controller controller;
    struct sc_file_pusher pusher;
    struct sc_controller replacement_controller;
    struct sc_file_pusher replacement_pusher;
    initial_controller = &controller;
    initial_pusher = &pusher;
    expected_controller = &controller;
    expected_pusher = &pusher;
    expected_keyboard = &keyboard;
    expected_mouse = &mouse;
    expected_gamepad = &gamepad;
    struct sc_screen screen = {.video = true};
    struct sc_input_manager manager;
    const struct sc_input_manager_params params = {
        .screen = &screen,
        .shortcut_mods = SC_SHORTCUT_MOD_LALT,
        .clipboard_autosync = true,
        .mouse_bindings = {
            .pri = {SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK},
            .sec = {SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK, SC_MOUSE_BINDING_CLICK},
        },
    };
    sc_input_manager_init(&manager, &params);
    assert_remote_blocked(&manager);
    send_local_events(&manager);
    struct sc_input_binding binding = {
        .generation = SC_INPUT_TEST_GENERATION,
        .controller = &controller, .fp = &pusher, .kp = &keyboard,
        .mp = &mouse, .gp = &gamepad,
    };
    gamepad_connected = true;
    sc_input_manager_bind(&manager, &binding, true);
    assert(!sc_input_manager_is_ready(&manager));
    assert(!opens);
    assert_remote_blocked(&manager);
    send_local_events(&manager);
    assert(!sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_REPLACEMENT_GENERATION));
    assert_remote_blocked(&manager);
    assert(sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_GENERATION));
    assert(opens == 1 && gamepad_calls == 1);
    assert(!sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_GENERATION));
    SDL_Event duplicate_added = {.gdevice = {.type = SDL_EVENT_GAMEPAD_ADDED,
        .which = SC_INPUT_TEST_GAMEPAD_ID}};
    sc_input_manager_handle_event(&manager, &duplicate_added);
    assert(opens == 1 && gamepad_calls == 1);
    send_remote_events(&manager);
    assert(key_calls == 1 && text_calls == 1 && mouse_calls == 4);
    assert(gamepad_calls == 3 && control_calls == 1 && file_calls == 1 && resize_calls == 1);
    assert(accepted_controller == &controller && accepted_pusher == &pusher);
    assert(old_owned_file && !strcmp(old_owned_file, "synthetic.apk"));
    // Async ACK requests stay with the accepted old controller and key endpoint.
    keyboard.async_paste = true;
    SDL_Event paste = {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_V,
        .mod = SDL_KMOD_CTRL, .scancode = SDL_SCANCODE_V}};
    sc_input_manager_handle_event(&manager, &paste);
    assert(paste_calls == 1 && last_clipboard_sequence == 1 && last_key_sequence == 1);
    assert(manager.next_sequence == 2);
    mouse.relative_mode = true;
    assert(sc_input_manager_is_relative(&manager));
    manager.vfinger_down = true;
    manager.mouse_buttons_state = SC_MOUSE_BUTTON_LEFT;
    manager.next_sequence = 99;
    sc_input_manager_detach(&manager);
    assert(!sc_input_manager_is_relative(&manager));
    mouse.relative_mode = false;
    assert(closes == 1);
    assert(!manager.binding.controller && !manager.binding.kp && !manager.binding.mp);
    assert(!manager.binding.gp && !manager.binding.fp && !manager.binding.generation);
    assert(!manager.vfinger_down && !manager.mouse_buttons_state && manager.next_sequence == 1);
    assert_remote_blocked(&manager);
    send_local_events(&manager);
    SDL_Event removed = {.gdevice = {.type = SDL_EVENT_GAMEPAD_REMOVED,
        .which = SC_INPUT_TEST_GAMEPAD_ID}};
    sc_input_manager_handle_event(&manager, &removed);
    assert(closes == 1 && gamepad_calls == 3);
    assert(accepted_controller == &controller && accepted_pusher == &pusher);
    assert(old_owned_file && old_owned_clipboard);
    binding.generation = SC_INPUT_TEST_REPLACEMENT_GENERATION;
    binding.controller = &replacement_controller;
    binding.fp = &replacement_pusher;
    expected_controller = &replacement_controller;
    expected_pusher = &replacement_pusher;
    sc_input_manager_bind(&manager, &binding, true);
    assert(!sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_GENERATION));
    assert_remote_blocked(&manager);
    assert(sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_REPLACEMENT_GENERATION));
    assert(opens == 2);
    assert(accepted_controller == &controller && accepted_pusher == &pusher);
    SDL_Event replacement_file = {.drop = {.type = SDL_EVENT_DROP_FILE,
        .data = "replacement.apk"}};
    sc_input_manager_handle_event(&manager, &replacement_file);
    assert(accepted_pusher == &replacement_pusher);
    assert(!strcmp(old_owned_file, "synthetic.apk"));
    assert(!strcmp(new_owned_file, "replacement.apk"));
    unsigned gamepad_before = gamepad_calls;
    sc_input_manager_handle_event(&manager, &removed);
    assert(closes == 2 && gamepad_calls == gamepad_before + 1);
    sc_input_manager_destroy(&manager);
    assert(closes == 2);
    // Non-video AOA-style processors remain meaningful without a controller.
    binding.controller = NULL;
    binding.fp = NULL;
    sc_input_manager_bind(&manager, &binding, false);
    assert(sc_input_manager_is_ready(&manager));
    SDL_Event key = {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_A,
        .scancode = SDL_SCANCODE_A}};
    sc_input_manager_handle_event(&manager, &key);
    assert(key_calls == 3);
    assert(!sc_input_manager_request_resize(&manager, 100, 100));
    sc_input_manager_destroy(&manager);
    assert(opens == closes);
    // Mouse-only input must support wheel without dereferencing a null keyboard.
    binding.kp = NULL;
    binding.gp = NULL;
    sc_input_manager_bind(&manager, &binding, false);
    SDL_Event wheel = {.wheel = {.type = SDL_EVENT_MOUSE_WHEEL, .y = 1}};
    sc_input_manager_handle_event(&manager, &wheel);
    assert(mouse_calls == 5);
    sc_input_manager_destroy(&manager);
    assert(clipboard_reads == 1);
    // No-control visual sessions still preserve local shortcuts after presentation.
    binding = (struct sc_input_binding) {.generation = SC_INPUT_TEST_GENERATION};
    sc_input_manager_bind(&manager, &binding, true);
    assert_remote_blocked(&manager);
    assert(sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_GENERATION));
    assert_remote_blocked(&manager);
    send_local_events(&manager);
    sc_input_manager_destroy(&manager);
    // Camera shortcuts require a ready controller without enabling device HID.
    struct sc_input_manager_params camera_params = params;
    camera_params.camera = true;
    sc_input_manager_init(&manager, &camera_params);
    binding.controller = &controller;
    expected_controller = &controller;
    sc_input_manager_bind(&manager, &binding, true);
    SDL_Event torch = {.key = {.type = SDL_EVENT_KEY_DOWN, .key = SDLK_T,
        .mod = SDL_KMOD_LALT}};
    unsigned control_before = control_calls;
    sc_input_manager_handle_event(&manager, &torch);
    assert(control_calls == control_before);
    assert(sc_input_manager_mark_presented(&manager, SC_INPUT_TEST_GENERATION));
    sc_input_manager_handle_event(&manager, &torch);
    assert(control_calls == control_before + 1);
    assert(!sc_input_manager_request_resize(&manager, 100, 100));
    sc_input_manager_destroy(&manager);
    assert_remote_blocked(&manager);
    // Accepted payloads settle under their original endpoints after all binds.
    assert(!strcmp(old_owned_file, "synthetic.apk"));
    assert(!strcmp(new_owned_file, "replacement.apk"));
    assert(!strcmp(old_owned_clipboard, "synthetic clipboard"));
    free(old_owned_file);
    free(new_owned_file);
    free(old_owned_clipboard);
    return 0;
}
