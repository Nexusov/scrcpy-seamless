#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "device_msg.h"
#include "events.h"
#include "receiver.h"
#include "util/binary.h"
#include "util/log.h"

#define SC_RECEIVER_TEST_ALLOCATION_CAPACITY (SC_DISPATCHER_MAX_ITEMS * 2)
#define SC_RECEIVER_TEST_UHID_ID 42

struct tracked_allocation {
    void *pointer;
    unsigned releases;
};
static struct tracked_allocation payloads[SC_RECEIVER_TEST_ALLOCATION_CAPACITY];
static struct tracked_allocation records[SC_RECEIVER_TEST_ALLOCATION_CAPACITY];
static bool capture_payload;
static bool fail_task_allocation;
static unsigned payload_allocations;
static unsigned payload_releases;
static unsigned record_allocations;
static unsigned record_releases;
static unsigned task_attempts;
static unsigned clipboard_effects;
static unsigned hid_effects;
static struct sc_uhid_devices *expected_hid_target;
static const uint8_t uhid_message[] = {
    DEVICE_MSG_TYPE_UHID_OUTPUT, 0, SC_RECEIVER_TEST_UHID_ID, 0, 5, 1, 2, 3, 4, 5,
};
static const uint8_t clipboard_message[] = {
    DEVICE_MSG_TYPE_CLIPBOARD, 0, 0, 0, 3, 'A', 'B', 'C',
};
void *__real_malloc(size_t size);
void __real_free(void *pointer);
void *__wrap_malloc(size_t size);
void __wrap_free(void *pointer);

/** Observe actual device-message allocation without failing payload allocation. */
void *
__wrap_malloc(size_t size) {
    void *pointer = __real_malloc(size);

    if (capture_payload) {
        assert(payload_allocations < SC_RECEIVER_TEST_ALLOCATION_CAPACITY);
        assert(pointer);
        payloads[payload_allocations++].pointer = pointer;
    }

    return pointer;
}

/** Count exact releases while retaining ordinary allocator semantics. */
void
__wrap_free(void *pointer) {
    // Newest first: malloc may reuse an already released address.
    bool observed = false;
    for (unsigned index = payload_allocations; index; --index) {
        struct tracked_allocation *allocation = &payloads[index - 1];

        bool current_allocation = pointer && pointer == allocation->pointer
            && !allocation->releases;

        if (current_allocation) {
            ++allocation->releases;
            ++payload_releases;
            observed = true;
            break;
        }
    }
    for (unsigned index = record_allocations; index; --index) {
        struct tracked_allocation *allocation = &records[index - 1];

        bool current_allocation = pointer && pointer == allocation->pointer
            && !allocation->releases;

        if (!observed && current_allocation) {
            ++allocation->releases;
            ++record_releases;
            break;
        }
    }
    __real_free(pointer);
}

/** Fail only the work record allocated after successful production decoding. */
void *
sc_receiver_test_allocate_task(size_t size) {
    ++task_attempts;

    if (fail_task_allocation) {
        return NULL;
    }

    assert(record_allocations < SC_RECEIVER_TEST_ALLOCATION_CAPACITY);
    void *pointer = __real_malloc(size);
    assert(pointer);
    records[record_allocations++].pointer = pointer;
    return pointer;
}

/** Double the clipboard effect only, leaving production dispatch intact. */
void
sc_receiver_test_set_clipboard(const char *text) {
    assert(text);
    assert(!strcmp(text, "ABC"));
    ++clipboard_effects;
}

/** Assert the resolved current binding without touching physical HID devices. */
void
sc_uhid_devices_process_hid_output(struct sc_uhid_devices *devices,
                                   uint16_t id, const uint8_t *data,
                                   size_t size) {
    assert(devices == expected_hid_target);
    assert(id == SC_RECEIVER_TEST_UHID_ID);
    assert(size == 5);
    assert(!memcmp(data, &uhid_message[5], size));
    ++hid_effects;
}

/** Forbid socket effects; tests enter the actual receiver decoder directly. */
ssize_t
net_recv(sc_socket socket, void *buffer, size_t length) {
    (void) socket;
    (void) buffer;
    (void) length;
    assert(!"Receiver tests must not perform socket I/O");
    return -1;
}

/** Reset counters only after both allocation classes have settled completely. */
static void
reset_observations(void) {
    assert(payload_allocations == payload_releases);
    assert(record_allocations == record_releases);
    memset(payloads, 0, sizeof(payloads));
    memset(records, 0, sizeof(records));
    payload_allocations = payload_releases = 0;
    record_allocations = record_releases = 0;
    task_attempts = clipboard_effects = hid_effects = 0;
    fail_task_allocation = false;
    expected_hid_target = NULL;
}

/** Invoke the production decode-to-owner boundary with exact allocation counts. */
static void
process_message(struct sc_receiver *receiver, const uint8_t *message,
                 size_t length) {
    capture_payload = true;
    ssize_t consumed = sc_receiver_test_process_messages(receiver, message, length);
    capture_payload = false;
    assert(consumed == (ssize_t) length);
}

/** Control notification success without activating windows or real clipboard. */
static bool
test_wakeup(void *userdata, SDL_Event *event) {
    (void) event;
    return !userdata;
}

/** Bind actual receiver input to a real production dispatcher generation. */
static void
initialize_generation(struct sc_dispatcher *dispatcher,
                       struct sc_receiver *receiver,
                       struct sc_uhid_devices *devices, bool wake_failure) {
    assert(sc_dispatcher_init(dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               test_wakeup, wake_failure ? dispatcher : NULL));
    sc_dispatcher_generation generation;
    assert(sc_dispatcher_generation_begin(dispatcher, devices, &generation));
    *receiver = (struct sc_receiver) {
        .uhid_devices = devices,
        .dispatcher = dispatcher,
        .generation = generation,
    };
}

/** Settle queued ownership before releasing dispatcher synchronization storage. */
static void
finish_generation(struct sc_dispatcher *dispatcher) {
    assert(sc_dispatcher_shutdown(dispatcher));
    assert(sc_dispatcher_destroy(dispatcher));
    assert(payload_allocations == payload_releases);
    assert(record_allocations == record_releases);
}

/** Preserve the demonstrated UHID leak regression at the same failure boundary. */
static void
test_uhid_payload_released_after_task_allocation_failure(void) {
    reset_observations();
    struct sc_uhid_devices devices = {0};
    struct sc_receiver receiver = {.uhid_devices = &devices};
    fail_task_allocation = true;
    process_message(&receiver, uhid_message, sizeof(uhid_message));
    assert(payload_allocations == 1 && task_attempts == 1);
    assert(!record_allocations && !hid_effects);
    fprintf(stderr, "UHID allocation failure: allocated=%u released=%u\n",
            payload_allocations, payload_releases);
    assert(payload_releases == 1);
}

/** Cover equivalent allocation rejection for clipboard payload ownership. */
static void
test_clipboard_payload_released_after_task_allocation_failure(void) {
    reset_observations();
    struct sc_receiver receiver = {0};
    fail_task_allocation = true;
    process_message(&receiver, clipboard_message, sizeof(clipboard_message));
    assert(payload_allocations == 1 && payload_releases == 1);
    assert(task_attempts == 1 && !record_allocations && !clipboard_effects);
}

/** Execute both migrated adapters exactly once for the captured current token. */
static void
test_clipboard_and_uhid_execute_for_current_generation(void) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver receiver;
    struct sc_uhid_devices devices = {0};
    initialize_generation(&dispatcher, &receiver, &devices, false);
    expected_hid_target = &devices;
    process_message(&receiver, uhid_message, sizeof(uhid_message));
    process_message(&receiver, clipboard_message, sizeof(clipboard_message));
    assert(!payload_releases && !hid_effects && !clipboard_effects);
    assert(sc_dispatcher_drain(&dispatcher) == 2);
    assert(hid_effects == 1 && clipboard_effects == 1);
    assert(payload_releases == 2 && record_releases == 2);
    finish_generation(&dispatcher);
}

/** Never resolve a freed old target or apply stale work to its replacement. */
static void
test_old_receiver_work_rejected_after_replacement(void) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver old_receiver;
    struct sc_uhid_devices *old_devices = __real_malloc(sizeof(*old_devices));
    assert(old_devices);
    initialize_generation(&dispatcher, &old_receiver, old_devices, false);
    process_message(&old_receiver, uhid_message, sizeof(uhid_message));
    process_message(&old_receiver, clipboard_message, sizeof(clipboard_message));
    assert(sc_dispatcher_generation_revoke(&dispatcher, old_receiver.generation));
    __real_free(old_devices);
    struct sc_uhid_devices replacement = {0};
    sc_dispatcher_generation replacement_generation;
    assert(sc_dispatcher_generation_begin(&dispatcher, &replacement,
                                            &replacement_generation));
    assert(replacement_generation != old_receiver.generation);
    process_message(&old_receiver, uhid_message, sizeof(uhid_message));
    process_message(&old_receiver, clipboard_message, sizeof(clipboard_message));
    assert(!sc_dispatcher_drain(&dispatcher));
    assert(!hid_effects && !clipboard_effects);
    assert(payload_releases == 4 && record_releases == 4);
    finish_generation(&dispatcher);
}

/** Reject pressure under producer ownership and bound each main-thread turn. */
static void
test_receiver_count_pressure_and_bounded_drain(void) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver receiver;
    struct sc_uhid_devices devices = {0};
    initialize_generation(&dispatcher, &receiver, &devices, false);
    expected_hid_target = &devices;
    for (unsigned index = 0; index <= SC_DISPATCHER_MAX_ITEMS; ++index) {
        process_message(&receiver, uhid_message, sizeof(uhid_message));
    }
    struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(&dispatcher);
    assert(stats.retained_count == SC_DISPATCHER_MAX_ITEMS);
    assert(payload_releases == 1 && record_releases == 1);
    assert(receiver.dispatch_rejection_reported);
    assert(sc_dispatcher_drain(&dispatcher) == SC_DISPATCHER_DRAIN_LIMIT);
    assert(hid_effects == SC_DISPATCHER_DRAIN_LIMIT);
    finish_generation(&dispatcher);
}

/** Count full wire allocation including bytes beyond embedded clipboard NULs. */
static void
test_maximum_clipboard_and_retained_byte_pressure(bool embedded_null) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver receiver;
    initialize_generation(&dispatcher, &receiver, NULL, false);
    uint8_t *message = __real_malloc(DEVICE_MSG_MAX_SIZE);
    assert(message);
    message[0] = DEVICE_MSG_TYPE_CLIPBOARD;
    sc_write32be(&message[1], DEVICE_MSG_TEXT_MAX_LENGTH);
    memset(&message[5], 'a', DEVICE_MSG_TEXT_MAX_LENGTH);

    if (embedded_null) {
        message[5] = 0;
    }

    unsigned sent = 0;
    while (!receiver.dispatch_rejection_reported) {
        process_message(&receiver, message, DEVICE_MSG_MAX_SIZE);
        ++sent;
        assert(sent <= SC_DISPATCHER_MAX_ITEMS);
    }
    struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(&dispatcher);
    assert(stats.retained_count && stats.retained_count == sent - 1);
    assert(stats.retained_payload_bytes >=
           stats.retained_count * (DEVICE_MSG_TEXT_MAX_LENGTH + 1));
    assert(stats.retained_payload_bytes <= SC_DISPATCHER_MAX_PAYLOAD_BYTES);
    assert(payload_releases == 1 && record_releases == 1);
    assert(!clipboard_effects);
    __real_free(message);
    finish_generation(&dispatcher);
}

/** Accepted failed notification does not return payload ownership to producer. */
static void
test_receiver_wakeup_failure_keeps_one_release_owner(void) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver receiver;
    struct sc_uhid_devices devices = {0};
    initialize_generation(&dispatcher, &receiver, &devices, true);
    process_message(&receiver, uhid_message, sizeof(uhid_message));
    assert(payload_releases == 1 && record_releases == 1);
    assert(!hid_effects && !receiver.dispatch_rejection_reported);
    finish_generation(&dispatcher);
}

/** Missing UHID binding destroys data before allocating or executing work. */
static void
test_unexpected_uhid_payload_has_no_target_effect(void) {
    reset_observations();
    struct sc_receiver receiver = {0};
    process_message(&receiver, uhid_message, sizeof(uhid_message));
    assert(payload_releases == 1);
    assert(!task_attempts && !record_allocations && !hid_effects);
}

/** Close publication and cancel pending work without clipboard effects. */
static void
test_shutdown_settles_receiver_and_rejects_late_publication(void) {
    reset_observations();
    struct sc_dispatcher dispatcher;
    struct sc_receiver receiver;
    initialize_generation(&dispatcher, &receiver, NULL, false);
    process_message(&receiver, clipboard_message, sizeof(clipboard_message));
    assert(sc_dispatcher_shutdown(&dispatcher));
    process_message(&receiver, clipboard_message, sizeof(clipboard_message));
    assert(payload_releases == 2 && record_releases == 2);
    assert(!clipboard_effects);
    assert(sc_dispatcher_destroy(&dispatcher));
}

/** Exercise production receiver and dispatcher with no devices or real clipboard. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    sc_set_log_level(SC_LOG_LEVEL_INFO);
    test_uhid_payload_released_after_task_allocation_failure();
    test_clipboard_payload_released_after_task_allocation_failure();
    test_clipboard_and_uhid_execute_for_current_generation();
    test_old_receiver_work_rejected_after_replacement();
    test_receiver_count_pressure_and_bounded_drain();
    test_maximum_clipboard_and_retained_byte_pressure(false);
    test_maximum_clipboard_and_retained_byte_pressure(true);
    test_receiver_wakeup_failure_keeps_one_release_owner();
    test_unexpected_uhid_payload_has_no_target_effect();
    test_shutdown_settles_receiver_and_rejects_late_publication();
    reset_observations();
    SDL_Quit();
    fprintf(stderr, "Receiver dispatcher: 10 production-linked cases passed\n");
    return 0;
}
