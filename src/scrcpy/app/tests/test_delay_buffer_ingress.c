#include "common.h"

#include <assert.h>
#include <errno.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>
#include <libavutil/buffer.h>

#include "delay_buffer.h"
#include "events.h"
#include "video_ingress.h"

#define TEST_FRAME_WIDTH 4
#define TEST_FRAME_HEIGHT 4
#define TEST_PIXEL_BYTES 24
#define TEST_BUFFER_DELAY SC_TICK_FROM_MS(250)
#define TEST_SYSTEM_TIME SC_TICK_FROM_MS(1000)

struct test_gate {
    sc_mutex mutex;
    sc_cond condition;
    bool open;
};

struct fixture {
    struct sc_dispatcher dispatcher;
    struct sc_video_ingress ingress;
    struct sc_video_bridge bridge;
    struct sc_delay_buffer delay;
    struct sc_frame_source producer;
    struct sc_frame_sink observer;
    sc_dispatcher_generation generation;
    sc_mutex observations_mutex;
    sc_cond observations_condition;
    struct test_gate metadata_entered;
    struct test_gate metadata_release;
    struct test_gate delay_entered;
    struct test_gate delay_release;
    struct test_gate frame_entered;
    struct test_gate frame_release;
    struct test_gate close_join_entered;
    unsigned hold_metadata_width;
    bool hold_frame;
    bool reject_metadata;
    bool reject_frame;
    bool reject_open;
    unsigned expected_closes;
    bool generation_revoked;
    unsigned metadata_calls;
    unsigned frame_calls;
    unsigned sink_closes;
    unsigned delay_waits;
    sc_tick delay_deadline;
    unsigned observed_widths[8];
    unsigned presentations;
    struct sc_stream_session consumed_metadata;
    AVFrame *displayed;
    atomic_uintptr_t observed_queue;
    atomic_uint queue_releases;
};

struct push_operation {
    struct fixture *fixture;
    AVFrame *frame;
    bool accepted;
};

static struct fixture *active_fixture;
static atomic_uint frame_headers_allocated;
static atomic_uint frame_headers_released;
static atomic_bool fail_next_header;
static atomic_bool fail_next_ref;
static atomic_bool fail_next_queue_allocation;
static atomic_bool fail_buffer_thread;

AVFrame *__real_av_frame_alloc(void);
void __real_av_frame_free(AVFrame **frame);
AVFrame *__wrap_av_frame_alloc(void);
void __wrap_av_frame_free(AVFrame **frame);
sc_tick __wrap_sc_tick_now(void);
bool __wrap_sc_cond_timedwait(sc_cond *condition, sc_mutex *mutex,
                              sc_tick deadline);
void __real_sc_thread_join(sc_thread *thread, int *status);
void __wrap_sc_thread_join(sc_thread *thread, int *status);
void __real_free(void *pointer);
void __wrap_free(void *pointer);
int __real_av_frame_ref(AVFrame *destination, const AVFrame *source);
int __wrap_av_frame_ref(AVFrame *destination, const AVFrame *source);
void *__real_reallocarray(void *pointer, size_t count, size_t size);
void *__wrap_reallocarray(void *pointer, size_t count, size_t size);
bool __real_sc_thread_create(sc_thread *thread, sc_thread_fn function,
                              const char *name, void *userdata);
bool __wrap_sc_thread_create(sc_thread *thread, sc_thread_fn function,
                              const char *name, void *userdata);

/** Count actual production frame headers without replacing FFmpeg references. */
AVFrame *
__wrap_av_frame_alloc(void) {

    if (atomic_exchange(&fail_next_header, false)) {
        return NULL;
    }

    AVFrame *frame = __real_av_frame_alloc();

    if (frame) {
        atomic_fetch_add(&frame_headers_allocated, 1);
    }

    return frame;
}

/** Account exact header destruction after worker and dispatcher settlement. */
void
__wrap_av_frame_free(AVFrame **frame) {

    if (*frame) {
        atomic_fetch_add(&frame_headers_released, 1);
    }

    __real_av_frame_free(frame);
}

/** Observe the queue's actual backing-storage release after worker settlement. */
void
__wrap_free(void *pointer) {

    if (active_fixture && pointer &&
            (uintptr_t) pointer == atomic_load(&active_fixture->observed_queue)) {
        atomic_fetch_add(&active_fixture->queue_releases, 1);
    }

    __real_free(pointer);
}

/** Fail one reference acquisition while every accepted reference remains real. */
int
__wrap_av_frame_ref(AVFrame *destination, const AVFrame *source) {

    if (atomic_exchange(&fail_next_ref, false)) {
        return AVERROR(ENOMEM);
    }

    return __real_av_frame_ref(destination, source);
}

/** Reject a named queue allocation without changing its admission algorithm. */
void *
__wrap_reallocarray(void *pointer, size_t count, size_t size) {

    if (atomic_exchange(&fail_next_queue_allocation, false)) {
        return NULL;
    }

    return __real_reallocarray(pointer, count, size);
}

/** Exercise the real downstream-open rollback on buffering-thread start failure. */
bool
__wrap_sc_thread_create(sc_thread *thread, sc_thread_fn function,
                         const char *name, void *userdata) {

    if (!strcmp(name, "scrcpy-dbuf") && atomic_exchange(&fail_buffer_thread, false)) {
        return false;
    }

    return __real_sc_thread_create(thread, function, name, userdata);
}

/** Supply a fixed scheduling instant to production delay-clock arithmetic. */
sc_tick
__wrap_sc_tick_now(void) {
    return TEST_SYSTEM_TIME;
}

/** Initialize an explicit producer/worker rendezvous. */
static void
gate_init(struct test_gate *gate) {
    *gate = (struct test_gate) {0};
    assert(sc_mutex_init(&gate->mutex));
    assert(sc_cond_init(&gate->condition));
}

/** Publish a transition without relying on elapsed time. */
static void
gate_open(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);
    gate->open = true;
    sc_cond_broadcast(&gate->condition);
    sc_mutex_unlock(&gate->mutex);
}

/** Wait for a semantic transition; the Meson timeout is only a watchdog. */
static void
gate_wait(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);

    while (!gate->open) {
        sc_cond_wait(&gate->condition, &gate->mutex);
    }

    sc_mutex_unlock(&gate->mutex);
}

/** Release synchronization only after all borrowing threads have joined. */
static void
gate_destroy(struct test_gate *gate) {
    sc_cond_destroy(&gate->condition);
    sc_mutex_destroy(&gate->mutex);
}

/** Observe close reaching the real worker join without replacing that join. */
void
__wrap_sc_thread_join(sc_thread *thread, int *status) {

    if (active_fixture && thread == &active_fixture->delay.thread) {
        gate_open(&active_fixture->close_join_entered);
    }

    __real_sc_thread_join(thread, status);
}

/** Observe the real delay deadline and gate its timeout instead of sleeping. */
bool
__wrap_sc_cond_timedwait(sc_cond *condition, sc_mutex *mutex,
                         sc_tick deadline) {
    struct fixture *fixture = active_fixture;
    assert(condition == &fixture->delay.wait_cond);
    fixture->delay_deadline = deadline;
    ++fixture->delay_waits;
    sc_mutex_unlock(mutex);
    gate_open(&fixture->delay_entered);
    gate_wait(&fixture->delay_release);
    sc_mutex_lock(mutex);
    return false;
}

/** Observe actual final decoded-pixel release independently of frame headers. */
static void
release_pixels(void *context, uint8_t *pixels) {
    atomic_fetch_add((atomic_uint *) context, 1);
    free(pixels);
}

/** Create real FFmpeg reference-counted pixels with a controlled PTS. */
static AVFrame *
create_frame(atomic_uint *releases, int64_t pts) {
    AVFrame *frame = __wrap_av_frame_alloc();
    assert(frame);
    uint8_t *pixels = calloc(TEST_PIXEL_BYTES, 1);
    assert(pixels);
    frame->buf[0] = av_buffer_create(pixels, TEST_PIXEL_BYTES,
                                    release_pixels, releases, 0);
    assert(frame->buf[0]);
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = TEST_FRAME_WIDTH;
    frame->height = TEST_FRAME_HEIGHT;
    frame->pts = pts;
    frame->data[0] = pixels;
    return frame;
}

/** Consume through the real ingress after the actual dispatcher lease. */
static void
present(void *binding, struct sc_video_ingress *ingress,
          sc_dispatcher_generation generation) {
    struct fixture *fixture = binding;
    assert(sc_thread_is_main());
    av_frame_unref(fixture->displayed);
    unsigned skipped;
    assert(sc_video_ingress_consume(ingress, generation, fixture->displayed,
                                    &fixture->consumed_metadata, &skipped));
    ++fixture->presentations;
}

/** Keep wakeup publication external to the dispatcher lock without SDL effects. */
static bool
wakeup(void *context, SDL_Event *event) {
    (void) context;
    (void) event;
    return true;
}

/** Forward the real sink open through an observing adapter. */
static bool
observer_open(struct sc_frame_sink *sink, const AVCodecContext *context,
                const struct sc_stream_session *session) {
    struct fixture *fixture = container_of(sink, struct fixture, observer);

    if (fixture->reject_open) {
        return false;
    }

    bool opened = fixture->bridge.frame_sink.ops->open(&fixture->bridge.frame_sink,
                                                       context, session);

    if (opened) {
        fixture->expected_closes = 1;
    }

    return opened;
}

/** Confirm close runs exactly once after outstanding real callbacks settle. */
static void
observer_close(struct sc_frame_sink *sink) {
    struct fixture *fixture = container_of(sink, struct fixture, observer);
    ++fixture->sink_closes;
    fixture->bridge.frame_sink.ops->close(&fixture->bridge.frame_sink);
}

/** Gate a real metadata delivery before forwarding to the production bridge. */
static bool
observer_session(struct sc_frame_sink *sink,
                  const struct sc_stream_session *session) {
    struct fixture *fixture = container_of(sink, struct fixture, observer);

    if (session->video.width == fixture->hold_metadata_width) {
        gate_open(&fixture->metadata_entered);
        gate_wait(&fixture->metadata_release);
    }

    bool accepted = !fixture->reject_metadata &&
        fixture->bridge.frame_sink.ops->push_session(
            &fixture->bridge.frame_sink, session);
    sc_mutex_lock(&fixture->observations_mutex);
    assert(fixture->metadata_calls < ARRAY_LEN(fixture->observed_widths));
    fixture->observed_widths[fixture->metadata_calls++] = session->video.width;
    sc_cond_broadcast(&fixture->observations_condition);
    sc_mutex_unlock(&fixture->observations_mutex);
    return accepted;
}

/** Observe and optionally hold a real frame callback; never model the bridge. */
static bool
observer_frame(struct sc_frame_sink *sink, const AVFrame *frame) {
    struct fixture *fixture = container_of(sink, struct fixture, observer);

    if (fixture->hold_frame) {
        gate_open(&fixture->frame_entered);
        gate_wait(&fixture->frame_release);
    }

    bool accepted = !fixture->reject_frame &&
        fixture->bridge.frame_sink.ops->push(&fixture->bridge.frame_sink, frame);
    sc_mutex_lock(&fixture->observations_mutex);
    ++fixture->frame_calls;
    sc_cond_broadcast(&fixture->observations_condition);
    sc_mutex_unlock(&fixture->observations_mutex);
    return accepted;
}

/** Wait for actual downstream calls, including rejected calls, to return. */
static void
wait_calls(struct fixture *fixture, unsigned metadata, unsigned frames) {
    sc_mutex_lock(&fixture->observations_mutex);

    while (fixture->metadata_calls < metadata || fixture->frame_calls < frames) {
        sc_cond_wait(&fixture->observations_condition,
                      &fixture->observations_mutex);
    }

    sc_mutex_unlock(&fixture->observations_mutex);
}

/** Initialize the real producer → delay → observing adapter → bridge graph. */
static void
fixture_prepare(struct fixture *fixture) {
    *fixture = (struct fixture) {0};
    active_fixture = fixture;
    gate_init(&fixture->metadata_entered);
    gate_init(&fixture->metadata_release);
    gate_init(&fixture->delay_entered);
    gate_init(&fixture->delay_release);
    gate_init(&fixture->frame_entered);
    gate_init(&fixture->frame_release);
    gate_init(&fixture->close_join_entered);
    assert(sc_mutex_init(&fixture->observations_mutex));
    assert(sc_cond_init(&fixture->observations_condition));
    fixture->displayed = __wrap_av_frame_alloc();
    assert(fixture->displayed);
    assert(sc_dispatcher_init(&fixture->dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               wakeup, fixture));
    assert(sc_dispatcher_generation_begin(&fixture->dispatcher, fixture,
                                           &fixture->generation));
    assert(sc_video_ingress_init(&fixture->ingress, &fixture->dispatcher,
                                  present));
    sc_video_ingress_bind(&fixture->ingress, fixture->generation);
    sc_video_bridge_init(&fixture->bridge, &fixture->ingress,
                          fixture->generation);
    static const struct sc_frame_sink_ops observer_ops = {
        .open = observer_open, .close = observer_close,
        .push = observer_frame, .push_session = observer_session,
    };
    fixture->observer.ops = &observer_ops;
    sc_delay_buffer_init(&fixture->delay, TEST_BUFFER_DELAY, true);
    sc_frame_source_add_sink(&fixture->delay.frame_source, &fixture->observer);
    sc_frame_source_init(&fixture->producer);
    sc_frame_source_add_sink(&fixture->producer, &fixture->delay.frame_sink);
}

/** Open the actual sink graph with the same synthetic initial stream metadata. */
static bool
fixture_open(struct fixture *fixture) {
    const AVCodecContext context = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = TEST_FRAME_WIDTH, .height = TEST_FRAME_HEIGHT,
    };
    const struct sc_stream_session session = {
        .video = {.width = 64, .height = 48, .client_resized = false},
    };
    return sc_frame_source_sinks_open(&fixture->producer, &context, &session);
}

/** Start one actual delay worker and its borrowed downstream sink. */
static void
fixture_init(struct fixture *fixture) {
    fixture_prepare(fixture);
    assert(fixture_open(fixture));
}

/** Release app state only after the actual worker and downstream sink close. */
static void
fixture_release(struct fixture *fixture) {
    assert(fixture->sink_closes == fixture->expected_closes);
    unsigned expected_queue_releases = atomic_load(&fixture->observed_queue) ? 1 : 0;
    assert(atomic_load(&fixture->queue_releases) == expected_queue_releases);
    sc_video_ingress_revoke(&fixture->ingress, fixture->generation);

    if (!fixture->generation_revoked) {
        assert(sc_dispatcher_generation_revoke(&fixture->dispatcher,
                                                fixture->generation));
    }
    assert(sc_dispatcher_shutdown(&fixture->dispatcher));
    sc_video_ingress_destroy(&fixture->ingress);
    __wrap_av_frame_free(&fixture->displayed);
    assert(sc_dispatcher_destroy(&fixture->dispatcher));
    sc_cond_destroy(&fixture->observations_condition);
    sc_mutex_destroy(&fixture->observations_mutex);
    gate_destroy(&fixture->frame_release);
    gate_destroy(&fixture->close_join_entered);
    gate_destroy(&fixture->frame_entered);
    gate_destroy(&fixture->delay_release);
    gate_destroy(&fixture->delay_entered);
    gate_destroy(&fixture->metadata_release);
    gate_destroy(&fixture->metadata_entered);
    active_fixture = NULL;
}

/** Join the actual delay worker before releasing ingress and synchronization. */
static void
fixture_destroy(struct fixture *fixture) {
    atomic_store(&fixture->observed_queue, (uintptr_t) fixture->delay.queue.data);
    sc_frame_source_sinks_close(&fixture->producer);
    fixture_release(fixture);
}

/** Exercise the original producer API on its own owned thread. */
static int
producer_push(void *context) {
    struct push_operation *operation = context;
    operation->accepted = sc_frame_source_sinks_push(
        &operation->fixture->producer, operation->frame);
    return 0;
}

/** Demonstrate an in-flight predecessor cannot be overtaken by the first frame. */
static void
test_metadata_before_first_frame(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    const struct sc_stream_session preceding = {
        .video = {.width = 128, .height = 96, .client_resized = true},
    };
    fixture.hold_metadata_width = preceding.video.width;
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &preceding));
    gate_wait(&fixture.metadata_entered);
    atomic_uint pixel_releases = 0;
    AVFrame *frame = create_frame(&pixel_releases, 0);
    struct push_operation operation = {.fixture = &fixture, .frame = frame};
    sc_thread producer;
    assert(sc_thread_create(&producer, producer_push, "ordered-first", &operation));
    sc_thread_join(&producer, NULL);
    assert(operation.accepted);
    sc_dispatcher_drain(&fixture.dispatcher);
    unsigned early_presentations = fixture.presentations;
    struct sc_stream_session early_metadata = fixture.consumed_metadata;
    gate_open(&fixture.metadata_release);
    wait_calls(&fixture, 1, 1);
    sc_dispatcher_drain(&fixture.dispatcher);
    struct sc_stream_session final_metadata = fixture.consumed_metadata;
    fixture_destroy(&fixture);
    __wrap_av_frame_free(&frame);
    assert(atomic_load(&pixel_releases) == 1);
    printf("executed ordering cases: 1; early=%u width=%u resized=%d; final width=%u resized=%d\n",
           early_presentations, early_metadata.video.width,
           early_metadata.video.client_resized, final_metadata.video.width,
           final_metadata.video.client_resized);
    fflush(stdout);
    assert(!early_presentations);
    assert(final_metadata.video.width == preceding.video.width);
    assert(final_metadata.video.client_resized);
}

/** Verify both queued predecessors and a later update retain semantic order. */
static void
test_multiple_and_subsequent_metadata(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    const struct sc_stream_session first = {
        .video = {.width = 128, .height = 96, .client_resized = true},
    };
    const struct sc_stream_session second = {
        .video = {.width = 192, .height = 144, .client_resized = false},
    };
    const struct sc_stream_session subsequent = {
        .video = {.width = 256, .height = 192, .client_resized = true},
    };
    fixture.hold_metadata_width = first.video.width;
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &first));
    gate_wait(&fixture.metadata_entered);
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &second));
    atomic_uint pixel_releases = 0;
    AVFrame *frame = create_frame(&pixel_releases, 0);
    assert(sc_frame_source_sinks_push(&fixture.producer, frame));
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &subsequent));
    sc_dispatcher_drain(&fixture.dispatcher);
    assert(!fixture.presentations);
    gate_open(&fixture.metadata_release);
    wait_calls(&fixture, 3, 1);
    sc_dispatcher_drain(&fixture.dispatcher);
    assert(fixture.presentations == 1);
    assert(fixture.consumed_metadata.video.width == second.video.width);
    assert(!fixture.consumed_metadata.video.client_resized);
    assert(fixture.observed_widths[0] == first.video.width);
    assert(fixture.observed_widths[1] == second.video.width);
    assert(fixture.observed_widths[2] == subsequent.video.width);
    fixture_destroy(&fixture);
    __wrap_av_frame_free(&frame);
    assert(atomic_load(&pixel_releases) == 1);
}

/** Assert scheduling decisions at the real wait boundary without latency claims. */
static void
test_asap_and_ordinary_delay(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    atomic_uint first_releases = 0, second_releases = 0;
    AVFrame *first = create_frame(&first_releases, 0);
    AVFrame *second = create_frame(&second_releases, 1000);
    assert(sc_frame_source_sinks_push(&fixture.producer, first));
    wait_calls(&fixture, 0, 1);
    assert(!fixture.delay_waits);
    sc_dispatcher_drain(&fixture.dispatcher);
    assert(fixture.presentations == 1);
    assert(sc_frame_source_sinks_push(&fixture.producer, second));
    gate_wait(&fixture.delay_entered);
    assert(fixture.delay_waits == 1);
    assert(fixture.delay_deadline == TEST_SYSTEM_TIME + TEST_BUFFER_DELAY);
    sc_dispatcher_drain(&fixture.dispatcher);
    assert(fixture.presentations == 1);
    gate_open(&fixture.delay_release);
    wait_calls(&fixture, 0, 2);
    sc_dispatcher_drain(&fixture.dispatcher);
    assert(fixture.presentations == 2);
    assert(fixture.displayed->pts == second->pts);
    fixture_destroy(&fixture);
    __wrap_av_frame_free(&first);
    __wrap_av_frame_free(&second);
    assert(atomic_load(&first_releases) == 1);
    assert(atomic_load(&second_releases) == 1);
}

/** Execute the actual close and join while an observing callback remains held. */
static int
close_worker(void *context) {
    struct fixture *fixture = context;
    atomic_store(&fixture->observed_queue, (uintptr_t) fixture->delay.queue.data);
    sc_frame_source_sinks_close(&fixture->producer);
    return 0;
}

/** Close joins a held metadata borrower and drops its queued frame exactly once. */
static void
test_close_with_outstanding_metadata(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    const struct sc_stream_session metadata = {
        .video = {.width = 128, .height = 96, .client_resized = true},
    };
    fixture.hold_metadata_width = metadata.video.width;
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &metadata));
    gate_wait(&fixture.metadata_entered);
    atomic_uint releases = 0;
    AVFrame *frame = create_frame(&releases, 0);
    assert(sc_frame_source_sinks_push(&fixture.producer, frame));
    __wrap_av_frame_free(&frame);
    sc_thread closer;
    assert(sc_thread_create(&closer, close_worker, "metadata-close", &fixture));
    gate_wait(&fixture.close_join_entered);
    assert(!fixture.sink_closes);
    assert(!atomic_load(&releases));
    gate_open(&fixture.metadata_release);
    sc_thread_join(&closer, NULL);
    assert(atomic_load(&releases) == 1);
    assert(fixture.metadata_calls == 1 && !fixture.frame_calls);
    assert(!fixture.presentations);
    fixture_release(&fixture);
}

/** Revocation rejects a held actual frame callback; close retains it until join. */
static void
test_revocation_with_outstanding_frame(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    fixture.hold_frame = true;
    atomic_uint releases = 0;
    AVFrame *frame = create_frame(&releases, 0);
    assert(sc_frame_source_sinks_push(&fixture.producer, frame));
    gate_wait(&fixture.frame_entered);
    __wrap_av_frame_free(&frame);
    sc_video_ingress_revoke(&fixture.ingress, fixture.generation);
    assert(sc_dispatcher_generation_revoke(&fixture.dispatcher,
                                            fixture.generation));
    fixture.generation_revoked = true;
    sc_thread closer;
    assert(sc_thread_create(&closer, close_worker, "frame-close", &fixture));
    gate_wait(&fixture.close_join_entered);
    assert(!atomic_load(&releases));
    gate_open(&fixture.frame_release);
    sc_thread_join(&closer, NULL);
    assert(atomic_load(&releases) == 1);
    assert(fixture.frame_calls == 1 && !fixture.presentations);
    fixture_release(&fixture);
}

/** A downstream rejection retires both the active callback and queued ownership. */
static void
test_downstream_rejections(void) {
    for (unsigned scenario = 0; scenario < 2; ++scenario) {
        struct fixture fixture;
        fixture_init(&fixture);
        const struct sc_stream_session metadata = {
            .video = {.width = 128, .height = 96, .client_resized = true},
        };
        fixture.hold_metadata_width = metadata.video.width;
        fixture.reject_metadata = !scenario;
        fixture.reject_frame = scenario;
        assert(sc_frame_source_sinks_push_session(&fixture.producer, &metadata));
        gate_wait(&fixture.metadata_entered);
        atomic_uint releases = 0;
        AVFrame *frame = create_frame(&releases, 0);
        assert(sc_frame_source_sinks_push(&fixture.producer, frame));
        __wrap_av_frame_free(&frame);
        gate_open(&fixture.metadata_release);
        wait_calls(&fixture, 1, scenario ? 1 : 0);
        fixture_destroy(&fixture);
        assert(atomic_load(&releases) == 1);
        assert(!fixture.presentations);
        assert(fixture.frame_calls == scenario);
    }
}

/** Failed FFmpeg header admission cannot leave a partially initialized queue slot. */
static void
test_failed_frame_admission(void) {
    struct fixture fixture;
    fixture_init(&fixture);
    atomic_uint first_releases = 0, rejected_releases = 0;
    AVFrame *first = create_frame(&first_releases, 0);
    AVFrame *rejected = create_frame(&rejected_releases, 1000);
    assert(sc_frame_source_sinks_push(&fixture.producer, first));
    wait_calls(&fixture, 0, 1);
    sc_dispatcher_drain(&fixture.dispatcher);
    const struct sc_stream_session metadata = {
        .video = {.width = 128, .height = 96, .client_resized = true},
    };
    fixture.hold_metadata_width = metadata.video.width;
    assert(sc_frame_source_sinks_push_session(&fixture.producer, &metadata));
    gate_wait(&fixture.metadata_entered);
    atomic_store(&fail_next_header, true);
    assert(!sc_frame_source_sinks_push(&fixture.producer, rejected));
    sc_mutex_lock(&fixture.delay.mutex);
    unsigned pending = sc_vecdeque_size(&fixture.delay.queue);
    sc_mutex_unlock(&fixture.delay.mutex);
    printf("executed failed-admission cases: 1; retained queue slots=%u\n", pending);
    fflush(stdout);
    assert(!pending);
    gate_open(&fixture.metadata_release);
    wait_calls(&fixture, 1, 1);
    fixture_destroy(&fixture);
    __wrap_av_frame_free(&first);
    __wrap_av_frame_free(&rejected);
    assert(atomic_load(&first_releases) == 1);
    assert(atomic_load(&rejected_releases) == 1);
}

/** Reference and queue-allocation rejection retire only locally acquired headers. */
static void
test_reference_and_queue_rejection(void) {
    for (unsigned scenario = 0; scenario < 2; ++scenario) {
        struct fixture fixture;
        fixture_init(&fixture);
        atomic_uint releases = 0;
        AVFrame *frame = create_frame(&releases, 0);
        atomic_store(&fail_next_ref, !scenario);
        atomic_store(&fail_next_queue_allocation, scenario);
        assert(!sc_frame_source_sinks_push(&fixture.producer, frame));
        assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
        sc_mutex_lock(&fixture.delay.mutex);
        assert(sc_vecdeque_is_empty(&fixture.delay.queue));
        assert(!fixture.delay.clock.range);
        sc_mutex_unlock(&fixture.delay.mutex);
        assert(sc_frame_source_sinks_push(&fixture.producer, frame));
        wait_calls(&fixture, 0, 1);
        assert(!fixture.delay_waits);
        sc_dispatcher_drain(&fixture.dispatcher);
        assert(fixture.presentations == 1);
        fixture_destroy(&fixture);
        __wrap_av_frame_free(&frame);
        assert(atomic_load(&releases) == 1);
    }
}

/** Failed downstream open and worker creation unwind the actual opened graph. */
static void
test_partial_open_and_worker_start(void) {
    for (unsigned scenario = 0; scenario < 2; ++scenario) {
        struct fixture fixture;
        fixture_prepare(&fixture);
        fixture.reject_open = !scenario;
        atomic_store(&fail_buffer_thread, scenario);
        assert(!fixture_open(&fixture));
        assert(!fixture.bridge.open);
        assert(fixture.sink_closes == scenario);
        assert(!fixture.presentations && !fixture.metadata_calls && !fixture.frame_calls);
        fixture_release(&fixture);
    }
}

/** Run the actual production composition without visible windows or devices. */
int
main(int argc, char **argv) {
    assert(SDL_Init(SDL_INIT_EVENTS));

    if (argc == 2 && !strcmp(argv[1], "--admission")) {
        test_failed_frame_admission();
        SDL_Quit();
        return 0;
    }

    test_metadata_before_first_frame();
    test_multiple_and_subsequent_metadata();
    test_asap_and_ordinary_delay();
    test_close_with_outstanding_metadata();
    test_revocation_with_outstanding_frame();
    test_downstream_rejections();
    test_failed_frame_admission();
    test_reference_and_queue_rejection();
    test_partial_open_and_worker_start();
    assert(atomic_load(&frame_headers_allocated) ==
           atomic_load(&frame_headers_released));
    puts("delay buffer ingress: 9 production-composition groups passed");
    SDL_Quit();
    return 0;
}
