#include "events.h"

#include "util/log.h"

bool
sc_push_event_impl(uint32_t type, void *ptr, const char *name) {
    SDL_Event event = {
        .user = {
            .type = type,
            .data1 = ptr,
        }
    };
    bool ok = SDL_PushEvent(&event);
    if (!ok) {
        LOGE("Could not post %s event: %s", name, SDL_GetError());
        return false;
    }

    return true;
}

// Dequeue one event so an owned payload can be released during teardown.
bool
sc_dequeue_event(uint32_t type, SDL_Event *event) {
    return SDL_PeepEvents(event, 1, SDL_GETEVENT, type, type) == 1;
}
