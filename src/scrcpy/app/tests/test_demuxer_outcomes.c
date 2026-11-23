#include "common.h"

#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <libavcodec/avcodec.h>

#include "demuxer.h"
#include "util/binary.h"

enum test_case {
    CODEC_SHORT, CODEC_UNKNOWN, CODEC_DISABLED, REMOTE_CONFIGURATION,
    INITIAL_HEADER_SHORT, INITIAL_HEADER_WRONG, INITIAL_DIMENSIONS_ZERO,
    SINK_OPEN_REJECTED, PAYLOAD_SHORT, PAYLOAD_ZERO, HEADER_SHORT,
    LATE_DIMENSIONS_ZERO, AUDIO_SESSION, VALID_PACKET, SINK_PUSH_REJECTED,
    METADATA_REJECTED, PACKET_ALLOCATION_FAILED, CODEC_OPEN_FAILED,
};

static enum test_case scenario;
static uint8_t stream[128];
static size_t stream_length;
static size_t stream_position;
static unsigned opens;
static unsigned closes;
static unsigned disables;
static unsigned packets;
static unsigned metadata_updates;
static unsigned completions;
static enum sc_demuxer_status observed_status;
static AVPacket *retained_packet;

static int test_new_packet(AVPacket *packet, int size);
static int test_open_codec(AVCodecContext *context, const AVCodec *codec,
                            AVDictionary **options);

/* Real run/start/join, source graph and FFmpeg references; inject only the two
 * named external allocation/codec-open failures. Socket bytes are synthetic. */
#define av_new_packet test_new_packet
#define avcodec_open2 test_open_codec
#ifndef SC_DEMUXER_TEST_SOURCE
# define SC_DEMUXER_TEST_SOURCE "../src/demuxer.c"
#endif
#include SC_DEMUXER_TEST_SOURCE
#undef avcodec_open2
#undef av_new_packet

static int
test_new_packet(AVPacket *packet, int size) {
    return scenario == PACKET_ALLOCATION_FAILED ? AVERROR(ENOMEM)
                                                : av_new_packet(packet, size);
}

static int
test_open_codec(AVCodecContext *context, const AVCodec *codec, AVDictionary **options) {
    return scenario == CODEC_OPEN_FAILED ? AVERROR(EINVAL)
                                         : avcodec_open2(context, codec, options);
}

ssize_t
net_recv_all(sc_socket socket, void *output, size_t length) {
    assert(socket == (sc_socket) (uintptr_t) 1);
    size_t available = stream_length - stream_position;
    size_t received = MIN(length, available);
    memcpy(output, stream + stream_position, received);
    stream_position += received;
    return (ssize_t) received;
}

static bool
open_sink(struct sc_packet_sink *sink, AVCodecContext *context,
            const struct sc_stream_session *session) {
    (void) sink;
    assert(context);
    assert(session || scenario == AUDIO_SESSION);
    ++opens;
    return scenario != SINK_OPEN_REJECTED;
}

static void
close_sink(struct sc_packet_sink *sink) {
    (void) sink;
    assert(opens == 1 && !disables);
    ++closes;
}

static void
disable_sink(struct sc_packet_sink *sink) {
    (void) sink;
    assert(!closes && (!opens || scenario == SINK_OPEN_REJECTED));
    ++disables;
}

static bool
push_packet(struct sc_packet_sink *sink, const AVPacket *packet) {
    (void) sink;
    assert(opens == 1 && !closes && !disables);
    ++packets;
    assert(packet->size == 3 && packet->data[0] == 1 && packet->data[2] == 3);
    int referenced = av_packet_ref(retained_packet, packet);
    assert(!referenced);
    return scenario != SINK_PUSH_REJECTED;
}

static bool
push_session(struct sc_packet_sink *sink, const struct sc_stream_session *session) {
    (void) sink;
    assert(session->video.width == 64 && session->video.height == 48);
    ++metadata_updates;
    return scenario != METADATA_REJECTED;
}

static void
ended(struct sc_demuxer *demuxer, enum sc_demuxer_status status, void *userdata) {
    assert(userdata == demuxer);
    ++completions;
    observed_status = status;
}

static void
append_session(uint32_t width, uint32_t height) {
    uint8_t *header = stream + stream_length;
    memset(header, 0, 12);
    header[0] = 0x80;
    sc_write32be(header + 4, width);
    sc_write32be(header + 8, height);
    stream_length += 12;
}

static void
append_packet(uint32_t length, bool complete) {
    uint8_t *header = stream + stream_length;
    memset(header, 0, 12);
    sc_write64be(header, 123);
    sc_write32be(header + 8, length);
    stream_length += 12;

    if (length) {
        stream[stream_length++] = 1;

        if (complete) {
            stream[stream_length++] = 2;
            stream[stream_length++] = 3;
        }
    }
}

static void
test_outcome(enum test_case selected, enum sc_demuxer_status expected) {
    scenario = selected;
    stream_position = 0;
    stream_length = 4;
    opens = closes = disables = packets = metadata_updates = completions = 0;
    memset(stream, 0, sizeof(stream));
    sc_write32be(stream, selected == AUDIO_SESSION ? UINT32_C(0x00726177) : UINT32_C(0x68323634));

    if (selected == CODEC_SHORT) {
        stream_length = 2;
    } else if (selected == CODEC_UNKNOWN) {
        sc_write32be(stream, UINT32_C(0xffffffff));
    } else if (selected == CODEC_DISABLED || selected == REMOTE_CONFIGURATION) {
        sc_write32be(stream, selected == CODEC_DISABLED ? 0 : 1);
    } else if (selected == AUDIO_SESSION) {
        append_session(32, 24);
    } else if (selected != INITIAL_HEADER_SHORT) {
        append_session(selected == INITIAL_DIMENSIONS_ZERO ? 0 : 32, 24);

        if (selected == INITIAL_HEADER_WRONG) {
            stream[4] = 0;
        } else if (selected == PAYLOAD_SHORT) {
            append_packet(3, false);
        } else if (selected == PAYLOAD_ZERO) {
            append_packet(0, true);
        } else if (selected == HEADER_SHORT) {
            stream[stream_length++] = 0;
        } else if (selected == LATE_DIMENSIONS_ZERO) {
            append_session(0, 24);
        } else if (selected == METADATA_REJECTED) {
            append_session(64, 48);
        } else {
            append_packet(3, true);
        }
    }

    static const struct sc_packet_sink_ops operations = {
        .open = open_sink, .close = close_sink, .push = push_packet,
        .push_session = push_session, .disable = disable_sink,
    };
    static const struct sc_demuxer_callbacks callbacks = {.on_ended = ended};
    struct sc_packet_sink sink = {.ops = &operations};
    struct sc_demuxer demuxer;
    sc_demuxer_init(&demuxer, "synthetic", (sc_socket) (uintptr_t) 1, &callbacks, &demuxer);
    sc_packet_source_add_sink(&demuxer.packet_source, &sink);
    retained_packet = av_packet_alloc();
    assert(retained_packet);
    bool started = sc_demuxer_start(&demuxer);
    assert(started);
    sc_demuxer_join(&demuxer);
    assert(completions == 1);

    if (observed_status != expected) {
        fprintf(stderr, "demuxer case=%u expected=%u actual=%u\n", selected, expected, observed_status);
    }

    assert(observed_status == expected);
    assert(disables <= 1 && closes <= 1 && !(disables && closes));
    assert((opens && selected != SINK_OPEN_REJECTED) ? closes == 1 : disables == 1);

    if (selected == VALID_PACKET || selected == SINK_PUSH_REJECTED) {
        assert(packets == 1 && retained_packet->buf);
        assert(av_buffer_get_ref_count(retained_packet->buf) == 1);
        assert(retained_packet->data[0] == 1 && retained_packet->data[2] == 3);
    }

    av_packet_free(&retained_packet);
    assert(!retained_packet);
}

int
main(void) {
    const enum sc_demuxer_status expected[] = {
        SC_DEMUXER_STATUS_EOS, SC_DEMUXER_STATUS_PROTOCOL_ERROR, SC_DEMUXER_STATUS_DISABLED,
        SC_DEMUXER_STATUS_ERROR, SC_DEMUXER_STATUS_EOS, SC_DEMUXER_STATUS_PROTOCOL_ERROR,
        SC_DEMUXER_STATUS_PROTOCOL_ERROR, SC_DEMUXER_STATUS_SINK_ERROR, SC_DEMUXER_STATUS_EOS,
        SC_DEMUXER_STATUS_PROTOCOL_ERROR, SC_DEMUXER_STATUS_EOS, SC_DEMUXER_STATUS_PROTOCOL_ERROR,
        SC_DEMUXER_STATUS_PROTOCOL_ERROR, SC_DEMUXER_STATUS_EOS, SC_DEMUXER_STATUS_SINK_ERROR,
        SC_DEMUXER_STATUS_SINK_ERROR, SC_DEMUXER_STATUS_ERROR, SC_DEMUXER_STATUS_ERROR,
    };

    for (unsigned index = 0; index < ARRAY_LEN(expected); ++index) {
        test_outcome((enum test_case) index, expected[index]);
    }

    puts("demuxer outcomes: 18 real worker/source/FFmpeg-reference cases");
    return 0;
}
