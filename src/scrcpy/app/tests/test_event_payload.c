#include <assert.h>
#include <stdlib.h>

#include <SDL3/SDL.h>

#include "coords.h"
#include "events.h"

// Verify ownership transfer across the SDL event queue used for initial size.
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));

    struct sc_size *size = malloc(sizeof(*size));
    assert(size);
    size->width = 1920;
    size->height = 1080;

    assert(sc_push_event_with_data(SC_EVENT_OPEN_WINDOW, size));

    SDL_Event event;
    assert(sc_dequeue_event(SC_EVENT_OPEN_WINDOW, &event));
    assert(event.user.data1 == size);
    assert(size->width == 1920);
    assert(size->height == 1080);
    free(size);

    assert(!sc_dequeue_event(SC_EVENT_OPEN_WINDOW, &event));
    SDL_Quit();
    return 0;
}
