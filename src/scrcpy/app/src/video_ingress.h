#ifndef SC_VIDEO_INGRESS_H
#define SC_VIDEO_INGRESS_H

#include "common.h"
#include "dispatcher.h"
#include "frame_buffer.h"
#include "trait/frame_sink.h"

struct sc_video_ingress;
typedef void (*sc_video_present_fn)(void *binding,
                                    struct sc_video_ingress *ingress,
                                    sc_dispatcher_generation generation);

/* App-owned latest mailbox; all fields below its mutex are mutex protected. */
struct sc_video_ingress {
    sc_mutex mutex;
    struct sc_frame_buffer mailbox;
    AVFrame *retired_frame; // main-thread revoke, released outside mutex
    struct sc_dispatcher *dispatcher;
    sc_video_present_fn present;
    sc_dispatcher_generation generation;
    struct sc_stream_session metadata;
    uint64_t ticket;
    bool notification_pending;
    unsigned skipped;
};

/* Producer-owned bridge; retain through producer join and sink closure. */
struct sc_video_bridge {
    struct sc_frame_sink frame_sink;
    struct sc_video_ingress *ingress;
    sc_dispatcher_generation generation;
    struct sc_stream_session metadata;
    bool open;
    AVFrame *retired_frame; // serialized producer, released outside mutex
};

bool
sc_video_ingress_init(struct sc_video_ingress *ingress,
                      struct sc_dispatcher *dispatcher,
                      sc_video_present_fn present);
void
sc_video_ingress_destroy(struct sc_video_ingress *ingress);
void
sc_video_ingress_bind(struct sc_video_ingress *ingress,
                      sc_dispatcher_generation generation);
void
sc_video_ingress_revoke(struct sc_video_ingress *ingress,
                        sc_dispatcher_generation generation);
bool
sc_video_ingress_consume(struct sc_video_ingress *ingress,
                         sc_dispatcher_generation generation, AVFrame *frame,
                         struct sc_stream_session *metadata,
                         unsigned *skipped);
void
sc_video_bridge_init(struct sc_video_bridge *bridge,
                     struct sc_video_ingress *ingress,
                     sc_dispatcher_generation generation);

#endif
