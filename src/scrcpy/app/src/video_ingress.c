#include "video_ingress.h"

#include <assert.h>
#include <limits.h>
#include <stdlib.h>
#include <string.h>

#include "util/log.h"

struct sc_video_notification {
    struct sc_video_ingress *ingress;
    sc_dispatcher_generation generation;
    uint64_t ticket;
    AVFrame *retired_frame;
};

/** Clear only this notification's claim, including failure after admission. */
static void
sc_video_notification_release(struct sc_video_notification *notification,
                               bool discard) {
    struct sc_video_ingress *ingress = notification->ingress;
    sc_mutex_lock(&ingress->mutex);

    if (ingress->generation == notification->generation &&
            ingress->ticket == notification->ticket) {
        if (discard && ingress->notification_pending) {
            sc_frame_buffer_take(&ingress->mailbox, notification->retired_frame);
            ingress->skipped = 0;
        }
        ingress->notification_pending = false;
    }

    sc_mutex_unlock(&ingress->mutex);
    av_frame_unref(notification->retired_frame);
}

/** Resolve presentation only after dispatcher generation validation. */
static void
sc_video_notification_run(void *binding, void *payload) {
    struct sc_video_notification *notification = payload;
    sc_video_notification_release(notification, false);
    notification->ingress->present(binding, notification->ingress,
                                     notification->generation);
}

/** Retire the owned record independently of any generation-owned bridge. */
static void
sc_video_notification_destroy(void *payload) {
    struct sc_video_notification *notification = payload;
    sc_video_notification_release(notification, true);
    av_frame_free(&notification->retired_frame);
    free(notification);
}

/** Initialize mailbox storage with complete partial-failure unwind. */
bool
sc_video_ingress_init(struct sc_video_ingress *ingress,
                      struct sc_dispatcher *dispatcher,
                      sc_video_present_fn present) {
    memset(ingress, 0, sizeof(*ingress));

    if (!sc_mutex_init(&ingress->mutex)) {
        return false;
    }

    if (!sc_frame_buffer_init(&ingress->mailbox)) {
        sc_mutex_destroy(&ingress->mutex);
        return false;
    }
    ingress->retired_frame = av_frame_alloc();
    if (!ingress->retired_frame) {
        sc_frame_buffer_destroy(&ingress->mailbox);
        sc_mutex_destroy(&ingress->mutex);
        return false;
    }

    ingress->dispatcher = dispatcher;
    ingress->present = present;
    return true;
}

/** Destroy only after revocation, notification settlement and producer join. */
void
sc_video_ingress_destroy(struct sc_video_ingress *ingress) {
    assert(!ingress->generation);
    sc_frame_buffer_destroy(&ingress->mailbox);
    av_frame_free(&ingress->retired_frame);
    sc_mutex_destroy(&ingress->mutex);
}

/** Bind the existing allocator's value, with no independent generation counter. */
void
sc_video_ingress_bind(struct sc_video_ingress *ingress,
                      sc_dispatcher_generation generation) {
    assert(sc_thread_is_main());
    assert(generation);
    sc_mutex_lock(&ingress->mutex);
    assert(!ingress->generation);
    ingress->generation = generation;
    ingress->notification_pending = false;
    ingress->skipped = 0;
    sc_mutex_unlock(&ingress->mutex);
    LOGD("Video ingress bound generation=%" PRIu64, generation);
}

/** Serialize admission closure and mailbox reset before destination release. */
void
sc_video_ingress_revoke(struct sc_video_ingress *ingress,
                        sc_dispatcher_generation generation) {
    assert(sc_thread_is_main());
    sc_mutex_lock(&ingress->mutex);
    bool revoked = ingress->generation == generation;

    if (revoked) {
        ingress->generation = 0;
        ingress->notification_pending = false;
        ingress->skipped = 0;
        sc_frame_buffer_take(&ingress->mailbox, ingress->retired_frame);
    }

    sc_mutex_unlock(&ingress->mutex);
    av_frame_unref(ingress->retired_frame);

    if (revoked) {
        LOGD("Video ingress revoked generation=%" PRIu64, generation);
    }
}

/** Move the frame and its publication-time metadata as one protected operation. */
bool
sc_video_ingress_consume(struct sc_video_ingress *ingress,
                         sc_dispatcher_generation generation, AVFrame *frame,
                         struct sc_stream_session *metadata,
                         unsigned *skipped) {
    sc_mutex_lock(&ingress->mutex);
    bool available = generation && ingress->generation == generation &&
                     sc_frame_buffer_has_frame(&ingress->mailbox);

    if (available) {
        sc_frame_buffer_consume(&ingress->mailbox, frame);
        *metadata = ingress->metadata;
        *skipped = ingress->skipped;
        ingress->skipped = 0;
    }

    sc_mutex_unlock(&ingress->mutex);
    return available;
}

/** Publish the latest reference, then admit at most one pending notification. */
static bool
sc_video_bridge_push(struct sc_frame_sink *sink, const AVFrame *frame) {
    struct sc_video_bridge *bridge =
        container_of(sink, struct sc_video_bridge, frame_sink);
    struct sc_video_ingress *ingress = bridge->ingress;

    if (!frame->buf[0] || frame->format != AV_PIX_FMT_YUV420P ||
            frame->width <= 0 || frame->width > UINT16_MAX ||
            frame->height <= 0 || frame->height > UINT16_MAX) {
        return false;
    }

    sc_mutex_lock(&ingress->mutex);

    if (!bridge->open || ingress->generation != bridge->generation) {
        sc_mutex_unlock(&ingress->mutex);
        return false;
    }

    bool previous_pending = sc_frame_buffer_has_frame(&ingress->mailbox);
    bool published = sc_frame_buffer_publish(&ingress->mailbox, frame,
                                              bridge->retired_frame);

    if (!published) {
        sc_mutex_unlock(&ingress->mutex);
        return false;
    }

    ingress->metadata = bridge->metadata;

    if (previous_pending && ingress->skipped != UINT_MAX) {
        ++ingress->skipped;
    }

    if (ingress->notification_pending) {
        sc_mutex_unlock(&ingress->mutex);
        av_frame_unref(bridge->retired_frame);
        return true;
    }

    if (ingress->ticket == UINT64_MAX) {
        sc_mutex_unlock(&ingress->mutex);
        av_frame_unref(bridge->retired_frame);
        sc_mutex_lock(&ingress->mutex);

        if (ingress->generation == bridge->generation &&
                ingress->ticket == UINT64_MAX) {
            sc_frame_buffer_take(&ingress->mailbox, bridge->retired_frame);
            ingress->skipped = 0;
        }
        sc_mutex_unlock(&ingress->mutex);
        av_frame_unref(bridge->retired_frame);
        return false;
    }
    uint64_t ticket = ++ingress->ticket;
    ingress->notification_pending = true;
    sc_mutex_unlock(&ingress->mutex);
    av_frame_unref(bridge->retired_frame);

    struct sc_video_notification *notification = malloc(sizeof(*notification));
    AVFrame *retired = notification ? av_frame_alloc() : NULL;

    if (!notification || !retired) {
        free(notification);
        struct sc_video_notification failed = {
            .ingress = ingress, .generation = bridge->generation,
            .ticket = ticket, .retired_frame = bridge->retired_frame,
        };
        sc_video_notification_release(&failed, true);
        LOG_OOM();
        return false;
    }

    *notification = (struct sc_video_notification) {
        .ingress = ingress,
        .generation = bridge->generation,
        .ticket = ticket,
        .retired_frame = retired,
    };

    struct sc_dispatcher_task task = {
        .run = sc_video_notification_run,
        .destroy = sc_video_notification_destroy,
        .payload = notification,
        .payload_bytes = sizeof(*notification) + sizeof(*retired),
    };
    enum sc_dispatcher_admission admission =
        sc_dispatcher_post(ingress->dispatcher, bridge->generation, &task, NULL);

    if (admission != SC_DISPATCHER_ACCEPTED) {
        sc_video_notification_destroy(notification);
        // A dropped notification releases its frame; later frames can progress.
        return true;
    }

    // Accepted may already have retired after a failed wake; never inspect it.
    return true;
}

/** Validate producer-local open state without mutating presentation. */
static bool
sc_video_bridge_open(struct sc_frame_sink *sink, const AVCodecContext *context,
                      const struct sc_stream_session *metadata) {
    struct sc_video_bridge *bridge =
        container_of(sink, struct sc_video_bridge, frame_sink);
    bool valid = !bridge->open && metadata &&
                 metadata->video.width > 0 &&
                 metadata->video.width <= UINT16_MAX &&
                 metadata->video.height > 0 &&
                 metadata->video.height <= UINT16_MAX &&
                 context->pix_fmt == AV_PIX_FMT_YUV420P &&
                 context->width > 0 && context->width <= UINT16_MAX &&
                 context->height > 0 && context->height <= UINT16_MAX;

    if (!valid) {
        return false;
    }

    sc_mutex_lock(&bridge->ingress->mutex);
    bool current = bridge->ingress->generation == bridge->generation;
    sc_mutex_unlock(&bridge->ingress->mutex);

    if (!current) {
        return false;
    }

    bridge->metadata = *metadata;
    bridge->retired_frame = av_frame_alloc();
    if (!bridge->retired_frame) {
        return false;
    }
    bridge->open = true;
    return true;
}

/** Close only producer-local state; an old close never resets app ingress. */
static void
sc_video_bridge_close(struct sc_frame_sink *sink) {
    struct sc_video_bridge *bridge =
        container_of(sink, struct sc_video_bridge, frame_sink);
    bridge->open = false;
    av_frame_free(&bridge->retired_frame);
}

/** Attach metadata to future frames rather than asynchronous presentation state. */
static bool
sc_video_bridge_push_session(struct sc_frame_sink *sink,
                              const struct sc_stream_session *metadata) {
    struct sc_video_bridge *bridge =
        container_of(sink, struct sc_video_bridge, frame_sink);
    sc_mutex_lock(&bridge->ingress->mutex);
    bool current = bridge->ingress->generation == bridge->generation;
    sc_mutex_unlock(&bridge->ingress->mutex);

    bool valid = metadata && metadata->video.width > 0 &&
                 metadata->video.width <= UINT16_MAX &&
                 metadata->video.height > 0 &&
                 metadata->video.height <= UINT16_MAX;

    if (!bridge->open || !current || !valid) {
        return false;
    }

    bridge->metadata = *metadata;
    return true;
}

/** Capture the generation before attaching this bridge to any producer. */
void
sc_video_bridge_init(struct sc_video_bridge *bridge,
                     struct sc_video_ingress *ingress,
                     sc_dispatcher_generation generation) {
    static const struct sc_frame_sink_ops operations = {
        .open = sc_video_bridge_open,
        .close = sc_video_bridge_close,
        .push = sc_video_bridge_push,
        .push_session = sc_video_bridge_push_session,
    };
    *bridge = (struct sc_video_bridge) {
        .frame_sink.ops = &operations,
        .ingress = ingress,
        .generation = generation,
    };
}
