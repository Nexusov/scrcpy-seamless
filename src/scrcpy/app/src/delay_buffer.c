#include "delay_buffer.h"

#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <libavcodec/avcodec.h>

#include "util/log.h"

/** Downcast frame_sink to sc_delay_buffer */
#define DOWNCAST(SINK) container_of(SINK, struct sc_delay_buffer, frame_sink)

static bool
sc_delay_buffer_charge_add(size_t *charge, size_t bytes) {

    if (bytes > SC_DELAY_BUFFER_MAX_RETAINED_BYTES - *charge) {
        return false;
    }

    *charge += bytes;
    return true;
}

static bool
sc_delay_buffer_charge_buffer(size_t *charge, const AVBufferRef *buffer) {
    return !buffer || (sc_delay_buffer_charge_add(charge, sizeof(*buffer))
        && sc_delay_buffer_charge_add(charge, buffer->size));
}

static bool
sc_delay_buffer_charge_dictionary(size_t *charge, const AVDictionary *dictionary) {
    const AVDictionaryEntry *entry = NULL;

    while ((entry = av_dict_get(dictionary, "", entry, AV_DICT_IGNORE_SUFFIX))) {

        if (!sc_delay_buffer_charge_add(charge, sizeof(*entry))
                || !sc_delay_buffer_charge_add(charge, strlen(entry->key) + 1)
                || !sc_delay_buffer_charge_add(charge, strlen(entry->value) + 1)) {
            return false;
        }
    }

    return true;
}

/** Charge complete visible buffers even when multiple references alias them. */
static bool
sc_delay_buffer_frame_charge(const AVFrame *frame, size_t *charge) {
    *charge = sizeof(struct sc_delayed_packet) + sizeof(AVFrame);

    // Production decoder frames are reference-counted; never guess copy sizes.
    if (!frame->buf[0] || frame->nb_extended_buf < 0 || frame->nb_side_data < 0) {
        return false;
    }

    for (unsigned index = 0; index < AV_NUM_DATA_POINTERS; ++index) {

        if (!sc_delay_buffer_charge_buffer(charge, frame->buf[index])) {
            return false;
        }
    }

    for (int index = 0; index < frame->nb_extended_buf; ++index) {

        if (!sc_delay_buffer_charge_add(charge, sizeof(AVBufferRef *))
                || !sc_delay_buffer_charge_buffer(charge,
                                                   frame->extended_buf[index])) {
            return false;
        }
    }

    for (int index = 0; index < frame->nb_side_data; ++index) {
        const AVFrameSideData *side = frame->side_data[index];

        if (!sc_delay_buffer_charge_add(charge, sizeof(*side) + sizeof(side))
                || !(side->buf ? sc_delay_buffer_charge_buffer(charge, side->buf)
                    : sc_delay_buffer_charge_add(charge, side->size))
                || !sc_delay_buffer_charge_dictionary(charge, side->metadata)) {
            return false;
        }
    }

    if (frame->extended_data != frame->data
            && !sc_delay_buffer_charge_add(charge,
                (size_t) frame->ch_layout.nb_channels * sizeof(uint8_t *))) {
        return false;
    }

    return sc_delay_buffer_charge_buffer(charge, frame->opaque_ref)
        && sc_delay_buffer_charge_buffer(charge, frame->hw_frames_ctx)
        && sc_delay_buffer_charge_dictionary(charge, frame->metadata);
}

static bool
sc_delay_buffer_reserve(struct sc_delay_buffer *db, size_t charge) {
    sc_mutex_lock(&db->mutex);

    if (db->stopped) {
        sc_mutex_unlock(&db->mutex);
        return false;
    }

    if (db->retained_items == SC_DELAY_BUFFER_MAX_RETAINED_ITEMS
            || charge > SC_DELAY_BUFFER_MAX_RETAINED_BYTES - db->retained_bytes) {
        db->stopped = true;
        sc_cond_signal(&db->queue_cond);
        sc_cond_signal(&db->wait_cond);
        sc_mutex_unlock(&db->mutex);
        LOGE("Delay buffer retained-memory limit reached");
        return false;
    }

    ++db->retained_items;
    db->retained_bytes += charge;
    sc_mutex_unlock(&db->mutex);
    return true;
}

static void
sc_delay_buffer_release_charge(struct sc_delay_buffer *db, size_t charge) {
    sc_mutex_lock(&db->mutex);
    assert(db->retained_items && db->retained_bytes >= charge);
    --db->retained_items;
    db->retained_bytes -= charge;
    sc_mutex_unlock(&db->mutex);
}

static bool
sc_delayed_packet_init_frame(struct sc_delayed_packet *dpacket,
                             const AVFrame *frame) {
    dpacket->type = SC_DELAYED_PACKET_TYPE_FRAME;
    dpacket->asap = false;
    dpacket->frame = av_frame_alloc();
    if (!dpacket->frame) {
        LOG_OOM();
        return false;
    }

    if (av_frame_ref(dpacket->frame, frame)) {
        LOG_OOM();
        av_frame_free(&dpacket->frame);
        return false;
    }

    return true;
}

static void
sc_delayed_packet_init_session(struct sc_delayed_packet *dpacket,
                               const struct sc_stream_session *session) {
    dpacket->type = SC_DELAYED_PACKET_TYPE_SESSION;
    dpacket->asap = false;
    dpacket->session = *session;
}

static void
sc_delayed_packet_destroy(struct sc_delayed_packet *dpacket) {
    if (dpacket->type == SC_DELAYED_PACKET_TYPE_FRAME) {
        av_frame_unref(dpacket->frame);
        av_frame_free(&dpacket->frame);
    }
}

static void
sc_delay_buffer_release(struct sc_delay_buffer *db,
                         struct sc_delayed_packet *packet) {
    sc_delayed_packet_destroy(packet);
    sc_delay_buffer_release_charge(db, packet->charge);
}

static int
run_buffering(void *data) {
    struct sc_delay_buffer *db = data;

    assert(db->delay > 0);

    for (;;) {
        sc_mutex_lock(&db->mutex);

        while (!db->stopped && sc_vecdeque_is_empty(&db->queue)) {
            sc_cond_wait(&db->queue_cond, &db->mutex);
        }

        if (db->stopped) {
            sc_mutex_unlock(&db->mutex);
            goto stopped;
        }

        struct sc_delayed_packet dpacket = sc_vecdeque_pop(&db->queue);

        bool ok;
        if (dpacket.type == SC_DELAYED_PACKET_TYPE_FRAME) {
            sc_tick max_deadline = sc_tick_now() + db->delay;
            // PTS (written by the server) are expressed in microseconds
            sc_tick pts = SC_TICK_FROM_US(dpacket.frame->pts);

            bool timed_out = false;
            while (!db->stopped && !dpacket.asap && !timed_out) {
                sc_tick deadline = sc_clock_to_system_time(&db->clock, pts)
                                 + db->delay;
                if (deadline > max_deadline) {
                    deadline = max_deadline;
                }

                timed_out =
                    !sc_cond_timedwait(&db->wait_cond, &db->mutex, deadline);
            }

            bool stopped = db->stopped;
            sc_mutex_unlock(&db->mutex);

            if (stopped) {
                sc_delay_buffer_release(db, &dpacket);
                goto stopped;
            }

#ifdef SC_BUFFERING_DEBUG
            LOGD("Buffering: %" PRItick ";%" PRItick ";%" PRItick,
                 pts, dpacket.push_date, sc_tick_now());
#endif

            ok = sc_frame_source_sinks_push(&db->frame_source, dpacket.frame);
        } else {
            assert(dpacket.type == SC_DELAYED_PACKET_TYPE_SESSION);
            sc_mutex_unlock(&db->mutex);
            ok = sc_frame_source_sinks_push_session(&db->frame_source,
                                                    &dpacket.session);
        }

        sc_delay_buffer_release(db, &dpacket);
        if (!ok) {
            LOGE("Delayed packet could not be pushed, stopping");
            sc_mutex_lock(&db->mutex);
            // Prevent to push any new packet
            db->stopped = true;
            sc_mutex_unlock(&db->mutex);
            goto stopped;
        }
    }

stopped:
    sc_mutex_lock(&db->mutex);
    assert(db->stopped);
    sc_mutex_unlock(&db->mutex);

    // Flush queue
    while (!sc_vecdeque_is_empty(&db->queue)) {
        struct sc_delayed_packet dpacket = sc_vecdeque_pop(&db->queue);
        sc_delay_buffer_release(db, &dpacket);
    }

    LOGD("Buffering thread ended");

    return 0;
}

static bool
sc_delay_buffer_frame_sink_open(struct sc_frame_sink *sink,
                                const AVCodecContext *ctx,
                                const struct sc_stream_session *session) {
    struct sc_delay_buffer *db = DOWNCAST(sink);
    (void) ctx;
    (void) session;

    bool ok = sc_mutex_init(&db->mutex);
    if (!ok) {
        return false;
    }

    ok = sc_cond_init(&db->queue_cond);
    if (!ok) {
        goto error_destroy_mutex;
    }

    ok = sc_cond_init(&db->wait_cond);
    if (!ok) {
        goto error_destroy_queue_cond;
    }

    sc_clock_init(&db->clock);
    sc_vecdeque_init(&db->queue);
    db->stopped = false;
    db->retained_items = db->retained_bytes = 0;

    if (!sc_frame_source_sinks_open(&db->frame_source, ctx, session)) {
        goto error_destroy_wait_cond;
    }

    ok = sc_thread_create(&db->thread, run_buffering, "scrcpy-dbuf", db);
    if (!ok) {
        LOGE("Could not start buffering thread");
        goto error_close_sinks;
    }

    return true;

error_close_sinks:
    sc_frame_source_sinks_close(&db->frame_source);
error_destroy_wait_cond:
    sc_vecdeque_destroy(&db->queue);
    sc_cond_destroy(&db->wait_cond);
error_destroy_queue_cond:
    sc_cond_destroy(&db->queue_cond);
error_destroy_mutex:
    sc_mutex_destroy(&db->mutex);

    return false;
}

static void
sc_delay_buffer_frame_sink_close(struct sc_frame_sink *sink) {
    struct sc_delay_buffer *db = DOWNCAST(sink);

    sc_mutex_lock(&db->mutex);
    db->stopped = true;
    sc_cond_signal(&db->queue_cond);
    sc_cond_signal(&db->wait_cond);
    sc_mutex_unlock(&db->mutex);

    sc_thread_join(&db->thread, NULL);
    assert(!db->retained_items && !db->retained_bytes);

    // Join settles popped work and queue draining before backing storage release.
    sc_vecdeque_destroy(&db->queue);
    sc_frame_source_sinks_close(&db->frame_source);

    sc_cond_destroy(&db->wait_cond);
    sc_cond_destroy(&db->queue_cond);
    sc_mutex_destroy(&db->mutex);
}

static bool
sc_delay_buffer_frame_sink_push(struct sc_frame_sink *sink,
                                const AVFrame *frame) {
    struct sc_delay_buffer *db = DOWNCAST(sink);

    size_t charge;

    if (!sc_delay_buffer_frame_charge(frame, &charge)) {
        // Oversized/unaccountable input also closes admission, without a ref.
        sc_delay_buffer_reserve(db, SC_DELAY_BUFFER_MAX_RETAINED_BYTES + 1);
        return false;
    }

    if (!sc_delay_buffer_reserve(db, charge)) {
        return false;
    }

    // Prepare ownership before publication; a failed init cannot leave a slot.
    struct sc_delayed_packet candidate;

    if (!sc_delayed_packet_init_frame(&candidate, frame)) {
        sc_delay_buffer_release_charge(db, charge);
        return false;
    }

    candidate.charge = charge;

    sc_mutex_lock(&db->mutex);

    if (db->stopped) {
        sc_mutex_unlock(&db->mutex);
        sc_delay_buffer_release(db, &candidate);
        return false;
    }

    struct sc_delayed_packet *dpacket =
        sc_vecdeque_push_uninitialized(&db->queue);
    if (!dpacket) {
        sc_mutex_unlock(&db->mutex);
        sc_delay_buffer_release(db, &candidate);
        LOG_OOM();
        return false;
    }

    sc_tick pts = SC_TICK_FROM_US(frame->pts);
    sc_clock_update(&db->clock, sc_tick_now(), pts);
    sc_cond_signal(&db->wait_cond);
    candidate.asap = db->first_frame_asap && db->clock.range == 1;
    *dpacket = candidate; // ownership transfers only with a fully initialized slot

#ifdef SC_BUFFERING_DEBUG
    dpacket->push_date = sc_tick_now();
#endif

    sc_cond_signal(&db->queue_cond);

    sc_mutex_unlock(&db->mutex);

    return true;
}

static bool
sc_delay_buffer_frame_sink_push_session(struct sc_frame_sink *sink,
                                      const struct sc_stream_session *session) {
    struct sc_delay_buffer *db = DOWNCAST(sink);
    const size_t charge = sizeof(struct sc_delayed_packet);

    if (!sc_delay_buffer_reserve(db, charge)) {
        return false;
    }

    sc_mutex_lock(&db->mutex);

    if (db->stopped) {
        sc_mutex_unlock(&db->mutex);
        sc_delay_buffer_release_charge(db, charge);
        return false;
    }

    struct sc_delayed_packet *dpacket =
        sc_vecdeque_push_uninitialized(&db->queue);
    if (!dpacket) {
        sc_mutex_unlock(&db->mutex);
        sc_delay_buffer_release_charge(db, charge);
        LOG_OOM();
        return false;
    }

    sc_delayed_packet_init_session(dpacket, session);
    dpacket->charge = charge;

#ifdef SC_BUFFERING_DEBUG
    dpacket->push_date = sc_tick_now();
#endif

    sc_cond_signal(&db->queue_cond);

    sc_mutex_unlock(&db->mutex);

    return true;
}

void
sc_delay_buffer_init(struct sc_delay_buffer *db, sc_tick delay,
                     bool first_frame_asap) {
    assert(delay > 0);

    db->delay = delay;
    db->first_frame_asap = first_frame_asap;

    sc_frame_source_init(&db->frame_source);

    static const struct sc_frame_sink_ops ops = {
        .open = sc_delay_buffer_frame_sink_open,
        .close = sc_delay_buffer_frame_sink_close,
        .push = sc_delay_buffer_frame_sink_push,
        .push_session = sc_delay_buffer_frame_sink_push_session,
    };

    db->frame_sink.ops = &ops;
}
