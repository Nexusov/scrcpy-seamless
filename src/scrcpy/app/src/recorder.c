#include "recorder.h"

#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <stdlib.h>
#include <stdatomic.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/time.h>
#include <libavutil/display.h>

#ifdef _WIN32
# include <io.h>
# include <sys/stat.h>
#else
# include <unistd.h>
#endif

#include "util/log.h"
#include "util/str.h"

/** Downcast packet sinks to recorder */
#define DOWNCAST_VIDEO(SINK) \
    container_of(SINK, struct sc_recorder, video_packet_sink)
#define DOWNCAST_AUDIO(SINK) \
    container_of(SINK, struct sc_recorder, audio_packet_sink)

static const AVRational SCRCPY_TIME_BASE = {1, 1000000}; // timestamps in us

enum { SC_RECORDER_IO_BUFFER_SIZE = 32768 };

struct sc_recorder_packet_lease {
    struct sc_recorder *recorder;
    AVBufferRef *previous_opaque;
    AVBufferRef *previous_buffer;
    size_t charge;
    atomic_uint guards;
    bool committed;
};

static const AVOutputFormat *
find_muxer(const char *name) {
#ifdef SCRCPY_LAVF_HAS_NEW_MUXER_ITERATOR_API
    void *opaque = NULL;
#endif
    const AVOutputFormat *oformat = NULL;
    do {
#ifdef SCRCPY_LAVF_HAS_NEW_MUXER_ITERATOR_API
        oformat = av_muxer_iterate(&opaque);
#else
        oformat = av_oformat_next(oformat);
#endif
        // until null or containing the requested name
    } while (oformat && !sc_str_list_contains(oformat->name, ',', name));
    return oformat;
}

static AVPacket *
sc_recorder_packet_ref(const AVPacket *packet) {
    AVPacket *p = av_packet_alloc();
    if (!p) {
        LOG_OOM();
        return NULL;
    }

    if (av_packet_ref(p, packet)) {
        av_packet_free(&p);
        return NULL;
    }

    return p;
}

static void
sc_recorder_release_charge(struct sc_recorder *recorder, size_t charge) {
    sc_mutex_lock(&recorder->mutex);
    assert(recorder->retained_packets && recorder->retained_bytes >= charge);
    --recorder->retained_packets;
    recorder->retained_bytes -= charge;
    sc_mutex_unlock(&recorder->mutex);
}

/** Public packet properties carry quota through copies and muxer retention. */
static void
sc_recorder_packet_lease_release(void *userdata, uint8_t *data) {
    (void) data;
    struct sc_recorder_packet_lease *lease = userdata;

    if (atomic_fetch_sub(&lease->guards, 1) != 1) {
        return;
    }

    struct sc_recorder *recorder = lease->recorder;
    size_t charge = lease->charge;
    bool committed = lease->committed;

    if (committed) {
        // Both public guards settled, including payload destruction after opaque.
        av_buffer_unref(&lease->previous_buffer);
        av_buffer_unref(&lease->previous_opaque);
    }

    free(lease);

    if (committed) {
        sc_recorder_release_charge(recorder, charge);
    }
}

static bool
sc_recorder_packet_lease_attach(struct sc_recorder *recorder, AVPacket *packet,
                                 size_t charge) {
    struct sc_recorder_packet_lease *lease = malloc(sizeof(*lease));

    if (!lease) {
        return false;
    }

    AVBufferRef *previous = packet->opaque_ref;
    *lease = (struct sc_recorder_packet_lease) {
        .recorder = recorder, .previous_opaque = previous,
        .previous_buffer = packet->buf, .charge = charge,
    };
    atomic_init(&lease->guards, 1);
    AVBufferRef *reference = av_buffer_create(previous ? previous->data : NULL,
        previous ? previous->size : 0, sc_recorder_packet_lease_release, lease, 0);

    if (!reference) {
        // Creation failure leaves the prior opaque reference with the packet.
        free(lease);
        return false;
    }

    AVBufferRef *buffer = NULL;

    if (packet->buf) {
        buffer = av_buffer_create(packet->buf->data, packet->buf->size,
            sc_recorder_packet_lease_release, lease, AV_BUFFER_FLAG_READONLY);

        if (!buffer) {
            av_buffer_unref(&reference);
            return false;
        }

        atomic_store(&lease->guards, 2);
    }

    lease->committed = true;
    packet->opaque_ref = reference;
    packet->buf = buffer;
    return true;
}

static void
sc_recorder_queue_clear(struct sc_recorder_queue *queue) {
    while (!sc_vecdeque_is_empty(queue)) {
        AVPacket *packet = sc_vecdeque_pop(queue);
        av_packet_free(&packet);
    }
}

static bool
sc_recorder_charge_add(size_t *charge, size_t bytes) {

    if (bytes > SC_RECORDER_MAX_RETAINED_BYTES - *charge) {
        return false;
    }

    *charge += bytes;
    return true;
}

static bool
sc_recorder_packet_charge(const AVPacket *packet, size_t *charge) {
    *charge = sizeof(AVPacket) + sizeof(AVPacket *)
        + sizeof(struct sc_recorder_packet_lease) + 2 * sizeof(AVBufferRef);

    if (packet->size < 0 || packet->side_data_elems < 0) {
        return false;
    }

    size_t bytes = packet->buf ? packet->buf->size
        : (size_t) packet->size + AV_INPUT_BUFFER_PADDING_SIZE;

    if (!sc_recorder_charge_add(charge, sizeof(AVBufferRef))
            || !sc_recorder_charge_add(charge, bytes)) {
        return false;
    }

    for (int index = 0; index < packet->side_data_elems; ++index) {

        if (!sc_recorder_charge_add(charge, sizeof(AVPacketSideData))
                || !sc_recorder_charge_add(charge, packet->side_data[index].size)) {
            return false;
        }
    }

    return !packet->opaque_ref
        || (sc_recorder_charge_add(charge, sizeof(AVBufferRef))
            && sc_recorder_charge_add(charge, packet->opaque_ref->size));
}

static void
sc_recorder_admission_failed_locked(struct sc_recorder *recorder) {
    recorder->admission_failed = true;
    recorder->stopped = true;
    sc_cond_broadcast(&recorder->cond);
}

static void
sc_recorder_admission_failed(struct sc_recorder *recorder) {
    sc_mutex_lock(&recorder->mutex);
    sc_recorder_admission_failed_locked(recorder);
    sc_mutex_unlock(&recorder->mutex);
}

static bool
sc_recorder_queue_push(struct sc_recorder *recorder,
                        struct sc_recorder_queue *queue, int stream_index,
                        const AVPacket *packet) {
    size_t charge;

    if (!sc_recorder_packet_charge(packet, &charge)) {
        sc_mutex_lock(&recorder->mutex);
        bool active = !recorder->stopped;

        if (active) {
            sc_recorder_admission_failed_locked(recorder);
        }

        sc_mutex_unlock(&recorder->mutex);

        if (active) {
            LOGE("Recorder packet exceeds retained-memory limit");
        }

        return false;
    }

    sc_mutex_lock(&recorder->mutex);

    if (recorder->stopped) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    if (recorder->retained_packets == SC_RECORDER_MAX_RETAINED_PACKETS
            || charge > SC_RECORDER_MAX_RETAINED_BYTES - recorder->retained_bytes) {
        sc_recorder_admission_failed_locked(recorder);
        sc_mutex_unlock(&recorder->mutex);
        LOGE("Recorder retained-memory limit reached");
        return false;
    }

    ++recorder->retained_packets;
    recorder->retained_bytes += charge;
    sc_mutex_unlock(&recorder->mutex);

    AVPacket *reference = sc_recorder_packet_ref(packet);

    if (!reference) {
        sc_recorder_admission_failed(recorder);
        sc_recorder_release_charge(recorder, charge);
        LOG_OOM();
        return false;
    }

    if (!sc_recorder_packet_lease_attach(recorder, reference, charge)) {
        sc_recorder_admission_failed(recorder);
        av_packet_free(&reference);
        sc_recorder_release_charge(recorder, charge);
        LOG_OOM();
        return false;
    }

    reference->stream_index = stream_index;
    sc_mutex_lock(&recorder->mutex);

    if (recorder->stopped) {
        sc_mutex_unlock(&recorder->mutex);
        av_packet_free(&reference);
        return false;
    }

    bool admitted = sc_vecdeque_push(queue, reference);

    if (admitted) {
        sc_cond_signal(&recorder->cond);
    }

    if (!admitted) {
        sc_recorder_admission_failed_locked(recorder);
    }

    sc_mutex_unlock(&recorder->mutex);

    if (!admitted) {
        av_packet_free(&reference);
        LOG_OOM();
    }

    return admitted;
}

static const char *
sc_recorder_get_format_name(enum sc_record_format format) {
    switch (format) {
        case SC_RECORD_FORMAT_MP4:
        case SC_RECORD_FORMAT_M4A:
        case SC_RECORD_FORMAT_AAC:
            return "mp4";
        case SC_RECORD_FORMAT_MKV:
        case SC_RECORD_FORMAT_MKA:
            return "matroska";
        case SC_RECORD_FORMAT_OPUS:
            return "opus";
        case SC_RECORD_FORMAT_FLAC:
            return "flac";
        case SC_RECORD_FORMAT_WAV:
            return "wav";
        default:
            return NULL;
    }
}

static bool
sc_recorder_set_extradata(AVStream *ostream, const AVPacket *packet) {
    uint8_t *extradata = av_malloc(packet->size * sizeof(uint8_t));
    if (!extradata) {
        LOG_OOM();
        return false;
    }

    // copy the first packet to the extra data
    memcpy(extradata, packet->data, packet->size);

    ostream->codecpar->extradata = extradata;
    ostream->codecpar->extradata_size = packet->size;
    return true;
}

static inline void
sc_recorder_rescale_packet(AVStream *stream, AVPacket *packet) {
    av_packet_rescale_ts(packet, SCRCPY_TIME_BASE, stream->time_base);
}

static bool
sc_recorder_write_stream(struct sc_recorder *recorder,
                         struct sc_recorder_stream *st, AVPacket *packet) {
    AVStream *stream = recorder->ctx->streams[st->index];
    sc_recorder_rescale_packet(stream, packet);
    if (st->last_pts != AV_NOPTS_VALUE && packet->pts <= st->last_pts) {
        LOGD("Fixing PTS non monotonically increasing in stream %d "
             "(%" PRIi64 " >= %" PRIi64 ")",
             st->index, st->last_pts, packet->pts);
        packet->pts = ++st->last_pts;
        packet->dts = packet->pts;
    } else {
        st->last_pts = packet->pts;
    }
    return av_interleaved_write_frame(recorder->ctx, packet) >= 0;
}

static inline bool
sc_recorder_write_video(struct sc_recorder *recorder, AVPacket *packet) {
    return sc_recorder_write_stream(recorder, &recorder->video_stream, packet);
}

static inline bool
sc_recorder_write_audio(struct sc_recorder *recorder, AVPacket *packet) {
    return sc_recorder_write_stream(recorder, &recorder->audio_stream, packet);
}

static int
sc_recorder_write_file(void *userdata, const uint8_t *buffer, int size) {
    FILE *file = userdata;
    size_t written = fwrite(buffer, 1, size, file);

    if (written != (size_t) size) {
        return AVERROR(errno ? errno : EIO);
    }

    return size;
}

static int64_t
sc_recorder_seek_file(void *userdata, int64_t offset, int whence) {
    FILE *file = userdata;
#ifdef _WIN32
# define SC_RECORDER_SEEK _fseeki64
# define SC_RECORDER_TELL _ftelli64
#else
# define SC_RECORDER_SEEK fseeko
# define SC_RECORDER_TELL ftello
#endif

    if (whence == AVSEEK_SIZE) {
        int64_t current = SC_RECORDER_TELL(file);

        if (current < 0 || SC_RECORDER_SEEK(file, 0, SEEK_END)) {
            return AVERROR(errno ? errno : EIO);
        }

        int64_t size = SC_RECORDER_TELL(file);

        if (SC_RECORDER_SEEK(file, current, SEEK_SET) || size < 0) {
            return AVERROR(errno ? errno : EIO);
        }

        return size;
    }

    whence &= ~AVSEEK_FORCE;

    if (whence != SEEK_SET && whence != SEEK_CUR && whence != SEEK_END) {
        return AVERROR(EINVAL);
    }

    if (SC_RECORDER_SEEK(file, offset, whence)) {
        return AVERROR(errno ? errno : EIO);
    }

    int64_t position = SC_RECORDER_TELL(file);
    return position < 0 ? AVERROR(errno ? errno : EIO) : position;
#undef SC_RECORDER_SEEK
#undef SC_RECORDER_TELL
}

static bool
sc_recorder_open_exclusive(struct sc_recorder *recorder) {
#ifdef _WIN32
    wchar_t *path = sc_str_to_wchars(recorder->filename);

    if (!path) {
        return false;
    }

    int descriptor = _wopen(path, _O_CREAT | _O_EXCL | _O_WRONLY | _O_BINARY,
                             _S_IREAD | _S_IWRITE);
    free(path);
#else
    int descriptor = open(recorder->filename, O_CREAT | O_EXCL | O_WRONLY, 0666);
#endif

    if (descriptor == -1) {
        return false;
    }

    // Creation succeeded even if a later allocation/header write fails.
    recorder->output_opened = true;
#ifdef _WIN32
    FILE *file = _fdopen(descriptor, "wb");
#else
    FILE *file = fdopen(descriptor, "wb");
#endif

    if (!file) {
#ifdef _WIN32
        _close(descriptor);
#else
        close(descriptor);
#endif
        return false;
    }

    uint8_t *buffer = av_malloc(SC_RECORDER_IO_BUFFER_SIZE);

    if (!buffer) {
        fclose(file);
        return false;
    }

    AVIOContext *io = avio_alloc_context(buffer, SC_RECORDER_IO_BUFFER_SIZE,
                                         1, file, NULL, sc_recorder_write_file,
                                         sc_recorder_seek_file);

    if (!io) {
        av_free(buffer);
        fclose(file);
        return false;
    }

    io->seekable = AVIO_SEEKABLE_NORMAL;
    recorder->exclusive_file = file;
    recorder->ctx->pb = io;
    recorder->ctx->flags |= AVFMT_FLAG_CUSTOM_IO;
    return true;
}

static bool
sc_recorder_open_output_file(struct sc_recorder *recorder) {
    const char *format_name = sc_recorder_get_format_name(recorder->format);
    assert(format_name);
    const AVOutputFormat *format = find_muxer(format_name);
    if (!format) {
        LOGE("Could not find muxer");
        return false;
    }

    recorder->ctx = avformat_alloc_context();
    if (!recorder->ctx) {
        LOG_OOM();
        return false;
    }

    bool opened;

    if (recorder->exclusive_output) {
        opened = sc_recorder_open_exclusive(recorder);
    } else {
        char *file_url = sc_str_concat("file:", recorder->filename);

        if (!file_url) {
            avformat_free_context(recorder->ctx);
            recorder->ctx = NULL;
            return false;
        }

        opened = avio_open(&recorder->ctx->pb, file_url, AVIO_FLAG_WRITE) >= 0;
        free(file_url);
    }

    if (!opened) {
        LOGE("Failed to open output file: %s", recorder->filename);
        avformat_free_context(recorder->ctx);
        recorder->ctx = NULL;
        return false;
    }

    // contrary to the deprecated API (av_oformat_next()), av_muxer_iterate()
    // returns (on purpose) a pointer-to-const, but AVFormatContext.oformat
    // still expects a pointer-to-non-const (it has not be updated accordingly)
    // <https://github.com/FFmpeg/FFmpeg/commit/0694d8702421e7aff1340038559c438b61bb30dd>
    recorder->ctx->oformat = (AVOutputFormat *) format;

    av_dict_set(&recorder->ctx->metadata, "comment",
                "Recorded by scrcpy " SCRCPY_VERSION, 0);

    sc_mutex_lock(&recorder->mutex);
    recorder->output_opened = true;
    recorder->output_ready = true;
    sc_cond_broadcast(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);

    LOGI("Recording started to %s file: %s", format_name, recorder->filename);
    return true;
}

static bool
sc_recorder_close_output_file(struct sc_recorder *recorder) {
    sc_mutex_lock(&recorder->mutex);
    recorder->stopped = true;
    sc_cond_broadcast(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);
    bool closed;

    if (recorder->exclusive_output) {
        avio_flush(recorder->ctx->pb);
        int io_error = recorder->ctx->pb->error;
        av_freep(&recorder->ctx->pb->buffer);
        avio_context_free(&recorder->ctx->pb);
        int file_error = fclose(recorder->exclusive_file);
        recorder->exclusive_file = NULL;
        closed = io_error >= 0 && !file_error;
    } else {
        closed = avio_close(recorder->ctx->pb) >= 0;
    }

    avformat_free_context(recorder->ctx);
    recorder->ctx = NULL;
    return closed;
}

static inline bool
sc_recorder_must_wait_for_config_packets(struct sc_recorder *recorder) {
    if (recorder->video && sc_vecdeque_is_empty(&recorder->video_queue)) {
        // The video queue is empty
        return true;
    }

    if (recorder->audio && recorder->audio_expects_config_packet
            && sc_vecdeque_is_empty(&recorder->audio_queue)) {
        // The audio queue is empty (when audio is enabled)
        return true;
    }

    // No queue is empty
    return false;
}

static enum sc_recorder_result
sc_recorder_process_header(struct sc_recorder *recorder) {
    sc_mutex_lock(&recorder->mutex);

    while (!recorder->stopped &&
              ((recorder->video && !recorder->video_init)
            || (recorder->audio && !recorder->audio_init)
            || sc_recorder_must_wait_for_config_packets(recorder))) {
        sc_cond_wait(&recorder->cond, &recorder->mutex);
    }

    bool incomplete = (recorder->video && !recorder->video_init)
        || (recorder->audio && !recorder->audio_init)
        || sc_recorder_must_wait_for_config_packets(recorder);

    if (incomplete) {
        assert(recorder->stopped);
        sc_mutex_unlock(&recorder->mutex);
        LOGW("Recording stopped before headers were processed");
        return SC_RECORDER_RESULT_CANCELLED_BEFORE_MEDIA;
    }

    AVPacket *video_pkt = NULL;
    if (!sc_vecdeque_is_empty(&recorder->video_queue)) {
        assert(recorder->video);
        video_pkt = sc_vecdeque_pop(&recorder->video_queue);
    }

    AVPacket *audio_pkt = NULL;
    if (recorder->audio_expects_config_packet &&
            !sc_vecdeque_is_empty(&recorder->audio_queue)) {
        assert(recorder->audio);
        audio_pkt = sc_vecdeque_pop(&recorder->audio_queue);
    }

    sc_mutex_unlock(&recorder->mutex);

    enum sc_recorder_result result = SC_RECORDER_RESULT_FAILED;

    if (video_pkt) {
        if (video_pkt->pts != AV_NOPTS_VALUE) {
            LOGE("The first video packet is not a config packet");
            goto end;
        }

        assert(recorder->video_stream.index >= 0);
        AVStream *video_stream =
            recorder->ctx->streams[recorder->video_stream.index];
        bool ok = sc_recorder_set_extradata(video_stream, video_pkt);
        if (!ok) {
            goto end;
        }
    }

    if (audio_pkt) {
        if (audio_pkt->pts != AV_NOPTS_VALUE) {
            LOGE("The first audio packet is not a config packet");
            goto end;
        }

        assert(recorder->audio_stream.index >= 0);
        AVStream *audio_stream =
            recorder->ctx->streams[recorder->audio_stream.index];
        bool ok = sc_recorder_set_extradata(audio_stream, audio_pkt);
        if (!ok) {
            goto end;
        }
    }

    bool ok = avformat_write_header(recorder->ctx, NULL) >= 0;
    if (!ok) {
        LOGE("Failed to write header to %s", recorder->filename);
        goto end;
    }

    result = SC_RECORDER_RESULT_SUCCEEDED;

end:
    if (video_pkt) {
        av_packet_free(&video_pkt);
    }
    if (audio_pkt) {
        av_packet_free(&audio_pkt);
    }

    return result;
}

static enum sc_recorder_result
sc_recorder_process_packets(struct sc_recorder *recorder) {
    int64_t pts_origin = AV_NOPTS_VALUE;

    enum sc_recorder_result header = sc_recorder_process_header(recorder);

    if (header != SC_RECORDER_RESULT_SUCCEEDED) {
        return header;
    }

    AVPacket *video_pkt = NULL;
    AVPacket *audio_pkt = NULL;

    // We can write a video packet only once we received the next one so that
    // we can set its duration (next_pts - current_pts)
    AVPacket *video_pkt_previous = NULL;

    bool error = false;

    for (;;) {
        sc_mutex_lock(&recorder->mutex);

        while (!recorder->stopped) {
            if (recorder->video && !video_pkt &&
                    !sc_vecdeque_is_empty(&recorder->video_queue)) {
                // A new packet may be assigned to video_pkt and be processed
                break;
            }
            if (recorder->audio && !audio_pkt
                    && !sc_vecdeque_is_empty(&recorder->audio_queue)) {
                // A new packet may be assigned to audio_pkt and be processed
                break;
            }
            sc_cond_wait(&recorder->cond, &recorder->mutex);
        }

        // If stopped is set, continue to process the remaining events (to
        // finish the recording) before actually stopping.

        // If there is no video, then the video_queue will remain empty forever
        // and video_pkt will always be NULL.
        assert(recorder->video || (!video_pkt
                && sc_vecdeque_is_empty(&recorder->video_queue)));

        // If there is no audio, then the audio_queue will remain empty forever
        // and audio_pkt will always be NULL.
        assert(recorder->audio || (!audio_pkt
                && sc_vecdeque_is_empty(&recorder->audio_queue)));

        if (!video_pkt && !sc_vecdeque_is_empty(&recorder->video_queue)) {
            video_pkt = sc_vecdeque_pop(&recorder->video_queue);
        }

        if (!audio_pkt && !sc_vecdeque_is_empty(&recorder->audio_queue)) {
            audio_pkt = sc_vecdeque_pop(&recorder->audio_queue);
        }

        if (recorder->stopped && !video_pkt && !audio_pkt) {
            assert(sc_vecdeque_is_empty(&recorder->video_queue));
            assert(sc_vecdeque_is_empty(&recorder->audio_queue));
            sc_mutex_unlock(&recorder->mutex);
            break;
        }

        assert(video_pkt || audio_pkt); // at least one

        sc_mutex_unlock(&recorder->mutex);

        // Ignore further config packets (e.g. on device orientation
        // change). The next non-config packet will have the config packet
        // data prepended.
        if (video_pkt && video_pkt->pts == AV_NOPTS_VALUE) {
            av_packet_free(&video_pkt);
            video_pkt = NULL;
        }

        if (audio_pkt && audio_pkt->pts == AV_NOPTS_VALUE) {
            av_packet_free(&audio_pkt);
            audio_pkt = NULL;
        }

        if (pts_origin == AV_NOPTS_VALUE) {
            if (!recorder->audio && video_pkt) {
                pts_origin = video_pkt->pts;
            } else if (!recorder->video && audio_pkt) {
                pts_origin = audio_pkt->pts;
            } else if (recorder->video && recorder->audio && video_pkt
                    && audio_pkt) {
                pts_origin = MIN(video_pkt->pts, audio_pkt->pts);
            } else if (recorder->stopped) {
                if (video_pkt) {
                    // The recorder is stopped without audio, record the video
                    // packets
                    pts_origin = video_pkt->pts;
                } else {
                    // Fail if there is no video
                    error = true;
                    goto end;
                }
            } else {
                // We need both video and audio packets to initialize pts_origin
                continue;
            }
        }

        assert(pts_origin != AV_NOPTS_VALUE);

        if (video_pkt) {
            video_pkt->pts -= pts_origin;
            video_pkt->dts = video_pkt->pts;

            if (video_pkt_previous) {
                // we now know the duration of the previous packet
                video_pkt_previous->duration = video_pkt->pts
                                             - video_pkt_previous->pts;

                bool ok = sc_recorder_write_video(recorder, video_pkt_previous);
                av_packet_free(&video_pkt_previous);
                if (!ok) {
                    LOGE("Could not record video packet");
                    error = true;
                    goto end;
                }
            }

            video_pkt_previous = video_pkt;
            video_pkt = NULL;
        }

        if (audio_pkt) {
            audio_pkt->pts -= pts_origin;
            audio_pkt->dts = audio_pkt->pts;

            bool ok = sc_recorder_write_audio(recorder, audio_pkt);
            if (!ok) {
                LOGE("Could not record audio packet");
                error = true;
                goto end;
            }

            av_packet_free(&audio_pkt);
            audio_pkt = NULL;
        }
    }

    // Write the last video packet
    if (video_pkt_previous) {
        // assign an arbitrary duration to the last packet
        video_pkt_previous->duration = 100000;
        bool ok = sc_recorder_write_video(recorder, video_pkt_previous);
        if (!ok) {
            LOGE("Could not record last video packet");
            error = true;
        }
        av_packet_free(&video_pkt_previous);
    }

    int ret = av_write_trailer(recorder->ctx);
    if (ret < 0) {
        LOGE("Failed to write trailer to %s", recorder->filename);
        error = true;
    }

end:
    av_packet_free(&video_pkt_previous);

    if (video_pkt) {
        av_packet_free(&video_pkt);
    }
    if (audio_pkt) {
        av_packet_free(&audio_pkt);
    }

    return error ? SC_RECORDER_RESULT_FAILED : SC_RECORDER_RESULT_SUCCEEDED;
}

static enum sc_recorder_result
sc_recorder_record(struct sc_recorder *recorder) {
    bool ok = sc_recorder_open_output_file(recorder);
    if (!ok) {
        return SC_RECORDER_RESULT_FAILED;
    }

    enum sc_recorder_result result = sc_recorder_process_packets(recorder);
    bool closed = sc_recorder_close_output_file(recorder);
    return closed ? result : SC_RECORDER_RESULT_FAILED;
}

static int
run_recorder(void *data) {
    struct sc_recorder *recorder = data;

    // Recording is a background task
    bool ok = sc_thread_set_priority(SC_THREAD_PRIORITY_LOW);
    (void) ok; // We don't care if it worked

    enum sc_recorder_result result = sc_recorder_record(recorder);
    sc_mutex_lock(&recorder->mutex);
    result = recorder->admission_failed ? SC_RECORDER_RESULT_FAILED : result;
    recorder->result = result;
    // Prevent the producer to push any new packet
    recorder->stopped = true;
    sc_cond_broadcast(&recorder->cond);
    // Discard pending packets
    sc_mutex_unlock(&recorder->mutex);
    sc_recorder_queue_clear(&recorder->video_queue);
    sc_recorder_queue_clear(&recorder->audio_queue);

    if (result == SC_RECORDER_RESULT_SUCCEEDED) {
        const char *format_name = sc_recorder_get_format_name(recorder->format);
        LOGI("Recording complete to %s file: %s", format_name,
                                                  recorder->filename);
    } else if (result == SC_RECORDER_RESULT_CANCELLED_BEFORE_MEDIA) {
        LOGI("Recording cancelled before media: %s", recorder->filename);
    } else {
        LOGE("Recording failed to %s", recorder->filename);
    }

    LOGD("Recorder thread ended");

    recorder->cbs->on_ended(recorder, result != SC_RECORDER_RESULT_FAILED,
                            recorder->cbs_userdata);

    return 0;
}

static bool
sc_recorder_set_orientation(AVStream *stream, enum sc_orientation orientation) {
    assert(!sc_orientation_is_mirror(orientation));

    uint8_t *raw_data;
#ifdef SCRCPY_LAVC_HAS_CODECPAR_CODEC_SIDEDATA
    AVPacketSideData *sd =
        av_packet_side_data_new(&stream->codecpar->coded_side_data,
                                &stream->codecpar->nb_coded_side_data,
                                AV_PKT_DATA_DISPLAYMATRIX,
                                sizeof(int32_t) * 9, 0);
    if (!sd) {
        LOG_OOM();
        return false;
    }

    raw_data = sd->data;
#else
    raw_data = av_stream_new_side_data(stream, AV_PKT_DATA_DISPLAYMATRIX,
                                      sizeof(int32_t) * 9);
    if (!raw_data) {
        LOG_OOM();
        return false;
    }
#endif

    int32_t *matrix = (int32_t *) raw_data;

    unsigned rotation = orientation;
    unsigned angle = rotation * 90;

    av_display_rotation_set(matrix, angle);

    return true;
}

static bool
sc_recorder_video_packet_sink_open(struct sc_packet_sink *sink,
                                   AVCodecContext *ctx,
                                   const struct sc_stream_session *session) {
    (void) session;

    struct sc_recorder *recorder = DOWNCAST_VIDEO(sink);
    // only written from this thread, no need to lock
    assert(!recorder->video_init);

    sc_mutex_lock(&recorder->mutex);

    while (!recorder->output_ready && !recorder->stopped) {
        sc_cond_wait(&recorder->cond, &recorder->mutex);
    }

    if (recorder->stopped) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    AVStream *stream = avformat_new_stream(recorder->ctx, ctx->codec);
    if (!stream) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    int r = avcodec_parameters_from_context(stream->codecpar, ctx);
    if (r < 0) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    recorder->video_stream.index = stream->index;

    if (recorder->orientation != SC_ORIENTATION_0) {
        if (!sc_recorder_set_orientation(stream, recorder->orientation)) {
            sc_mutex_unlock(&recorder->mutex);
            return false;
        }

        LOGI("Record orientation set to %s",
             sc_orientation_get_name(recorder->orientation));
    }

    recorder->video_init = true;
    sc_cond_signal(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);

    return true;
}

static void
sc_recorder_video_packet_sink_close(struct sc_packet_sink *sink) {
    struct sc_recorder *recorder = DOWNCAST_VIDEO(sink);
    // only written from this thread, no need to lock
    assert(recorder->video_init);

    sc_mutex_lock(&recorder->mutex);
    // EOS also stops the recorder
    recorder->stopped = true;
    sc_cond_signal(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);
}

static bool
sc_recorder_video_packet_sink_push(struct sc_packet_sink *sink,
                                   const AVPacket *packet) {
    struct sc_recorder *recorder = DOWNCAST_VIDEO(sink);
    // only written from this thread, no need to lock
    assert(recorder->video_init);

    return sc_recorder_queue_push(recorder, &recorder->video_queue,
                                   recorder->video_stream.index, packet);
}

static bool
sc_recorder_audio_packet_sink_open(struct sc_packet_sink *sink,
                                   AVCodecContext *ctx,
                                   const struct sc_stream_session *session) {
    (void) session;

    struct sc_recorder *recorder = DOWNCAST_AUDIO(sink);
    assert(recorder->audio);
    // only written from this thread, no need to lock
    assert(!recorder->audio_init);

    sc_mutex_lock(&recorder->mutex);

    while (!recorder->output_ready && !recorder->stopped) {
        sc_cond_wait(&recorder->cond, &recorder->mutex);
    }

    if (recorder->stopped) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    AVStream *stream = avformat_new_stream(recorder->ctx, ctx->codec);
    if (!stream) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    int r = avcodec_parameters_from_context(stream->codecpar, ctx);
    if (r < 0) {
        sc_mutex_unlock(&recorder->mutex);
        return false;
    }

    recorder->audio_stream.index = stream->index;

    // A config packet is provided for all supported formats except raw audio
    recorder->audio_expects_config_packet =
        ctx->codec_id != AV_CODEC_ID_PCM_S16LE;

    recorder->audio_init = true;
    sc_cond_signal(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);

    return true;
}

static void
sc_recorder_audio_packet_sink_close(struct sc_packet_sink *sink) {
    struct sc_recorder *recorder = DOWNCAST_AUDIO(sink);
    assert(recorder->audio);
    // only written from this thread, no need to lock
    assert(recorder->audio_init);

    sc_mutex_lock(&recorder->mutex);
    // EOS also stops the recorder
    recorder->stopped = true;
    sc_cond_signal(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);
}

static bool
sc_recorder_audio_packet_sink_push(struct sc_packet_sink *sink,
                                   const AVPacket *packet) {
    struct sc_recorder *recorder = DOWNCAST_AUDIO(sink);
    assert(recorder->audio);
    // only written from this thread, no need to lock
    assert(recorder->audio_init);

    return sc_recorder_queue_push(recorder, &recorder->audio_queue,
                                   recorder->audio_stream.index, packet);
}

static void
sc_recorder_audio_packet_sink_disable(struct sc_packet_sink *sink) {
    struct sc_recorder *recorder = DOWNCAST_AUDIO(sink);
    assert(recorder->audio);
    // only written from this thread, no need to lock
    assert(!recorder->audio_init);

    LOGW("Audio stream recording disabled");

    sc_mutex_lock(&recorder->mutex);
    recorder->audio = false;
    recorder->audio_init = true;
    sc_cond_signal(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);
}

static void
sc_recorder_stream_init(struct sc_recorder_stream *stream) {
    stream->index = -1;
    stream->last_pts = AV_NOPTS_VALUE;
}

bool
sc_recorder_init(struct sc_recorder *recorder, const char *filename,
                 enum sc_record_format format, bool video, bool audio,
                 enum sc_orientation orientation,
                 const struct sc_recorder_callbacks *cbs, void *cbs_userdata) {
    assert(!sc_orientation_is_mirror(orientation));

    recorder->filename = strdup(filename);
    if (!recorder->filename) {
        LOG_OOM();
        return false;
    }

    bool ok = sc_mutex_init(&recorder->mutex);
    if (!ok) {
        goto error_free_filename;
    }

    ok = sc_cond_init(&recorder->cond);
    if (!ok) {
        goto error_mutex_destroy;
    }

    assert(video || audio);
    recorder->video = video;
    recorder->audio = audio;

    recorder->orientation = orientation;

    sc_vecdeque_init(&recorder->video_queue);
    sc_vecdeque_init(&recorder->audio_queue);
    recorder->stopped = false;
    recorder->admission_failed = false;
    recorder->retained_packets = recorder->retained_bytes = 0;

    recorder->video_init = false;
    recorder->audio_init = false;

    recorder->audio_expects_config_packet = false;

    sc_recorder_stream_init(&recorder->video_stream);
    sc_recorder_stream_init(&recorder->audio_stream);

    recorder->format = format;
    recorder->ctx = NULL;
    recorder->exclusive_file = NULL;
    recorder->exclusive_output = false;
    recorder->output_ready = false;
    recorder->output_opened = false;
    recorder->result = SC_RECORDER_RESULT_FAILED;

    assert(cbs && cbs->on_ended);
    recorder->cbs = cbs;
    recorder->cbs_userdata = cbs_userdata;

    if (video) {
        static const struct sc_packet_sink_ops video_ops = {
            .open = sc_recorder_video_packet_sink_open,
            .close = sc_recorder_video_packet_sink_close,
            .push = sc_recorder_video_packet_sink_push,
        };

        recorder->video_packet_sink.ops = &video_ops;
    }

    if (audio) {
        static const struct sc_packet_sink_ops audio_ops = {
            .open = sc_recorder_audio_packet_sink_open,
            .close = sc_recorder_audio_packet_sink_close,
            .push = sc_recorder_audio_packet_sink_push,
            .disable = sc_recorder_audio_packet_sink_disable,
        };

        recorder->audio_packet_sink.ops = &audio_ops;
    }

    return true;

error_mutex_destroy:
    sc_mutex_destroy(&recorder->mutex);
error_free_filename:
    free(recorder->filename);

    return false;
}

bool
sc_recorder_start(struct sc_recorder *recorder) {
    bool ok = sc_thread_create(&recorder->thread, run_recorder,
                               "scrcpy-recorder", recorder);
    if (!ok) {
        LOGE("Could not start recorder thread");
        return false;
    }

    return true;
}

void
sc_recorder_set_exclusive_output(struct sc_recorder *recorder) {
    assert(!recorder->output_ready && !recorder->ctx);
    recorder->exclusive_output = true;
}

bool
sc_recorder_output_opened(const struct sc_recorder *recorder) {
    return recorder->output_opened;
}

enum sc_recorder_result
sc_recorder_get_result(const struct sc_recorder *recorder) {
    assert(!recorder->retained_packets && !recorder->retained_bytes);
    // Producer reference/allocation failure may follow worker completion.
    return recorder->admission_failed ? SC_RECORDER_RESULT_FAILED
                                      : recorder->result;
}

void
sc_recorder_stop(struct sc_recorder *recorder) {
    sc_mutex_lock(&recorder->mutex);
    recorder->stopped = true;
    sc_cond_broadcast(&recorder->cond);
    sc_mutex_unlock(&recorder->mutex);
}

void
sc_recorder_join(struct sc_recorder *recorder) {
    sc_thread_join(&recorder->thread, NULL);
}

void
sc_recorder_destroy(struct sc_recorder *recorder) {
    assert(!recorder->retained_packets && !recorder->retained_bytes);
    sc_vecdeque_destroy(&recorder->video_queue);
    sc_vecdeque_destroy(&recorder->audio_queue);
    sc_cond_destroy(&recorder->cond);
    sc_mutex_destroy(&recorder->mutex);
    free(recorder->filename);
}
