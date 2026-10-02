#ifndef SC_EVENTS_H
#define SC_EVENTS_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>
#include <SDL3/SDL_events.h>
#include <SDL3/SDL_init.h>

enum {
    SC_EVENT_NEW_FRAME = SDL_EVENT_USER,
    SC_EVENT_OPEN_WINDOW,
    SC_EVENT_DEVICE_DISCONNECTED,
    SC_EVENT_SERVER_CONNECTION_FAILED,
    SC_EVENT_SERVER_CONNECTED,
    SC_EVENT_DEMUXER_ERROR,
    SC_EVENT_RECORDER_ERROR,
    SC_EVENT_TIME_LIMIT_REACHED,
    SC_EVENT_CONTROLLER_ERROR,
    SC_EVENT_AOA_OPEN_ERROR,
    SC_EVENT_DISCONNECTED_ICON_LOADED,
    SC_EVENT_DISCONNECTED_TIMEOUT,
    // Application-owned wakeups must survive the legacy session event flush.
    SC_EVENT_DISPATCHER_WAKEUP,
};

bool
sc_push_event_impl(uint32_t type, void *ptr, const char *name);

#define sc_push_event(TYPE) sc_push_event_impl(TYPE, NULL, # TYPE)
#define sc_push_event_with_data(TYPE, PTR) sc_push_event_impl(TYPE, PTR, # TYPE)

bool
sc_dequeue_event(uint32_t type, SDL_Event *event);

#endif
