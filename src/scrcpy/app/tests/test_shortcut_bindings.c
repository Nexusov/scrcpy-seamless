#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "shortcut_bindings.h"

static struct sc_shortcut_map
parse(const char *json) {
    struct sc_shortcut_bindings bindings;
    bool parsed = sc_shortcut_bindings_parse(json, &bindings);
    assert(parsed);
    struct sc_shortcut_map map;
    sc_shortcut_map_init(&map, &bindings);
    return map;
}

static enum sc_shortcut_action
match(const struct sc_shortcut_map *map, SDL_Keycode key, uint16_t modifiers,
        bool camera, unsigned repeat) {
    bool claimed;
    return sc_shortcut_map_match(map, key, modifiers, SDL_KMOD_LALT,
                                  camera, repeat, &claimed);
}

static void
test_defaults(void) {
    assert(SC_SHORTCUT_ACTION_COUNT == 34);
    struct sc_shortcut_map map = parse("{}");
    assert(match(&map, SDLK_H, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_HOME);
    assert(match(&map, SDLK_B, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_BACK);
    assert(match(&map, SDLK_BACKSPACE, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_BACK);
    assert(match(&map, SDLK_F11, 0, false, 0) == SC_SHORTCUT_ACTION_FULLSCREEN);
    assert(match(&map, SDLK_N, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_NOTIFICATIONS);
    assert(match(&map, SDLK_N, SDL_KMOD_LALT, false, 1) == SC_SHORTCUT_ACTION_QUICK_SETTINGS);
    assert(match(&map, SDLK_N, SDL_KMOD_LALT, false, 8) == SC_SHORTCUT_ACTION_QUICK_SETTINGS);
    assert(match(&map, SDLK_UP, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_VOLUME_UP);
    assert(match(&map, SDLK_UP, SDL_KMOD_LALT, true, 0) == SC_SHORTCUT_ACTION_CAMERA_ZOOM_IN);
    assert(match(&map, SDLK_LEFT, SDL_KMOD_LALT | SDL_KMOD_SHIFT, true, 0)
           == SC_SHORTCUT_ACTION_FLIP_HORIZONTAL);
}

static void
test_override_alias_disable(void) {
    struct sc_shortcut_map map = parse("{\"back\":\"F12\",\"fullscreen\":\"\"}");
    assert(match(&map, SDLK_F12, 0, false, 0) == SC_SHORTCUT_ACTION_BACK);
    assert(match(&map, SDLK_B, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_NONE);
    assert(match(&map, SDLK_BACKSPACE, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_NONE);
    assert(match(&map, SDLK_F11, 0, false, 0) == SC_SHORTCUT_ACTION_NONE);
    map = parse("{\"quickSettings\":\"MOD+J\"}");
    assert(match(&map, SDLK_J, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_QUICK_SETTINGS);
    assert(match(&map, SDLK_N, SDL_KMOD_LALT, false, 1) == SC_SHORTCUT_ACTION_NONE);
    map = parse("{\"quickSettings\":\"MOD+N\"}");
    assert(match(&map, SDLK_N, SDL_KMOD_LALT, false, 1) == SC_SHORTCUT_ACTION_QUICK_SETTINGS);
}

static void
test_conflicts_and_invalid(void) {
    static const char *const rejected[] = {
        "[]", "null", "{", "{\"future\":\"MOD+J\"}",
        "{\"home\":null}", "{\"home\":true}",
        "{\"home\":\"MOD+J\",\"home\":\"MOD+L\"}",
        "{\"home\":\"MOD+Q\"}", "{\"pause\":\"MOD+T\"}",
        "{\"home\":\"A\"}", "{\"home\":\"Ctrl+A\"}",
        "{\"home\":\"MOD+\"}", "{\"home\":\"MOD+a\"}",
        "{\"home\":\"MOD+shift+Z\"}", "{\"home\":\"Shift+F12\"}",
        "{\"home\":\"F0\"}", "{\"home\":\"F01\"}", "{\"home\":\"F25\"}",
        "{\"home\":\"MOD+J\\u0000evil\"}",
    };
    struct sc_shortcut_bindings bindings;
    memset(&bindings, 0xa5, sizeof(bindings));
    struct sc_shortcut_bindings before = bindings;

    for (unsigned index = 0; index < ARRAY_LEN(rejected); ++index) {
        bool parsed = sc_shortcut_bindings_parse(rejected[index], &bindings);
        assert(!parsed);
        assert(!memcmp(&bindings, &before, sizeof(bindings)));
    }

    struct sc_shortcut_map map = parse("{\"volumeUp\":\"MOD+T\"}");
    assert(match(&map, SDLK_T, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_VOLUME_UP);
    assert(match(&map, SDLK_T, SDL_KMOD_LALT, true, 0) == SC_SHORTCUT_ACTION_TORCH_ON);
    map = parse("{\"home\":\"MOD+Q\",\"quitMirror\":\"\"}");
    assert(match(&map, SDLK_Q, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_HOME);
}

static void
test_bounds_and_function_keys(void) {
    char maximum[SC_SHORTCUT_BINDINGS_MAX_BYTES + 2];
    memset(maximum, ' ', sizeof(maximum));
    memcpy(maximum, "{}", 2);
    maximum[SC_SHORTCUT_BINDINGS_MAX_BYTES] = '\0';
    struct sc_shortcut_bindings bindings;
    bool parsed = sc_shortcut_bindings_parse(maximum, &bindings);
    assert(parsed);
    maximum[SC_SHORTCUT_BINDINGS_MAX_BYTES] = ' ';
    maximum[SC_SHORTCUT_BINDINGS_MAX_BYTES + 1] = '\0';
    parsed = sc_shortcut_bindings_parse(maximum, &bindings);
    assert(!parsed);
    struct sc_shortcut_map map = parse("{\"home\":\"MOD+F24\"}");
    assert(match(&map, SDLK_F24, SDL_KMOD_LALT, false, 0) == SC_SHORTCUT_ACTION_HOME);
    assert(match(&map, SDLK_F24, 0, false, 0) == SC_SHORTCUT_ACTION_NONE);
}

int
main(void) {
    test_defaults();
    test_override_alias_disable();
    test_conflicts_and_invalid();
    test_bounds_and_function_keys();
    puts("Native shortcut bindings: 4 production parser/semantic-map groups passed");
    return 0;
}
