#include "common.h"

#include <assert.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "controller.h"
#include "device_msg.h"
#include "dispatcher.h"
#include "events.h"
#include "session.h"
#include "usb/aoa_hid.h"
#include "util/acksync.h"
#include "util/binary.h"
#include "util/net.h"

/* External-effect seams link the actual shipping owner and module code. */
bool
__wrap_sc_server_init(struct sc_server *server,
                       const struct sc_server_params *params,
                       const struct sc_server_callbacks *callbacks,
                       void *userdata);
bool
__wrap_sc_server_start(struct sc_server *server);
void
__wrap_sc_server_stop(struct sc_server *server);
void
__wrap_sc_server_join(struct sc_server *server);
void
__wrap_sc_server_destroy(struct sc_server *server);
bool
__wrap_sc_usb_init(struct sc_usb *usb);
bool
__wrap_sc_usb_select_device(struct sc_usb *usb, const char *serial,
                             struct sc_usb_device *device);
bool
__wrap_sc_usb_connect(struct sc_usb *usb, libusb_device *device,
                       const struct sc_usb_callbacks *callbacks, void *userdata);
void
__wrap_sc_usb_stop(struct sc_usb *usb);
void
__wrap_sc_usb_join(struct sc_usb *usb);
void
__wrap_sc_usb_disconnect(struct sc_usb *usb);
void
__wrap_sc_usb_destroy(struct sc_usb *usb);
int LIBUSB_CALL
__wrap_libusb_control_transfer(libusb_device_handle *handle, uint8_t request_type,
                               uint8_t request, uint16_t value, uint16_t index,
                               unsigned char *data, uint16_t length,
                               unsigned int timeout);
bool
__wrap_sc_aoa_start(struct sc_aoa *aoa);
enum sc_acksync_wait_result
__wrap_sc_acksync_wait(struct sc_acksync *acksync, uint64_t sequence,
                       sc_tick deadline);
void
__wrap_sc_aoa_join(struct sc_aoa *aoa);
ssize_t
__wrap_net_recv(sc_socket socket, void *buffer, size_t length);
ssize_t
__wrap_net_send_all(sc_socket socket, const void *buffer, size_t length);
void
__wrap_sc_controller_join(struct sc_controller *controller);
void
__wrap_sc_acksync_destroy(struct sc_acksync *acksync);

#define SC_TEST_ACK_SEQUENCE 42
#define SC_TEST_CONTROL_SOCKET ((sc_socket) (uintptr_t) 101)

static SDL_Semaphore *receiver_entered;
static SDL_Semaphore *receiver_release;
static SDL_Semaphore *aoa_wait_entered;
static bool receiver_read_once;
static bool controller_joined;
static bool aoa_joined;
static unsigned ack_destructions;
static unsigned server_stops;
static unsigned server_joins;
static unsigned server_destructions;
static unsigned usb_destructions;

bool __real_sc_aoa_start(struct sc_aoa *aoa);
void __real_sc_aoa_join(struct sc_aoa *aoa);
void __real_sc_controller_join(struct sc_controller *controller);
void __real_sc_acksync_destroy(struct sc_acksync *acksync);
enum sc_acksync_wait_result
__real_sc_acksync_wait(struct sc_acksync *acksync, uint64_t sequence,
                       sc_tick deadline);

/** Supply an owned synthetic server without starting ADB or a phone process. */
bool
__wrap_sc_server_init(struct sc_server *server,
                       const struct sc_server_params *params,
                       const struct sc_server_callbacks *callbacks,
                       void *userdata) {
    (void) params;
    memset(server, 0, sizeof(*server));
    server->serial = strdup("independent-session-fixture");
    assert(server->serial);
    server->control_socket = SC_TEST_CONTROL_SOCKET;
    server->cbs = callbacks;
    server->cbs_userdata = userdata;
    return true;
}

/** Exercise the production connected adapter without an external worker. */
bool
__wrap_sc_server_start(struct sc_server *server) {
    server->cbs->on_connected(server, server->cbs_userdata);
    return true;
}

/** Record transport shutdown while the controlled receive call remains live. */
void
__wrap_sc_server_stop(struct sc_server *server) {
    (void) server;
    ++server_stops;
}

/** Record settlement of the synthetic external server boundary. */
void
__wrap_sc_server_join(struct sc_server *server) {
    (void) server;
    ++server_joins;
}

/** Release exactly the synthetic acquisition and no shared ADB resources. */
void
__wrap_sc_server_destroy(struct sc_server *server) {
    free(server->serial);
    ++server_destructions;
}

/** Keep physical USB initialization outside this production-owner test. */
bool
__wrap_sc_usb_init(struct sc_usb *usb) {
    memset(usb, 0, sizeof(*usb));
    return true;
}

/** Supply one synthetic descriptor without enumerating real devices. */
bool
__wrap_sc_usb_select_device(struct sc_usb *usb, const char *serial,
                             struct sc_usb_device *device) {
    (void) usb;
    (void) serial;
    memset(device, 0, sizeof(*device));
    device->serial = strdup("synthetic-usb-fixture");
    device->manufacturer = strdup("fixture");
    device->product = strdup("fixture");
    assert(device->serial && device->manufacturer && device->product);
    return true;
}

/** Acquire no real handle or event thread. */
bool
__wrap_sc_usb_connect(struct sc_usb *usb, libusb_device *device,
                       const struct sc_usb_callbacks *callbacks, void *userdata) {
    (void) usb;
    (void) device;
    (void) callbacks;
    (void) userdata;
    return true;
}

/** Synthetic connections have no libusb stop operation. */
void
__wrap_sc_usb_stop(struct sc_usb *usb) {
    (void) usb;
}

/** Synthetic connections have no libusb publisher to join. */
void
__wrap_sc_usb_join(struct sc_usb *usb) {
    (void) usb;
}

/** Synthetic handles require no external disconnect. */
void
__wrap_sc_usb_disconnect(struct sc_usb *usb) {
    (void) usb;
}

/** Count the production owner's synthetic USB release. */
void
__wrap_sc_usb_destroy(struct sc_usb *usb) {
    (void) usb;
    ++usb_destructions;
}

/** Replace only the external HID transfer, retaining the real AOA worker. */
int LIBUSB_CALL
__wrap_libusb_control_transfer(libusb_device_handle *handle, uint8_t request_type,
                               uint8_t request, uint16_t value, uint16_t index,
                               unsigned char *data, uint16_t length,
                               unsigned int timeout) {
    (void) handle;
    (void) request_type;
    (void) request;
    (void) value;
    (void) index;
    (void) data;
    (void) length;
    (void) timeout;
    return 0;
}

/** Admit one real AOA input whose ACK dependency must be interrupted at stop. */
bool
__wrap_sc_aoa_start(struct sc_aoa *aoa) {
    if (!__real_sc_aoa_start(aoa)) {
        return false;
    }
    struct sc_hid_input input = {.hid_id = 1, .data = {0}, .size = 1};
    assert(sc_aoa_push_input_with_ack_to_wait(aoa, &input,
                                              SC_TEST_ACK_SEQUENCE));
    return true;
}

/** Observe entry into the actual synchronization primitive before stop. */
enum sc_acksync_wait_result
__wrap_sc_acksync_wait(struct sc_acksync *acksync, uint64_t sequence,
                       sc_tick deadline) {
    assert(sequence == SC_TEST_ACK_SEQUENCE);
    SDL_SignalSemaphore(aoa_wait_entered);
    return __real_sc_acksync_wait(acksync, sequence, deadline);
}

/** Observe actual worker settlement rather than inferring it from stop. */
void
__wrap_sc_aoa_join(struct sc_aoa *aoa) {
    __real_sc_aoa_join(aoa);
    aoa_joined = true;
}

/** Deliver an actual serialized ACK only when the owner reaches receiver join. */
ssize_t
__wrap_net_recv(sc_socket socket, void *buffer, size_t length) {
    assert(socket == SC_TEST_CONTROL_SOCKET);

    if (receiver_read_once) {
        return 0;
    }
    receiver_read_once = true;
    SDL_SignalSemaphore(receiver_entered);
    SDL_WaitSemaphore(receiver_release);
    assert(length >= 9);
    uint8_t *message = buffer;
    message[0] = DEVICE_MSG_TYPE_ACK_CLIPBOARD;
    sc_write64be(&message[1], SC_TEST_ACK_SEQUENCE);
    return 9;
}

/** No outgoing control message is required by this acquisition scenario. */
ssize_t
__wrap_net_send_all(sc_socket socket, const void *buffer, size_t length) {
    (void) socket;
    (void) buffer;
    (void) length;
    assert(!"Session fixture must not perform external socket writes");
    return -1;
}

/** Release the gated peer and join both actual controller-owned publishers. */
void
__wrap_sc_controller_join(struct sc_controller *controller) {
    SDL_SignalSemaphore(receiver_release);
    __real_sc_controller_join(controller);
    assert(controller->receiver.acksync->ack == SC_TEST_ACK_SEQUENCE);
    controller_joined = true;
}

/** Fail deterministically before freeing storage still borrowed by receiver. */
void
__wrap_sc_acksync_destroy(struct sc_acksync *acksync) {
    assert(aoa_joined);
    assert(controller_joined);
    ++ack_destructions;
    __real_sc_acksync_destroy(acksync);
}

/** Drive the actual shipping owner through ACK-borrowing stop and retirement. */
int
main(void) {
    assert(SDL_Init(SDL_INIT_EVENTS));
    receiver_entered = SDL_CreateSemaphore(0);
    receiver_release = SDL_CreateSemaphore(0);
    aoa_wait_entered = SDL_CreateSemaphore(0);
    assert(receiver_entered && receiver_release && aoa_wait_entered);
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    struct scrcpy_options options = {
        .control = true,
        .keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_AOA,
        .mouse_input_mode = SC_MOUSE_INPUT_MODE_DISABLED,
        .gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_DISABLED,
    };
    bool screen_initialized = false;
    struct sc_session *session = sc_session_create(&options, &dispatcher,
                                                  NULL, &screen_initialized);
    assert(session);
    struct sc_server_params params = {0};
    assert(sc_session_start_server(session, &params));
    uint32_t event;
    SDL_Event notification;
    bool connected = false;
    int notification_count;

    while ((notification_count = SDL_PeepEvents(&notification, 1, SDL_GETEVENT,
                                   SC_EVENT_SESSION_NOTIFICATION,
                                   SC_EVENT_SESSION_NOTIFICATION)) > 0) {
        if (sc_session_handle_event(session, &notification, &event)) {
            connected = true;
            break;
        }
        sc_dispatcher_handle_event(&dispatcher, &notification);
    }
    assert(connected || notification_count >= 0);

    if (!connected) {
        connected = sc_session_take_event(session, &event);
    }
    assert(connected);
    assert(event == SC_EVENT_SERVER_CONNECTED);
    assert(sc_session_start(session));
    SDL_WaitSemaphore(receiver_entered);
    SDL_WaitSemaphore(aoa_wait_entered);
    sc_session_request_stop(session);
    sc_session_request_stop(session);
    sc_session_join(session);
    sc_session_join(session);
    assert(!sc_session_start(session));
    sc_session_destroy(session);
    assert(ack_destructions == 1);
    assert(server_stops == 1 && server_joins == 1 && server_destructions == 1);
    assert(usb_destructions == 1);
    sc_dispatcher_destroy(&dispatcher);
    SDL_DestroySemaphore(receiver_entered);
    SDL_DestroySemaphore(receiver_release);
    SDL_DestroySemaphore(aoa_wait_entered);
    SDL_Quit();
    puts("Session lifetime: 1 real-owner/receiver/AOA ACK retirement case passed");
    return 0;
}
