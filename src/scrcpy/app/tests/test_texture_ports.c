#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <libavutil/frame.h>
#include <SDL3/SDL.h>

#include "texture.h"

enum { PLANE_COUNT = 3, FRAME_WIDTH = 64, FRAME_HEIGHT = 32,
       FIRST_TEXTURE_ID = 11 };
static int fake_renderer;
static int fake_texture;
static SDL_PropertiesID borrowed_properties;
static unsigned created_textures;
static unsigned destroyed_textures;
static unsigned borrowed_destroy_attempts;
static unsigned generated_mipmaps;
static unsigned filter_parameters;
static GLuint bound_texture;
static bool owner_closing;
static bool fail_properties;
static bool fail_chroma_id;
static bool fail_update;
static bool use_opengles;
static SDL_Colorspace actual_colorspace;

const char *__wrap_SDL_GetRendererName(SDL_Renderer *renderer);
SDL_Texture *__wrap_SDL_CreateTextureWithProperties(SDL_Renderer *renderer,
                                                    SDL_PropertiesID properties);
SDL_PropertiesID __wrap_SDL_GetTextureProperties(SDL_Texture *texture);
void __wrap_SDL_DestroyProperties(SDL_PropertiesID properties);
void __wrap_SDL_DestroyTexture(SDL_Texture *texture);
bool __wrap_SDL_UpdateYUVTexture(SDL_Texture *texture, const SDL_Rect *rectangle,
                               const Uint8 *y_plane, int y_pitch,
                               const Uint8 *u_plane, int u_pitch,
                               const Uint8 *v_plane, int v_pitch);

const char *
__wrap_SDL_GetRendererName(SDL_Renderer *renderer) {
    assert(renderer == (SDL_Renderer *) &fake_renderer);
    return use_opengles ? "opengles2" : "opengl";
}

SDL_Texture *
__wrap_SDL_CreateTextureWithProperties(SDL_Renderer *renderer,
                                      SDL_PropertiesID properties) {
    assert(renderer == (SDL_Renderer *) &fake_renderer);
    assert(!borrowed_properties);
    actual_colorspace = SDL_GetNumberProperty(properties,
                             SDL_PROP_TEXTURE_CREATE_COLORSPACE_NUMBER, 0);
    borrowed_properties = SDL_CreateProperties();
    assert(borrowed_properties);
    const char *keys[] = {
        SDL_PROP_TEXTURE_OPENGL_TEXTURE_NUMBER,
        SDL_PROP_TEXTURE_OPENGL_TEXTURE_U_NUMBER,
        SDL_PROP_TEXTURE_OPENGL_TEXTURE_V_NUMBER,
    };
    const char *es_keys[] = {
        SDL_PROP_TEXTURE_OPENGLES2_TEXTURE_NUMBER,
        SDL_PROP_TEXTURE_OPENGLES2_TEXTURE_U_NUMBER,
        SDL_PROP_TEXTURE_OPENGLES2_TEXTURE_V_NUMBER,
    };

    for (unsigned index = 0; index < PLANE_COUNT; ++index) {
        int64_t texture_id = fail_chroma_id && index == 1
                          ? 0 : FIRST_TEXTURE_ID + index;
        assert(SDL_SetNumberProperty(borrowed_properties,
                         use_opengles ? es_keys[index] : keys[index], texture_id));
    }
    ++created_textures;
    return (SDL_Texture *) &fake_texture;
}

SDL_PropertiesID
__wrap_SDL_GetTextureProperties(SDL_Texture *texture) {
    assert(texture == (SDL_Texture *) &fake_texture);
    return fail_properties ? 0 : borrowed_properties;
}

void __real_SDL_DestroyProperties(SDL_PropertiesID properties);

/** Detect premature release without letting the double invalidate test storage. */
void
__wrap_SDL_DestroyProperties(SDL_PropertiesID properties) {

    if (properties == borrowed_properties && !owner_closing) {
        ++borrowed_destroy_attempts;
        return;
    }
    __real_SDL_DestroyProperties(properties);
}

void
__wrap_SDL_DestroyTexture(SDL_Texture *texture) {
    assert(texture == (SDL_Texture *) &fake_texture);
    assert(borrowed_properties);
    owner_closing = true;
    SDL_DestroyProperties(borrowed_properties);
    owner_closing = false;
    borrowed_properties = 0;
    ++destroyed_textures;
}

bool
__wrap_SDL_UpdateYUVTexture(SDL_Texture *texture, const SDL_Rect *rectangle,
                          const Uint8 *y_plane, int y_pitch,
                          const Uint8 *u_plane, int u_pitch,
                          const Uint8 *v_plane, int v_pitch) {
    assert(texture == (SDL_Texture *) &fake_texture && !rectangle);
    assert(y_plane && u_plane && v_plane && y_pitch && u_pitch && v_pitch);
    return !fail_update;
}

static void
bind_texture(GLenum target, GLuint texture) {
    assert(target == GL_TEXTURE_2D);
    bound_texture = texture;
}

static void
set_integer(GLenum target, GLenum name, GLint value) {
    assert(target == GL_TEXTURE_2D && name == GL_TEXTURE_MIN_FILTER);
    assert(value == GL_LINEAR_MIPMAP_LINEAR && bound_texture);
    ++filter_parameters;
}

static void
set_float(GLenum target, GLenum name, GLfloat value) {
    assert(target == GL_TEXTURE_2D && name == GL_TEXTURE_LOD_BIAS);
    assert(value == -1.f && bound_texture);
}

static void
generate_mipmap(GLenum target) {
    assert(target == GL_TEXTURE_2D);
    assert(bound_texture == FIRST_TEXTURE_ID + generated_mipmaps);
    ++generated_mipmaps;
}

void
sc_opengl_init(struct sc_opengl *gl) {
    *gl = (struct sc_opengl) {
        .version = "controlled test backend", .BindTexture = bind_texture,
        .TexParameteri = set_integer, .TexParameterf = set_float,
        .GenerateMipmap = generate_mipmap,
    };
}

bool
sc_opengl_version_at_least(struct sc_opengl *gl, int major, int minor,
                          int es_major, int es_minor) {
    (void) gl;
    assert(major == 3 && minor == 0 && es_major == 2 && es_minor == 0);
    return true;
}

/** Exercise actual texture creation/update/release with real frame references. */
static void
test_frame(const char *mode, enum AVColorSpace colorspace,
           enum AVColorRange range, bool mipmaps) {
    assert(!borrowed_properties);
    created_textures = destroyed_textures = borrowed_destroy_attempts = 0;
    generated_mipmaps = filter_parameters = 0;
    struct sc_texture texture;
    assert(sc_texture_init(&texture, (SDL_Renderer *) &fake_renderer, mipmaps));
    AVFrame *frame = av_frame_alloc();
    assert(frame);
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = FRAME_WIDTH;
    frame->height = FRAME_HEIGHT;
    frame->colorspace = colorspace;
    frame->color_range = range;
    assert(!av_frame_get_buffer(frame, 0));
    bool accepted = sc_texture_set_from_frame(&texture, frame);

    if (fail_properties || fail_chroma_id || fail_update) {
        assert(!accepted);
    } else {
        assert(accepted);

        if (!strcmp(mode, "ownership")) {
            assert(!borrowed_destroy_attempts);
        }

        if (!strcmp(mode, "planes")) {
            assert(filter_parameters == PLANE_COUNT);
            assert(generated_mipmaps == PLANE_COUNT);
        }

        if (!strcmp(mode, "colorspace")) {
            SDL_Colorspace expected = range == AVCOL_RANGE_JPEG
                ? SDL_COLORSPACE_BT709_FULL : SDL_COLORSPACE_BT709_LIMITED;
            assert(actual_colorspace == expected);
        }
    }
    av_frame_free(&frame);
    sc_texture_destroy(&texture);
    assert(created_textures == destroyed_textures && !borrowed_properties);
    assert(!borrowed_destroy_attempts);
}

int
main(int argument_count, char **arguments) {
    assert(SDL_Init(SDL_INIT_EVENTS));

    if (argument_count == 2) {
        bool color = !strcmp(arguments[1], "colorspace");
        test_frame(arguments[1], AVCOL_SPC_UNSPECIFIED, AVCOL_RANGE_JPEG, !color);
    } else {
        test_frame("ownership", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
        test_frame("planes", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
        use_opengles = true;
        test_frame("planes", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
        use_opengles = false;
        test_frame("colorspace", AVCOL_SPC_UNSPECIFIED, AVCOL_RANGE_JPEG, false);
        test_frame("colorspace", AVCOL_SPC_UNSPECIFIED, AVCOL_RANGE_MPEG, false);
        test_frame("colorspace", AVCOL_SPC_YCGCO, AVCOL_RANGE_JPEG, false);
        test_frame("colorspace", AVCOL_SPC_YCGCO, AVCOL_RANGE_MPEG, false);
        fail_properties = true;
        test_frame("failure", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
        fail_properties = false;
        fail_chroma_id = true;
        test_frame("failure", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
        fail_chroma_id = false;
        fail_update = true;
        test_frame("failure", AVCOL_SPC_BT709, AVCOL_RANGE_MPEG, true);
    }
    SDL_Quit();
    puts("Texture ports: production-linked checks passed (10 cases in full mode)");
    return 0;
}
