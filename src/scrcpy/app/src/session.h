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

enum sc_session_metric {
    SC_SESSION_METRIC_AUDIO_PACKETS = 1u << 0,
    SC_SESSION_METRIC_AUDIO_DECODE = 1u << 1,
    SC_SESSION_METRIC_AUDIO_PLAYBACK = 1u << 2,
    SC_SESSION_METRIC_VIDEO_DECODE = 1u << 3,
    SC_SESSION_METRIC_CONTROL_WRITES = 1u << 4,
};

struct sc_session_metrics {
    uint64_t generation;
    unsigned available; // bitmask of sc_session_metric values
    uint64_t audio_packets_read;
    uint64_t audio_frames_decoded;
    // Samples are PCM sample frames across channels, including submitted silence.
    uint64_t audio_samples_submitted;
    uint64_t audio_underflow_samples;
    uint64_t audio_overflow_samples_dropped;
    uint32_t audio_pcm_peak_samples; // highest producer observation before trim
    uint64_t video_frames_decoded;
    uint64_t control_messages_written;
};

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

/* Read generation totals after all workers and audio callbacks have settled. */
struct sc_session_metrics
sc_session_get_metrics(const struct sc_session *session);

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
