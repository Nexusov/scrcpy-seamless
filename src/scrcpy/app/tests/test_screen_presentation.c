#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <libavutil/buffer.h>

#include "events.h"
#include "generation_targets.h"
#include "icon.h"
#include "ipc/machine.h"
#include "screen.h"
#include "video_ingress.h"
#include "util/sdl.h"
#include "screen_test_effects.h"

#define TEST_FRAME_WIDTH 64
#define TEST_FRAME_HEIGHT 32
#define TEST_WINDOW_ID 7
#define TEST_OTHER_WINDOW_ID 8
#define TEST_TITLE_CAPACITY 128
#ifdef _WIN32
# define TEST_FIRST_READINESS 1
# define TEST_SECOND_READINESS 2
#else
# define TEST_FIRST_READINESS 0
# define TEST_SECOND_READINESS 0
#endif

/* P7.2: effect doubles never create a window, touch a clipboard or deliver HID. */
static unsigned window_token;
static unsigned renderer_token;
static unsigned texture_token;
static SDL_Window *const owned_window = (SDL_Window *) &window_token;
static SDL_Renderer *const owned_renderer = (SDL_Renderer *) &renderer_token;
static SDL_Texture *const owned_texture = (SDL_Texture *) &texture_token;
static struct {
    bool fail_window;
    bool fail_renderer;
    bool fail_texture_init;
    bool fail_text_input;
    bool fail_texture_update;
    bool fail_render;
    bool fail_draw_color;
    bool fail_clear;
    bool fail_present;
    bool fail_watch;
    bool focused;
    bool captured;
    bool reenter_watcher;
    unsigned windows;
    unsigned renderers;
    unsigned textures;
    unsigned presentations;
    unsigned readiness;
    unsigned resize_requests;
    unsigned size_changes;
    unsigned watch_additions;
    unsigned watch_removals;
    SDL_WindowFlags flags;
    struct sc_size size;
    struct sc_point position;
    float aspect;
    int64_t texture_identity;
    char title[TEST_TITLE_CAPACITY];
    SDL_EventFilter watcher;
    void *watcher_context;
} effects;

static unsigned frame_allocations;
static unsigned frame_releases;
static unsigned allocation_attempts;
static unsigned fail_frame_attempt;
static SDL_Event latest_wakeup;

AVFrame *__real_av_frame_alloc(void);
void __real_av_frame_free(AVFrame **frame);
AVFrame *__wrap_av_frame_alloc(void);
void __wrap_av_frame_free(AVFrame **frame);

/** Fail an exact production frame-container allocation and retain real refs. */
AVFrame *
__wrap_av_frame_alloc(void) {
    ++allocation_attempts;

    if (allocation_attempts == fail_frame_attempt) {
        return NULL;
    }

    AVFrame *frame = __real_av_frame_alloc();

    if (frame) {
        ++frame_allocations;
    }

    return frame;
}

/** Account each acquired container while FFmpeg owns its buffer retirement. */
void
__wrap_av_frame_free(AVFrame **frame) {

    if (frame && *frame) {
        ++frame_releases;
    }

    __real_av_frame_free(frame);
}

/** Reset only after every owned test resource has settled. */
static void
reset_effects(void) {
    assert(frame_allocations == frame_releases);
    assert(!effects.windows && !effects.renderers && !effects.textures);
    assert(!effects.watcher);
    memset(&effects, 0, sizeof(effects));
    effects.focused = true;
    allocation_attempts = fail_frame_attempt = 0;
}

/** Model window creation without invoking a desktop window manager. */
SDL_Window *
sc_sdl_create_window(const char *title, int64_t x, int64_t y, int64_t width,
                     int64_t height, int64_t flags) {

    if (effects.fail_window) {
        return NULL;
    }

    ++effects.windows;
    effects.size = (struct sc_size) {width, height};
    effects.position = (struct sc_point) {x, y};
    effects.flags = flags;
    snprintf(effects.title, sizeof(effects.title), "%s", title);
    return owned_window;
}

/** Model renderer acquisition only. */
SDL_Renderer *
sc_screen_test_create_renderer(SDL_Window *window, const char *name) {
    assert(window == owned_window);
    (void) name;

    if (effects.fail_renderer) {
        return NULL;
    }

    ++effects.renderers;
    return owned_renderer;
}

/** Avoid selecting an OpenGL context in the deterministic effect lane. */
const char *
sc_screen_test_renderer_name(SDL_Renderer *renderer) {
    assert(renderer == owned_renderer);
    return "screen-test";
}

/** Assert that no platform graphics context was acquired by the test. */
bool
sc_screen_test_destroy_gl_context(SDL_GLContext context) {
    assert(!context);
    return true;
}

/** Reject window release while a callback context remains registered. */
void
sc_screen_test_destroy_window(SDL_Window *window) {
    assert(window == owned_window);
    assert(!effects.watcher);
    assert(effects.windows);
    --effects.windows;
}

/** Observe renderer retirement without SDL rendering. */
void
sc_screen_test_destroy_renderer(SDL_Renderer *renderer) {
    assert(renderer == owned_renderer);
    assert(effects.renderers);
    --effects.renderers;
}

/** Supply a bounded test-owned display geometry. */
SDL_DisplayID
sc_screen_test_primary_display(void) {
    return TEST_WINDOW_ID;
}

/** Supply display bounds without querying a physical desktop. */
bool
sc_screen_test_display_bounds(SDL_DisplayID display, SDL_Rect *rect) {
    assert(display == TEST_WINDOW_ID);
    *rect = (SDL_Rect) {0, 0, 1920, 1080};
    return true;
}

/** Return the local window state mutated by production presentation actions. */
SDL_WindowFlags
sc_screen_test_window_flags(SDL_Window *window) {
    assert(window == owned_window);
    return effects.flags;
}

/** Return the identity used to filter continuous-resize callbacks. */
SDL_WindowID
sc_screen_test_window_id(SDL_Window *window) {
    assert(window == owned_window);
    return TEST_WINDOW_ID;
}

/** Retain unit scaling for deterministic renderer geometry. */
float
sc_screen_test_pixel_density(SDL_Window *window) {
    assert(window == owned_window);
    return 1.f;
}

/** Expose relevant-window focus as an explicitly controlled effect. */
SDL_Window *
sc_screen_test_keyboard_focus(void) {
    return effects.focused ? owned_window : NULL;
}

/** Accept draw-color setup without invoking a graphics driver. */
bool
sc_screen_test_draw_color(SDL_Renderer *renderer, Uint8 red, Uint8 green,
                          Uint8 blue, Uint8 alpha) {
    assert(renderer == owned_renderer);
    (void) red; (void) green; (void) blue; (void) alpha;
    return !effects.fail_draw_color;
}

/** Control texture rendering separately from texture upload and presentation. */
bool
sc_screen_test_render_texture(SDL_Renderer *renderer, SDL_Texture *texture,
                              const SDL_FRect *source,
                              const SDL_FRect *destination) {
    assert(renderer == owned_renderer && texture == owned_texture);
    assert(destination && destination->w > 0 && destination->h > 0);
    (void) source;

    if (effects.reenter_watcher) {
        effects.reenter_watcher = false;
        SDL_Event resize = {
            .window = {
                .type = SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED,
                .windowID = TEST_WINDOW_ID,
            },
        };
        assert(effects.watcher(effects.watcher_context, &resize));
    }

    return !effects.fail_render;
}

/** Preserve the rotated rendering route with the same controlled failure. */
bool
sc_screen_test_render_rotated(SDL_Renderer *renderer, SDL_Texture *texture,
                              const SDL_FRect *source,
                              const SDL_FRect *destination, double angle,
                              const SDL_FPoint *center, SDL_FlipMode flip) {
    (void) angle; (void) center; (void) flip;
    return sc_screen_test_render_texture(renderer, texture, source, destination);
}

/** Record aspect changes performed by production geometry policy. */
bool
sc_screen_test_aspect_ratio(SDL_Window *window, float minimum, float maximum) {
    assert(window == owned_window && minimum == maximum);
    effects.aspect = minimum;
    return true;
}

/** Model local fullscreen changes without any foreground activation. */
bool
sc_screen_test_fullscreen(SDL_Window *window, bool fullscreen) {
    assert(window == owned_window);

    if (fullscreen) {
        effects.flags |= SDL_WINDOW_FULLSCREEN;
        return true;
    }

    effects.flags &= ~SDL_WINDOW_FULLSCREEN;
    return true;
}

/** Record title changes without exposing external device information. */
bool
sc_screen_test_title(SDL_Window *window, const char *title) {
    assert(window == owned_window && title);
    snprintf(effects.title, sizeof(effects.title), "%s", title);
    return true;
}

/** Accept an optional icon effect only. */
bool
sc_screen_test_icon(SDL_Window *window, SDL_Surface *icon) {
    assert(window == owned_window && icon);
    return true;
}

/** Fail text-input activation to exercise the acquired-resource unwind. */
bool
sc_screen_test_start_text(SDL_Window *window) {
    assert(window == owned_window);
    return !effects.fail_text_input;
}

/** Capture the actual production callback without installing a global watcher. */
bool
sc_screen_test_add_watch(SDL_EventFilter callback, void *userdata) {
    ++effects.watch_additions;

    if (effects.fail_watch) {
        return false;
    }

    assert(!effects.watcher);
    effects.watcher = callback;
    effects.watcher_context = userdata;
    return true;
}

/** Enforce exact callback retirement before freeing presentation resources. */
void
sc_screen_test_remove_watch(SDL_EventFilter callback, void *userdata) {
    assert(callback == effects.watcher && userdata == effects.watcher_context);
    ++effects.watch_removals;
    effects.watcher = NULL;
    effects.watcher_context = NULL;
}

/** Read test-owned window geometry. */
struct sc_size
sc_sdl_get_window_size(SDL_Window *window) {
    assert(window == owned_window);
    return effects.size;
}

/** Record window size actions performed by production policy. */
void
sc_sdl_set_window_size(SDL_Window *window, struct sc_size size) {
    assert(window == owned_window);
    effects.size = size;
    ++effects.size_changes;
}

/** Read test-owned window position. */
struct sc_point
sc_sdl_get_window_position(SDL_Window *window) {
    assert(window == owned_window);
    return effects.position;
}

/** Retain local position without invoking the operating system. */
void
sc_sdl_set_window_position(SDL_Window *window, struct sc_point position) {
    assert(window == owned_window);
    effects.position = position;
}

/** Show only the modeled window. */
void
sc_sdl_show_window(SDL_Window *window) {
    assert(window == owned_window);
    effects.flags &= ~SDL_WINDOW_HIDDEN;
}

/** Hide only the modeled window. */
void
sc_sdl_hide_window(SDL_Window *window) {
    assert(window == owned_window);
    effects.flags |= SDL_WINDOW_HIDDEN;
}

/** Accept clearing without a graphics driver. */
bool
sc_sdl_render_clear(SDL_Renderer *renderer) {
    assert(renderer == owned_renderer);
    return !effects.fail_clear;
}

/** Control the final presentation result independently from rendering. */
bool
sc_sdl_render_present(SDL_Renderer *renderer) {
    assert(renderer == owned_renderer);
    ++effects.presentations;
    return !effects.fail_present;
}

/** Acquire only a modeled texture effect. */
bool
sc_texture_init(struct sc_texture *texture, SDL_Renderer *renderer,
                 bool mipmaps) {
    assert(renderer == owned_renderer);
    (void) mipmaps;

    if (effects.fail_texture_init) {
        return false;
    }

    memset(texture, 0, sizeof(*texture));
    texture->renderer = renderer;
    ++effects.textures;
    return true;
}

/** Retire the modeled texture owner exactly once. */
void
sc_texture_destroy(struct sc_texture *texture) {
    assert(texture->renderer == owned_renderer && effects.textures);
    --effects.textures;
    texture->texture = NULL;
}

/** Control upload while using actual decoded-frame references. */
bool
sc_texture_set_from_frame(struct sc_texture *texture, const AVFrame *frame) {
    assert(texture->renderer == owned_renderer && frame->buf[0]);

    if (effects.fail_texture_update) {
        return false;
    }

    texture->texture = owned_texture;
    texture->texture_size = (struct sc_size) {frame->width, frame->height};
    effects.texture_identity = frame->pts;
    return true;
}

/** Avoid icon pixel conversion in this presentation test lane. */
bool
sc_texture_set_from_surface(struct sc_texture *texture, SDL_Surface *surface) {
    (void) texture; (void) surface;
    assert(!"Unexpected icon texture effect");
    return false;
}

/** Reset only the modeled graphics effect. */
void
sc_texture_reset(struct sc_texture *texture) {
    texture->texture = NULL;
}

/** Do not load external image files in the synthetic presentation lane. */
SDL_Surface *
sc_icon_load(const char *filename) {
    (void) filename;
    return NULL;
}

/** Forbid an unowned icon release. */
void
sc_icon_destroy(SDL_Surface *icon) {
    assert(!icon);
}

/** Initialize only persistent local capture state. */
void
sc_mouse_capture_init(struct sc_mouse_capture *capture, SDL_Window *window,
                      uint8_t shortcut_mods) {
    (void) shortcut_mods;
    capture->window = window;
}

/** Record capture intent without changing the real mouse. */
void
sc_mouse_capture_set_active(struct sc_mouse_capture *capture, bool active) {
    assert(capture->window == owned_window);
    effects.captured = active;
}

/** Read modeled capture state. */
bool
sc_mouse_capture_is_active(struct sc_mouse_capture *capture) {
    assert(capture->window == owned_window);
    return effects.captured;
}

/** Preserve local capture toggle without physical input effects. */
void
sc_mouse_capture_toggle(struct sc_mouse_capture *capture) {
    sc_mouse_capture_set_active(capture, !effects.captured);
}

/** Leave synthetic events to the actual production input router. */
bool
sc_mouse_capture_handle_event(struct sc_mouse_capture *capture,
                              const SDL_Event *event) {
    (void) capture; (void) event;
    return false;
}

/** Observe machine readiness at the actual successful-new-frame boundary. */
void
sc_machine_on_frame_presented(void) {
    ++effects.readiness;
}

/** Prohibit accidental remote control in geometry-only test routes. */
bool
sc_controller_push_msg(struct sc_controller *controller,
                       const struct sc_control_msg *message) {
    (void) controller; (void) message;
    assert(!"Unexpected remote control effect");
    return false;
}

/** Count admitted display resize delivery through the actual input gate. */
void
sc_controller_resize_display(struct sc_controller *controller, uint16_t width,
                             uint16_t height) {
    assert(controller && width && height);
    ++effects.resize_requests;
}

/** Prohibit file queue access in presentation-only tests. */
bool
sc_file_pusher_request(struct sc_file_pusher *pusher,
                       enum sc_file_pusher_action action, char *file) {
    (void) pusher; (void) action; (void) file;
    assert(!"Unexpected file effect");
    return false;
}

struct screen_context {
    struct sc_dispatcher dispatcher;
    struct sc_generation_targets targets;
    struct sc_screen screen;
    struct sc_video_bridge bridge;
    struct sc_screen_params params;
    sc_dispatcher_generation generation;
    struct sc_controller controller;
    struct sc_mouse_processor mouse;
};

/** Forbid unexpected endpoint use while retaining a valid relative processor. */
static void
unexpected_mouse_motion(struct sc_mouse_processor *processor,
                         const struct sc_mouse_motion_event *event) {
    (void) processor; (void) event;
    assert(!"Unexpected device mouse motion");
}

/** Forbid unexpected device clicks in capture-policy tests. */
static void
unexpected_mouse_click(struct sc_mouse_processor *processor,
                        const struct sc_mouse_click_event *event) {
    (void) processor; (void) event;
    assert(!"Unexpected device mouse click");
}

static const struct sc_mouse_processor_ops mouse_operations = {
    .process_mouse_motion = unexpected_mouse_motion,
    .process_mouse_click = unexpected_mouse_click,
};

/** Admit owned wake notifications without touching native window resources. */
static bool
wake_dispatcher(void *userdata, SDL_Event *event) {
    (void) userdata;
    latest_wakeup = *event;
    return SDL_PushEvent(event);
}

/** Build a producer-owned FFmpeg frame with actual shared buffer operations. */
static AVFrame *
create_frame(int width, int height, int64_t identity) {
    AVFrame *frame = av_frame_alloc();
    assert(frame);
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = width;
    frame->height = height;
    frame->pts = identity;
    assert(!av_frame_get_buffer(frame, 0));
    return frame;
}

/** Begin the real dispatcher generation before binding presentation ingress. */
static void
prepare_context(struct screen_context *context, bool video, bool relative) {
    memset(context, 0, sizeof(*context));
    context->targets.screen = &context->screen;
    assert(sc_dispatcher_init(&context->dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               wake_dispatcher, NULL));
    assert(sc_dispatcher_generation_begin(&context->dispatcher,
                                           &context->targets,
                                           &context->generation));
    context->mouse.relative_mode = relative;
    context->mouse.ops = &mouse_operations;
    context->params = (struct sc_screen_params) {
        .video = video,
        .controller = &context->controller,
        .mp = relative ? &context->mouse : NULL,
        .window_title = "owned presentation",
        .window_x = SC_WINDOW_POSITION_UNDEFINED,
        .window_y = SC_WINDOW_POSITION_UNDEFINED,
        .window_aspect_ratio_lock = true,
        .render_fit = SC_RENDER_FIT_LETTERBOX,
        .dispatcher = &context->dispatcher,
        .generation = context->generation,
    };
}

/** Initialize presentation only after preparing its owning dispatcher binding. */
static void
init_context(struct screen_context *context, bool video, bool relative) {
    prepare_context(context, video, relative);
    assert(sc_screen_init(&context->screen, &context->params));
    sc_video_bridge_init(&context->bridge, &context->screen.ingress,
                          context->generation);
}

/** Open the actual producer bridge with immutable initial stream metadata. */
static void
open_bridge(struct sc_video_bridge *bridge, int width, int height,
             bool client_resized) {
    AVCodecContext codec = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = width,
        .height = height,
    };
    struct sc_stream_session metadata = {
        .video = {width, height, client_resized},
    };
    assert(bridge->frame_sink.ops->open(&bridge->frame_sink, &codec, &metadata));
}

/** Publish through the real bridge and execute the actual presentation handler. */
static void
present_frame(struct screen_context *context, const AVFrame *frame) {
    assert(context->bridge.frame_sink.ops->push(&context->bridge.frame_sink,
                                                frame));
    assert(sc_dispatcher_drain(&context->dispatcher));
}

/** Revoke borrowing, settle owned notifications and destroy in owner order. */
static void
destroy_context(struct screen_context *context) {

    if (context->bridge.open) {
        context->bridge.frame_sink.ops->close(&context->bridge.frame_sink);
    }

    sc_screen_prepare_reconnect(&context->screen);
    assert(sc_dispatcher_generation_revoke(&context->dispatcher,
                                            context->generation));
    assert(sc_dispatcher_shutdown(&context->dispatcher));
    sc_screen_interrupt(&context->screen);
    sc_screen_join(&context->screen);
    sc_screen_destroy(&context->screen);
    assert(sc_dispatcher_destroy(&context->dispatcher));
    assert(frame_allocations == frame_releases);
}

/** Bind replacement endpoints while retaining app-owned window/frame state. */
static void
replace_generation(struct screen_context *context, bool relative) {
    sc_screen_prepare_reconnect(&context->screen);

    if (context->bridge.open) {
        context->bridge.frame_sink.ops->close(&context->bridge.frame_sink);
    }

    assert(sc_dispatcher_generation_revoke(&context->dispatcher,
                                            context->generation));
    assert(sc_dispatcher_generation_begin(&context->dispatcher,
                                           &context->targets,
                                           &context->generation));
    context->params.generation = context->generation;
    context->params.mp = relative ? &context->mouse : NULL;
    context->params.window_title = "replacement presentation";
    sc_screen_rebind(&context->screen, &context->params);
    sc_video_bridge_init(&context->bridge, &context->screen.ingress,
                          context->generation);
}

/** Fail each rendering stage and require one successful current new frame. */
static void
test_presentation_gate(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, true);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.captured && !effects.readiness);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness);
    AVFrame *frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    effects.fail_texture_update = true;
    present_frame(&context, frame);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness && !context.screen.frame->buf[0]);
    effects.fail_texture_update = false;
    effects.fail_render = true;
    unsigned presentations_before_draw_failure = effects.presentations;
    present_frame(&context, frame);
    assert(effects.presentations == presentations_before_draw_failure);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness && !context.screen.frame->buf[0]);
    effects.fail_render = false;
    effects.fail_present = true;
    present_frame(&context, frame);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness && !context.screen.frame->buf[0]);
    effects.fail_present = false;
    present_frame(&context, frame);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_FIRST_READINESS && effects.captured);
    assert(context.screen.frame->pts == frame->pts);
    assert(av_buffer_get_ref_count(frame->buf[0]) == 2);
    av_frame_free(&frame);
    destroy_context(&context);
    puts("screen: texture/render/present failures and first presentation gate");
}

/** A failed clear or draw-color prerequisite cannot claim successful readiness. */
static void
test_render_prerequisites(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    effects.fail_clear = true;
    present_frame(&context, frame);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness && !effects.presentations);
    assert(!context.screen.frame->buf[0]);
    effects.fail_clear = false;
    effects.fail_draw_color = true;
    present_frame(&context, frame);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!effects.readiness && !effects.presentations);
    assert(!context.screen.frame->buf[0]);
    effects.fail_draw_color = false;
    present_frame(&context, frame);
    assert(sc_input_manager_is_ready(&context.screen.im));
    av_frame_free(&frame);
    destroy_context(&context);
    puts("screen: failed clear/draw-color prerequisites preserve the presentation gate");
}

/** Failed size-changing frames cannot enqueue window or remote resize effects. */
static void
test_failed_ready_frame_resize(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *resized = create_frame(TEST_FRAME_WIDTH * 2,
                                    TEST_FRAME_HEIGHT * 2, 2);
    present_frame(&context, displayed);
    assert(sc_input_manager_is_ready(&context.screen.im));
    unsigned size_changes = effects.size_changes;
    struct sc_size previous_size = effects.size;
    effects.fail_render = true;
    present_frame(&context, resized);
    assert(context.screen.frame->pts == displayed->pts);
    assert(effects.size_changes == size_changes);
    assert(effects.size.width == previous_size.width);
    assert(effects.size.height == previous_size.height);
    assert(!effects.resize_requests);
    assert(effects.readiness == TEST_FIRST_READINESS);
    effects.fail_render = false;
    present_frame(&context, resized);
    assert(context.screen.frame->pts == resized->pts);
    assert(effects.size_changes == size_changes + 1);
    assert(effects.readiness == TEST_SECOND_READINESS);
    av_frame_free(&displayed);
    av_frame_free(&resized);
    destroy_context(&context);
    puts("screen: failed ready frame emits no transient resize effects");
}

/** Preserve last display and user geometry across failed and stale replacement. */
static void
test_reconnect_last_frame(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, true);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *old_frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    present_frame(&context, old_frame);
    struct sc_video_bridge old_bridge = context.bridge;
    memset(&context.bridge, 0, sizeof(context.bridge));
    effects.size = (struct sc_size) {640, 360};
    effects.position = (struct sc_point) {123, 234};
    unsigned size_changes = effects.size_changes;
    replace_generation(&context, true);
    assert(!strcmp(effects.title, "scrcpy - Reconnecting..."));
    assert(!effects.captured && context.screen.frame->pts == old_frame->pts);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    AVFrame *new_frame = create_frame(TEST_FRAME_WIDTH * 2,
                                      TEST_FRAME_HEIGHT * 2, 2);
    assert(!old_bridge.frame_sink.ops->push(&old_bridge.frame_sink, new_frame));
    old_bridge.frame_sink.ops->close(&old_bridge.frame_sink);
    assert(context.screen.frame->pts == old_frame->pts);
    open_bridge(&context.bridge, new_frame->width, new_frame->height, false);
    effects.fail_texture_update = true;
    present_frame(&context, new_frame);
    assert(context.screen.frame->pts == old_frame->pts);
    assert(av_buffer_get_ref_count(old_frame->buf[0]) == 2);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(!strcmp(effects.title, "scrcpy - Reconnecting..."));
    effects.fail_texture_update = false;
    present_frame(&context, new_frame);
    assert(context.screen.frame->pts == new_frame->pts);
    assert(av_buffer_get_ref_count(old_frame->buf[0]) == 1);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(effects.size.width == 640 && effects.size.height == 360);
    assert(effects.position.x == 123 && effects.position.y == 234);
    assert(effects.size_changes == size_changes);
    assert(effects.windows == 1 && effects.renderers == 1);
    assert(effects.captured && effects.readiness == TEST_SECOND_READINESS);
    assert(!strcmp(effects.title, "replacement presentation"));
    av_frame_free(&old_frame);
    av_frame_free(&new_frame);
    destroy_context(&context);
    puts("screen: reconnect retains last frame, geometry, title and window");
}

/** Failed replacement rendering restores the texture and presentation geometry. */
static void
test_failed_reconnect_render(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *replacement = create_frame(TEST_FRAME_WIDTH * 2,
                                        TEST_FRAME_HEIGHT * 2, 2);
    present_frame(&context, displayed);
    struct sc_size previous_frame_size = context.screen.frame_size;
    struct sc_size previous_content_size = context.screen.content_size;
    replace_generation(&context, false);
    open_bridge(&context.bridge, replacement->width, replacement->height, false);
    effects.fail_render = true;
    present_frame(&context, replacement);
    assert(context.screen.frame->pts == displayed->pts);
    assert(effects.texture_identity == displayed->pts);
    assert(context.screen.frame_size.width == previous_frame_size.width);
    assert(context.screen.frame_size.height == previous_frame_size.height);
    assert(context.screen.content_size.width == previous_content_size.width);
    assert(context.screen.content_size.height == previous_content_size.height);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_FIRST_READINESS);
    effects.fail_render = false;
    effects.fail_present = true;
    present_frame(&context, replacement);
    assert(context.screen.frame->pts == displayed->pts);
    assert(effects.texture_identity == displayed->pts);
    assert(context.screen.content_size.width == previous_content_size.width);
    assert(context.screen.content_size.height == previous_content_size.height);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_FIRST_READINESS);
    effects.fail_present = false;
    SDL_Event exposed = {.type = SDL_EVENT_WINDOW_EXPOSED};
    sc_screen_handle_event(&context.screen, &exposed);
    assert(effects.texture_identity == displayed->pts);
    assert(effects.readiness == TEST_FIRST_READINESS);
    present_frame(&context, replacement);
    assert(effects.texture_identity == replacement->pts);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_SECOND_READINESS);
    av_frame_free(&displayed);
    av_frame_free(&replacement);
    destroy_context(&context);
    puts("screen: failed replacement render/present restores previous texture and geometry");
}

/** Paused candidates remain separate and retained resume never emits readiness. */
static void
test_pause_and_reset(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *paused = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 2);
    present_frame(&context, displayed);
    sc_screen_set_paused(&context.screen, true);
    present_frame(&context, paused);
    assert(context.screen.frame->pts == displayed->pts);
    assert(context.screen.resume_frame->pts == paused->pts);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 2);
    assert(av_buffer_get_ref_count(paused->buf[0]) == 2);
    assert(effects.readiness == TEST_FIRST_READINESS);
    sc_screen_set_paused(&context.screen, false);
    assert(context.screen.frame->pts == paused->pts);
    assert(!context.screen.resume_frame && effects.readiness == TEST_FIRST_READINESS);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 1);
    sc_screen_set_paused(&context.screen, true);
    present_frame(&context, displayed);
    sc_screen_clear_pending_frames(&context.screen);
    assert(!context.screen.paused && !context.screen.resume_frame);
    assert(context.screen.frame->pts == paused->pts);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 1);
    av_frame_free(&displayed);
    av_frame_free(&paused);
    destroy_context(&context);
    puts("screen: paused/resume/displayed refs and reconnect reset");
}

/** Failed paused-frame upload must retain the previously successful display ref. */
static void
test_failed_pause_resume(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *paused = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 2);
    present_frame(&context, displayed);
    sc_screen_set_paused(&context.screen, true);
    present_frame(&context, paused);
    effects.fail_texture_update = true;
    sc_screen_set_paused(&context.screen, false);
    assert(context.screen.frame->pts == displayed->pts);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 2);
    assert(effects.readiness == TEST_FIRST_READINESS);
    effects.fail_texture_update = false;
    av_frame_free(&displayed);
    av_frame_free(&paused);
    destroy_context(&context);
    puts("screen: failed retained resume preserves the last successfully displayed ref");
}

/** Failed paused-reference allocation releases only the admitted candidate ref. */
static void
test_paused_allocation_failure(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *paused = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 2);
    present_frame(&context, displayed);
    sc_screen_set_paused(&context.screen, true);
    assert(context.bridge.frame_sink.ops->push(&context.bridge.frame_sink,
                                                paused));
    fail_frame_attempt = allocation_attempts + 1;
    assert(sc_dispatcher_drain(&context.dispatcher));
    assert(context.screen.frame->pts == displayed->pts);
    assert(!context.screen.resume_frame);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 2);
    assert(av_buffer_get_ref_count(paused->buf[0]) == 1);
    assert(effects.readiness == TEST_FIRST_READINESS);
    fail_frame_attempt = 0;
    present_frame(&context, paused);
    assert(context.screen.resume_frame->pts == paused->pts);
    assert(av_buffer_get_ref_count(paused->buf[0]) == 2);
    assert(effects.readiness == TEST_FIRST_READINESS);
    av_frame_free(&displayed);
    av_frame_free(&paused);
    destroy_context(&context);
    puts("screen: failed paused allocation releases candidate and permits later progress");
}

/** Old notifications and sink callbacks cannot consume or reset replacement refs. */
static void
test_late_work_after_replacement(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *displayed = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    AVFrame *old_pending = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 2);
    AVFrame *replacement = create_frame(TEST_FRAME_WIDTH * 2,
                                        TEST_FRAME_HEIGHT * 2, 3);
    present_frame(&context, displayed);
    assert(context.bridge.frame_sink.ops->push(&context.bridge.frame_sink,
                                                old_pending));
    SDL_Event old_wakeup = latest_wakeup;
    struct sc_video_bridge old_bridge = context.bridge;
    memset(&context.bridge, 0, sizeof(context.bridge));
    assert(av_buffer_get_ref_count(old_pending->buf[0]) == 2);
    replace_generation(&context, false);
    assert(av_buffer_get_ref_count(old_pending->buf[0]) == 1);
    open_bridge(&context.bridge, replacement->width, replacement->height, false);
    assert(context.bridge.frame_sink.ops->push(&context.bridge.frame_sink,
                                                replacement));
    struct sc_stream_session stale_metadata = {
        .video = {TEST_FRAME_HEIGHT, TEST_FRAME_WIDTH, true},
    };
    assert(!old_bridge.frame_sink.ops->push_session(&old_bridge.frame_sink,
                                                    &stale_metadata));
    assert(!old_bridge.frame_sink.ops->push(&old_bridge.frame_sink, old_pending));
    old_bridge.frame_sink.ops->close(&old_bridge.frame_sink);
    AVCodecContext old_codec = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = TEST_FRAME_HEIGHT,
        .height = TEST_FRAME_WIDTH,
    };
    assert(!old_bridge.frame_sink.ops->open(&old_bridge.frame_sink, &old_codec,
                                            &stale_metadata));
    assert(av_buffer_get_ref_count(replacement->buf[0]) == 2);
    assert(context.screen.frame->pts == displayed->pts);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_FIRST_READINESS);
    assert(!strcmp(effects.title, "scrcpy - Reconnecting..."));
    assert(sc_dispatcher_get_stats(&context.dispatcher).queued_count == 1);
    // The app-scoped wake may legitimately service current admitted work.
    assert(sc_dispatcher_handle_event(&context.dispatcher, &old_wakeup));
    assert(!sc_dispatcher_get_stats(&context.dispatcher).queued_count);
    assert(context.screen.frame->pts == replacement->pts);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(effects.readiness == TEST_SECOND_READINESS);
    assert(context.screen.frame_size.width == replacement->width);
    assert(context.screen.frame_size.height == replacement->height);
    assert(sc_dispatcher_handle_event(&context.dispatcher, &old_wakeup));
    assert(effects.readiness == TEST_SECOND_READINESS);
    av_frame_free(&displayed);
    av_frame_free(&old_pending);
    av_frame_free(&replacement);
    destroy_context(&context);
    puts("screen: queued old wake/open/frame/metadata/close preserve replacement progress");
}

/** Require prior capture intent, current relative binding and relevant focus. */
static void
test_capture_conditions(void) {
    for (unsigned condition = 0; condition < 3; ++condition) {
        reset_effects();
        struct screen_context context;
        init_context(&context, true, true);
        open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
        AVFrame *frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
        present_frame(&context, frame);

        if (condition == 0) {
            effects.captured = false;
        }

        replace_generation(&context, condition != 1);
        effects.focused = condition != 2;
        open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
        present_frame(&context, frame);
        assert(sc_input_manager_is_ready(&context.screen.im));
        assert(!effects.captured);
        av_frame_free(&frame);
        destroy_context(&context);
    }

    puts("screen: capture restoration respects intent, compatibility and focus");
}

/** Keep supported non-video bindings responsive without waiting for frames. */
static void
test_nonvideo_binding(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, false, true);
    assert(context.screen.window_shown);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(effects.captured && !effects.readiness);
    assert(!effects.watcher);
    destroy_context(&context);
    puts("screen: non-video binding is ready without decoder presentation");
}

/** Keep camera/no-control video presentation independent of remote endpoints. */
static void
test_no_control_camera(void) {
    reset_effects();
    struct screen_context context;
    prepare_context(&context, true, false);
    context.params.controller = NULL;
    context.params.camera = true;
    assert(sc_screen_init(&context.screen, &context.params));
    sc_video_bridge_init(&context.bridge, &context.screen.ingress,
                          context.generation);
    assert(!sc_input_manager_is_ready(&context.screen.im));
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    present_frame(&context, frame);
    assert(context.screen.frame->pts == frame->pts);
    assert(sc_input_manager_is_ready(&context.screen.im));
    assert(!context.screen.im.binding.controller);
    assert(!effects.captured && !effects.resize_requests);
    av_frame_free(&frame);
    destroy_context(&context);
    puts("screen: camera/no-control video presents without remote endpoints");
}

/** Keep publication-time resize metadata independent of later producer updates. */
static void
test_frame_metadata(void) {
    reset_effects();
    struct screen_context context;
    prepare_context(&context, true, false);
    context.params.orientation = SC_ORIENTATION_90;
    assert(sc_screen_init(&context.screen, &context.params));
    sc_video_bridge_init(&context.bridge, &context.screen.ingress,
                          context.generation);
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *initial = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    present_frame(&context, initial);
    assert(context.screen.content_size.width == TEST_FRAME_HEIGHT);
    assert(context.screen.content_size.height == TEST_FRAME_WIDTH);
    assert(effects.size.width == TEST_FRAME_HEIGHT);
    assert(effects.size.height == TEST_FRAME_WIDTH);
    assert(effects.aspect == (float) TEST_FRAME_HEIGHT / TEST_FRAME_WIDTH);
    unsigned size_changes = effects.size_changes;
    AVFrame *resized = create_frame(TEST_FRAME_WIDTH * 2,
                                    TEST_FRAME_HEIGHT * 2, 2);
    struct sc_stream_session metadata = {
        .video = {resized->width, resized->height, true},
    };
    assert(context.bridge.frame_sink.ops->push_session(
        &context.bridge.frame_sink, &metadata));
    assert(context.bridge.frame_sink.ops->push(&context.bridge.frame_sink,
                                                resized));
    metadata.video.client_resized = false;
    assert(context.bridge.frame_sink.ops->push_session(
        &context.bridge.frame_sink, &metadata));
    assert(sc_dispatcher_drain(&context.dispatcher));
    assert(effects.size_changes == size_changes);
    assert(context.screen.content_size.width == resized->height);
    assert(context.screen.content_size.height == resized->width);
    av_frame_free(&initial);
    av_frame_free(&resized);
    destroy_context(&context);
    puts("screen: initial orientation/aspect and publication-time resize metadata");
}

/** Settle a dispatcher when presentation initialization acquired only a prefix. */
static void
destroy_failed_context(struct screen_context *context) {
    assert(sc_dispatcher_generation_revoke(&context->dispatcher,
                                            context->generation));
    assert(sc_dispatcher_shutdown(&context->dispatcher));
    assert(sc_dispatcher_destroy(&context->dispatcher));
    assert(!effects.windows && !effects.renderers && !effects.textures);
    assert(!effects.watcher);
    assert(frame_allocations == frame_releases);
}

/** Fail every acquired-resource stage, including each real frame allocation. */
static void
test_partial_initialization(void) {
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    unsigned frame_attempts = allocation_attempts;
    assert(frame_attempts);
    destroy_context(&context);

    for (unsigned failure = 0; failure < 4; ++failure) {
        reset_effects();
        prepare_context(&context, true, false);
        effects.fail_window = failure == 0;
        effects.fail_renderer = failure == 1;
        effects.fail_texture_init = failure == 2;
        effects.fail_text_input = failure == 3;
        assert(!sc_screen_init(&context.screen, &context.params));
        destroy_failed_context(&context);
    }

    for (unsigned attempt = 1; attempt <= frame_attempts; ++attempt) {
        reset_effects();
        prepare_context(&context, true, false);
        fail_frame_attempt = attempt;
        assert(!sc_screen_init(&context.screen, &context.params));
        destroy_failed_context(&context);
    }

    reset_effects();
    prepare_context(&context, true, false);
    effects.fail_watch = true;
    assert(sc_screen_init(&context.screen, &context.params));
    sc_video_bridge_init(&context.bridge, &context.screen.ingress,
                          context.generation);
    destroy_context(&context);
    assert(!effects.watch_removals);
    puts("screen: partial acquisition unwind and failed-watch destruction");
}

#if defined(_WIN32) || defined(__APPLE__)
/** Invoke the captured actual event watcher on a controlled producer thread. */
static int
watch_from_worker(void *userdata) {
    SDL_Event *event = userdata;
    assert(!SDL_IsMainThread());
    assert(effects.watcher(effects.watcher_context, event));
    return 0;
}
#endif

/** Limit resize effects to this window/main thread and revoke remote delivery. */
static void
test_continuous_resize_watcher(void) {
#if defined(_WIN32) || defined(__APPLE__)
    reset_effects();
    struct screen_context context;
    init_context(&context, true, false);
    assert(effects.watcher && effects.watcher_context == &context.screen);
    context.screen.flex_display = true;
    open_bridge(&context.bridge, TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, false);
    AVFrame *frame = create_frame(TEST_FRAME_WIDTH, TEST_FRAME_HEIGHT, 1);
    present_frame(&context, frame);
    SDL_Event event = {
        .window = {
            .type = SDL_EVENT_WINDOW_RESIZED,
            .windowID = TEST_OTHER_WINDOW_ID,
            .data1 = TEST_FRAME_WIDTH * 2,
            .data2 = TEST_FRAME_HEIGHT * 2,
        },
    };
    unsigned presentations = effects.presentations;
    assert(effects.watcher(effects.watcher_context, &event));
    assert(!effects.resize_requests && effects.presentations == presentations);
    event.window.windowID = TEST_WINDOW_ID;
    sc_thread worker;
    assert(sc_thread_create(&worker, watch_from_worker, "screen-watch", &event));
    sc_thread_join(&worker, NULL);
    assert(!effects.resize_requests && effects.presentations == presentations);
    effects.reenter_watcher = true;
    assert(effects.watcher(effects.watcher_context, &event));
    assert(!effects.reenter_watcher);
    assert(!effects.resize_requests);
    assert(effects.presentations == presentations + 1);
    sc_screen_handle_event(&context.screen, &event);
    assert(effects.resize_requests == 1);
    sc_screen_prepare_reconnect(&context.screen);
    assert(effects.watcher(effects.watcher_context, &event));
    sc_screen_handle_event(&context.screen, &event);
    assert(effects.resize_requests == 1);
    av_frame_free(&frame);
    destroy_context(&context);
    assert(effects.watch_removals == 1);
    puts("screen: watcher identity/thread gate and detach before callback retirement");
#endif
}

/** Run deterministic production-linked presentation cases with real FFmpeg refs. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    test_failed_ready_frame_resize();
    test_render_prerequisites();
    test_presentation_gate();
    test_reconnect_last_frame();
    test_failed_reconnect_render();
    test_pause_and_reset();
    test_failed_pause_resume();
    test_paused_allocation_failure();
    test_late_work_after_replacement();
    test_capture_conditions();
    test_nonvideo_binding();
    test_no_control_camera();
    test_frame_metadata();
    test_partial_initialization();
    test_continuous_resize_watcher();
    SDL_Quit();
    return 0;
}
