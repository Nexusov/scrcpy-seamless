#include "common.h"

#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <SDL3/SDL.h>
#include <libavutil/buffer.h>
#include <libavutil/mem.h>

#include "events.h"
#include "video_ingress.h"

#define SC_VIDEO_TEST_WIDTH 4
#define SC_VIDEO_TEST_HEIGHT 4
#define SC_VIDEO_TEST_PIXEL_BYTES 24
#define SC_VIDEO_TEST_METADATA_WIDTH 800
#define SC_VIDEO_TEST_METADATA_HEIGHT 600
#define SC_VIDEO_TEST_NOTIFICATION_CAPACITY 256

struct test_gate {
    sc_mutex mutex;
    sc_cond condition;
    bool open;
};

struct notification_allocation {
    void *pointer;
    bool released;
};

struct test_fixture {
    struct sc_dispatcher dispatcher;
    struct sc_video_ingress ingress;
    struct sc_video_bridge bridge;
    sc_dispatcher_generation generation;
    AVFrame *displayed;
    struct sc_stream_session metadata;
    unsigned skipped;
    unsigned presentations;
    unsigned wakeups;
    unsigned header_allocation_base;
    unsigned header_release_base;
    bool fail_wakeup;
    struct test_gate *wake_entered;
    struct test_gate *wake_release;
    struct sc_video_bridge *refill_bridge;
    AVFrame *refill_frame;
};

struct test_worker {
    struct sc_video_bridge *bridge;
    AVFrame *frame;
    bool accepted;
    struct test_gate *start;
};

struct reentrant_buffer_probe {
    struct sc_video_ingress *ingress;
    AVFrame *empty;
    unsigned releases;
    struct test_gate *entered;
    struct test_gate *release;
};

struct dispatcher_worker {
    struct test_fixture *fixture;
    enum sc_dispatcher_admission admission;
};

static struct notification_allocation notifications[SC_VIDEO_TEST_NOTIFICATION_CAPACITY];
static unsigned notification_allocations;
static unsigned notification_releases;
static bool observe_notification;
static bool fail_notification_allocation;
static unsigned header_attempts;
static unsigned header_allocations;
static unsigned header_releases;
static unsigned failed_header;

void *__real_malloc(size_t size);
void __real_free(void *pointer);
void *__wrap_malloc(size_t size);
void __wrap_free(void *pointer);
AVFrame *__real_av_frame_alloc(void);
void __real_av_frame_free(AVFrame **frame);
AVFrame *__wrap_av_frame_alloc(void);
void __wrap_av_frame_free(AVFrame **frame);

/** Observe actual notification allocation and fail one explicit allocation. */
void *
__wrap_malloc(size_t size) {

    if (observe_notification && fail_notification_allocation) {
        fail_notification_allocation = false;
        return NULL;
    }

    void *pointer = __real_malloc(size);

    if (observe_notification && pointer) {
        assert(notification_allocations < SC_VIDEO_TEST_NOTIFICATION_CAPACITY);
        notifications[notification_allocations++] =
            (struct notification_allocation) {.pointer = pointer};
    }

    return pointer;
}

/** Count exact notification destruction while retaining ordinary frees. */
void
__wrap_free(void *pointer) {

    for (unsigned index = notification_allocations; index; --index) {
        struct notification_allocation *allocation = &notifications[index - 1];
        bool current = pointer && pointer == allocation->pointer &&
                       !allocation->released;

        if (current) {
            allocation->released = true;
            ++notification_releases;
            break;
        }
    }

    __real_free(pointer);
}

/** Fail an ingress-owned header allocation without replacing FFmpeg operations. */
AVFrame *
__wrap_av_frame_alloc(void) {
    ++header_attempts;

    if (header_attempts == failed_header) {
        return NULL;
    }

    AVFrame *frame = __real_av_frame_alloc();

    if (frame) {
        ++header_allocations;
    }

    return frame;
}

/** Observe ingress-owned header release independently from pixel release. */
void
__wrap_av_frame_free(AVFrame **frame) {

    if (*frame) {
        ++header_releases;
    }

    __real_av_frame_free(frame);
}

/** Initialize deterministic synchronization without elapsed-time ordering. */
static void
gate_init(struct test_gate *gate) {
    *gate = (struct test_gate) {0};
    assert(sc_mutex_init(&gate->mutex));
    assert(sc_cond_init(&gate->condition));
}

/** Release all participants waiting at this explicit transition. */
static void
gate_open(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);
    gate->open = true;
    sc_cond_broadcast(&gate->condition);
    sc_mutex_unlock(&gate->mutex);
}

/** Wait for an explicit state transition with the outer test timeout as watchdog. */
static void
gate_wait(struct test_gate *gate) {
    sc_mutex_lock(&gate->mutex);

    while (!gate->open) {
        sc_cond_wait(&gate->condition, &gate->mutex);
    }

    sc_mutex_unlock(&gate->mutex);
}

/** Destroy synchronization after all participating workers join. */
static void
gate_destroy(struct test_gate *gate) {
    sc_cond_destroy(&gate->condition);
    sc_mutex_destroy(&gate->mutex);
}

/** Observe exact decoded-pixel buffer release. */
static void
release_pixels(void *context, uint8_t *pixels) {
    ++*(unsigned *) context;
    __real_free(pixels);
}

/** Create a real refcounted decoded frame with a test-owned buffer destructor. */
static AVFrame *
create_frame(unsigned *releases) {
    AVFrame *frame = __real_av_frame_alloc();
    assert(frame);
    uint8_t *pixels = calloc(SC_VIDEO_TEST_PIXEL_BYTES, 1);
    assert(pixels);
    frame->buf[0] = av_buffer_create(pixels, SC_VIDEO_TEST_PIXEL_BYTES,
                                   release_pixels, releases, 0);
    assert(frame->buf[0]);
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = SC_VIDEO_TEST_WIDTH;
    frame->height = SC_VIDEO_TEST_HEIGHT;
    frame->data[0] = pixels;
    return frame;
}

/** Consume through the real presentation boundary with optional reentrant publication. */
static void
present_frame(void *binding, struct sc_video_ingress *ingress,
               sc_dispatcher_generation generation) {
    struct test_fixture *fixture = binding;
    assert(sc_thread_is_main());
    assert(ingress == &fixture->ingress);
    assert(generation == fixture->generation);
    av_frame_unref(fixture->displayed);
    bool consumed = sc_video_ingress_consume(ingress, generation,
                                             fixture->displayed,
                                             &fixture->metadata,
                                             &fixture->skipped);

    if (consumed) {
        ++fixture->presentations;
    }

    if (fixture->refill_frame) {
        AVFrame *frame = fixture->refill_frame;
        fixture->refill_frame = NULL;
        assert(fixture->refill_bridge->frame_sink.ops->push(
            &fixture->refill_bridge->frame_sink, frame));
    }
}

/** Publish controlled SDL wakes; a held old wake may fail after replacement. */
static bool
wakeup(void *context, SDL_Event *event) {
    struct test_fixture *fixture = context;
    ++fixture->wakeups;
    bool fail = fixture->fail_wakeup;

    if (fixture->wake_entered) {
        struct test_gate *entered = fixture->wake_entered;
        struct test_gate *release = fixture->wake_release;
        fixture->wake_entered = NULL;
        gate_open(entered);
        gate_wait(release);
    }

    return !fail && SDL_PushEvent(event);
}

/** Bind a bridge to the one production dispatcher generation authority. */
static void
bind_bridge(struct test_fixture *fixture) {
    assert(sc_dispatcher_generation_begin(&fixture->dispatcher, fixture,
                                           &fixture->generation));
    sc_video_ingress_bind(&fixture->ingress, fixture->generation);
    sc_video_bridge_init(&fixture->bridge, &fixture->ingress,
                          fixture->generation);
    const AVCodecContext context = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = SC_VIDEO_TEST_WIDTH,
        .height = SC_VIDEO_TEST_HEIGHT,
    };
    const struct sc_stream_session metadata = {
        .video = {.width = SC_VIDEO_TEST_METADATA_WIDTH,
                  .height = SC_VIDEO_TEST_METADATA_HEIGHT},
    };
    assert(fixture->bridge.frame_sink.ops->open(&fixture->bridge.frame_sink,
                                               &context, &metadata));
}

/** Initialize a fixture with events only and no window, device or clipboard. */
static void
fixture_init(struct test_fixture *fixture) {
    *fixture = (struct test_fixture) {0};
    fixture->header_allocation_base = header_allocations;
    fixture->header_release_base = header_releases;
    assert(sc_dispatcher_init(&fixture->dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                              wakeup, fixture));
    assert(sc_video_ingress_init(&fixture->ingress, &fixture->dispatcher,
                                 present_frame));
    fixture->displayed = __real_av_frame_alloc();
    assert(fixture->displayed);
    bind_bridge(fixture);
}

/** Close mailbox admission before settling dispatcher records and rebinding. */
static void
revoke_fixture(struct test_fixture *fixture) {
    sc_video_ingress_revoke(&fixture->ingress, fixture->generation);
    assert(sc_dispatcher_generation_revoke(&fixture->dispatcher,
                                            fixture->generation));
}

/** Settle exact-instance notifications before releasing app ingress storage. */
static void
fixture_destroy(struct test_fixture *fixture) {
    revoke_fixture(fixture);
    fixture->bridge.frame_sink.ops->close(&fixture->bridge.frame_sink);
    assert(sc_dispatcher_shutdown(&fixture->dispatcher));
    assert(sc_dispatcher_destroy(&fixture->dispatcher));
    sc_video_ingress_destroy(&fixture->ingress);
    __real_av_frame_free(&fixture->displayed);
    assert(notification_allocations == notification_releases);
    assert(header_allocations - fixture->header_allocation_base ==
           header_releases - fixture->header_release_base);
}

/** Deliver only this test's registered dispatcher events. */
static void
deliver_events(struct test_fixture *fixture) {
    SDL_Event event;

    while (sc_dequeue_event(SC_EVENT_DISPATCHER_WAKEUP, &event)) {
        assert(sc_dispatcher_handle_event(&fixture->dispatcher, &event));
    }
}

/** Observe notification allocation only during an actual producer call. */
static bool
push_frame(struct sc_video_bridge *bridge, AVFrame *frame) {
    observe_notification = true;
    bool accepted = bridge->frame_sink.ops->push(&bridge->frame_sink, frame);
    observe_notification = false;
    return accepted;
}

/** Run a producer with an optional gate immediately before the real bridge push. */
static int
worker_push(void *context) {
    struct test_worker *worker = context;

    if (worker->start) {
        gate_wait(worker->start);
    }

    worker->accepted = push_frame(worker->bridge, worker->frame);
    return 0;
}

/** Coalescing retains one frame/record and consumes its own metadata snapshot. */
static void
test_latest_metadata_and_duplicate_wakeup(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    unsigned first_releases = 0, latest_releases = 0;
    AVFrame *first = create_frame(&first_releases);
    AVFrame *latest = create_frame(&latest_releases);
    unsigned allocations_before = notification_allocations;
    assert(push_frame(&fixture.bridge, first));
    const struct sc_stream_session metadata = {
        .video = {.width = SC_VIDEO_TEST_METADATA_HEIGHT,
                  .height = SC_VIDEO_TEST_METADATA_WIDTH,
                  .client_resized = true},
    };
    assert(fixture.bridge.frame_sink.ops->push_session(
        &fixture.bridge.frame_sink, &metadata));
    assert(push_frame(&fixture.bridge, latest));
    const struct sc_stream_session future_metadata = {
        .video = {.width = SC_VIDEO_TEST_METADATA_WIDTH,
                  .height = SC_VIDEO_TEST_METADATA_HEIGHT},
    };
    assert(fixture.bridge.frame_sink.ops->push_session(
        &fixture.bridge.frame_sink, &future_metadata));
    assert(notification_allocations == allocations_before + 1);
    assert(fixture.wakeups == 1);
    assert(av_buffer_get_ref_count(first->buf[0]) == 1);
    assert(av_buffer_get_ref_count(latest->buf[0]) == 2);
    struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(&fixture.dispatcher);
    assert(stats.retained_count == 1 && stats.queued_count == 1);
    assert(stats.retained_payload_bytes);
    assert(stats.retained_payload_bytes <= SC_DISPATCHER_MAX_PAYLOAD_BYTES);
    SDL_Event event;
    assert(sc_dequeue_event(SC_EVENT_DISPATCHER_WAKEUP, &event));
    assert(sc_dispatcher_handle_event(&fixture.dispatcher, &event));
    assert(fixture.presentations == 1 && fixture.skipped == 1);
    assert(fixture.displayed->data[0] == latest->data[0]);
    assert(fixture.metadata.video.width == metadata.video.width);
    assert(fixture.metadata.video.height == metadata.video.height);
    assert(fixture.metadata.video.client_resized);
    assert(sc_dispatcher_handle_event(&fixture.dispatcher, &event));
    assert(fixture.presentations == 1);
    fixture_destroy(&fixture);
    __real_av_frame_free(&first);
    __real_av_frame_free(&latest);
    assert(first_releases == 1 && latest_releases == 1);
}

/** Obsolete bridge callbacks and queued wakes cannot mutate replacement pixels/metadata. */
static void
test_obsolete_bridge_and_wakeup(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    struct sc_video_bridge old_bridge = fixture.bridge;
    unsigned old_releases = 0, replacement_releases = 0;
    AVFrame *old = create_frame(&old_releases);
    AVFrame *replacement = create_frame(&replacement_releases);
    assert(push_frame(&old_bridge, old));
    deliver_events(&fixture);
    assert(fixture.presentations == 1);
    assert(fixture.displayed->data[0] == old->data[0]);
    assert(push_frame(&old_bridge, old));
    SDL_Event obsolete_event;
    assert(sc_dequeue_event(SC_EVENT_DISPATCHER_WAKEUP, &obsolete_event));
    sc_dispatcher_generation old_generation = fixture.generation;
    revoke_fixture(&fixture);
    assert(av_buffer_get_ref_count(old->buf[0]) == 2);
    assert(fixture.displayed->data[0] == old->data[0]);
    bind_bridge(&fixture);
    assert(push_frame(&fixture.bridge, replacement));
    const AVCodecContext context = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = SC_VIDEO_TEST_WIDTH,
        .height = SC_VIDEO_TEST_HEIGHT,
    };
    const struct sc_stream_session obsolete_metadata = {
        .video = {.width = 1, .height = 1, .client_resized = true},
    };
    assert(!old_bridge.frame_sink.ops->open(&old_bridge.frame_sink, &context,
                                           &obsolete_metadata));
    assert(!old_bridge.frame_sink.ops->push_session(&old_bridge.frame_sink,
                                                   &obsolete_metadata));
    assert(!push_frame(&old_bridge, old));
    old_bridge.frame_sink.ops->close(&old_bridge.frame_sink);
    sc_video_ingress_revoke(&fixture.ingress, old_generation);
    struct sc_stream_session untouched_metadata;
    unsigned untouched_skipped;
    assert(!sc_video_ingress_consume(&fixture.ingress, old_generation,
                                     fixture.displayed, &untouched_metadata,
                                     &untouched_skipped));
    assert(fixture.presentations == 1);
    assert(fixture.displayed->data[0] == old->data[0]);
    assert(av_buffer_get_ref_count(replacement->buf[0]) == 2);

    // The dispatcher wake is application-scoped: it may coalesce the new record.
    assert(sc_dispatcher_handle_event(&fixture.dispatcher, &obsolete_event));
    deliver_events(&fixture);
    assert(fixture.presentations == 2);
    assert(fixture.displayed->data[0] == replacement->data[0]);
    assert(fixture.metadata.video.width == SC_VIDEO_TEST_METADATA_WIDTH);
    assert(!fixture.metadata.video.client_resized);
    fixture_destroy(&fixture);
    __real_av_frame_free(&old);
    __real_av_frame_free(&replacement);
    assert(old_releases == 1 && replacement_releases == 1);
}

/** Rejected queue admission drops its ref and the next frame makes progress. */
static void
pressure_run(void *binding, void *payload) {
    assert(binding == *(struct test_fixture **) payload);
}

/** Release the actual payload bytes charged to the dispatcher pressure record. */
static void
pressure_destroy(void *payload) {
    free(payload);
}

/** Count/byte pressure and accepted wake failure each release pending ownership. */
static void
test_pressure_and_wakeup_failure(void) {
    const unsigned scenarios = 3;

    for (unsigned scenario = 0; scenario < scenarios; ++scenario) {
        struct test_fixture fixture;
        fixture_init(&fixture);
        unsigned releases = 0;
        AVFrame *frame = create_frame(&releases);
        struct sc_dispatcher_task task = {
            .run = pressure_run, .destroy = pressure_destroy,
            .payload_bytes = scenario == 1 ? SC_DISPATCHER_MAX_PAYLOAD_BYTES :
                                             sizeof(struct test_fixture *),
        };
        unsigned task_count = scenario == 0 ? SC_DISPATCHER_MAX_ITEMS :
                              scenario == 1 ? 1 : 0;

        for (unsigned index = 0; index < task_count; ++index) {
            task.payload = malloc(task.payload_bytes);
            assert(task.payload);
            *(struct test_fixture **) task.payload = &fixture;
            assert(sc_dispatcher_post(&fixture.dispatcher, fixture.generation,
                                       &task, NULL) == SC_DISPATCHER_ACCEPTED);
        }

        fixture.fail_wakeup = scenario == 2;
        assert(push_frame(&fixture.bridge, frame));
        assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
        struct sc_stream_session metadata;
        unsigned skipped;
        assert(!sc_video_ingress_consume(&fixture.ingress, fixture.generation,
                                         fixture.displayed, &metadata, &skipped));
        deliver_events(&fixture);
        assert(!fixture.presentations);
        fixture.fail_wakeup = false;
        assert(push_frame(&fixture.bridge, frame));
        deliver_events(&fixture);
        assert(fixture.presentations == 1);
        fixture_destroy(&fixture);
        __real_av_frame_free(&frame);
        assert(releases == 1);
    }
}

/** Publication during a callback survives the old record's later destructor. */
static void
test_publication_during_consumption(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    unsigned first_releases = 0, refill_releases = 0;
    AVFrame *first = create_frame(&first_releases);
    AVFrame *refill = create_frame(&refill_releases);
    fixture.refill_frame = refill;
    fixture.refill_bridge = &fixture.bridge;
    assert(push_frame(&fixture.bridge, first));
    deliver_events(&fixture);
    assert(fixture.presentations == 2);
    assert(fixture.displayed->data[0] == refill->data[0]);
    assert(av_buffer_get_ref_count(first->buf[0]) == 1);
    fixture_destroy(&fixture);
    __real_av_frame_free(&first);
    __real_av_frame_free(&refill);
    assert(first_releases == 1 && refill_releases == 1);
}

/** An admitted old publication can settle after replacement without dropping new work. */
static void
test_publication_revoke_orderings(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    struct sc_video_bridge old_bridge = fixture.bridge;
    unsigned old_releases = 0, replacement_releases = 0;
    AVFrame *old = create_frame(&old_releases);
    AVFrame *replacement = create_frame(&replacement_releases);
    struct test_gate entered, release;
    gate_init(&entered);
    gate_init(&release);
    fixture.wake_entered = &entered;
    fixture.wake_release = &release;
    fixture.fail_wakeup = true;
    struct test_worker worker = {.bridge = &old_bridge, .frame = old};
    sc_thread thread;
    assert(sc_thread_create(&thread, worker_push, "video-publish", &worker));
    gate_wait(&entered);
    revoke_fixture(&fixture);
    // An empty main-thread drain retires the old application wake reservation.
    assert(!sc_dispatcher_drain(&fixture.dispatcher));
    bind_bridge(&fixture);
    fixture.fail_wakeup = false;
    assert(push_frame(&fixture.bridge, replacement));
    gate_open(&release);
    sc_thread_join(&thread, NULL);
    assert(worker.accepted);
    assert(av_buffer_get_ref_count(old->buf[0]) == 1);
    assert(av_buffer_get_ref_count(replacement->buf[0]) == 2);
    deliver_events(&fixture);
    assert(fixture.presentations == 1);

    // Revoke precedes a second old worker's actual publication attempt.
    struct test_gate start;
    gate_init(&start);
    worker = (struct test_worker) {.bridge = &old_bridge, .frame = old,
                                  .start = &start};
    assert(sc_thread_create(&thread, worker_push, "video-obsolete", &worker));
    gate_open(&start);
    sc_thread_join(&thread, NULL);
    assert(!worker.accepted);
    assert(av_buffer_get_ref_count(old->buf[0]) == 1);
    old_bridge.frame_sink.ops->close(&old_bridge.frame_sink);
    fixture_destroy(&fixture);
    gate_destroy(&start);
    gate_destroy(&entered);
    gate_destroy(&release);
    __real_av_frame_free(&old);
    __real_av_frame_free(&replacement);
    assert(old_releases == 1 && replacement_releases == 1);
}

/** Partial ingress allocation and real frame-ref failure unwind acquired refs. */
static void
test_initialization_and_ref_failure(void) {
    const unsigned allocation_attempts[] = {1, 2, 3};

    for (unsigned index = 0; index < ARRAY_LEN(allocation_attempts); ++index) {
        struct sc_video_ingress ingress;
        header_attempts = 0;
        header_allocations = 0;
        header_releases = 0;
        failed_header = allocation_attempts[index];
        assert(!sc_video_ingress_init(&ingress, NULL, present_frame));
        assert(header_allocations == index && header_releases == index);
    }

    failed_header = 0;
    struct test_fixture fixture;
    fixture_init(&fixture);
    unsigned pending_releases = 0, rejected_releases = 0;
    AVFrame *pending = create_frame(&pending_releases);
    AVFrame *rejected = create_frame(&rejected_releases);
    assert(push_frame(&fixture.bridge, pending));
    av_max_alloc(1);
    bool accepted = push_frame(&fixture.bridge, rejected);
    av_max_alloc(INT_MAX);
    assert(!accepted);
    assert(av_buffer_get_ref_count(rejected->buf[0]) == 1);
    deliver_events(&fixture);
    assert(fixture.displayed->data[0] == pending->data[0]);
    fixture_destroy(&fixture);
    __real_av_frame_free(&pending);
    __real_av_frame_free(&rejected);
    assert(pending_releases == 1 && rejected_releases == 1);
}

/** Allocation failure cannot leave an unreachable pending frame; a later push recovers. */
static void
test_notification_allocation_failure(void) {
    const unsigned failure_scenarios = 2;

    for (unsigned scenario = 0; scenario < failure_scenarios; ++scenario) {
        struct test_fixture fixture;
        fixture_init(&fixture);
        unsigned releases = 0;
        AVFrame *frame = create_frame(&releases);
        fail_notification_allocation = !scenario;
        failed_header = scenario ? header_attempts + 1 : 0;
        assert(!push_frame(&fixture.bridge, frame));
        failed_header = 0;
        assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
        assert(!sc_dispatcher_get_stats(&fixture.dispatcher).retained_count);
        assert(push_frame(&fixture.bridge, frame));
        deliver_events(&fixture);
        assert(fixture.presentations == 1);
        fixture_destroy(&fixture);
        __real_av_frame_free(&frame);
        assert(releases == 1);
    }
}

/** Invalid/failed opens and metadata-only updates do not announce presentation. */
static void
test_open_and_metadata_without_frame(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    const struct sc_stream_session metadata = {
        .video = {.width = SC_VIDEO_TEST_METADATA_HEIGHT,
                  .height = SC_VIDEO_TEST_METADATA_WIDTH,
                  .client_resized = true},
    };
    assert(fixture.bridge.frame_sink.ops->push_session(&fixture.bridge.frame_sink,
                                                      &metadata));
    assert(!fixture.presentations && !fixture.wakeups);
    assert(!sc_dispatcher_get_stats(&fixture.dispatcher).retained_count);
    fixture.bridge.frame_sink.ops->close(&fixture.bridge.frame_sink);
    assert(!fixture.bridge.frame_sink.ops->push_session(&fixture.bridge.frame_sink,
                                                       &metadata));
    const AVCodecContext context = {
        .pix_fmt = AV_PIX_FMT_YUV420P,
        .width = SC_VIDEO_TEST_WIDTH,
        .height = SC_VIDEO_TEST_HEIGHT,
    };
    AVCodecContext invalid_context = context;
    invalid_context.pix_fmt = AV_PIX_FMT_NV12;
    assert(!fixture.bridge.frame_sink.ops->open(&fixture.bridge.frame_sink,
                                               &invalid_context, &metadata));
    failed_header = header_attempts + 1;
    assert(!fixture.bridge.frame_sink.ops->open(&fixture.bridge.frame_sink,
                                               &context, &metadata));
    failed_header = 0;
    unsigned releases = 0;
    AVFrame *frame = create_frame(&releases);
    assert(!push_frame(&fixture.bridge, frame));
    assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
    assert(!fixture.presentations && !fixture.wakeups);
    assert(fixture.bridge.frame_sink.ops->open(&fixture.bridge.frame_sink,
                                              &context, &metadata));
    assert(!fixture.bridge.frame_sink.ops->open(&fixture.bridge.frame_sink,
                                               &context, &metadata));
    const struct sc_stream_session invalid_metadata = {
        .video = {.height = SC_VIDEO_TEST_METADATA_HEIGHT},
    };
    assert(!fixture.bridge.frame_sink.ops->push_session(&fixture.bridge.frame_sink,
                                                       &invalid_metadata));
    assert(push_frame(&fixture.bridge, frame));
    deliver_events(&fixture);
    assert(fixture.presentations == 1);
    assert(fixture.metadata.video.client_resized);
    unsigned wakes_before_rejection = fixture.wakeups;
    frame->width = 0;
    assert(!push_frame(&fixture.bridge, frame));
    assert(fixture.wakeups == wakes_before_rejection);
    assert(fixture.presentations == 1);
    assert(av_buffer_get_ref_count(frame->buf[0]) == 2);
    fixture_destroy(&fixture);
    __real_av_frame_free(&frame);
    assert(releases == 1);
}

/** A real AVBuffer destructor may reenter ingress APIs and must run outside its lock. */
static void
release_reentrant_pixels(void *context, uint8_t *pixels) {
    struct reentrant_buffer_probe *probe = context;
    struct sc_stream_session metadata;
    unsigned skipped;
    assert(!sc_video_ingress_consume(probe->ingress, 0, probe->empty,
                                     &metadata, &skipped));

    if (probe->entered) {
        gate_open(probe->entered);
        gate_wait(probe->release);
    }

    ++probe->releases;
    __real_free(pixels);
}

/** Create a real frame whose last reference exercises an external cleanup callback. */
static AVFrame *
create_reentrant_frame(struct reentrant_buffer_probe *probe) {
    AVFrame *frame = __real_av_frame_alloc();
    assert(frame);
    uint8_t *pixels = calloc(SC_VIDEO_TEST_PIXEL_BYTES, 1);
    assert(pixels);
    frame->buf[0] = av_buffer_create(pixels, SC_VIDEO_TEST_PIXEL_BYTES,
                                   release_reentrant_pixels, probe, 0);
    assert(frame->buf[0]);
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = SC_VIDEO_TEST_WIDTH;
    frame->height = SC_VIDEO_TEST_HEIGHT;
    frame->data[0] = pixels;
    return frame;
}

/** Replacement and revoke release final refs without holding the ingress mutex. */
static void
test_reentrant_final_buffer_release(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    struct reentrant_buffer_probe probe = {
        .ingress = &fixture.ingress,
        .empty = __real_av_frame_alloc(),
    };
    assert(probe.empty);
    AVFrame *first = create_reentrant_frame(&probe);
    AVFrame *latest = create_reentrant_frame(&probe);
    assert(push_frame(&fixture.bridge, first));
    __real_av_frame_free(&first);
    assert(!probe.releases);
    assert(push_frame(&fixture.bridge, latest));
    assert(probe.releases == 1);
    __real_av_frame_free(&latest);
    fixture_destroy(&fixture);
    assert(probe.releases == 2);
    __real_av_frame_free(&probe.empty);
}

/** Emulate a receiver producer posting through the same application dispatcher. */
static int
receiver_worker_post(void *context) {
    struct dispatcher_worker *worker = context;
    struct test_fixture **payload = malloc(sizeof(*payload));
    assert(payload);
    *payload = worker->fixture;
    const struct sc_dispatcher_task task = {
        .run = pressure_run,
        .destroy = pressure_destroy,
        .payload = payload,
        .payload_bytes = sizeof(*payload),
    };
    worker->admission = sc_dispatcher_post(&worker->fixture->dispatcher,
                                            worker->fixture->generation,
                                            &task, NULL);
    assert(worker->admission == SC_DISPATCHER_ACCEPTED);
    return 0;
}

/** Failure-thread notification cleanup may finish after main revoke/replacement. */
static void
test_failure_retirement_quiescence(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    struct test_gate wake_entered, wake_release, cleanup_entered, cleanup_release;
    gate_init(&wake_entered);
    gate_init(&wake_release);
    gate_init(&cleanup_entered);
    gate_init(&cleanup_release);
    fixture.wake_entered = &wake_entered;
    fixture.wake_release = &wake_release;
    fixture.fail_wakeup = true;
    struct dispatcher_worker worker = {.fixture = &fixture};
    sc_thread thread;
    assert(sc_thread_create(&thread, receiver_worker_post, "receiver-wake",
                            &worker));
    gate_wait(&wake_entered);
    struct reentrant_buffer_probe probe = {
        .ingress = &fixture.ingress,
        .empty = __real_av_frame_alloc(),
        .entered = &cleanup_entered,
        .release = &cleanup_release,
    };
    assert(probe.empty);
    AVFrame *old = create_reentrant_frame(&probe);
    assert(push_frame(&fixture.bridge, old));
    __real_av_frame_free(&old);
    gate_open(&wake_release);
    gate_wait(&cleanup_entered);
    assert(!probe.releases);

    // The held destructor has already moved its old mailbox ref outside the lock.
    revoke_fixture(&fixture);
    fixture.bridge.frame_sink.ops->close(&fixture.bridge.frame_sink);
    bind_bridge(&fixture);
    fixture.fail_wakeup = false;
    unsigned replacement_releases = 0;
    AVFrame *replacement = create_frame(&replacement_releases);
    assert(push_frame(&fixture.bridge, replacement));
    deliver_events(&fixture);
    assert(fixture.presentations == 1);
    assert(fixture.displayed->data[0] == replacement->data[0]);
    gate_open(&cleanup_release);
    sc_thread_join(&thread, NULL);
    assert(worker.admission == SC_DISPATCHER_ACCEPTED);
    assert(probe.releases == 1);
    assert(!sc_dispatcher_get_stats(&fixture.dispatcher).retained_count);

    // App ingress destruction follows the final failure-side publisher join.
    fixture_destroy(&fixture);
    __real_av_frame_free(&probe.empty);
    __real_av_frame_free(&replacement);
    assert(replacement_releases == 1);
    gate_destroy(&wake_entered);
    gate_destroy(&wake_release);
    gate_destroy(&cleanup_entered);
    gate_destroy(&cleanup_release);
}

/** An exhausted monotonic ticket drops ownership and cannot alias across rebind. */
static void
test_notification_ticket_exhaustion(void) {
    struct test_fixture fixture;
    fixture_init(&fixture);
    // Controlled counter setup only; no producer or pending frame exists yet.
    fixture.ingress.ticket = UINT64_MAX;
    unsigned releases = 0;
    AVFrame *frame = create_frame(&releases);
    assert(!push_frame(&fixture.bridge, frame));
    assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
    assert(!fixture.wakeups && !fixture.presentations);
    assert(!sc_dispatcher_get_stats(&fixture.dispatcher).retained_count);
    revoke_fixture(&fixture);
    fixture.bridge.frame_sink.ops->close(&fixture.bridge.frame_sink);
    bind_bridge(&fixture);
    assert(!push_frame(&fixture.bridge, frame));
    assert(av_buffer_get_ref_count(frame->buf[0]) == 1);
    assert(!fixture.wakeups && !fixture.presentations);
    fixture_destroy(&fixture);
    __real_av_frame_free(&frame);
    assert(releases == 1);
}

/** Run actual generation ingress/mailbox/dispatcher with controlled presentation effects. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    test_latest_metadata_and_duplicate_wakeup();
    test_obsolete_bridge_and_wakeup();
    test_pressure_and_wakeup_failure();
    test_publication_during_consumption();
    test_publication_revoke_orderings();
    test_initialization_and_ref_failure();
    test_notification_allocation_failure();
    test_open_and_metadata_without_frame();
    test_reentrant_final_buffer_release();
    test_failure_retirement_quiescence();
    test_notification_ticket_exhaustion();
    puts("video ingress: 11 ownership/notification contract groups passed");
    SDL_Quit();
    return 0;
}
