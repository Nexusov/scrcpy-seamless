#ifndef SC_FRAME_BUFFER_H
#define SC_FRAME_BUFFER_H

#include "common.h"

#include <stdbool.h>
#include <libavutil/frame.h>

// forward declarations
typedef struct AVFrame AVFrame;

/**
 * A frame buffer holds 1 pending frame, which is the last frame received from
 * the producer (typically, the decoder).
 *
 * If a pending frame has not been consumed when the producer pushes a new
 * frame, then it is lost. The intent is to always provide access to the very
 * last frame to minimize latency.
 *
 * The owner serializes every operation. For generation ingress, the same owner
 * lock also protects admission/revocation and metadata attached to the pending
 * frame. This mailbox does not provide an independent synchronization boundary.
 * Only these APIs may access its fields outside this module.
 *
 * Publication takes one reference to a refcounted decoded frame, without taking
 * ownership of the producer's input frame. A failed reference preserves the old
 * pending frame. The temporary candidate is empty between calls. Consumption
 * moves the pending reference into an empty destination; discard releases only
 * mailbox references. Displayed and paused/resume references belong to consumers.
 * Publication may move the replaced frame into an empty retired destination, so
 * the caller can release its references after unlocking. Every frame header,
 * including a retired destination, must be allocated with av_frame_alloc().
 */

struct sc_frame_buffer {
    AVFrame *pending_frame;
    AVFrame *tmp_frame; // To preserve the pending frame on error

    bool pending_frame_consumed;
};

bool
sc_frame_buffer_init(struct sc_frame_buffer *fb);

void
sc_frame_buffer_destroy(struct sc_frame_buffer *fb);

bool
sc_frame_buffer_has_frame(struct sc_frame_buffer *fb);

bool
sc_frame_buffer_push(struct sc_frame_buffer *fb, const AVFrame *frame);

// Publish without releasing the replaced reference; retired must be empty.
bool
sc_frame_buffer_publish(struct sc_frame_buffer *fb, const AVFrame *frame,
                         AVFrame *retired);

// The destination must already be empty (av_frame_unref() if needed).
void
sc_frame_buffer_consume(struct sc_frame_buffer *fb, AVFrame *dst);

// Move only when a frame is available; dst must be empty even on an empty take.
bool
sc_frame_buffer_take(struct sc_frame_buffer *fb, AVFrame *dst);

// Reset an initialized mailbox to empty; repeated discard is harmless.
void
sc_frame_buffer_discard(struct sc_frame_buffer *fb);

#endif
