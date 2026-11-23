#ifndef SC_SHORTCUT_BINDINGS_H
#define SC_SHORTCUT_BINDINGS_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>
#include <SDL3/SDL_keycode.h>

#define SC_SHORTCUT_BINDINGS_MAX_BYTES 4096
#define SC_SHORTCUT_GESTURE_MAX_BYTES 32
#define SC_SHORTCUT_MAX_ALIASES 2

enum sc_shortcut_context {
    SC_SHORTCUT_CONTEXT_ANY,
    SC_SHORTCUT_CONTEXT_DISPLAY,
    SC_SHORTCUT_CONTEXT_CAMERA,
};

enum sc_shortcut_action {
    SC_SHORTCUT_ACTION_NONE = -1,
#define SC_SHORTCUT_ACTION(NAME, ID, CONTEXT, TAP, FIRST, SECOND) SC_SHORTCUT_ACTION_##NAME,
#include "shortcut_actions.generated.inc"
#undef SC_SHORTCUT_ACTION
    SC_SHORTCUT_ACTION_COUNT,
};

struct sc_shortcut_gesture {
    SDL_Keycode key;
    bool modifier;
    bool shift;
    uint8_t tap;
};

struct sc_shortcut_override {
    bool present;
    bool disabled;
    struct sc_shortcut_gesture gesture;
};

/* Fixed invocation-owned values; no pointers into JSON or another generation. */
struct sc_shortcut_bindings {
    struct sc_shortcut_override actions[SC_SHORTCUT_ACTION_COUNT];
};

struct sc_shortcut_map_entry {
    struct sc_shortcut_gesture gestures[SC_SHORTCUT_MAX_ALIASES];
    uint8_t count;
    bool overridden;
};

struct sc_shortcut_map {
    struct sc_shortcut_map_entry actions[SC_SHORTCUT_ACTION_COUNT];
};

bool
sc_shortcut_bindings_parse(const char *json, struct sc_shortcut_bindings *bindings);

void
sc_shortcut_map_init(struct sc_shortcut_map *map,
                      const struct sc_shortcut_bindings *bindings);

enum sc_shortcut_action
sc_shortcut_map_match(const struct sc_shortcut_map *map, SDL_Keycode key,
                       uint16_t modifiers, uint16_t shortcut_modifiers,
                       bool camera, unsigned repeat, bool *claimed);

#endif
