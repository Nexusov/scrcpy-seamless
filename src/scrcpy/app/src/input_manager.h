#ifndef SC_INPUTMANAGER_H
#define SC_INPUTMANAGER_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>
#include <SDL3/SDL_events.h>
#include <SDL3/SDL_keycode.h>

#include "controller.h"
#include "file_pusher.h"
#include "options.h"
#include "trait/gamepad_processor.h"
#include "trait/key_processor.h"
#include "trait/mouse_processor.h"

/** Borrowed endpoints belonging to one dispatcher generation. */
struct sc_input_binding {
    uint64_t generation;
    struct sc_controller *controller;
    struct sc_file_pusher *fp;
    struct sc_key_processor *kp;
    struct sc_mouse_processor *mp;
    struct sc_gamepad_processor *gp;
};

enum sc_input_binding_state {
    SC_INPUT_DETACHED,
    SC_INPUT_WAITING_PRESENTATION,
    SC_INPUT_READY,
};

struct sc_input_gamepad;

struct sc_input_manager {
    // P7.2: the only generation endpoint binding; SDL main-thread access only.
    struct sc_input_binding binding;
    enum sc_input_binding_state binding_state;
    struct sc_input_gamepad *gamepads; // owned local handles
    struct sc_screen *screen;

    bool camera;

    struct sc_mouse_bindings mouse_bindings;
    bool legacy_paste;
    bool clipboard_autosync;

    uint16_t sdl_shortcut_mods;

    bool vfinger_down;
    bool vfinger_invert_x;
    bool vfinger_invert_y;

    uint8_t mouse_buttons_state; // OR of enum sc_mouse_button values

    // Tracks the number of identical consecutive shortcut key down events.
    // Not to be confused with event->repeat, which counts the number of
    // system-generated repeated key presses.
    unsigned key_repeat;
    SDL_Keycode last_keycode;
    uint16_t last_mod;

    uint64_t next_sequence; // used for request acknowledgements
};

struct sc_input_manager_params {
    struct sc_screen *screen;
    bool camera;

    struct sc_mouse_bindings mouse_bindings;
    bool legacy_paste;
    bool clipboard_autosync;
    uint8_t shortcut_mods; // OR of enum sc_shortcut_mod values
};

void
sc_input_manager_init(struct sc_input_manager *im,
                      const struct sc_input_manager_params *params);

/** Bind initialized endpoints, deferring visual input until presentation. */
void
sc_input_manager_bind(struct sc_input_manager *im,
                      const struct sc_input_binding *binding,
                      bool require_presentation);

/** Revoke all endpoint borrowing and release local gamepads before stop. */
void
sc_input_manager_detach(struct sc_input_manager *im);

/** Enable only the matching generation after a successful new presentation. */
bool
sc_input_manager_mark_presented(struct sc_input_manager *im,
                                uint64_t generation);

/** Report whether remote delivery is enabled on the SDL main thread. */
bool
sc_input_manager_is_ready(const struct sc_input_manager *im);

/** Resolve relative capture compatibility through the live binding. */
bool
sc_input_manager_is_relative(const struct sc_input_manager *im);

/** Route device display resize through the same revocable binding. */
bool
sc_input_manager_request_resize(struct sc_input_manager *im, uint16_t width,
                                uint16_t height);

/** Release local router resources; initialized endpoints remain borrowed. */
void
sc_input_manager_destroy(struct sc_input_manager *im);

void
sc_input_manager_handle_event(struct sc_input_manager *im,
                              const SDL_Event *event);

#endif
