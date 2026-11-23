#ifndef SC_SESSION_H
#define SC_SESSION_H

#include "common.h"
#include <stdbool.h>
#include <stdint.h>
#include "dispatcher.h"
#include "options.h"
#include "recorder.h"
#include "server.h"

#define SC_SESSION_EVENT_WAIT_MS 50
#define SC_SESSION_CAPABILITY_VIDEO 1
#define SC_SESSION_CAPABILITY_AUDIO 2
#define SC_SESSION_CAPABILITY_CONTROL 4

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

/* Main-only: consume new optional losses and detach unavailable control input.
 * A loss is not evidence that the other channels are audibly/visibly ready. */
uint8_t
sc_session_take_degraded(struct sc_session *session);

uint8_t
sc_session_required_capabilities(const struct sc_session *session);

bool
sc_session_take_event(struct sc_session *session, uint32_t *event);

/* Resolve an exact captured-generation wake outside a dispatcher drain lease. */
bool
sc_session_handle_event(struct sc_session *session, const SDL_Event *event,
                        uint32_t *outcome);

void
sc_session_prepare_retirement(struct sc_session *session);

void
sc_session_request_stop(struct sc_session *session);

void
sc_session_join(struct sc_session *session);

/* Exact joined recorder receipt, captured before releasing external resources. */
bool
sc_session_recording_output_opened(const struct sc_session *session);

/* False means no recorder worker was started; do not manufacture a result. */
bool
sc_session_get_recording_result(const struct sc_session *session,
                                 enum sc_recorder_result *result);

/* After join, release external resources off main; keep notification storage. */
void
sc_session_release_resources(struct sc_session *session);

void
sc_session_destroy(struct sc_session *session);

#endif
