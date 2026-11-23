#include "common.h"

#include <assert.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <SDL3/SDL.h>

static int observe_interleaved_write(AVFormatContext *context, AVPacket *packet);
static void *lease_alloc(size_t size);
static AVBufferRef *lease_buffer_create(uint8_t *data, size_t size,
    void (*release)(void *, uint8_t *), void *userdata, int flags);

/* Run the real recorder worker and libavformat muxer. The only adapter observes
 * return from the real public write API, without inspecting private FFmpeg state. */
#define av_interleaved_write_frame observe_interleaved_write
#define malloc lease_alloc
#define av_buffer_create lease_buffer_create
#include "../src/recorder.c"
#undef av_interleaved_write_frame
#undef malloc
#undef av_buffer_create

enum { BACKING_BYTES = 1024 * 1024, VIDEO_PACKETS = 81 };
static atomic_uint allocated_backings;
static atomic_uint released_backings;
static atomic_int_fast64_t last_video_dts_us;
static SDL_Semaphore *video_written;
static SDL_Semaphore *audio_written;
static SDL_Semaphore *final_write_release;
static unsigned video_writes;
static bool ended_success;
static bool fail_lease_allocation;
static unsigned fail_lease_buffer_at;
static unsigned lease_create_count;
static size_t backing_bytes = BACKING_BYTES;

static void *
lease_alloc(size_t size) {
    return fail_lease_allocation ? NULL : malloc(size);
}

static AVBufferRef *
lease_buffer_create(uint8_t *data, size_t size,
    void (*release)(void *, uint8_t *), void *userdata, int flags) {
    ++lease_create_count;
    return lease_create_count == fail_lease_buffer_at ? NULL
        : av_buffer_create(data, size, release, userdata, flags);
}

static int
observe_interleaved_write(AVFormatContext *context, AVPacket *packet) {
    AVStream *stream = context->streams[packet->stream_index];
    bool video = stream->codecpar->codec_type == AVMEDIA_TYPE_VIDEO;
    int64_t dts_us = av_rescale_q(packet->dts, stream->time_base, AV_TIME_BASE_Q);
    int result = av_interleaved_write_frame(context, packet);
    assert(result >= 0);

    if (video) {
        atomic_store(&last_video_dts_us, dts_us);
    }

    SDL_SignalSemaphore(video ? video_written : audio_written);

    if (video && ++video_writes == VIDEO_PACKETS - 1) {
        SDL_WaitSemaphore(final_write_release);
    }

    return result;
}

static void
release_backing(void *userdata, uint8_t *data) {
    assert(!userdata);
    av_free(data);
    atomic_fetch_add(&released_backings, 1);
}

static AVPacket *
video_packet(bool config) {
    AVPacket *packet = av_packet_alloc();
    assert(packet);
    size_t size = config ? 8 : backing_bytes;
    uint8_t *data = av_mallocz(size);
    assert(data);

    if (config) {
        packet->buf = av_buffer_create(data, size, av_buffer_default_free, NULL, 0);
    } else {
        packet->buf = av_buffer_create(data, size, release_backing, NULL, 0);
        atomic_fetch_add(&allocated_backings, 1);
    }

    assert(packet->buf);
    data[0] = 0xff;
    data[1] = 0xd8;
    data[6] = 0xff;
    data[7] = 0xd9;
    packet->data = data;
    packet->size = 8; // Valid reference to a small slice of a whole owned backing.
    packet->pts = packet->dts = config ? AV_NOPTS_VALUE : 0;
    return packet;
}

static void
recording_ended(struct sc_recorder *recorder, bool success, void *userdata) {
    assert(recorder->stopped && !userdata);
    ended_success = success;
}

static void
open_video(struct sc_recorder *recorder) {
    AVCodecContext *context = avcodec_alloc_context3(NULL);
    assert(context);
    context->codec_id = AV_CODEC_ID_MJPEG;
    context->codec_type = AVMEDIA_TYPE_VIDEO;
    context->width = 64;
    context->height = 32;
    context->pix_fmt = AV_PIX_FMT_YUVJ420P;
    assert(recorder->video_packet_sink.ops->open(&recorder->video_packet_sink,
                                                context, NULL));
    avcodec_free_context(&context);
}

static void
open_audio(struct sc_recorder *recorder) {
    AVCodecContext *context = avcodec_alloc_context3(NULL);
    assert(context);
    context->codec_id = AV_CODEC_ID_PCM_S16LE;
    context->codec_type = AVMEDIA_TYPE_AUDIO;
    context->sample_rate = 48000;
    context->sample_fmt = AV_SAMPLE_FMT_S16;
    context->bits_per_raw_sample = 16;
    av_channel_layout_default(&context->ch_layout, 2);
    assert(recorder->audio_packet_sink.ops->open(&recorder->audio_packet_sink,
                                                context, NULL));
    avcodec_free_context(&context);
}

/** Real admission, public copy/move APIs and both local allocation failures. */
static void
test_lease_properties(void) {
    static const struct sc_recorder_callbacks callbacks = {.on_ended = recording_ended};

    for (unsigned scenario = 0; scenario < 4; ++scenario) {
        struct sc_recorder recorder;
        assert(sc_recorder_init(&recorder, "not-opened.mkv", SC_RECORD_FORMAT_MKV,
            true, false, SC_ORIENTATION_0, &callbacks, NULL));
        AVPacket *source = av_packet_alloc();
        assert(source && !av_new_packet(source, 8));
        source->opaque_ref = av_buffer_alloc(16);
        assert(source->opaque_ref);
        memset(source->opaque_ref->data, 0x5a, 16);
        fail_lease_allocation = scenario == 1;
        fail_lease_buffer_at = scenario >= 2 ? scenario - 1 : 0;
        lease_create_count = 0;
        bool admitted = sc_recorder_queue_push(&recorder, &recorder.video_queue, 0, source);
        fail_lease_allocation = false;
        fail_lease_buffer_at = 0;
        assert(admitted == !scenario);

        if (admitted) {
            AVPacket *owned = sc_vecdeque_pop(&recorder.video_queue);
            assert(owned->opaque_ref->data == source->opaque_ref->data);
            assert(owned->opaque_ref->size == source->opaque_ref->size);
            assert(!memcmp(owned->opaque_ref->data, source->opaque_ref->data, 16));
            AVPacket *copy = av_packet_clone(owned);
            AVPacket *props = av_packet_alloc();
            AVPacket *moved = av_packet_alloc();
            assert(copy && props && moved && !av_packet_copy_props(props, owned));
            av_packet_move_ref(moved, owned);
            AVBufferRef *payload_guard = av_buffer_ref(moved->buf);
            assert(payload_guard && !av_buffer_is_writable(payload_guard));
            av_packet_free(&owned);
            av_packet_free(&moved);
            av_packet_free(&copy);
            assert(recorder.retained_packets == 1);
            assert(av_buffer_get_ref_count(source->opaque_ref) == 2);
            av_packet_free(&props);
            assert(recorder.retained_packets == 1);
            assert(av_buffer_get_ref_count(source->opaque_ref) == 2);
            av_buffer_unref(&payload_guard);
        }

        assert(!recorder.retained_packets && !recorder.retained_bytes);
        assert(av_buffer_get_ref_count(source->opaque_ref) == 1);
        av_packet_free(&source);
        sc_recorder_destroy(&recorder);
    }
}

int
main(int argc, char **argv) {
    if (argc == 2 && !strcmp(argv[1], "--versions")) {
        printf("FFmpeg=%s avformat=%u header=%u avcodec=%u header=%u avutil=%u header=%u\n",
               av_version_info(), avformat_version(), LIBAVFORMAT_VERSION_INT,
               avcodec_version(), LIBAVCODEC_VERSION_INT,
               avutil_version(), LIBAVUTIL_VERSION_INT);
        return 0;
    }

    bool observe_only = argc == 2 && !strcmp(argv[1], "--observe");
    assert(SDL_Init(0));
    test_lease_properties();

    if (argc == 2 && !strcmp(argv[1], "--within-limit")) {
        backing_bytes = 4096;
    }
    video_written = SDL_CreateSemaphore(0);
    audio_written = SDL_CreateSemaphore(0);
    final_write_release = SDL_CreateSemaphore(0);
    assert(video_written && audio_written && final_write_release);
    struct sc_recorder recorder;
    static const struct sc_recorder_callbacks callbacks = {
        .on_ended = recording_ended,
    };
    // Meson may run the two scenarios concurrently in the same directory.
    const char *filename = backing_bytes == BACKING_BYTES
                         ? "mux-reference-limit.mkv" : "mux-reference-drain.mkv";
    assert(sc_recorder_init(&recorder, filename, SC_RECORD_FORMAT_MKV, true, true,
                            SC_ORIENTATION_0, &callbacks, NULL));
    sc_recorder_set_exclusive_output(&recorder);
    assert(sc_recorder_start(&recorder));
    open_video(&recorder);
    open_audio(&recorder);
    struct sc_packet_sink *video = &recorder.video_packet_sink;
    AVPacket *config = video_packet(true);
    assert(video->ops->push(video, config));
    av_packet_free(&config);
    AVPacket *frame = video_packet(false);
    assert(video->ops->push(video, frame));
    av_packet_free(&frame);
    AVPacket *audio = av_packet_alloc();
    assert(audio && !av_new_packet(audio, 8));
    memset(audio->data, 0, 8);
    audio->pts = audio->dts = 0;
    assert(recorder.audio_packet_sink.ops->push(&recorder.audio_packet_sink, audio));
    av_packet_free(&audio);
    SDL_WaitSemaphore(audio_written);
    int64_t max_interleave_delta = recorder.ctx->max_interleave_delta;

    bool rejected = false;

    for (unsigned index = 1; index < VIDEO_PACKETS; ++index) {
        frame = video_packet(false);
        bool accepted = video->ops->push(video, frame);
        av_packet_free(&frame);

        if (!accepted) {
            rejected = true;
            break;
        }

        SDL_WaitSemaphore(video_written);
    }

    size_t live_backings = atomic_load(&allocated_backings)
        - atomic_load(&released_backings);
    size_t live_bytes = live_backings * backing_bytes;
    sc_mutex_lock(&recorder.mutex);
    size_t queue_charge = recorder.retained_bytes;
    size_t retained_packets = recorder.retained_packets;
    sc_mutex_unlock(&recorder.mutex);
    printf("actual muxer reference probe: created=%u released=%u live_bytes=%zu "
           "owner_charge=%zu retained_packets=%zu normalized_dts_us=%" PRIi64
           " max_interleave_delta=%" PRIi64 "\n",
           atomic_load(&allocated_backings), atomic_load(&released_backings),
           live_bytes, queue_charge, retained_packets,
           atomic_load(&last_video_dts_us), max_interleave_delta);
    fflush(stdout);
    sc_recorder_stop(&recorder);
    SDL_SignalSemaphore(final_write_release);
    sc_recorder_join(&recorder);
    assert(ended_success == !rejected);
    assert(sc_recorder_get_result(&recorder) == (rejected
        ? SC_RECORDER_RESULT_FAILED : SC_RECORDER_RESULT_SUCCEEDED));
    assert(atomic_load(&allocated_backings) == atomic_load(&released_backings));
    sc_recorder_destroy(&recorder);
    assert(!remove(filename));
    SDL_DestroySemaphore(video_written);
    SDL_DestroySemaphore(audio_written);
    SDL_DestroySemaphore(final_write_release);
    SDL_Quit();

    if (!observe_only) {
        assert(live_bytes <= SC_RECORDER_MAX_RETAINED_BYTES);
        assert(rejected == (backing_bytes == BACKING_BYTES));
    }

    return 0;
}
