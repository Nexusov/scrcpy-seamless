#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <libavutil/buffer.h>

#include "decoder.h"
#include "trait/packet_source.h"

#define TEST_SINK_COUNT 2
#define TEST_FRAME_WIDTH 32
#define TEST_FRAME_HEIGHT 24

struct frame_endpoint {
    struct sc_frame_sink sink;
    AVFrame *frame;
    bool fail_open;
    unsigned opens;
    unsigned closes;
    unsigned releases;
};

struct packet_endpoint {
    struct sc_packet_sink sink;
    bool fail_open;
    unsigned opens;
    unsigned closes;
};

/** Count final buffer release through the actual FFmpeg reference API. */
static void
release_buffer(void *userdata, uint8_t *bytes) {
    struct frame_endpoint *endpoint = userdata;
    ++endpoint->releases;
    av_free(bytes);
}

/** Acquire a real frame reference only when this external sink opens. */
static bool
open_frame(struct sc_frame_sink *sink, const AVCodecContext *context,
            const struct sc_stream_session *metadata) {
    struct frame_endpoint *endpoint = container_of(sink, struct frame_endpoint, sink);
    assert(context->codec_type == AVMEDIA_TYPE_VIDEO);
    assert(metadata->video.width == TEST_FRAME_WIDTH);
    ++endpoint->opens;

    if (endpoint->fail_open) {
        return false;
    }
    endpoint->frame = av_frame_alloc();
    assert(endpoint->frame);
    uint8_t *bytes = av_malloc(1);
    assert(bytes);
    endpoint->frame->buf[0] = av_buffer_create(bytes, 1, release_buffer, endpoint, 0);
    assert(endpoint->frame->buf[0]);
    return true;
}

/** Release only the successfully opened downstream frame endpoint. */
static void
close_frame(struct sc_frame_sink *sink) {
    struct frame_endpoint *endpoint = container_of(sink, struct frame_endpoint, sink);
    assert(endpoint->frame);
    ++endpoint->closes;
    av_frame_free(&endpoint->frame);
}

/** Control a second packet sink after the actual decoder and frame graph. */
static bool
open_packet(struct sc_packet_sink *sink, AVCodecContext *context,
             const struct sc_stream_session *metadata) {
    (void) context;
    (void) metadata;
    struct packet_endpoint *endpoint = container_of(sink, struct packet_endpoint, sink);
    ++endpoint->opens;
    return !endpoint->fail_open;
}

/** Count close cascade ownership without closing an unopened packet endpoint. */
static void
close_packet(struct sc_packet_sink *sink) {
    struct packet_endpoint *endpoint = container_of(sink, struct packet_endpoint, sink);
    assert(!endpoint->fail_open);
    ++endpoint->closes;
}

/** Exercise the real packet-source → decoder → frame-source open/close path. */
static void
test_graph(int failing_frame, bool failing_packet) {
    static const struct sc_frame_sink_ops frame_operations = {
        .open = open_frame,
        .close = close_frame,
    };
    static const struct sc_packet_sink_ops packet_operations = {
        .open = open_packet,
        .close = close_packet,
    };
    struct frame_endpoint endpoints[TEST_SINK_COUNT] = {0};
    struct sc_decoder decoder;
    sc_decoder_init(&decoder, "graph-test");

    for (unsigned index = 0; index < TEST_SINK_COUNT; ++index) {
        endpoints[index].sink.ops = &frame_operations;
        endpoints[index].fail_open = failing_frame == (int) index;
        sc_frame_source_add_sink(&decoder.frame_source, &endpoints[index].sink);
    }
    struct packet_endpoint packet = {
        .sink.ops = &packet_operations,
        .fail_open = failing_packet,
    };
    struct sc_packet_source source;
    sc_packet_source_init(&source);
    sc_packet_source_add_sink(&source, &decoder.packet_sink);
    sc_packet_source_add_sink(&source, &packet.sink);
    AVCodecContext *context = avcodec_alloc_context3(NULL);
    assert(context);
    context->codec_type = AVMEDIA_TYPE_VIDEO;
    struct sc_stream_session metadata = {
        .video = {.width = TEST_FRAME_WIDTH, .height = TEST_FRAME_HEIGHT},
    };
    bool opened = sc_packet_source_sinks_open(&source, context, &metadata);
    assert(opened == (failing_frame < 0 && !failing_packet));

    if (opened) {
        assert(decoder.frame);
        sc_packet_source_sinks_close(&source);
    }

    for (unsigned index = 0; index < TEST_SINK_COUNT; ++index) {
        bool visited = failing_frame < 0 || index <= (unsigned) failing_frame;
        bool acquired = visited && !endpoints[index].fail_open;
        assert(endpoints[index].opens == visited);
        assert(endpoints[index].closes == acquired);
        assert(endpoints[index].releases == acquired);
        assert(!endpoints[index].frame);
    }
    assert(!decoder.frame);
    assert(packet.opens == (failing_frame < 0));
    assert(packet.closes == opened);
    avcodec_free_context(&context);
}

/** Verify successful and partial graph opens each have exactly one close owner. */
int
main(void) {
    test_graph(0, false);
    test_graph(1, false);
    test_graph(-1, true);
    test_graph(-1, false);
    puts("session sink graph: 4 actual decoder/packet/frame cascade cases passed");
    return 0;
}
