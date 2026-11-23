#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/buffer.h>
#include <SDL3/SDL.h>

#include "options.h"
#include "util/thread.h"

static void *test_reallocarray(void *pointer, size_t count, size_t size);
static void test_free(void *pointer);
static int test_avio_open(AVIOContext **context, const char *url, int flags);
static int test_avio_close(AVIOContext *context);
static int test_write_header(AVFormatContext *context, AVDictionary **options);
static void test_cond_wait(sc_cond *condition, sc_mutex *mutex);
static bool test_thread_create(sc_thread *thread, sc_thread_fn function,
                               const char *name, void *userdata);

/* Keep the production recorder, including its static sink adapters and worker.
 * Replace only allocation admission, external output and deterministic gates. */
#define reallocarray test_reallocarray
#define free test_free
#define avio_open test_avio_open
#define avio_close test_avio_close
#define avformat_write_header test_write_header
#define sc_cond_wait test_cond_wait
#define sc_thread_create test_thread_create
#include "../src/recorder.c"
#undef reallocarray
#undef free
#undef avio_open
#undef avio_close
#undef avformat_write_header
#undef sc_cond_wait
#undef sc_thread_create

enum { PACKET_SIZE = 8, VIDEO_WIDTH = 64, VIDEO_HEIGHT = 32 };
static bool fail_admission;
static bool fail_thread_start;
static bool gate_header;
static SDL_Semaphore *worker_waiting;
static SDL_Semaphore *header_release;
static AVIOContext output_context;
static void *video_storage;
static void *audio_storage;
static unsigned queue_releases;
static unsigned buffer_releases;
static unsigned ended_callbacks;
static unsigned output_closes;

static void *
test_reallocarray(void *pointer, size_t count, size_t size) {

    if (fail_admission) {
        return NULL;
    }
    return reallocarray(pointer, count, size);
}

static void
test_free(void *pointer) {

    if (pointer && (pointer == video_storage || pointer == audio_storage)) {
        ++queue_releases;
    }
    free(pointer);
}

static int
test_avio_open(AVIOContext **context, const char *url, int flags) {
    assert(!strcmp(url, "file:synthetic-not-opened.mkv"));
    assert(flags == AVIO_FLAG_WRITE);
    *context = &output_context;
    return 0;
}

static int
test_avio_close(AVIOContext *context) {
    assert(context == &output_context);
    ++output_closes;
    return 0;
}

/** Hold the real worker at an output boundary until the owner requests Stop. */
static int
test_write_header(AVFormatContext *context, AVDictionary **options) {
    assert(context->pb == &output_context && !options);

    if (gate_header) {
        SDL_WaitSemaphore(header_release);
    }
    return AVERROR(EIO);
}

static void
test_cond_wait(sc_cond *condition, sc_mutex *mutex) {
    SDL_SignalSemaphore(worker_waiting);
    sc_cond_wait(condition, mutex);
}

static bool
test_thread_create(sc_thread *thread, sc_thread_fn function,
                    const char *name, void *userdata) {

    if (fail_thread_start) {
        return false;
    }
    return sc_thread_create(thread, function, name, userdata);
}

static void
on_ended(struct sc_recorder *recorder, bool success, void *userdata) {
    assert(recorder->stopped && !success && !userdata);
    ++ended_callbacks;
}

static const struct sc_recorder_callbacks callbacks = {.on_ended = on_ended};

static void
release_buffer(void *userdata, uint8_t *data) {
    assert(!userdata);
    ++buffer_releases;
    av_free(data);
}

static AVPacket *
new_packet(void) {
    AVPacket *packet = av_packet_alloc();
    assert(packet);
    uint8_t *data = av_malloc(PACKET_SIZE);
    assert(data);
    memset(data, 0, PACKET_SIZE);
    packet->buf = av_buffer_create(data, PACKET_SIZE, release_buffer, NULL, 0);
    assert(packet->buf);
    packet->data = data;
    packet->size = PACKET_SIZE;
    packet->pts = AV_NOPTS_VALUE;
    return packet;
}

static void
open_sink(struct sc_recorder *recorder, bool video) {
    const AVCodec *codec = avcodec_find_decoder(video ? AV_CODEC_ID_H264
                                                    : AV_CODEC_ID_PCM_S16LE);
    assert(codec);
    AVCodecContext *context = avcodec_alloc_context3(codec);
    assert(context);
    context->width = VIDEO_WIDTH;
    context->height = VIDEO_HEIGHT;
    struct sc_packet_sink *sink = video ? &recorder->video_packet_sink
                                       : &recorder->audio_packet_sink;
    assert(sink->ops->open(sink, context, NULL));
    avcodec_free_context(&context);
}

static void
init_recorder(struct sc_recorder *recorder) {
    assert(sc_recorder_init(recorder, "synthetic-not-opened.mkv",
                            SC_RECORD_FORMAT_MKV, true, true, SC_ORIENTATION_0,
                            &callbacks, NULL));
}

/** Rejected queue admission must retire the actual newly acquired packet ref. */
static void
test_rejected_packet(bool video) {
    struct sc_recorder recorder;
    init_recorder(&recorder);
    assert(sc_recorder_open_output_file(&recorder));
    open_sink(&recorder, video);
    AVPacket *packet = new_packet();
    fail_admission = true;
    struct sc_packet_sink *sink = video ? &recorder.video_packet_sink
                                       : &recorder.audio_packet_sink;
    assert(!sink->ops->push(sink, packet));
    fail_admission = false;
    assert(av_buffer_get_ref_count(packet->buf) == 1);
    av_packet_free(&packet);
    sc_recorder_stop(&recorder);
    sc_recorder_close_output_file(&recorder);
    sc_recorder_destroy(&recorder);
}

/** The real worker clears payloads; final destruction releases queue backing. */
static void
test_joined_queue_release(void) {
    struct sc_recorder recorder;
    init_recorder(&recorder);
    worker_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    assert(worker_waiting && header_release);
    gate_header = true;
    assert(sc_recorder_start(&recorder));
    SDL_WaitSemaphore(worker_waiting);
    open_sink(&recorder, true);
    open_sink(&recorder, false);
    AVPacket *video = new_packet();
    AVPacket *audio = new_packet();
    assert(recorder.video_packet_sink.ops->push(&recorder.video_packet_sink,
                                                video));
    assert(recorder.audio_packet_sink.ops->push(&recorder.audio_packet_sink,
                                                audio));
    video_storage = recorder.video_queue.data;
    audio_storage = recorder.audio_queue.data;
    assert(video_storage && audio_storage);
    av_packet_free(&video);
    av_packet_free(&audio);
    sc_recorder_stop(&recorder);
    SDL_SignalSemaphore(header_release);
    sc_recorder_join(&recorder);
    assert(ended_callbacks == 1);
    assert(sc_vecdeque_is_empty(&recorder.video_queue));
    assert(sc_vecdeque_is_empty(&recorder.audio_queue));
    assert(!queue_releases);
    sc_recorder_destroy(&recorder);
    assert(queue_releases == 2);
    assert(buffer_releases == 2);
    video_storage = audio_storage = NULL;
    gate_header = false;
    SDL_DestroySemaphore(worker_waiting);
    SDL_DestroySemaphore(header_release);
}

static void
test_unstarted_owner(bool fail_start) {
    struct sc_recorder recorder;
    init_recorder(&recorder);
    fail_thread_start = fail_start;

    if (fail_start) {
        assert(!sc_recorder_start(&recorder));
    }
    sc_recorder_stop(&recorder);
    sc_recorder_destroy(&recorder);
    fail_thread_start = false;
}

int
main(int argument_count, char **arguments) {
    assert(SDL_Init(0));

    if (argument_count == 2) {

        if (!strcmp(arguments[1], "backing")) {
            test_joined_queue_release();
        } else {
            test_rejected_packet(!strcmp(arguments[1], "video"));
        }
    } else {
        test_rejected_packet(true);
        test_rejected_packet(false);
        assert(buffer_releases == 2 && output_closes == 2);
        buffer_releases = 0;
        test_joined_queue_release();
        test_unstarted_owner(false);
        test_unstarted_owner(true);
        // Two rejection setups and the joined worker each close one output.
        assert(output_closes == 3);
        assert(ended_callbacks == 1);
        assert(queue_releases == 2);
        puts("Recorder lifetime: 5 production-linked cases passed");
    }
    SDL_Quit();
    return 0;
}
