/* Reuse external-effect doubles; all routing below is the production handler. */
int input_binding_neighbor_main(int argument_count, char **arguments);
#define main input_binding_neighbor_main
#define sc_controller_push_msg neighbor_controller_push_msg
#define sc_push_event_impl neighbor_push_event
#define sc_fps_counter_start neighbor_fps_start
#include "test_input_binding.c"
#undef sc_controller_push_msg
#undef sc_push_event_impl
#undef sc_fps_counter_start
#undef main

#include <stdio.h>
#include "android/input.h"
#include "android/keycodes.h"

static unsigned injected_down;
static unsigned injected_up;
static enum android_keycode injected_key;
static unsigned quit_calls;
static unsigned fps_calls;

bool sc_controller_push_msg(struct sc_controller *, const struct sc_control_msg *);
bool sc_push_event_impl(uint32_t, void *, const char *);
bool sc_fps_counter_start(struct sc_fps_counter *);

bool
sc_push_event_impl(uint32_t type, void *pointer, const char *name) {
    assert(type == SDL_EVENT_QUIT);
    ++quit_calls;
    return neighbor_push_event(type, pointer, name);
}

bool
sc_fps_counter_start(struct sc_fps_counter *counter) {
    ++fps_calls;
    return neighbor_fps_start(counter);
}

bool
sc_controller_push_msg(struct sc_controller *controller,
                       const struct sc_control_msg *message) {
    if (message->type == SC_CONTROL_MSG_TYPE_INJECT_KEYCODE) {
        assert(controller == expected_controller);
        injected_key = message->inject_keycode.keycode;
        injected_down += message->inject_keycode.action == AKEY_EVENT_ACTION_DOWN;
        injected_up += message->inject_keycode.action == AKEY_EVENT_ACTION_UP;
    }

    return neighbor_controller_push_msg(controller, message);
}

struct shortcut_fixture {
    struct sc_screen screen;
    struct sc_controller controller;
    struct sc_key_processor keyboard;
    struct sc_input_manager manager;
};

static void
fixture_init(struct shortcut_fixture *fixture, const char *json, bool camera,
             bool gated) {
    static const struct sc_key_processor_ops operations = {
        .process_key = process_key, .process_text = process_text,
    };
    memset(fixture, 0, sizeof(*fixture));
    fixture->screen.video = true;
    fixture->keyboard.ops = &operations;
    expected_controller = initial_controller = &fixture->controller;
    expected_keyboard = &fixture->keyboard;
    old_owned_clipboard = NULL;
    test_modifiers = 0;
    gamepad_connected = false;
    struct sc_shortcut_bindings bindings;
    assert(sc_shortcut_bindings_parse(json, &bindings));
    struct sc_input_manager_params params = {
        .screen = &fixture->screen, .camera = camera,
        .shortcut_mods = SC_SHORTCUT_MOD_LALT, .shortcut_bindings = &bindings,
    };
    sc_input_manager_init(&fixture->manager, &params);
    struct sc_input_binding binding = {
        .generation = SC_INPUT_TEST_GENERATION, .controller = &fixture->controller,
        .kp = &fixture->keyboard,
    };
    sc_input_manager_bind(&fixture->manager, &binding, gated);
}

static void
send_key(struct shortcut_fixture *fixture, SDL_Keycode key, SDL_Scancode scan,
         SDL_Keymod modifiers, bool down, bool repeat) {
    SDL_Event event = {.key = {
        .type = down ? SDL_EVENT_KEY_DOWN : SDL_EVENT_KEY_UP,
        .key = key, .scancode = scan, .mod = modifiers, .repeat = repeat,
    }};
    sc_input_manager_handle_event(&fixture->manager, &event);
}

static void
test_captured_release(void) {
    struct shortcut_fixture fixture;
    fixture_init(&fixture, "{}", false, false);
    unsigned down = injected_down, up = injected_up, raw = key_calls;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, SDL_KMOD_LALT, true, false);
    assert(injected_down == down + 1 && injected_key == AKEYCODE_HOME);
    send_key(&fixture, SDLK_LALT, SDL_SCANCODE_LALT, 0, false, false);
    fixture.screen.paused = true;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, false, false);
    assert(injected_up == up + 1 && key_calls == raw);
    sc_input_manager_destroy(&fixture.manager);
}

static void
test_replacement_tombstone(void) {
    struct shortcut_fixture fixture;
    fixture_init(&fixture, "{}", false, false);
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, SDL_KMOD_LALT, true, false);
    struct sc_input_binding replacement = fixture.manager.binding;
    replacement.generation++;
    sc_input_manager_bind(&fixture.manager, &replacement, false);
    unsigned raw = key_calls, down = injected_down, up = injected_up;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, true, true);
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, false, false);
    assert(key_calls == raw && injected_down == down && injected_up == up);
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, true, false);
    assert(key_calls == raw + 1);
    sc_input_manager_destroy(&fixture.manager);
}

static void
test_gate_and_focus_release(void) {
    struct shortcut_fixture fixture;
    fixture_init(&fixture, "{}", false, true);
    unsigned down = injected_down, up = injected_up;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, SDL_KMOD_LALT, true, false);
    assert(sc_input_manager_mark_presented(&fixture.manager, SC_INPUT_TEST_GENERATION));
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, false, false);
    assert(injected_down == down && injected_up == up);
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, SDL_KMOD_LALT, true, false);
    SDL_Event lost = {.window = {.type = SDL_EVENT_WINDOW_FOCUS_LOST}};
    sc_input_manager_handle_event(&fixture.manager, &lost);
    sc_input_manager_handle_event(&fixture.manager, &lost);
    assert(injected_down == down + 1 && injected_up == up + 1);
    unsigned raw = key_calls;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, 0, false, false);
    assert(key_calls == raw && injected_up == up + 1);
    sc_input_manager_destroy(&fixture.manager);
}

static void
test_text_and_aliases(void) {
    struct shortcut_fixture fixture;
    fixture_init(&fixture, "{\"home\":\"F12\",\"fullscreen\":\"\"}", false, false);
    unsigned down = injected_down, raw = key_calls, text = text_calls;
    send_key(&fixture, SDLK_H, SDL_SCANCODE_H, SDL_KMOD_LALT, true, false);
    assert(injected_down == down);
    send_key(&fixture, SDLK_F12, SDL_SCANCODE_F12, 0, true, false);
    assert(injected_down == down + 1);
    send_key(&fixture, SDLK_F12, SDL_SCANCODE_F12, 0, false, false);
    send_key(&fixture, SDLK_F11, SDL_SCANCODE_F11, 0, true, false);
    assert(key_calls == raw + 1);
    send_key(&fixture, SDLK_C, SDL_SCANCODE_C, SDL_KMOD_LALT, true, false);
    SDL_Event input = {.text = {.type = SDL_EVENT_TEXT_INPUT, .text = "synthetic"}};
    sc_input_manager_handle_event(&fixture.manager, &input);
    assert(text_calls == text);
    send_key(&fixture, SDLK_A, SDL_SCANCODE_A, 0, true, false);
    sc_input_manager_handle_event(&fixture.manager, &input);
    assert(text_calls == text + 1);
    sc_input_manager_destroy(&fixture.manager);
}

static void
test_every_action_remaps(void) {
    static const char *identities[] = {
#define SC_SHORTCUT_ACTION(NAME, ID, CONTEXT, TAP, FIRST, SECOND) ID,
#include "shortcut_actions.generated.inc"
#undef SC_SHORTCUT_ACTION
    };

    for (unsigned action = 0; action < SC_SHORTCUT_ACTION_COUNT; ++action) {
        char json[SC_SHORTCUT_BINDINGS_MAX_BYTES];
        size_t position = 0;
        json[position++] = '{';

        for (unsigned item = 0; item < SC_SHORTCUT_ACTION_COUNT; ++item) {
            int written = snprintf(json + position, sizeof(json) - position,
                "%s\"%s\":\"%s\"", item ? "," : "", identities[item],
                item == action ? "F12" : "");
            assert(written > 0 && (size_t) written < sizeof(json) - position);
            position += (size_t) written;
        }
        strcpy(json + position, "}");
        bool camera = action >= SC_SHORTCUT_ACTION_TORCH_ON;
        struct shortcut_fixture fixture;
        fixture_init(&fixture, json, camera, false);
        fixture.keyboard.hid = true;
        fixture.screen.paused = action == SC_SHORTCUT_ACTION_UNPAUSE;
        bool paused = fixture.screen.paused;
        unsigned effects = local_calls + control_calls + quit_calls + fps_calls;
        enum sc_orientation orientation = fixture.screen.orientation;
        send_key(&fixture, SDLK_F12, SDL_SCANCODE_F12, 0, true, false);
        bool local_state = fixture.screen.paused != paused
            || fixture.screen.orientation != orientation;
        assert(local_state || local_calls + control_calls + quit_calls + fps_calls > effects);
        send_key(&fixture, SDLK_F12, SDL_SCANCODE_F12, 0, false, false);
        free(old_owned_clipboard);
        old_owned_clipboard = NULL;
        sc_input_manager_destroy(&fixture.manager);
    }
}

static void
test_taps_repeats_and_conditions(void) {
    struct shortcut_fixture fixture;
    fixture_init(&fixture, "{}", false, false);
    unsigned calls = control_calls;
    send_key(&fixture, SDLK_N, SDL_SCANCODE_N, SDL_KMOD_LALT, true, false);
    send_key(&fixture, SDLK_N, SDL_SCANCODE_N, SDL_KMOD_LALT, false, false);
    send_key(&fixture, SDLK_N, SDL_SCANCODE_N, SDL_KMOD_LALT, true, false);
    assert(control_calls == calls + 2);
    unsigned down = injected_down;
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, SDL_KMOD_LALT, true, false);
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, 0, true, true);
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, 0, false, false);
    assert(injected_down == down + 2);
    fixture.keyboard.hid = false;
    calls = control_calls;
    send_key(&fixture, SDLK_K, SDL_SCANCODE_K, SDL_KMOD_LALT, true, false);
    assert(control_calls == calls);
    fixture.keyboard.hid = true;
    send_key(&fixture, SDLK_K, SDL_SCANCODE_K, SDL_KMOD_LALT, true, false);
    assert(control_calls == calls + 1);
    sc_input_manager_destroy(&fixture.manager);

    fixture_init(&fixture, "{\"quickSettings\":\"F12\"}", false, false);
    calls = control_calls;
    send_key(&fixture, SDLK_F12, SDL_SCANCODE_F12, 0, true, false);
    assert(control_calls == calls + 1);
    sc_input_manager_destroy(&fixture.manager);

    fixture_init(&fixture, "{}", true, false);
    fixture.screen.paused = true;
    calls = control_calls;
    send_key(&fixture, SDLK_T, SDL_SCANCODE_T, SDL_KMOD_LALT, true, false);
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, SDL_KMOD_LALT, true, false);
    assert(control_calls == calls + 1);
    fixture.screen.paused = false;
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, SDL_KMOD_LALT, true, false);
    send_key(&fixture, SDLK_UP, SDL_SCANCODE_UP, 0, true, true);
    assert(control_calls == calls + 3);
    sc_input_manager_destroy(&fixture.manager);
}

int
main(int count, char **arguments) {
    test_captured_release();

    if (count == 2 && !strcmp(arguments[1], "--release-only")) {
        return 0;
    }
    test_replacement_tombstone();
    test_gate_and_focus_release();
    test_text_and_aliases();
    test_every_action_remaps();
    test_taps_repeats_and_conditions();
    puts("input shortcuts: 6 groups, all 34 remapped actions");
    return 0;
}
