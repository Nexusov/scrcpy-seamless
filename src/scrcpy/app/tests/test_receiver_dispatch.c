#include "common.h"

#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

#include "device_msg.h"
#include "events.h"
#include "receiver.h"
#include "util/log.h"

static bool capture_payload;
static bool fail_task_allocation;
static void *decoded_payload;
static unsigned payload_allocations;
static unsigned payload_releases;
static unsigned task_allocations;
static unsigned legacy_posts;
static unsigned hid_effects;

void *__real_malloc(size_t size);
void __real_free(void *pointer);

/** Count the actual deserializer allocation, without failing its allocator. */
void *
__wrap_malloc(size_t size) {
    void *pointer = __real_malloc(size);

    if (capture_payload) {
        assert(!decoded_payload);
        assert(pointer);
        decoded_payload = pointer;
        ++payload_allocations;
    }

    return pointer;
}

/** Observe the actual payload release and retain normal allocator semantics. */
void
__wrap_free(void *pointer) {

    if (pointer && pointer == decoded_payload) {
        ++payload_releases;
        assert(payload_releases == 1);
    }

    __real_free(pointer);
}

/** Fail only the later work-record allocation after decoding has succeeded. */
void *
sc_receiver_test_allocate_task(size_t size) {
    ++task_allocations;

    if (fail_task_allocation) {
        return NULL;
    }

    return __real_malloc(size);
}

/** Reject legacy posting without executing clipboard or HID effects. */
bool
sc_run_on_main_thread(sc_runnable_fn run, void *userdata, bool wait_complete) {
    (void) run;
    (void) userdata;
    (void) wait_complete;
    ++legacy_posts;
    return false;
}

/** Detect any unexpected HID effect without touching an input device. */
void
sc_uhid_devices_process_hid_output(struct sc_uhid_devices *devices,
                                   uint16_t id, const uint8_t *data,
                                   size_t size) {
    (void) devices;
    (void) id;
    (void) data;
    (void) size;
    ++hid_effects;
}

/** Replace socket I/O only; tests enter the actual receiver decode path. */
ssize_t
net_recv(sc_socket socket, void *buffer, size_t length) {
    (void) socket;
    (void) buffer;
    (void) length;
    assert(!"Receiver tests must not perform socket I/O");
    return -1;
}

/** Decode valid UHID data, then prove failed work allocation releases it. */
static void
test_decoded_uhid_payload_released_after_task_allocation_failure(void) {
    static const uint8_t message[] = {
        DEVICE_MSG_TYPE_UHID_OUTPUT,
        0, 42,
        0, 5,
        1, 2, 3, 4, 5,
    };
    struct sc_uhid_devices devices = {0};
    struct sc_receiver receiver = {.uhid_devices = &devices};

    fail_task_allocation = true;
    capture_payload = true;
    ssize_t consumed = sc_receiver_test_process_messages(&receiver, message,
                                                          sizeof(message));
    capture_payload = false;

    assert(consumed == (ssize_t) sizeof(message));
    assert(payload_allocations == 1);
    assert(task_allocations == 1);
    assert(!legacy_posts);
    assert(!hid_effects);
    fprintf(stderr, "UHID allocation failure: allocated=%u released=%u\n",
            payload_allocations, payload_releases);
    assert(payload_releases == 1);
}

/** Run production-linked ownership checks with no phone or clipboard effects. */
int
main(void) {
    sc_set_log_level(SC_LOG_LEVEL_INFO);
    test_decoded_uhid_payload_released_after_task_allocation_failure();
    return 0;
}
