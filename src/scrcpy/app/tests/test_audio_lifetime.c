#include "common.h"

#include <assert.h>
#include <errno.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>
#include <libavutil/opt.h>

#include "audio_player.h"

#define TEST_SAMPLE_RATE 48000
#define TEST_CHANNELS 2
#define TEST_FRAME_SAMPLES 480
#define TEST_GATE_TIMEOUT_MS 5000

enum failure_stage {
    FAIL_NONE,
    FAIL_RESAMPLER_ALLOC,
    FAIL_RESAMPLER_INIT,
    FAIL_REGULATOR_MUTEX,
    FAIL_RING_ALLOC,
    FAIL_RESAMPLE_BUFFER,
    FAIL_OUTPUT_HINT,
    FAIL_OUTPUT_BUFFER,
    FAIL_STREAM_OPEN,
    FAIL_STREAM_RESUME,
};

struct audio_fixture {
    struct sc_audio_player player;
    AVCodecContext *context;
    enum failure_stage failure;
    unsigned array_allocations;
    unsigned resamplers_acquired;
    unsigned resamplers_released;
    unsigned mutexes_acquired;
    unsigned mutexes_released;
    unsigned rings_released;
    unsigned regulators_released;
    unsigned output_buffers_released;
    unsigned streams_acquired;
    unsigned streams_released;
    bool allocating_array;
    void *output_buffer;
    SDL_AudioStreamCallback callback;
    void *callback_userdata;
    SDL_Semaphore *callback_entered;
    SDL_Semaphore *callback_release;
    SDL_Semaphore *destroy_entered;
    atomic_bool hold_callback;
    atomic_bool callback_active;
    atomic_bool skip_pause;
    atomic_uint callback_calls;
};

static struct audio_fixture *active_fixture;

SwrContext *__real_swr_alloc(void);
int __real_swr_init(SwrContext *context);
void __real_swr_free(SwrContext **context);
bool __real_sc_mutex_init(sc_mutex *mutex);
void __real_sc_mutex_destroy(sc_mutex *mutex);
void *__real_sc_allocarray(size_t count, size_t size);
void *__real_malloc(size_t size);
void __real_free(void *pointer);
void __real_sc_audiobuf_destroy(struct sc_audiobuf *buffer);
void __real_sc_audio_regulator_destroy(struct sc_audio_regulator *regulator);
void __real_sc_audio_regulator_pull(struct sc_audio_regulator *regulator,
                                   uint8_t *output, uint32_t samples);
bool SDLCALL __real_SDL_SetHint(const char *name, const char *value);
SDL_AudioStream *SDLCALL __real_SDL_OpenAudioDeviceStream(
    SDL_AudioDeviceID device, const SDL_AudioSpec *specification,
    SDL_AudioStreamCallback callback, void *userdata);
bool SDLCALL __real_SDL_ResumeAudioDevice(SDL_AudioDeviceID device);
bool SDLCALL __real_SDL_PauseAudioDevice(SDL_AudioDeviceID device);
void SDLCALL __real_SDL_DestroyAudioStream(SDL_AudioStream *stream);

SwrContext *__wrap_swr_alloc(void);
int __wrap_swr_init(SwrContext *context);
void __wrap_swr_free(SwrContext **context);
bool __wrap_sc_mutex_init(sc_mutex *mutex);
void __wrap_sc_mutex_destroy(sc_mutex *mutex);
void *__wrap_sc_allocarray(size_t count, size_t size);
void *__wrap_malloc(size_t size);
void __wrap_free(void *pointer);
void __wrap_sc_audiobuf_destroy(struct sc_audiobuf *buffer);
void __wrap_sc_audio_regulator_destroy(struct sc_audio_regulator *regulator);
void __wrap_sc_audio_regulator_pull(struct sc_audio_regulator *regulator,
                                   uint8_t *output, uint32_t samples);
bool SDLCALL __wrap_SDL_SetHint(const char *name, const char *value);
SDL_AudioStream *SDLCALL __wrap_SDL_OpenAudioDeviceStream(
    SDL_AudioDeviceID device, const SDL_AudioSpec *specification,
    SDL_AudioStreamCallback callback, void *userdata);
bool SDLCALL __wrap_SDL_ResumeAudioDevice(SDL_AudioDeviceID device);
bool SDLCALL __wrap_SDL_PauseAudioDevice(SDL_AudioDeviceID device);
void SDLCALL __wrap_SDL_DestroyAudioStream(SDL_AudioStream *stream);

/** Reject one actual resampler acquisition without replacing its cleanup. */
SwrContext *
__wrap_swr_alloc(void) {

    if (active_fixture->failure == FAIL_RESAMPLER_ALLOC) {
        return NULL;
    }

    SwrContext *context = __real_swr_alloc();

    if (context) {
        ++active_fixture->resamplers_acquired;
    }

    return context;
}

/** Exercise the production resampler-initialization rollback. */
int
__wrap_swr_init(SwrContext *context) {

    if (active_fixture->failure == FAIL_RESAMPLER_INIT) {
        return AVERROR(ENOMEM);
    }

    return __real_swr_init(context);
}

/** Count real resampler release, including partial initialization. */
void
__wrap_swr_free(SwrContext **context) {

    if (*context) {
        ++active_fixture->resamplers_released;
    }

    __real_swr_free(context);
}

/** Inject only the regulator's own mutex acquisition. */
bool
__wrap_sc_mutex_init(sc_mutex *mutex) {

    if (active_fixture->failure == FAIL_REGULATOR_MUTEX) {
        return false;
    }

    bool acquired = __real_sc_mutex_init(mutex);

    if (acquired) {
        ++active_fixture->mutexes_acquired;
    }

    return acquired;
}

/** Verify actual mutex release only follows successful acquisition. */
void
__wrap_sc_mutex_destroy(sc_mutex *mutex) {
    ++active_fixture->mutexes_released;
    assert(!atomic_load(&active_fixture->callback_active));
    __real_sc_mutex_destroy(mutex);
}

/** Fail the named ring or callback-buffer acquisition independently. */
void *
__wrap_sc_allocarray(size_t count, size_t size) {
    struct audio_fixture *fixture = active_fixture;
    unsigned allocation = ++fixture->array_allocations;
    bool fail_ring = fixture->failure == FAIL_RING_ALLOC && allocation == 1;
    bool fail_output = fixture->failure == FAIL_OUTPUT_BUFFER && allocation == 2;

    if (fail_ring || fail_output) {
        return NULL;
    }

    fixture->allocating_array = true;
    void *pointer = __real_sc_allocarray(count, size);
    fixture->allocating_array = false;

    if (allocation == 2) {
        fixture->output_buffer = pointer;
    }

    return pointer;
}

/** Fail the real regulator's resampling buffer allocation. */
void *
__wrap_malloc(size_t size) {

    bool fail_resample_buffer = active_fixture
        && active_fixture->failure == FAIL_RESAMPLE_BUFFER
        && !active_fixture->allocating_array;

    if (fail_resample_buffer) {
        return NULL;
    }

    return __real_malloc(size);
}

/** Observe callback-buffer release without replacing real deallocation. */
void
__wrap_free(void *pointer) {

    if (active_fixture && pointer && pointer == active_fixture->output_buffer) {
        assert(!atomic_load(&active_fixture->callback_active));
        assert(active_fixture->streams_released
               == active_fixture->streams_acquired);
        ++active_fixture->output_buffers_released;
    }

    __real_free(pointer);
}

/** Count real ring release after all callback borrowing has ended. */
void
__wrap_sc_audiobuf_destroy(struct sc_audiobuf *buffer) {
    ++active_fixture->rings_released;
    assert(!atomic_load(&active_fixture->callback_active));
    __real_sc_audiobuf_destroy(buffer);
}

/** Enforce stream settlement before production regulator destruction. */
void
__wrap_sc_audio_regulator_destroy(struct sc_audio_regulator *regulator) {
    assert(!atomic_load(&active_fixture->callback_active));
    assert(active_fixture->streams_released
           == active_fixture->streams_acquired);
    ++active_fixture->regulators_released;
    __real_sc_audio_regulator_destroy(regulator);
}

/** Gate an executing real player callback before its real regulator pull. */
void
__wrap_sc_audio_regulator_pull(struct sc_audio_regulator *regulator,
                              uint8_t *output, uint32_t samples) {
    struct audio_fixture *fixture = active_fixture;

    if (atomic_exchange(&fixture->hold_callback, false)) {
        SDL_SignalSemaphore(fixture->callback_entered);
        assert(SDL_WaitSemaphoreTimeout(fixture->callback_release,
                                        TEST_GATE_TIMEOUT_MS));
    }

    __real_sc_audio_regulator_pull(regulator, output, samples);
}

/** Exercise the production output-hint rollback without changing other hints. */
bool SDLCALL
__wrap_SDL_SetHint(const char *name, const char *value) {
    bool is_output_hint = !strcmp(name, SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES);

    if (active_fixture && is_output_hint
            && active_fixture->failure == FAIL_OUTPUT_HINT) {
        return false;
    }

    return __real_SDL_SetHint(name, value);
}

/** Observe entry and exit around the actual application's SDL callback. */
static void SDLCALL
observe_audio_callback(void *userdata, SDL_AudioStream *stream,
                       int additional_amount, int total_amount) {
    struct audio_fixture *fixture = userdata;
    assert(!atomic_exchange(&fixture->callback_active, true));
    atomic_fetch_add(&fixture->callback_calls, 1);
    fixture->callback(fixture->callback_userdata, stream, additional_amount,
                      total_amount);
    assert(atomic_exchange(&fixture->callback_active, false));
}

/** Preserve the actual dummy backend and stream-owned device construction. */
SDL_AudioStream *SDLCALL
__wrap_SDL_OpenAudioDeviceStream(SDL_AudioDeviceID device,
                                const SDL_AudioSpec *specification,
                                SDL_AudioStreamCallback callback,
                                void *userdata) {
    struct audio_fixture *fixture = active_fixture;

    if (fixture->failure == FAIL_STREAM_OPEN) {
        return NULL;
    }

    fixture->callback = callback;
    fixture->callback_userdata = userdata;
    SDL_AudioStream *stream = __real_SDL_OpenAudioDeviceStream(
        device, specification, observe_audio_callback, fixture);

    if (stream) {
        ++fixture->streams_acquired;
    }

    return stream;
}

/** Exercise resume failure while preserving real paused-stream cleanup. */
bool SDLCALL
__wrap_SDL_ResumeAudioDevice(SDL_AudioDeviceID device) {

    if (active_fixture->failure == FAIL_STREAM_RESUME) {
        return false;
    }

    return __real_SDL_ResumeAudioDevice(device);
}

/** Bypass pause in one case to test destruction's independent exclusion. */
bool SDLCALL
__wrap_SDL_PauseAudioDevice(SDL_AudioDeviceID device) {

    if (atomic_load(&active_fixture->skip_pause)) {
        return true;
    }

    return __real_SDL_PauseAudioDevice(device);
}

/** Observe the real SDL destroy boundary and require callback completion. */
void SDLCALL
__wrap_SDL_DestroyAudioStream(SDL_AudioStream *stream) {
    SDL_SignalSemaphore(active_fixture->destroy_entered);
    __real_SDL_DestroyAudioStream(stream);
    assert(!atomic_load(&active_fixture->callback_active));
    ++active_fixture->streams_released;
}

/** Initialize isolated real player state without selecting physical hardware. */
static void
fixture_init(struct audio_fixture *fixture, enum failure_stage failure) {
    memset(fixture, 0, sizeof(*fixture));
    fixture->failure = failure;
    atomic_init(&fixture->hold_callback, false);
    atomic_init(&fixture->callback_active, false);
    atomic_init(&fixture->skip_pause, false);
    atomic_init(&fixture->callback_calls, 0);
    fixture->callback_entered = SDL_CreateSemaphore(0);
    fixture->callback_release = SDL_CreateSemaphore(0);
    fixture->destroy_entered = SDL_CreateSemaphore(0);
    assert(fixture->callback_entered);
    assert(fixture->callback_release);
    assert(fixture->destroy_entered);
    fixture->context = avcodec_alloc_context3(NULL);
    assert(fixture->context);
    fixture->context->sample_rate = TEST_SAMPLE_RATE;
    fixture->context->sample_fmt = SC_AV_SAMPLE_FMT;
#ifdef SCRCPY_LAVU_HAS_CHLAYOUT
    av_channel_layout_default(&fixture->context->ch_layout, TEST_CHANNELS);
#else
    fixture->context->channel_layout = AV_CH_LAYOUT_STEREO;
#endif
    sc_audio_player_init(&fixture->player, SC_TICK_FROM_MS(20),
                         SC_TICK_FROM_MS(5));
    active_fixture = fixture;
}

/** Release only harness resources after production cleanup has settled. */
static void
fixture_destroy(struct audio_fixture *fixture) {
    assert(fixture->resamplers_acquired == fixture->resamplers_released);
    assert(fixture->mutexes_acquired == fixture->mutexes_released);
    assert(fixture->streams_acquired == fixture->streams_released);
    assert(!atomic_load(&fixture->callback_active));
    avcodec_free_context(&fixture->context);
    SDL_DestroySemaphore(fixture->callback_entered);
    SDL_DestroySemaphore(fixture->callback_release);
    SDL_DestroySemaphore(fixture->destroy_entered);
    active_fixture = NULL;
}

/** Characterize every fallible acquisition and the actual partial rollback. */
static void
test_partial_open_failures(void) {

    for (enum failure_stage stage = FAIL_RESAMPLER_ALLOC;
            stage <= FAIL_STREAM_RESUME; ++stage) {
        struct audio_fixture fixture;
        fixture_init(&fixture, stage);
        struct sc_frame_sink *sink = &fixture.player.frame_sink;
        assert(!sink->ops->open(sink, fixture.context, NULL));
        assert(fixture.resamplers_acquired
               == (stage != FAIL_RESAMPLER_ALLOC));
        assert(fixture.mutexes_acquired == (stage >= FAIL_RING_ALLOC));
        assert(fixture.rings_released == (stage >= FAIL_RESAMPLE_BUFFER));
        assert(fixture.regulators_released == (stage >= FAIL_OUTPUT_HINT));
        assert(fixture.output_buffers_released == (stage >= FAIL_STREAM_OPEN));
        assert(fixture.streams_acquired == (stage == FAIL_STREAM_RESUME));
        fixture_destroy(&fixture);
    }
}

/** Run the real close on a worker while the real callback is retained. */
static int
close_player(void *userdata) {
    struct audio_fixture *fixture = userdata;
    struct sc_frame_sink *sink = &fixture->player.frame_sink;
    sink->ops->close(sink);
    return 0;
}

/** Prove real stream destruction settles a currently executing callback. */
static void
test_callback_settlement(void) {
    struct audio_fixture fixture;
    fixture_init(&fixture, FAIL_NONE);
    atomic_store(&fixture.hold_callback, true);
    atomic_store(&fixture.skip_pause, true);
    struct sc_frame_sink *sink = &fixture.player.frame_sink;
    assert(sink->ops->open(sink, fixture.context, NULL));
    assert(SDL_WaitSemaphoreTimeout(fixture.callback_entered,
                                    TEST_GATE_TIMEOUT_MS));
    assert(atomic_load(&fixture.callback_active));

    AVFrame *frame = av_frame_alloc();
    assert(frame);
    frame->sample_rate = TEST_SAMPLE_RATE;
    frame->format = SC_AV_SAMPLE_FMT;
    frame->nb_samples = TEST_FRAME_SAMPLES;
    frame->pts = 0;
#ifdef SCRCPY_LAVU_HAS_CHLAYOUT
    av_channel_layout_default(&frame->ch_layout, TEST_CHANNELS);
#else
    frame->channel_layout = AV_CH_LAYOUT_STEREO;
#endif
    assert(!av_frame_get_buffer(frame, 0));
    memset(frame->data[0], 0,
           TEST_FRAME_SAMPLES * TEST_CHANNELS * sizeof(float));
    assert(sink->ops->push(sink, frame));
    av_frame_free(&frame);

    sc_thread closer;
    assert(sc_thread_create(&closer, close_player, "audio-close", &fixture));
    assert(SDL_WaitSemaphoreTimeout(fixture.destroy_entered,
                                    TEST_GATE_TIMEOUT_MS));
    assert(atomic_load(&fixture.callback_active));
    assert(!fixture.regulators_released);
    assert(!fixture.output_buffers_released);
    SDL_SignalSemaphore(fixture.callback_release);
    sc_thread_join(&closer, NULL);
    assert(atomic_load(&fixture.callback_calls));
    assert(fixture.regulators_released == 1);
    assert(fixture.rings_released == 1);
    assert(fixture.output_buffers_released == 1);
    fixture_destroy(&fixture);
}

/** Verify the ordinary pause-then-destroy application close independently. */
static void
test_normal_close(void) {
    struct audio_fixture fixture;
    fixture_init(&fixture, FAIL_NONE);
    struct sc_frame_sink *sink = &fixture.player.frame_sink;
    assert(sink->ops->open(sink, fixture.context, NULL));
    sink->ops->close(sink);
    assert(fixture.regulators_released == 1);
    assert(fixture.rings_released == 1);
    assert(fixture.output_buffers_released == 1);
    fixture_destroy(&fixture);
}

/** Exercise actual player and SDL synchronization with the dummy driver only. */
int
main(void) {
    assert(SDL_SetHint(SDL_HINT_AUDIO_DRIVER, "dummy"));
    assert(SDL_Init(SDL_INIT_AUDIO));
    assert(!strcmp(SDL_GetCurrentAudioDriver(), "dummy"));
    printf("audio lifetime: SDL %d (%s), isolated driver %s\n", SDL_GetVersion(),
           SDL_GetRevision(), SDL_GetCurrentAudioDriver());
    test_partial_open_failures();
    test_callback_settlement();
    test_normal_close();
    SDL_Quit();
    puts("audio lifetime: nine partial-open failures, callback settlement, normal close passed");
    return 0;
}
