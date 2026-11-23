#include "common.h"

#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <SDL3/SDL.h>

#include "util/thread.h"
#include "recording_segments.h"
#include "trait/packet_source.h"

#ifdef _WIN32
# include <direct.h>
#else
# include <unistd.h>
#endif

static AVFormatContext *test_alloc_context(void);
static AVStream *test_new_stream(AVFormatContext *context, const AVCodec *codec);
static void test_cond_wait(sc_cond *condition, sc_mutex *mutex);
static int test_write_header(AVFormatContext *context, AVDictionary **options);
static int test_write_trailer(AVFormatContext *context);
static int test_close_file(FILE *file);
static AVIOContext *test_alloc_io(unsigned char *buffer, int buffer_size,
                                  int write_flag, void *opaque,
                                  int (*read_packet)(void *, uint8_t *, int),
                                  int (*write_packet)(void *, const uint8_t *, int),
                                  int64_t (*seek)(void *, int64_t, int));
static void test_free_io(AVIOContext **context);
static int test_write_packet(AVFormatContext *context, AVPacket *packet);
static int test_packet_ref(AVPacket *destination, const AVPacket *source);
static size_t test_write_bytes(const void *buffer, size_t size, size_t count,
                                FILE *file);

/* Gate the real recorder's output creation, without replacing its sink/worker. */
#define avformat_alloc_context test_alloc_context
#define avformat_new_stream test_new_stream
#define sc_cond_wait test_cond_wait
#define avformat_write_header test_write_header
#define av_write_trailer test_write_trailer
#define fclose test_close_file
#define avio_alloc_context test_alloc_io
#define avio_context_free test_free_io
#define av_interleaved_write_frame test_write_packet
#define fwrite test_write_bytes
#define av_packet_ref test_packet_ref
#include "../src/recorder.c"
#undef avformat_alloc_context
#undef avformat_new_stream
#undef sc_cond_wait
#undef avformat_write_header
#undef av_write_trailer
#undef fclose
#undef avio_alloc_context
#undef avio_context_free
#undef av_interleaved_write_frame
#undef fwrite
#undef av_packet_ref

static SDL_Semaphore *output_waiting;
static SDL_Semaphore *output_release;
static SDL_Semaphore *producer_waiting;
static bool gate_output;
static bool gate_header;
static bool fail_io_allocation;
static bool expect_sink_open = true;
static _Thread_local bool producer_thread;
static unsigned ended_count;
static bool ended_success;
static unsigned closed_files;
static unsigned freed_io;
static unsigned written_trailers;
static unsigned freed_buffers;
static SDL_Semaphore *header_waiting;
static SDL_Semaphore *header_release;
static struct sc_recorder *audio_wait_recorder;
static SDL_Semaphore *audio_config_waiting;
static unsigned header_stream_count;
static bool fail_header_write;
static bool fail_audio_write;
static bool fail_video_write;
static bool fail_output_write;
static bool fail_file_close;
static struct sc_recorder *initial_wait_recorder;
static SDL_Semaphore *initial_config_waiting;
static unsigned header_calls;
static bool gate_packet_write;
static SDL_Semaphore *packet_write_waiting;
static SDL_Semaphore *packet_write_release;
static struct sc_recorder *reservation_probe;
static bool gate_candidate_reference;
static bool fail_candidate_reference;
static SDL_Semaphore *candidate_waiting;
static SDL_Semaphore *candidate_release;

static int
test_packet_ref(AVPacket *destination, const AVPacket *source) {

    if (reservation_probe) {
        // Reference creation is outside the queue lock, after reservation.
        sc_mutex_lock(&reservation_probe->mutex);
        size_t queued = sc_vecdeque_size(&reservation_probe->video_queue)
            + sc_vecdeque_size(&reservation_probe->audio_queue);
        assert(reservation_probe->retained_packets > queued);
        assert(reservation_probe->retained_bytes <= SC_RECORDER_MAX_RETAINED_BYTES);
        sc_mutex_unlock(&reservation_probe->mutex);
    }

    if (gate_candidate_reference) {
        gate_candidate_reference = false;
        SDL_SignalSemaphore(candidate_waiting);
        SDL_WaitSemaphore(candidate_release);

        if (fail_candidate_reference) {
            return AVERROR(ENOMEM);
        }
    }

    return av_packet_ref(destination, source);
}

static AVFormatContext *
test_alloc_context(void) {
    if (gate_output) {
        SDL_SignalSemaphore(output_waiting);
        SDL_WaitSemaphore(output_release);
    }

    return avformat_alloc_context();
}

static AVStream *
test_new_stream(AVFormatContext *context, const AVCodec *codec) {
    if (!context) {
        fputs("BEHAVIORAL RED: real sink reached avformat_new_stream before "
              "the recorder published its output context\n", stderr);
        exit(86);
    }

    return avformat_new_stream(context, codec);
}

static void
test_cond_wait(sc_cond *condition, sc_mutex *mutex) {
    if (producer_thread && gate_output) {
        SDL_SignalSemaphore(producer_waiting);
    }

    if (audio_wait_recorder && condition == &audio_wait_recorder->cond
            && audio_wait_recorder->video_init && audio_wait_recorder->audio
            && !audio_wait_recorder->audio_init
            && !sc_vecdeque_is_empty(&audio_wait_recorder->video_queue)) {
        // Called with the real recorder mutex held, before the actual wait.
        SDL_SignalSemaphore(audio_config_waiting);
    }

    if (initial_wait_recorder && condition == &initial_wait_recorder->cond
            && initial_wait_recorder->output_ready
            && !initial_wait_recorder->audio_init) {
        SDL_SignalSemaphore(initial_config_waiting);
    }

    sc_cond_wait(condition, mutex);
}

static int
test_write_header(AVFormatContext *context, AVDictionary **options) {
    ++header_calls;
    header_stream_count = context->nb_streams;
    if (gate_header) {
        SDL_SignalSemaphore(header_waiting);
        SDL_WaitSemaphore(header_release);
    }

    if (fail_header_write) {
        return AVERROR(EIO);
    }

    return avformat_write_header(context, options);
}

static int
test_write_trailer(AVFormatContext *context) {
    ++written_trailers;
    return av_write_trailer(context);
}

static int
test_close_file(FILE *file) {
    ++closed_files;
    int result = fclose(file);

    if (fail_file_close) {
        errno = EIO;
        return EOF;
    }

    return result;
}

static size_t
test_write_bytes(const void *buffer, size_t size, size_t count, FILE *file) {
    if (fail_output_write) {
        errno = EIO;
        return 0;
    }

    return fwrite(buffer, size, count, file);
}

static int
test_write_packet(AVFormatContext *context, AVPacket *packet) {
    enum AVMediaType type = context->streams[packet->stream_index]->codecpar->codec_type;

    if (gate_packet_write) {
        gate_packet_write = false;
        SDL_SignalSemaphore(packet_write_waiting);
        SDL_WaitSemaphore(packet_write_release);
    }

    if ((type == AVMEDIA_TYPE_AUDIO && fail_audio_write)
            || (type == AVMEDIA_TYPE_VIDEO && fail_video_write)) {
        return AVERROR(EIO);
    }

    return av_interleaved_write_frame(context, packet);
}

static AVIOContext *
test_alloc_io(unsigned char *buffer, int buffer_size, int write_flag, void *opaque,
               int (*read_packet)(void *, uint8_t *, int),
               int (*write_packet)(void *, const uint8_t *, int),
               int64_t (*seek)(void *, int64_t, int)) {
    if (fail_io_allocation) {
        return NULL;
    }

    return avio_alloc_context(buffer, buffer_size, write_flag, opaque,
                               read_packet, write_packet, seek);
}

static void
test_free_io(AVIOContext **context) {
    assert(*context);
    ++freed_io;
    avio_context_free(context);
    assert(!*context);
}

static void
on_ended(struct sc_recorder *recorder, bool success, void *userdata) {
    assert(recorder->stopped && !userdata);
    ++ended_count;
    ended_success = success;
}

static const struct sc_recorder_callbacks callbacks = {.on_ended = on_ended};

static int
open_audio(void *userdata) {
    struct sc_recorder *recorder = userdata;
    producer_thread = true;
    const AVCodec *codec = avcodec_find_decoder(AV_CODEC_ID_PCM_S16LE);
    assert(codec);
    AVCodecContext *context = avcodec_alloc_context3(codec);
    assert(context);
    context->sample_rate = 48000;
    context->sample_fmt = AV_SAMPLE_FMT_S16;
    context->bits_per_raw_sample = 16;
    av_channel_layout_default(&context->ch_layout, 2);
    bool opened = recorder->audio_packet_sink.ops->open(
                        &recorder->audio_packet_sink, context, NULL);
    assert(opened == expect_sink_open);
    avcodec_free_context(&context);
    producer_thread = false;
    return 0;
}

static void
test_context_publication(void) {
    struct sc_recorder recorder = {0};
    bool initialized = sc_recorder_init(&recorder,
                        "publication.wav", SC_RECORD_FORMAT_WAV,
                        false, true, SC_ORIENTATION_0, &callbacks, NULL);
    assert(initialized);
    output_waiting = SDL_CreateSemaphore(0);
    output_release = SDL_CreateSemaphore(0);
    producer_waiting = SDL_CreateSemaphore(0);
    assert(output_waiting && output_release && producer_waiting);
    gate_output = true;
    bool started = sc_recorder_start(&recorder);
    assert(started);
    SDL_WaitSemaphore(output_waiting);
    sc_thread producer;
    bool created = sc_thread_create(&producer, open_audio, "record-source",
                                      &recorder);
    assert(created);
    SDL_WaitSemaphore(producer_waiting);
    SDL_SignalSemaphore(output_release);
    sc_thread_join(&producer, NULL);
    sc_recorder_stop(&recorder);
    sc_recorder_join(&recorder);
    assert(ended_count == 1 && ended_success);
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_SUCCEEDED);
    sc_recorder_destroy(&recorder);
    gate_output = false;
    SDL_DestroySemaphore(output_waiting);
    SDL_DestroySemaphore(output_release);
    SDL_DestroySemaphore(producer_waiting);
    int removed = remove("publication.wav");
    assert(!removed);
}

static void
test_paths(void) {
    static const char *const originals[] = {
        "clip.mkv", "dir.ext/clip", "C:\\folder.name\\clip.mp4",
        ".hidden", "dir/.hidden.mkv",
    };
    static const char *const expected[] = {
        "clip.part01.mkv", "dir.ext/clip.part01",
        "C:\\folder.name\\clip.part01.mp4", ".hidden.part01",
        "dir/.hidden.part01.mkv",
    };

    for (unsigned index = 0; index < ARRAY_LEN(originals); ++index) {
        struct sc_recording_segments segments;
        bool initialized = sc_recording_segments_init(&segments, originals[index]);
        assert(initialized);
        struct sc_recording_segment segment;
        bool prepared = sc_recording_segments_prepare(&segments, &segment);
        assert(prepared && !strcmp(segment.filename, expected[index]));
        assert(segment.number == 1 && segments.next_number == 1);
        bool committed = sc_recording_segments_commit(&segments, &segment, false);
        assert(!committed && segments.next_number == 1);
        committed = sc_recording_segments_commit(&segments, &segment, true);
        assert(committed && segments.next_number == 2);
        committed = sc_recording_segments_commit(&segments, &segment, true);
        assert(!committed && segments.next_number == 2);
        sc_recording_segment_destroy(&segment);
    }

    struct sc_recording_segments segments;
    bool initialized = sc_recording_segments_init(&segments, "");
    assert(!initialized);
    initialized = sc_recording_segments_init(&segments, "dir/");
    assert(!initialized);
    initialized = sc_recording_segments_init(&segments, "dir/.");
    assert(!initialized);
    initialized = sc_recording_segments_init(&segments, "..");
    assert(!initialized);
    initialized = sc_recording_segments_init(&segments, "clip.mkv");
    assert(initialized);
    segments.next_number = UINT64_MAX;
    struct sc_recording_segment segment;
    bool prepared = sc_recording_segments_prepare(&segments, &segment);
    assert(prepared && !strcmp(segment.filename,
                                "clip.part18446744073709551615.mkv"));
    bool committed = sc_recording_segments_commit(&segments, &segment, true);
    assert(committed);
    sc_recording_segment_destroy(&segment);
    prepared = sc_recording_segments_prepare(&segments, &segment);
    assert(!prepared);
}

static void
test_init_exclusive(struct sc_recorder *recorder, const char *filename) {
    bool initialized = sc_recorder_init(recorder, filename, SC_RECORD_FORMAT_WAV,
                                        false, true, SC_ORIENTATION_0,
                                        &callbacks, NULL);
    assert(initialized);
    sc_recorder_set_exclusive_output(recorder);
}

static void
test_failed_open_and_collision(void) {
    static const char sentinel[] = "existing recording must survive";
    FILE *file = fopen("collision.part01.wav", "wb");
    assert(file);
    assert(fwrite(sentinel, 1, sizeof(sentinel), file) == sizeof(sentinel));
    assert(!fclose(file));
    static const char *const originals[] = {"collision.wav", "missing/path.wav"};
    unsigned close_baseline = closed_files;

    for (unsigned index = 0; index < ARRAY_LEN(originals); ++index) {
        struct sc_recording_segments segments;
        bool initialized = sc_recording_segments_init(&segments, originals[index]);
        assert(initialized);
        struct sc_recording_segment segment;
        bool prepared = sc_recording_segments_prepare(&segments, &segment);
        assert(prepared);
        struct sc_recorder recorder;
        test_init_exclusive(&recorder, segment.filename);
        bool started = sc_recorder_start(&recorder);
        assert(started);
        expect_sink_open = false;
        open_audio(&recorder);
        expect_sink_open = true;
        sc_recorder_stop(&recorder);
        sc_recorder_join(&recorder);
        assert(!ended_success && !sc_recorder_output_opened(&recorder));
        assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_FAILED);
        assert(!recorder.ctx && !recorder.exclusive_file);
        bool committed = sc_recording_segments_commit(&segments, &segment,
                                              sc_recorder_output_opened(&recorder));
        assert(!committed && segments.next_number == 1);
        sc_recorder_destroy(&recorder);
        sc_recording_segment_destroy(&segment);
    }

    assert(closed_files == close_baseline);
    file = fopen("collision.part01.wav", "rb");
    assert(file);
    char read_back[sizeof(sentinel)];
    assert(fread(read_back, 1, sizeof(read_back), file) == sizeof(read_back));
    assert(!memcmp(read_back, sentinel, sizeof(sentinel)));
    assert(!fclose(file));
    assert(!remove("collision.part01.wav"));
}

static void
test_buffer_release(void *userdata, uint8_t *data) {
    assert(!userdata);
    ++freed_buffers;
    av_free(data);
}

static AVPacket *
test_audio_packet(void) {
    enum { AUDIO_PACKET_SIZE = 3840 };
    AVPacket *packet = av_packet_alloc();
    assert(packet);
    uint8_t *data = av_mallocz(AUDIO_PACKET_SIZE);
    assert(data);
    packet->buf = av_buffer_create(data, AUDIO_PACKET_SIZE,
                                    test_buffer_release, NULL, 0);
    assert(packet->buf);
    packet->data = data;
    packet->size = AUDIO_PACKET_SIZE;
    packet->pts = 0;
    packet->dts = 0;
    packet->duration = 20000;
    return packet;
}

static void
test_finished_file(const char *filename) {
    // The reviewed runtime ships muxers, not a WAV demuxer. Inspect its bytes.
    FILE *file = fopen(filename, "rb");
    assert(file);
    unsigned char riff[8];
    assert(fread(riff, 1, sizeof(riff), file) == sizeof(riff));
    assert(!memcmp(riff, "RIFF", 4));
    assert(!fseek(file, 0, SEEK_END));
    long size = ftell(file);
    uint32_t declared = (uint32_t) riff[4] | ((uint32_t) riff[5] << 8)
        | ((uint32_t) riff[6] << 16) | ((uint32_t) riff[7] << 24);
    assert(size > 8 && declared == (uint32_t) size - 8);
    assert(!fseek(file, 8, SEEK_SET));
    char wave[4];
    assert(fread(wave, 1, sizeof(wave), file) == sizeof(wave));
    assert(!memcmp(wave, "WAVE", 4));
    bool found_audio = false;
    bool found_format = false;
    unsigned char chunk[8];

    while (fread(chunk, 1, sizeof(chunk), file) == sizeof(chunk)) {
        uint32_t length = (uint32_t) chunk[4] | ((uint32_t) chunk[5] << 8)
            | ((uint32_t) chunk[6] << 16) | ((uint32_t) chunk[7] << 24);

        if (!memcmp(chunk, "data", 4)) {
            assert(length == 3840);
            found_audio = true;
        }

        if (!memcmp(chunk, "fmt ", 4)) {
            assert(length == 16);
            unsigned char format[16];
            assert(fread(format, 1, sizeof(format), file) == sizeof(format));
            assert(format[0] == 1 && format[1] == 0); // PCM
            assert(format[2] == 2 && format[3] == 0); // stereo
            uint32_t sample_rate = (uint32_t) format[4]
                | ((uint32_t) format[5] << 8) | ((uint32_t) format[6] << 16)
                | ((uint32_t) format[7] << 24);
            assert(sample_rate == 48000 && format[14] == 16);
            found_format = true;
            continue;
        }

        assert(!fseek(file, length + (length & 1), SEEK_CUR));
    }

    assert(found_audio && found_format);
    assert(!fclose(file));
}

static void
test_two_segments_and_header_stop(void) {
    struct sc_recording_segments segments;
    static const char original[] = "actual.wav";
    bool initialized = sc_recording_segments_init(&segments, original);
    assert(initialized);
    unsigned close_baseline = closed_files;
    unsigned io_baseline = freed_io;
    unsigned trailer_baseline = written_trailers;
    unsigned buffer_baseline = freed_buffers;
    header_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    assert(header_waiting && header_release);

    for (unsigned index = 0; index < 2; ++index) {
        struct sc_recording_segment segment;
        bool prepared = sc_recording_segments_prepare(&segments, &segment);
        assert(prepared && segment.number == index + 1);
        struct sc_recorder recorder;
        test_init_exclusive(&recorder, segment.filename);
        gate_header = true;
        bool started = sc_recorder_start(&recorder);
        assert(started);
        open_audio(&recorder);
        AVPacket *packet = test_audio_packet();
        bool pushed = recorder.audio_packet_sink.ops->push(
                                      &recorder.audio_packet_sink, packet);
        assert(pushed);
        SDL_WaitSemaphore(header_waiting);
        assert(av_buffer_get_ref_count(packet->buf) == 2);
        av_packet_free(&packet);

        if (!index) {
            // Stop while the actual output header boundary is still in flight.
            sc_recorder_stop(&recorder);
        }

        SDL_SignalSemaphore(header_release);
        sc_recorder_stop(&recorder);
        sc_recorder_join(&recorder);
        assert(ended_success && sc_recorder_output_opened(&recorder));
        assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_SUCCEEDED);
        assert(!recorder.ctx && !recorder.exclusive_file);
        assert(closed_files == close_baseline + index + 1);
        assert(freed_io == io_baseline + index + 1);
        assert(written_trailers == trailer_baseline + index + 1);
        assert(freed_buffers == buffer_baseline + index + 1);
        bool committed = sc_recording_segments_commit(&segments, &segment,
                                              sc_recorder_output_opened(&recorder));
        assert(committed && segments.next_number == index + 2);
        sc_recorder_destroy(&recorder);
        gate_header = false;
        test_finished_file(segment.filename);
        assert(!remove(segment.filename));
        sc_recording_segment_destroy(&segment);
        assert(!strcmp(original, "actual.wav"));
    }

    SDL_DestroySemaphore(header_waiting);
    SDL_DestroySemaphore(header_release);
}

static void
test_partial_io_allocation(void) {
    struct sc_recording_segments segments;
    bool initialized = sc_recording_segments_init(&segments, "partial.wav");
    assert(initialized);
    struct sc_recording_segment segment;
    bool prepared = sc_recording_segments_prepare(&segments, &segment);
    assert(prepared);
    unsigned close_baseline = closed_files;
    unsigned io_baseline = freed_io;
    struct sc_recorder recorder;
    test_init_exclusive(&recorder, segment.filename);
    fail_io_allocation = true;
    bool started = sc_recorder_start(&recorder);
    assert(started);
    sc_recorder_join(&recorder);
    fail_io_allocation = false;
    assert(!ended_success && sc_recorder_output_opened(&recorder));
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_FAILED);
    assert(closed_files == close_baseline + 1 && freed_io == io_baseline);
    assert(!recorder.ctx && !recorder.exclusive_file);
    bool committed = sc_recording_segments_commit(&segments, &segment,
                                              sc_recorder_output_opened(&recorder));
    assert(committed && segments.next_number == 2);
    sc_recorder_destroy(&recorder);
    assert(!remove(segment.filename));
    sc_recording_segment_destroy(&segment);
}

static bool
test_failed_audio_decoder(struct sc_packet_sink *sink, AVCodecContext *context,
                            const struct sc_stream_session *session) {
    (void) sink;
    (void) context;
    (void) session;
    return false;
}

static void
test_unopened_close(struct sc_packet_sink *sink) {
    (void) sink;
    assert(!"An unopened failed decoder must not be closed");
}

static AVPacket *
test_video_packet(bool config) {
    AVPacket *packet = test_audio_packet();
    packet->size = 8;
    packet->data[0] = 0xff;
    packet->data[1] = 0xd8;
    packet->data[6] = 0xff;
    packet->data[7] = 0xd9;
    packet->pts = config ? AV_NOPTS_VALUE : 0;
    packet->dts = packet->pts;
    return packet;
}

/** The real unopened recorder audio sink must unblock its video-only worker. */
static void
test_optional_audio_readiness(void) {
    struct sc_recorder recorder;
    bool initialized = sc_recorder_init(&recorder, "optional-audio.mkv",
                            SC_RECORD_FORMAT_MKV, true, true, SC_ORIENTATION_0,
                            &callbacks, NULL);
    assert(initialized);
    sc_recorder_set_exclusive_output(&recorder);
    audio_config_waiting = SDL_CreateSemaphore(0);
    header_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    assert(audio_config_waiting && header_waiting && header_release);
    audio_wait_recorder = &recorder;
    gate_header = true;
    unsigned buffer_baseline = freed_buffers;
    unsigned trailer_baseline = written_trailers;
    unsigned close_baseline = closed_files;
    bool started = sc_recorder_start(&recorder);
    assert(started);
    AVCodecContext *video_context = avcodec_alloc_context3(NULL);
    assert(video_context);
    video_context->codec_id = AV_CODEC_ID_MJPEG;
    video_context->codec_type = AVMEDIA_TYPE_VIDEO;
    video_context->width = 64;
    video_context->height = 32;
    video_context->pix_fmt = AV_PIX_FMT_YUVJ420P;
    bool opened = recorder.video_packet_sink.ops->open(
                            &recorder.video_packet_sink, video_context, NULL);
    assert(opened);
    avcodec_free_context(&video_context);
    AVPacket *config = test_video_packet(true);
    AVPacket *frame = test_video_packet(false);
    bool pushed = recorder.video_packet_sink.ops->push(
                            &recorder.video_packet_sink, config);
    assert(pushed);
    pushed = recorder.video_packet_sink.ops->push(&recorder.video_packet_sink, frame);
    assert(pushed);
    SDL_WaitSemaphore(audio_config_waiting);
    assert(av_buffer_get_ref_count(config->buf) == 2);
    assert(av_buffer_get_ref_count(frame->buf) == 2);
    av_packet_free(&config);
    av_packet_free(&frame);
    static const struct sc_packet_sink_ops failed_ops = {
        .open = test_failed_audio_decoder,
        .close = test_unopened_close,
    };
    struct sc_packet_sink failed_decoder = {.ops = &failed_ops};
    struct sc_packet_source source;
    sc_packet_source_init(&source);
    sc_packet_source_add_sink(&source, &failed_decoder);
    sc_packet_source_add_sink(&source, &recorder.audio_packet_sink);
    AVCodecContext *audio_context = avcodec_alloc_context3(NULL);
    assert(audio_context);
    opened = sc_packet_source_sinks_open(&source, audio_context, NULL);
    assert(!opened);
    avcodec_free_context(&audio_context);
    sc_mutex_lock(&recorder.mutex);
    bool audio_settled = recorder.audio_init && !recorder.audio;
    sc_mutex_unlock(&recorder.mutex);
    assert(audio_settled);
    // No Stop has occurred: the worker must reach its real output header now.
    SDL_WaitSemaphore(header_waiting);
    SDL_SignalSemaphore(header_release);
    sc_recorder_stop(&recorder);
    sc_recorder_join(&recorder);
    assert(ended_success && sc_recorder_output_opened(&recorder));
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_SUCCEEDED);
    assert(header_stream_count == 1 && recorder.audio_stream.index == -1);
    assert(written_trailers == trailer_baseline + 1);
    assert(closed_files == close_baseline + 1);
    assert(freed_buffers == buffer_baseline + 2);
    assert(sc_vecdeque_is_empty(&recorder.video_queue));
    assert(sc_vecdeque_is_empty(&recorder.audio_queue));
    sc_recorder_destroy(&recorder);
    audio_wait_recorder = NULL;
    gate_header = false;
    SDL_DestroySemaphore(audio_config_waiting);
    SDL_DestroySemaphore(header_waiting);
    SDL_DestroySemaphore(header_release);
    assert(!remove("optional-audio.mkv"));
}

enum recording_failure {
    RECORDING_HEADER_FAILURE,
    RECORDING_AUDIO_PACKET_FAILURE,
    RECORDING_VIDEO_PACKET_FAILURE,
    RECORDING_AVIO_FAILURE,
    RECORDING_FILE_CLOSE_FAILURE,
    RECORDING_LAST_VIDEO_FAILURE,
};

/** Preserve real refs and output ownership on every bounded output error path. */
static void
test_recording_failure(enum recording_failure failure) {
    bool video = failure == RECORDING_AUDIO_PACKET_FAILURE
        || failure == RECORDING_VIDEO_PACKET_FAILURE
        || failure == RECORDING_LAST_VIDEO_FAILURE;
    bool audio = failure != RECORDING_VIDEO_PACKET_FAILURE
        && failure != RECORDING_LAST_VIDEO_FAILURE;
    char filename[48];
    int length = snprintf(filename, sizeof(filename), "failure-%u.mkv", failure);
    assert(length > 0 && (size_t) length < sizeof(filename));
    struct sc_recorder recorder;
    bool initialized = sc_recorder_init(&recorder, filename, SC_RECORD_FORMAT_MKV,
                            video, audio, SC_ORIENTATION_0, &callbacks, NULL);
    assert(initialized);
    sc_recorder_set_exclusive_output(&recorder);
    header_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    assert(header_waiting && header_release);
    gate_header = true;
    fail_header_write = failure == RECORDING_HEADER_FAILURE;
    fail_audio_write = failure == RECORDING_AUDIO_PACKET_FAILURE;
    fail_video_write = failure == RECORDING_VIDEO_PACKET_FAILURE
        || failure == RECORDING_LAST_VIDEO_FAILURE;
    fail_output_write = failure == RECORDING_AVIO_FAILURE;
    fail_file_close = failure == RECORDING_FILE_CLOSE_FAILURE;
    unsigned buffer_baseline = freed_buffers;
    unsigned close_baseline = closed_files;
    unsigned io_baseline = freed_io;
    unsigned packet_count = 0;
    bool started = sc_recorder_start(&recorder);
    assert(started);

    if (video) {
        AVCodecContext *context = avcodec_alloc_context3(NULL);
        assert(context);
        context->codec_id = AV_CODEC_ID_MJPEG;
        context->codec_type = AVMEDIA_TYPE_VIDEO;
        context->width = 64;
        context->height = 32;
        context->pix_fmt = AV_PIX_FMT_YUVJ420P;
        bool opened = recorder.video_packet_sink.ops->open(
                            &recorder.video_packet_sink, context, NULL);
        assert(opened);
        avcodec_free_context(&context);
        AVPacket *config = test_video_packet(true);
        bool pushed = recorder.video_packet_sink.ops->push(
                            &recorder.video_packet_sink, config);
        assert(pushed);
        av_packet_free(&config);
        ++packet_count;
    }

    if (audio) {
        open_audio(&recorder);
    }

    SDL_WaitSemaphore(header_waiting);
    AVBufferRef *previous_probe = NULL;

    if (video) {
        AVPacket *frame = test_video_packet(false);
        previous_probe = av_buffer_ref(frame->buf);
        assert(previous_probe);
        bool pushed = recorder.video_packet_sink.ops->push(
                            &recorder.video_packet_sink, frame);
        assert(pushed);
        av_packet_free(&frame);
        ++packet_count;

        if (!audio && failure != RECORDING_LAST_VIDEO_FAILURE) {
            frame = test_video_packet(false);
            frame->pts = 20000;
            frame->dts = frame->pts;
            pushed = recorder.video_packet_sink.ops->push(
                            &recorder.video_packet_sink, frame);
            assert(pushed);
            av_packet_free(&frame);
            ++packet_count;
        }
    }

    if (audio) {
        AVPacket *packet = test_audio_packet();
        bool pushed = recorder.audio_packet_sink.ops->push(
                            &recorder.audio_packet_sink, packet);
        assert(pushed);
        av_packet_free(&packet);
        ++packet_count;
    }

    sc_recorder_stop(&recorder);
    SDL_SignalSemaphore(header_release);
    sc_recorder_join(&recorder);
    assert(!ended_success && sc_recorder_output_opened(&recorder));
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_FAILED);
    assert(!recorder.ctx && !recorder.exclusive_file);
    assert(closed_files == close_baseline + 1 && freed_io == io_baseline + 1);

    if (previous_probe) {
        assert(av_buffer_get_ref_count(previous_probe) == 1);
        av_buffer_unref(&previous_probe);
    }

    assert(freed_buffers == buffer_baseline + packet_count);
    assert(sc_vecdeque_is_empty(&recorder.video_queue));
    assert(sc_vecdeque_is_empty(&recorder.audio_queue));
    sc_recorder_destroy(&recorder);
    fail_header_write = false;
    fail_audio_write = false;
    fail_video_write = false;
    fail_output_write = false;
    fail_file_close = false;
    gate_header = false;
    SDL_DestroySemaphore(header_waiting);
    SDL_DestroySemaphore(header_release);
    assert(!remove(filename));
}

static void
test_cancel_before_media(bool close_failure) {
    struct sc_recorder recorder;
    const char *filename = close_failure ? "cancel-close-failure.wav"
        : "cancel-before-media.wav";
    test_init_exclusive(&recorder, filename);
    fail_file_close = close_failure;
    initial_wait_recorder = &recorder;
    initial_config_waiting = SDL_CreateSemaphore(0);
    assert(initial_config_waiting);
    unsigned header_baseline = header_calls;
    unsigned close_baseline = closed_files;
    bool started = sc_recorder_start(&recorder);
    assert(started);
    SDL_WaitSemaphore(initial_config_waiting);
    sc_recorder_stop(&recorder);
    sc_recorder_join(&recorder);
    assert(ended_success == !close_failure);
    assert(sc_recorder_get_result(&recorder) == (close_failure
        ? SC_RECORDER_RESULT_FAILED : SC_RECORDER_RESULT_CANCELLED_BEFORE_MEDIA));
    assert(header_calls == header_baseline);
    assert(closed_files == close_baseline + 1);
    assert(sc_recorder_output_opened(&recorder));
    sc_recorder_destroy(&recorder);
    fail_file_close = false;
    initial_wait_recorder = NULL;
    SDL_DestroySemaphore(initial_config_waiting);
    assert(!remove(filename));
}

/** Keep the real worker inside header output while packet admission fills. */
static void
test_retained_packet_limit(bool video, bool bytes) {
    const unsigned retained_limit = 4096;
    struct sc_recorder recorder;
    const char *filename = video ? "bounded-recorder.mkv" : "bounded-recorder.wav";

    if (video) {
        assert(sc_recorder_init(&recorder, filename, SC_RECORD_FORMAT_MKV,
                                true, false, SC_ORIENTATION_0, &callbacks, NULL));
        sc_recorder_set_exclusive_output(&recorder);
    } else {
        test_init_exclusive(&recorder, filename);
    }
    gate_header = true;
    header_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    assert(header_waiting && header_release);
    assert(sc_recorder_start(&recorder));

    if (video) {
        AVCodecContext *context = avcodec_alloc_context3(NULL);
        assert(context);
        context->codec_id = AV_CODEC_ID_MJPEG;
        context->codec_type = AVMEDIA_TYPE_VIDEO;
        context->width = 64;
        context->height = 32;
        context->pix_fmt = AV_PIX_FMT_YUVJ420P;
        assert(recorder.video_packet_sink.ops->open(&recorder.video_packet_sink,
                                                   context, NULL));
        avcodec_free_context(&context);
        AVPacket *config = test_video_packet(true);
        assert(recorder.video_packet_sink.ops->push(&recorder.video_packet_sink,
                                                   config));
        av_packet_free(&config);
    } else {
        open_audio(&recorder);
    }

    SDL_WaitSemaphore(header_waiting);
    AVPacket *packet = video ? test_video_packet(false) : test_audio_packet();
    AVBufferRef *probe = NULL;

    if (bytes) {
        av_buffer_unref(&packet->buf);
        packet->buf = av_buffer_alloc(2 * 1024 * 1024);
        assert(packet->buf);
        packet->data = packet->buf->data;
        // A small visible slice must still retain/charge its whole backing.
        packet->size = 8;
        packet->opaque_ref = av_buffer_alloc(1024 * 1024);
        assert(packet->opaque_ref);
        assert(av_packet_new_side_data(packet, AV_PKT_DATA_SKIP_SAMPLES, 1024));
        probe = av_buffer_ref(packet->buf);
        assert(probe);
    }

    struct sc_packet_sink *sink = video ? &recorder.video_packet_sink
                                        : &recorder.audio_packet_sink;
    unsigned admitted = video ? 1 : 0; // the popped video config is still owned
    reservation_probe = &recorder;

    while (admitted < retained_limit && sink->ops->push(sink, packet)) {
        ++admitted;
    }

    bool overflow = sink->ops->push(sink, packet);
    reservation_probe = NULL;
    printf("recorder retained probe: %u admissions including %u in-flight config; overflow=%d\n",
           admitted, video, overflow);
    fflush(stdout);
    assert(!overflow);

    if (bytes) {
        assert(admitted < retained_limit);
        assert(av_buffer_get_ref_count(probe) == (int) admitted + 2);
    } else {
        assert(admitted == retained_limit);
    }

    sc_mutex_lock(&recorder.mutex);
    assert(recorder.retained_packets == admitted);
    assert(recorder.retained_bytes <= SC_RECORDER_MAX_RETAINED_BYTES);
    assert(recorder.admission_failed);
    sc_mutex_unlock(&recorder.mutex);
    av_packet_free(&packet);
    SDL_SignalSemaphore(header_release);
    sc_recorder_stop(&recorder);
    sc_recorder_join(&recorder);
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_FAILED);
    assert(!ended_success);
    assert(!recorder.retained_packets && !recorder.retained_bytes);

    if (probe) {
        assert(av_buffer_get_ref_count(probe) == 1);
        av_buffer_unref(&probe);
    }

    sc_recorder_destroy(&recorder);
    gate_header = false;
    SDL_DestroySemaphore(header_waiting);
    SDL_DestroySemaphore(header_release);
    assert(!remove(filename));
}

/** Two popped video packets remain charged while the previous write blocks. */
static void
test_retained_video_lookahead(void) {
    struct sc_recorder recorder;
    const char *filename = "bounded-lookahead.mkv";
    assert(sc_recorder_init(&recorder, filename, SC_RECORD_FORMAT_MKV,
                            true, false, SC_ORIENTATION_0, &callbacks, NULL));
    sc_recorder_set_exclusive_output(&recorder);
    packet_write_waiting = SDL_CreateSemaphore(0);
    packet_write_release = SDL_CreateSemaphore(0);
    assert(packet_write_waiting && packet_write_release);
    gate_packet_write = true;
    assert(sc_recorder_start(&recorder));
    AVCodecContext *context = avcodec_alloc_context3(NULL);
    assert(context);
    context->codec_id = AV_CODEC_ID_MJPEG;
    context->codec_type = AVMEDIA_TYPE_VIDEO;
    context->width = 64;
    context->height = 32;
    context->pix_fmt = AV_PIX_FMT_YUVJ420P;
    assert(recorder.video_packet_sink.ops->open(&recorder.video_packet_sink,
                                               context, NULL));
    avcodec_free_context(&context);
    AVPacket *config = test_video_packet(true);
    AVPacket *frame = test_video_packet(false);
    AVBufferRef *probe = av_buffer_ref(frame->buf);
    assert(probe);
    struct sc_packet_sink *sink = &recorder.video_packet_sink;
    assert(sink->ops->push(sink, config));
    av_packet_free(&config);
    assert(sink->ops->push(sink, frame));
    frame->pts = frame->dts = 1000;
    assert(sink->ops->push(sink, frame));
    SDL_WaitSemaphore(packet_write_waiting);
    sc_mutex_lock(&recorder.mutex);
    assert(recorder.retained_packets == 2);
    assert(sc_vecdeque_is_empty(&recorder.video_queue));
    sc_mutex_unlock(&recorder.mutex);

    for (unsigned index = 2; index < SC_RECORDER_MAX_RETAINED_PACKETS; ++index) {
        assert(sink->ops->push(sink, frame));
    }

    assert(!sink->ops->push(sink, frame));
    av_packet_free(&frame);
    SDL_SignalSemaphore(packet_write_release);
    sc_recorder_stop(&recorder);
    sc_recorder_join(&recorder);
    assert(sc_recorder_get_result(&recorder) == SC_RECORDER_RESULT_FAILED);
    assert(!recorder.retained_packets && !recorder.retained_bytes);
    assert(av_buffer_get_ref_count(probe) == 1);
    av_buffer_unref(&probe);
    sc_recorder_destroy(&recorder);
    SDL_DestroySemaphore(packet_write_waiting);
    SDL_DestroySemaphore(packet_write_release);
    assert(!remove(filename));
}

struct pending_recording_push {
    struct sc_recorder *recorder;
    AVPacket *packet;
    bool accepted;
};

static int
push_pending_recording(void *userdata) {
    struct pending_recording_push *push = userdata;
    struct sc_packet_sink *sink = &push->recorder->audio_packet_sink;
    push->accepted = sink->ops->push(sink, push->packet);
    return 0;
}

/** A reserved candidate may fail after the worker successfully closes output. */
static void
test_late_candidate_result(bool fail_reference) {
    struct sc_recorder recorder;
    const char *filename = "late-candidate.wav";
    test_init_exclusive(&recorder, filename);
    gate_header = true;
    header_waiting = SDL_CreateSemaphore(0);
    header_release = SDL_CreateSemaphore(0);
    candidate_waiting = SDL_CreateSemaphore(0);
    candidate_release = SDL_CreateSemaphore(0);
    assert(header_waiting && header_release && candidate_waiting && candidate_release);
    assert(sc_recorder_start(&recorder));
    open_audio(&recorder);
    SDL_WaitSemaphore(header_waiting);
    AVPacket *packet = test_audio_packet();
    struct pending_recording_push push = {.recorder = &recorder, .packet = packet};
    gate_candidate_reference = true;
    fail_candidate_reference = fail_reference;
    sc_thread producer;
    assert(sc_thread_create(&producer, push_pending_recording, "late-rec-push", &push));
    SDL_WaitSemaphore(candidate_waiting);
    sc_recorder_stop(&recorder);
    SDL_SignalSemaphore(header_release);
    sc_recorder_join(&recorder);
    assert(ended_success); // truthful earlier worker completion, not final receipt
    sc_mutex_lock(&recorder.mutex);
    assert(recorder.retained_packets == 1);
    assert(sc_vecdeque_is_empty(&recorder.audio_queue));
    sc_mutex_unlock(&recorder.mutex);
    SDL_SignalSemaphore(candidate_release);
    sc_thread_join(&producer, NULL);
    assert(!push.accepted);

    if (!fail_reference) {
        AVPacket unavailable = {.size = -1};
        // An unadmitted call after Stop is only a retirement rejection.
        assert(!recorder.audio_packet_sink.ops->push(&recorder.audio_packet_sink,
                                                    &unavailable));
        assert(!recorder.admission_failed);
    }
    enum sc_recorder_result result = sc_recorder_get_result(&recorder);
    printf("late recording candidate: failure=%d worker_callback_success=%d final=%u\n",
           fail_reference, ended_success, result);
    fflush(stdout);
    assert(result == (fail_reference ? SC_RECORDER_RESULT_FAILED
                                     : SC_RECORDER_RESULT_SUCCEEDED));
    assert(!recorder.retained_packets && !recorder.retained_bytes);
    assert(av_buffer_get_ref_count(packet->buf) == 1);
    av_packet_free(&packet);
    sc_recorder_destroy(&recorder);
    gate_header = false;
    fail_candidate_reference = false;
    SDL_DestroySemaphore(header_waiting);
    SDL_DestroySemaphore(header_release);
    SDL_DestroySemaphore(candidate_waiting);
    SDL_DestroySemaphore(candidate_release);
    assert(!remove(filename));
}

int
main(int argument_count, char **arguments) {
    assert(SDL_Init(0));
    char *previous_directory = SDL_GetCurrentDirectory();
    assert(previous_directory);
    char directory[80];
    int length = snprintf(directory, sizeof(directory), "recording-segments-%" PRIu64,
                            (uint64_t) SDL_GetTicksNS());
    assert(length > 0 && (size_t) length < sizeof(directory));
#ifdef _WIN32
    assert(!_mkdir(directory));
    assert(!_chdir(directory));
#else
    assert(!mkdir(directory, 0700));
    assert(!chdir(directory));
#endif
    if (argument_count == 2 && !strcmp(arguments[1], "--last-video-packet")) {
        test_recording_failure(RECORDING_LAST_VIDEO_FAILURE);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--late-candidate")) {
        test_late_candidate_result(true);
        test_late_candidate_result(false);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--limits")) {
        test_retained_packet_limit(false, false);
        test_retained_packet_limit(true, false);
        test_retained_packet_limit(false, true);
        test_retained_video_lookahead();
        test_late_candidate_result(true);
        test_late_candidate_result(false);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--audio-readiness")) {
        test_optional_audio_readiness();
    } else if (argument_count == 2 && !strcmp(arguments[1], "--previous-ref")) {
        test_recording_failure(RECORDING_AUDIO_PACKET_FAILURE);
    } else if (argument_count == 2 && !strcmp(arguments[1], "--cancel-before-media")) {
        test_cancel_before_media(false);
    } else {
        test_context_publication();
        test_paths();
        test_failed_open_and_collision();
        test_two_segments_and_header_stop();
        test_partial_io_allocation();
        test_optional_audio_readiness();

        for (enum recording_failure failure = RECORDING_HEADER_FAILURE;
                failure <= RECORDING_LAST_VIDEO_FAILURE; ++failure) {
            test_recording_failure(failure);
        }
        test_cancel_before_media(false);
        test_cancel_before_media(true);
        test_retained_packet_limit(false, false);
        test_retained_packet_limit(true, false);
        test_retained_packet_limit(false, true);
        test_retained_video_lookahead();
        test_late_candidate_result(true);
        test_late_candidate_result(false);
    }
#ifdef _WIN32
    assert(!_chdir(previous_directory));
    assert(!_rmdir(directory));
#else
    assert(!chdir(previous_directory));
    assert(!rmdir(directory));
#endif
    SDL_free(previous_directory);
    SDL_Quit();
    puts("Recording segments: production-linked cases passed "
         "(including real optional-audio readiness/trailer/ref settlement)");
    return 0;
}
