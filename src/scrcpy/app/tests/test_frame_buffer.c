#include "common.h"

#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <libavutil/buffer.h>
#include <libavutil/mem.h>
#include <libavutil/pixfmt.h>

#include "frame_buffer.h"

#define SC_FRAME_TEST_WIDTH 4
#define SC_FRAME_TEST_HEIGHT 4
#define SC_FRAME_TEST_BYTES (SC_FRAME_TEST_WIDTH * SC_FRAME_TEST_HEIGHT)
#define SC_FRAME_TEST_FAILED_ALLOCATION_LIMIT 1

static unsigned frame_allocation_attempts;
static unsigned frame_allocations;
static unsigned frame_releases;
static unsigned failed_frame_allocation;

AVFrame *__real_av_frame_alloc(void);
void __real_av_frame_free(AVFrame **frame);
AVFrame *__wrap_av_frame_alloc(void);
void __wrap_av_frame_free(AVFrame **frame);

/** Fail one mailbox header allocation while retaining real FFmpeg frames. */
AVFrame *
__wrap_av_frame_alloc(void) {
    ++frame_allocation_attempts;

    if (frame_allocation_attempts == failed_frame_allocation) {
        return NULL;
    }

    AVFrame *frame = __real_av_frame_alloc();

    if (frame) {
        ++frame_allocations;
    }

    return frame;
}

/** Count actual mailbox header releases without changing FFmpeg ownership. */
void
__wrap_av_frame_free(AVFrame **frame) {

    if (*frame) {
        ++frame_releases;
    }

    __real_av_frame_free(frame);
}

/** Count exact buffer destruction independently from AVFrame header release. */
static void
release_pixels(void *context, uint8_t *pixels) {
    unsigned *releases = context;
    ++*releases;
    free(pixels);
}

/** Create a producer-owned refcounted video frame with observable pixel lifetime. */
static AVFrame *
create_frame(unsigned *releases) {
    AVFrame *frame = __real_av_frame_alloc();
    assert(frame);
    uint8_t *pixels = calloc(SC_FRAME_TEST_BYTES, 1);
    assert(pixels);
    frame->buf[0] = av_buffer_create(pixels, SC_FRAME_TEST_BYTES, release_pixels,
                                   releases, 0);
    assert(frame->buf[0]);
    frame->format = AV_PIX_FMT_GRAY8;
    frame->width = SC_FRAME_TEST_WIDTH;
    frame->height = SC_FRAME_TEST_HEIGHT;
    frame->data[0] = pixels;
    frame->linesize[0] = SC_FRAME_TEST_WIDTH;
    return frame;
}

/** Reset instrumentation between independent mailbox initialization attempts. */
static void
reset_allocations(unsigned failed_allocation) {
    frame_allocation_attempts = 0;
    frame_allocations = 0;
    frame_releases = 0;
    failed_frame_allocation = failed_allocation;
}

/** Partial initialization releases exactly the mailbox headers it acquired. */
static void
test_initialization_failure(void) {
    const unsigned allocation_attempts[] = {1, 2};

    for (unsigned index = 0; index < ARRAY_LEN(allocation_attempts); ++index) {
        reset_allocations(allocation_attempts[index]);
        struct sc_frame_buffer buffer = {0};
        assert(!sc_frame_buffer_init(&buffer));
        assert(frame_allocations == index);
        assert(frame_releases == frame_allocations);
        sc_frame_buffer_destroy(&buffer);
        assert(frame_releases == frame_allocations);
    }
}

/** Publication keeps the producer ref, replaces the old pending ref and moves once. */
static void
test_latest_frame_reference_ownership(void) {
    reset_allocations(0);
    struct sc_frame_buffer buffer;
    assert(sc_frame_buffer_init(&buffer));
    assert(!sc_frame_buffer_has_frame(&buffer));
    unsigned first_releases = 0;
    unsigned latest_releases = 0;
    AVFrame *first = create_frame(&first_releases);
    AVFrame *latest = create_frame(&latest_releases);
    AVFrame *consumed = __real_av_frame_alloc();
    assert(consumed);
    assert(sc_frame_buffer_push(&buffer, first));
    assert(sc_frame_buffer_has_frame(&buffer));
    assert(av_buffer_get_ref_count(first->buf[0]) == 2);
    assert(sc_frame_buffer_push(&buffer, latest));
    assert(av_buffer_get_ref_count(first->buf[0]) == 1);
    assert(av_buffer_get_ref_count(latest->buf[0]) == 2);
    assert(!first_releases && !latest_releases);
    sc_frame_buffer_consume(&buffer, consumed);
    assert(!sc_frame_buffer_has_frame(&buffer));
    assert(consumed->data[0] == latest->data[0]);
    assert(av_buffer_get_ref_count(latest->buf[0]) == 2);
    sc_frame_buffer_destroy(&buffer);
    assert(frame_allocations == 2 && frame_releases == 2);
    assert(!latest_releases);
    __real_av_frame_free(&first);
    __real_av_frame_free(&latest);
    assert(first_releases == 1 && !latest_releases);
    __real_av_frame_free(&consumed);
    assert(latest_releases == 1);
}

/** A real FFmpeg ref allocation failure preserves pending and producer ownership. */
static void
test_ref_failure_preserves_pending(void) {
    reset_allocations(0);
    struct sc_frame_buffer buffer;
    assert(sc_frame_buffer_init(&buffer));
    unsigned pending_releases = 0;
    unsigned rejected_releases = 0;
    AVFrame *pending = create_frame(&pending_releases);
    AVFrame *rejected = create_frame(&rejected_releases);
    AVFrame *consumed = __real_av_frame_alloc();
    assert(consumed);
    assert(sc_frame_buffer_push(&buffer, pending));

    // av_frame_ref() needs a new real AVBufferRef; fail its actual allocation.
    av_max_alloc(SC_FRAME_TEST_FAILED_ALLOCATION_LIMIT);
    bool accepted = sc_frame_buffer_push(&buffer, rejected);
    av_max_alloc(INT_MAX);
    assert(!accepted);
    assert(sc_frame_buffer_has_frame(&buffer));
    assert(av_buffer_get_ref_count(pending->buf[0]) == 2);
    assert(av_buffer_get_ref_count(rejected->buf[0]) == 1);
    sc_frame_buffer_consume(&buffer, consumed);
    assert(consumed->data[0] == pending->data[0]);
    av_frame_unref(consumed);

    // The failed candidate is empty and the next current publication progresses.
    assert(sc_frame_buffer_push(&buffer, rejected));
    sc_frame_buffer_consume(&buffer, consumed);
    assert(consumed->data[0] == rejected->data[0]);
    sc_frame_buffer_destroy(&buffer);
    __real_av_frame_free(&pending);
    __real_av_frame_free(&rejected);
    assert(pending_releases == 1 && !rejected_releases);
    __real_av_frame_free(&consumed);
    assert(rejected_releases == 1);
    assert(frame_allocations == frame_releases);
}

/** Discard releases only mailbox refs, preserving independent display/resume refs. */
static void
test_discard_retains_consumer_frames(void) {
    reset_allocations(0);
    struct sc_frame_buffer buffer;
    assert(sc_frame_buffer_init(&buffer));
    unsigned displayed_releases = 0;
    unsigned paused_releases = 0;
    unsigned pending_releases = 0;
    AVFrame *displayed_source = create_frame(&displayed_releases);
    AVFrame *paused_source = create_frame(&paused_releases);
    AVFrame *pending_source = create_frame(&pending_releases);
    AVFrame *displayed = __real_av_frame_alloc();
    AVFrame *paused = __real_av_frame_alloc();
    assert(displayed && paused);
    assert(sc_frame_buffer_push(&buffer, displayed_source));
    sc_frame_buffer_consume(&buffer, displayed);
    assert(sc_frame_buffer_push(&buffer, paused_source));
    sc_frame_buffer_consume(&buffer, paused);
    assert(sc_frame_buffer_push(&buffer, pending_source));
    assert(av_buffer_get_ref_count(pending_source->buf[0]) == 2);
    sc_frame_buffer_discard(&buffer);
    assert(!sc_frame_buffer_has_frame(&buffer));
    assert(av_buffer_get_ref_count(pending_source->buf[0]) == 1);
    assert(av_buffer_get_ref_count(displayed_source->buf[0]) == 2);
    assert(av_buffer_get_ref_count(paused_source->buf[0]) == 2);
    sc_frame_buffer_discard(&buffer);
    __real_av_frame_free(&pending_source);
    assert(pending_releases == 1);
    __real_av_frame_free(&displayed_source);
    __real_av_frame_free(&paused_source);
    assert(!displayed_releases && !paused_releases);
    __real_av_frame_free(&paused);
    assert(paused_releases == 1 && !displayed_releases);

    // Reset/discard leaves an initialized mailbox usable without reallocation.
    assert(sc_frame_buffer_push(&buffer, displayed));
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 2);
    sc_frame_buffer_destroy(&buffer);
    assert(av_buffer_get_ref_count(displayed->buf[0]) == 1);
    __real_av_frame_free(&displayed);
    assert(displayed_releases == 1);
    assert(frame_allocations == 2 && frame_releases == 2);
}

/** Replacement/take defer final buffer callbacks until their caller releases refs. */
static void
test_deferred_reference_release(void) {
    reset_allocations(0);
    struct sc_frame_buffer buffer;
    assert(sc_frame_buffer_init(&buffer));
    unsigned first_releases = 0;
    unsigned latest_releases = 0;
    AVFrame *first = create_frame(&first_releases);
    AVFrame *latest = create_frame(&latest_releases);
    assert(sc_frame_buffer_push(&buffer, first));
    __real_av_frame_free(&first);
    AVFrame *retired = __real_av_frame_alloc();
    assert(retired);
    assert(sc_frame_buffer_publish(&buffer, latest, retired));
    assert(retired->data[0]);
    assert(!first_releases);
    assert(av_buffer_get_ref_count(latest->buf[0]) == 2);
    __real_av_frame_free(&retired);
    assert(first_releases == 1);
    __real_av_frame_free(&latest);
    AVFrame *consumed = __real_av_frame_alloc();
    assert(consumed);
    assert(sc_frame_buffer_take(&buffer, consumed));
    assert(!sc_frame_buffer_has_frame(&buffer));
    assert(!latest_releases);
    AVFrame *empty = __real_av_frame_alloc();
    assert(empty);
    assert(!sc_frame_buffer_take(&buffer, empty));
    assert(!empty->buf[0]);
    __real_av_frame_free(&empty);
    sc_frame_buffer_destroy(&buffer);
    assert(!latest_releases);
    __real_av_frame_free(&consumed);
    assert(latest_releases == 1);
    assert(frame_allocations == frame_releases);
}

/** Run production mailbox ownership against the canonical real FFmpeg runtime. */
int
main(void) {
    test_initialization_failure();
    test_latest_frame_reference_ownership();
    test_ref_failure_preserves_pending();
    test_discard_retains_consumer_frames();
    test_deferred_reference_release();
    puts("frame buffer: 5 real-reference ownership contract groups passed");
    return 0;
}
