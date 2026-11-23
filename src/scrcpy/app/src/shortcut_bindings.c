#include "shortcut_bindings.h"

#include <assert.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

#include "shortcut_mod.h"
#include "../vendor/yyjson/yyjson.h"

#define SC_SHORTCUT_PARSE_MEMORY (64 * 1024)

struct sc_shortcut_definition {
    const char *name;
    enum sc_shortcut_context context;
    uint8_t tap;
    const char *gestures[SC_SHORTCUT_MAX_ALIASES];
};

static const struct sc_shortcut_definition definitions[] = {
#define SC_SHORTCUT_ACTION(NAME, ID, CONTEXT, TAP, FIRST, SECOND) \
    [SC_SHORTCUT_ACTION_##NAME] = {ID, CONTEXT, TAP, {FIRST, SECOND}},
#include "shortcut_actions.generated.inc"
#undef SC_SHORTCUT_ACTION
};

static bool
sc_shortcut_parse_gesture(const char *value, struct sc_shortcut_gesture *gesture) {
    size_t length = strlen(value);

    if (!length || length > SC_SHORTCUT_GESTURE_MAX_BYTES) {
        return false;
    }

    bool modifier = !strncmp(value, "MOD+", 4);
    const char *key = value + (modifier ? 4 : 0);
    bool shift = modifier && !strncmp(key, "Shift+", 6);

    if (shift) {
        key += 6;
    }

    SDL_Keycode code = SDLK_UNKNOWN;
    unsigned function = 0;

    if (key[0] == 'F' && key[1] >= '1' && key[1] <= '9') {
        function = key[1] - '0';

        if (key[2]) {
            if (key[2] < '0' || key[2] > '9' || key[3]) {
                return false;
            }

            function = function * 10 + key[2] - '0';
        }

        if (function > 24) {
            return false;
        }

        code = function <= 12 ? SDLK_F1 + function - 1 : SDLK_F13 + function - 13;
    } else if (modifier) {
        if (key[0] && !key[1]) {
            if (key[0] >= 'A' && key[0] <= 'Z') {
                code = SDLK_A + key[0] - 'A';
            } else if (key[0] >= '0' && key[0] <= '9') {
                code = SDLK_0 + key[0] - '0';
            }
        }

        if (!strcmp(key, "Left")) {
            code = SDLK_LEFT;
        } else if (!strcmp(key, "Right")) {
            code = SDLK_RIGHT;
        } else if (!strcmp(key, "Up")) {
            code = SDLK_UP;
        } else if (!strcmp(key, "Down")) {
            code = SDLK_DOWN;
        } else if (!strcmp(key, "Backspace")) {
            code = SDLK_BACKSPACE;
        }
    }

    if (code == SDLK_UNKNOWN) {
        return false;
    }

    *gesture = (struct sc_shortcut_gesture) {code, modifier, shift, 0};
    return true;
}

void
sc_shortcut_map_init(struct sc_shortcut_map *map,
                      const struct sc_shortcut_bindings *bindings) {
    memset(map, 0, sizeof(*map));

    for (unsigned action = 0; action < SC_SHORTCUT_ACTION_COUNT; ++action) {
        struct sc_shortcut_map_entry *entry = &map->actions[action];
        const struct sc_shortcut_override *override = bindings ? &bindings->actions[action] : NULL;

        if (override && override->present) {
            entry->overridden = true;

            if (!override->disabled) {
                entry->gestures[0] = override->gesture;
                entry->count = 1;
            }

            continue;
        }

        const struct sc_shortcut_definition *definition = &definitions[action];

        for (unsigned alias = 0; alias < SC_SHORTCUT_MAX_ALIASES; ++alias) {
            if (!definition->gestures[alias]) {
                break;
            }

            bool parsed = sc_shortcut_parse_gesture(definition->gestures[alias], &entry->gestures[alias]);
            assert(parsed);
            (void) parsed;
            entry->gestures[alias].tap = definition->tap;
            ++entry->count;
        }
    }
}

static bool
sc_shortcut_conflicts(const struct sc_shortcut_map *map) {
    for (unsigned first = 0; first < SC_SHORTCUT_ACTION_COUNT; ++first) {
        for (unsigned second = first + 1; second < SC_SHORTCUT_ACTION_COUNT; ++second) {
            enum sc_shortcut_context left_context = definitions[first].context;
            enum sc_shortcut_context right_context = definitions[second].context;

            if (left_context != SC_SHORTCUT_CONTEXT_ANY && right_context != SC_SHORTCUT_CONTEXT_ANY
                    && left_context != right_context) {
                continue;
            }

            for (unsigned left = 0; left < map->actions[first].count; ++left) {
                for (unsigned right = 0; right < map->actions[second].count; ++right) {
                    const struct sc_shortcut_gesture *a = &map->actions[first].gestures[left];
                    const struct sc_shortcut_gesture *b = &map->actions[second].gestures[right];

                    if (a->key == b->key && a->modifier == b->modifier && a->shift == b->shift
                            && (!a->tap || !b->tap || a->tap == b->tap)) {
                        return true;
                    }
                }
            }
        }
    }

    return false;
}

bool
sc_shortcut_bindings_parse(const char *json, struct sc_shortcut_bindings *bindings) {
    size_t length = strnlen(json, SC_SHORTCUT_BINDINGS_MAX_BYTES + 1);

    if (!length || length > SC_SHORTCUT_BINDINGS_MAX_BYTES) {
        return false;
    }

    void *storage = malloc(SC_SHORTCUT_PARSE_MEMORY);

    if (!storage) {
        return false;
    }

    yyjson_alc allocator;
    bool initialized = yyjson_alc_pool_init(&allocator, storage, SC_SHORTCUT_PARSE_MEMORY);
    assert(initialized);
    (void) initialized;
    yyjson_doc *document = yyjson_read_opts((char *) json, length, 0, &allocator, NULL);

    if (!document) {
        free(storage);
        return false;
    }

    yyjson_val *root = yyjson_doc_get_root(document);
    struct sc_shortcut_bindings candidate = {0};
    bool valid = yyjson_is_obj(root) && yyjson_obj_size(root) <= SC_SHORTCUT_ACTION_COUNT;
    size_t index, count;
    yyjson_val *key;
    yyjson_val *value;
    yyjson_obj_foreach(root, index, count, key, value) {
        const char *name = yyjson_get_str(key);
        unsigned action = 0;

        while (action < SC_SHORTCUT_ACTION_COUNT && strcmp(definitions[action].name, name)) {
            ++action;
        }

        if (!valid || action == SC_SHORTCUT_ACTION_COUNT || strlen(name) != yyjson_get_len(key)
                || candidate.actions[action].present || !yyjson_is_str(value)) {
            valid = false;
            break;
        }

        const char *gesture = yyjson_get_str(value);
        struct sc_shortcut_override *override = &candidate.actions[action];
        override->present = true;
        override->disabled = !yyjson_get_len(value);

        if (strlen(gesture) != yyjson_get_len(value) || (!override->disabled
                && !sc_shortcut_parse_gesture(gesture, &override->gesture))) {
            valid = false;
            break;
        }

        for (unsigned alias = 0; alias < SC_SHORTCUT_MAX_ALIASES; ++alias) {
            const char *original = definitions[action].gestures[alias];

            if (original && !strcmp(original, gesture)) {
                override->gesture.tap = definitions[action].tap;
            }
        }
    }

    yyjson_doc_free(document);
    free(storage);

    if (!valid) {
        return false;
    }

    struct sc_shortcut_map map;
    sc_shortcut_map_init(&map, &candidate);

    if (sc_shortcut_conflicts(&map)) {
        return false;
    }

    *bindings = candidate;
    return true;
}

enum sc_shortcut_action
sc_shortcut_map_match(const struct sc_shortcut_map *map, SDL_Keycode key,
                       uint16_t modifiers, uint16_t shortcut_modifiers,
                       bool camera, unsigned repeat, bool *claimed) {
    *claimed = false;
    bool modifier = sc_shortcut_mods_is_shortcut_mod(shortcut_modifiers, modifiers);
    bool shift = modifiers & SDL_KMOD_SHIFT;

    for (unsigned action = 0; action < SC_SHORTCUT_ACTION_COUNT; ++action) {
        enum sc_shortcut_context context = definitions[action].context;

        if ((context == SC_SHORTCUT_CONTEXT_CAMERA && !camera)
                || (context == SC_SHORTCUT_CONTEXT_DISPLAY && camera)) {
            continue;
        }

        const struct sc_shortcut_map_entry *entry = &map->actions[action];

        for (unsigned alias = 0; alias < entry->count; ++alias) {
            const struct sc_shortcut_gesture *gesture = &entry->gestures[alias];

            if (gesture->key != key || gesture->modifier != modifier || gesture->shift != shift
                    || (!gesture->modifier && (modifiers & SC_SDL_SHORTCUT_MODS_MASK))) {
                continue;
            }

            *claimed = true;

            if (((!gesture->tap || gesture->tap == 1) && !repeat)
                    || ((!gesture->tap || gesture->tap == 2) && repeat)) {
                return action;
            }
        }
    }

    return SC_SHORTCUT_ACTION_NONE;
}
