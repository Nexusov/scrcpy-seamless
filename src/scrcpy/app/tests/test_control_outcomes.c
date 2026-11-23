#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "controller.h"
#include "device_msg.h"
#include "util/binary.h"

static void *device_payload_allocate(size_t size);
#define malloc device_payload_allocate
#include "../src/device_msg.c"
#undef malloc

#define process_msg test_receiver_process_msg
#ifndef SC_RECEIVER_TEST_SOURCE
# define SC_RECEIVER_TEST_SOURCE "../src/receiver.c"
#endif
#include SC_RECEIVER_TEST_SOURCE
#undef process_msg
#include "../src/controller.c"

enum control_case {
    SENDER_IO, SENDER_LOCAL, SENDER_STOP, SENDER_PARTIAL_AFTER_SUCCESS,
    RECEIVER_EOF, RECEIVER_IO, RECEIVER_MALFORMED,
    RECEIVER_OOM, RECEIVER_FRAGMENTED, RECEIVER_FULL_BUFFER,
};

static enum control_case scenario;
static size_t received;
static unsigned send_calls;
static unsigned completions;
static enum sc_control_status observed_status;

static void *
device_payload_allocate(size_t size) {
    return scenario == RECEIVER_OOM ? NULL : malloc(size);
}

ssize_t
net_send_all(sc_socket socket, const void *bytes, size_t length) {
    (void) socket;
    (void) bytes;
    assert((scenario == SENDER_IO || scenario == SENDER_PARTIAL_AFTER_SUCCESS)
           && length > 1);
    ++send_calls;

    if (scenario == SENDER_PARTIAL_AFTER_SUCCESS) {
        return send_calls == 1 ? (ssize_t) length : (ssize_t) length - 1;
    }

    return -1;
}

ssize_t
net_recv(sc_socket socket, void *output, size_t capacity) {
    (void) socket;

    if (scenario == RECEIVER_IO) {
        return -1;
    }

    if (scenario == RECEIVER_MALFORMED && !received++) {
        *(uint8_t *) output = 0xff;
        return 1;
    }

    if (scenario == RECEIVER_FULL_BUFFER && !received++) {
        memset(output, 0, capacity);
        ((uint8_t *) output)[0] = DEVICE_MSG_TYPE_CLIPBOARD;
        sc_write32be((uint8_t *) output + 1, UINT32_MAX);
        return (ssize_t) capacity;
    }

    static const uint8_t clipboard[] = {DEVICE_MSG_TYPE_CLIPBOARD, 0, 0, 0, 1, 'X'};
    static const uint8_t hid[] = {DEVICE_MSG_TYPE_UHID_OUTPUT, 0, 1, 0, 1, 2};

    if (scenario == RECEIVER_OOM && !received++) {
        assert(capacity >= sizeof(clipboard));
        memcpy(output, clipboard, sizeof(clipboard));
        return sizeof(clipboard);
    }

    if (scenario == RECEIVER_FRAGMENTED && received < sizeof(hid)) {
        *(uint8_t *) output = hid[received++];
        return 1;
    }

    return 0;
}

void *
sc_receiver_test_allocate_task(size_t size) {
    (void) size;
    assert(false);
    return NULL;
}

void
sc_receiver_test_set_clipboard(const char *text) {
    (void) text;
    assert(false);
}

void
sc_uhid_devices_process_hid_output(struct sc_uhid_devices *devices,
                                   uint16_t id, const uint8_t *data, size_t size) {
    (void) devices;
    (void) id;
    (void) data;
    (void) size;
    assert(false);
}

static void
controller_ended(struct sc_controller *controller, enum sc_control_status status, void *userdata) {
    assert(controller == userdata);
    observed_status = status;
    ++completions;
}

static void
receiver_ended(struct sc_receiver *receiver, enum sc_control_status status, void *userdata) {
    assert(receiver == userdata);
    observed_status = status;
    ++completions;
}

static void
test_sender(enum control_case selected) {
    scenario = selected;
    send_calls = completions = 0;
    struct sc_controller controller;
    static const struct sc_controller_callbacks callbacks = {.on_ended = controller_ended};
    bool initialized = sc_controller_init(&controller, (sc_socket) (uintptr_t) 1, &callbacks, &controller);
    assert(initialized);

    if (selected == SENDER_STOP) {
        sc_controller_stop(&controller);
    } else {
        struct sc_control_msg message = {.type = selected == SENDER_LOCAL
            ? (enum sc_control_msg_type) 0xff : SC_CONTROL_MSG_TYPE_BACK_OR_SCREEN_ON};
        bool admitted = sc_controller_push_msg(&controller, &message);
        assert(admitted);

        if (selected == SENDER_PARTIAL_AFTER_SUCCESS) {
            assert(sc_controller_push_msg(&controller, &message));
        }
    }

    bool started = sc_thread_create(&controller.thread, run_controller, "sender-test", &controller);
    assert(started);
    sc_thread_join(&controller.thread, NULL);
    assert(completions == 1);
    bool transport_lost = selected == SENDER_IO || selected == SENDER_PARTIAL_AFTER_SUCCESS;
    assert(observed_status == (transport_lost ? SC_CONTROL_STATUS_TRANSPORT_LOST
        : selected == SENDER_LOCAL ? SC_CONTROL_STATUS_LOCAL_ERROR : SC_CONTROL_STATUS_STOPPED));
    unsigned expected_sends = selected == SENDER_PARTIAL_AFTER_SUCCESS ? 2 : transport_lost;
    assert(send_calls == expected_sends);
    assert(controller.messages_written == (selected == SENDER_PARTIAL_AFTER_SUCCESS));
    sc_controller_destroy(&controller);
}

static void
test_receiver(enum control_case selected) {
    scenario = selected;
    received = completions = 0;
    struct sc_receiver receiver;
    static const struct sc_receiver_callbacks callbacks = {.on_ended = receiver_ended};
    bool initialized = sc_receiver_init(&receiver, (sc_socket) (uintptr_t) 1, &callbacks, &receiver);
    assert(initialized);
    bool started = sc_receiver_start(&receiver);
    assert(started);
    sc_receiver_join(&receiver);
    assert(completions == 1);
    enum sc_control_status expected = selected == RECEIVER_MALFORMED || selected == RECEIVER_FULL_BUFFER
        ? SC_CONTROL_STATUS_PROTOCOL_ERROR : selected == RECEIVER_OOM
        ? SC_CONTROL_STATUS_LOCAL_ERROR : SC_CONTROL_STATUS_TRANSPORT_LOST;
    assert(observed_status == expected);
    sc_receiver_destroy(&receiver);
}

int
main(int argc, char *argv[]) {
    if (argc == 2 && !strcmp(argv[1], "--full-buffer")) {
        test_receiver(RECEIVER_FULL_BUFFER);
        return 0;
    }

    for (unsigned selected = SENDER_IO; selected <= SENDER_PARTIAL_AFTER_SUCCESS;
         ++selected) {
        test_sender((enum control_case) selected);
    }

    for (unsigned selected = RECEIVER_EOF; selected <= RECEIVER_FULL_BUFFER; ++selected) {
        test_receiver((enum control_case) selected);
    }

    puts("control outcomes: four sender and six receiver production-worker cases");
    return 0;
}
