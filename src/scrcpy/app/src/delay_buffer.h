#ifndef SC_DELAY_BUFFER_H
#define SC_DELAY_BUFFER_H

#include "common.h"

#include <stdbool.h>
#include <libavutil/frame.h>

#include "clock.h"
#include "trait/frame_source.h"
#include "trait/frame_sink.h"
#include "util/thread.h"
#include "util/tick.h"
#include "util/vecdeque.h"

//#define SC_BUFFERING_DEBUG // uncomment to debug

// forward declarations
typedef struct AVFrame AVFrame;

enum sc_delayed_packet_type {
    SC_DELAYED_PACKET_TYPE_FRAME,
    SC_DELAYED_PACKET_TYPE_SESSION,
};

struct sc_delayed_packet {
    enum sc_delayed_packet_type type;
    bool asap; // first video frame bypasses the wait, never its FIFO predecessors
    union {
        AVFrame *frame;
        struct sc_stream_session session;
    };
#ifdef SC_BUFFERING_DEBUG
    sc_tick push_date;
#endif
};

struct sc_delayed_packet_queue SC_VECDEQUE(struct sc_delayed_packet);

struct sc_delay_buffer {
    struct sc_frame_source frame_source; // frame source trait
    struct sc_frame_sink frame_sink; // frame sink trait

    sc_tick delay;
    bool first_frame_asap;

    sc_thread thread;
    sc_mutex mutex;
    sc_cond queue_cond;
    sc_cond wait_cond;

    struct sc_clock clock;
    struct sc_delayed_packet_queue queue;
    bool stopped;
};

struct sc_delay_buffer_callbacks {
    bool (*on_new_frame)(struct sc_delay_buffer *db, const AVFrame *frame,
                         void *userdata);
};

/**
 * Initialize a delay buffer.
 *
 * \param delay a (strictly) positive delay
 * Frame/session publication is serialized by the upstream owner. Successful
 * push transfers a reference to the queue; downstream delivery belongs to the
 * single worker. Close joins that worker before releasing sinks and storage.
 *
 * \param first_frame_asap if true, skip the first frame's timing wait after
 *                         preceding metadata is delivered (useful for video).
 */
void
sc_delay_buffer_init(struct sc_delay_buffer *db, sc_tick delay,
                     bool first_frame_asap);

#endif
