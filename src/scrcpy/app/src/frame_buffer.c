#include "frame_buffer.h"

#include <assert.h>

#include "util/log.h"

/** Allocate the pending and temporary frame headers without retaining pixels. */
bool
sc_frame_buffer_init(struct sc_frame_buffer *fb) {
    fb->pending_frame = av_frame_alloc();

    if (!fb->pending_frame) {
        LOG_OOM();
        return false;
    }

    fb->tmp_frame = av_frame_alloc();

    if (!fb->tmp_frame) {
        LOG_OOM();
        av_frame_free(&fb->pending_frame);
        return false;
    }

    // there is initially no frame, so consider it has already been consumed
    fb->pending_frame_consumed = true;

    return true;
}

/** Release every frame reference still owned by this mailbox. */
void
sc_frame_buffer_destroy(struct sc_frame_buffer *fb) {
    av_frame_free(&fb->pending_frame);
    av_frame_free(&fb->tmp_frame);
}

/** Exchange frame headers after a successful candidate reference acquisition. */
static inline void
swap_frames(AVFrame **lhs, AVFrame **rhs) {
    AVFrame *tmp = *lhs;
    *lhs = *rhs;
    *rhs = tmp;
}

/** Observe whether the serialized mailbox has an unconsumed publication. */
bool
sc_frame_buffer_has_frame(struct sc_frame_buffer *fb) {
    return !fb->pending_frame_consumed;
}

/** Acquire a candidate ref before exposing it as the latest pending frame. */
static bool
sc_frame_buffer_prepare_publication(struct sc_frame_buffer *fb,
                                     const AVFrame *frame) {
    // Use a temporary frame to preserve pending_frame in case of error.
    // tmp_frame is an empty frame, no need to call av_frame_unref() beforehand.
    int r = av_frame_ref(fb->tmp_frame, frame);

    if (r) {
        LOGE("Could not ref frame: %d", r);
        return false;
    }

    // Now that av_frame_ref() succeeded, we can replace the previous
    // pending_frame
    swap_frames(&fb->pending_frame, &fb->tmp_frame);
    fb->pending_frame_consumed = false;
    return true;
}

/** Publish with immediate replaced-reference release for serialized legacy users. */
bool
sc_frame_buffer_push(struct sc_frame_buffer *fb, const AVFrame *frame) {

    if (!sc_frame_buffer_prepare_publication(fb, frame)) {
        return false;
    }

    av_frame_unref(fb->tmp_frame);
    return true;
}

/** Publish without invoking final buffer release callbacks for the replaced frame. */
bool
sc_frame_buffer_publish(struct sc_frame_buffer *fb, const AVFrame *frame,
                         AVFrame *retired) {

    if (!sc_frame_buffer_prepare_publication(fb, frame)) {
        return false;
    }

    av_frame_move_ref(retired, fb->tmp_frame);
    return true;
}

/** Move a pending reference without releasing buffers, returning false when empty. */
bool
sc_frame_buffer_take(struct sc_frame_buffer *fb, AVFrame *dst) {

    if (!sc_frame_buffer_has_frame(fb)) {
        return false;
    }

    sc_frame_buffer_consume(fb, dst);
    return true;
}

/** Move the pending reference into an already empty consumer-owned frame. */
void
sc_frame_buffer_consume(struct sc_frame_buffer *fb, AVFrame *dst) {
    assert(!fb->pending_frame_consumed);
    fb->pending_frame_consumed = true;

    av_frame_move_ref(dst, fb->pending_frame);
    // av_frame_move_ref() resets its source frame, so no need to call
    // av_frame_unref()
}

/** Discard pending/candidate refs and reset publication state under the owner lock. */
void
sc_frame_buffer_discard(struct sc_frame_buffer *fb) {
    av_frame_unref(fb->pending_frame);
    av_frame_unref(fb->tmp_frame);
    fb->pending_frame_consumed = true;
}
