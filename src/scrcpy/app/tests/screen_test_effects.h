#ifndef SC_SCREEN_TEST_EFFECTS_H
#define SC_SCREEN_TEST_EFFECTS_H

#include <SDL3/SDL.h>

/* Test-only external effects; presentation and input ownership remain production. */
#define SDL_CreateRenderer sc_screen_test_create_renderer
#define SDL_GetRendererName sc_screen_test_renderer_name
#define SDL_GL_DestroyContext sc_screen_test_destroy_gl_context
#define SDL_DestroyRenderer sc_screen_test_destroy_renderer
#define SDL_DestroyWindow sc_screen_test_destroy_window
#define SDL_GetPrimaryDisplay sc_screen_test_primary_display
#define SDL_GetDisplayUsableBounds sc_screen_test_display_bounds
#define SDL_GetWindowFlags sc_screen_test_window_flags
#define SDL_GetWindowID sc_screen_test_window_id
#define SDL_GetWindowPixelDensity sc_screen_test_pixel_density
#define SDL_GetKeyboardFocus sc_screen_test_keyboard_focus
#define SDL_SetRenderDrawColor sc_screen_test_draw_color
#define SDL_RenderTexture sc_screen_test_render_texture
#define SDL_RenderTextureRotated sc_screen_test_render_rotated
#define SDL_SetWindowAspectRatio sc_screen_test_aspect_ratio
#define SDL_SetWindowFullscreen sc_screen_test_fullscreen
#define SDL_SetWindowTitle sc_screen_test_title
#define SDL_SetWindowIcon sc_screen_test_icon
#define SDL_StartTextInput sc_screen_test_start_text
#define SDL_AddEventWatch sc_screen_test_add_watch
#define SDL_RemoveEventWatch sc_screen_test_remove_watch

SDL_Renderer *sc_screen_test_create_renderer(SDL_Window *window,
                                              const char *name);
const char *sc_screen_test_renderer_name(SDL_Renderer *renderer);
bool sc_screen_test_destroy_gl_context(SDL_GLContext context);
void sc_screen_test_destroy_renderer(SDL_Renderer *renderer);
void sc_screen_test_destroy_window(SDL_Window *window);
SDL_DisplayID sc_screen_test_primary_display(void);
bool sc_screen_test_display_bounds(SDL_DisplayID display, SDL_Rect *rect);
SDL_WindowFlags sc_screen_test_window_flags(SDL_Window *window);
SDL_WindowID sc_screen_test_window_id(SDL_Window *window);
float sc_screen_test_pixel_density(SDL_Window *window);
SDL_Window *sc_screen_test_keyboard_focus(void);
bool sc_screen_test_draw_color(SDL_Renderer *renderer, Uint8 red, Uint8 green,
                               Uint8 blue, Uint8 alpha);
bool sc_screen_test_render_texture(SDL_Renderer *renderer, SDL_Texture *texture,
                                   const SDL_FRect *source,
                                   const SDL_FRect *destination);
bool sc_screen_test_render_rotated(SDL_Renderer *renderer, SDL_Texture *texture,
                                   const SDL_FRect *source,
                                   const SDL_FRect *destination, double angle,
                                   const SDL_FPoint *center, SDL_FlipMode flip);
bool sc_screen_test_aspect_ratio(SDL_Window *window, float minimum,
                                 float maximum);
bool sc_screen_test_fullscreen(SDL_Window *window, bool fullscreen);
bool sc_screen_test_title(SDL_Window *window, const char *title);
bool sc_screen_test_icon(SDL_Window *window, SDL_Surface *icon);
bool sc_screen_test_start_text(SDL_Window *window);
bool sc_screen_test_add_watch(SDL_EventFilter callback, void *userdata);
void sc_screen_test_remove_watch(SDL_EventFilter callback, void *userdata);

#endif
