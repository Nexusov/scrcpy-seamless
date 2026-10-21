#ifndef SC_SESSION_H
#define SC_SESSION_H

#include "common.h"
#include <stdbool.h>
#include <stdint.h>
#include "dispatcher.h"
#include "options.h"
#include "server.h"

#define SC_SESSION_EVENT_WAIT_MS 50

struct sc_screen;
struct sc_session;

/* Stable address from create until request-stop/join/destroy have settled. */
struct sc_session *
sc_session_create(const struct scrcpy_options *options,
                  struct sc_dispatcher *dispatcher, struct sc_screen *screen,
                  bool *screen_initialized);

bool
sc_session_start_server(struct sc_session *session,
                        const struct sc_server_params *params);

bool
sc_session_start(struct sc_session *session);

/* True only when startup itself observed Stop before an acquisition/start. */
bool
sc_session_startup_interrupted(const struct sc_session *session);

bool
sc_session_take_event(struct sc_session *session, uint32_t *event);

/* Resolve an exact captured-generation wake outside a dispatcher drain lease. */
bool
sc_session_handle_event(struct sc_session *session, const SDL_Event *event,
                        uint32_t *outcome);

void
sc_session_request_stop(struct sc_session *session);

void
sc_session_join(struct sc_session *session);

void
sc_session_destroy(struct sc_session *session);

#endif
