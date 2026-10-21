#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <SDL3/SDL.h>

#include "controller.h"
#include "demuxer.h"
#include "dispatcher.h"
#include "events.h"
#include "file_pusher.h"
#include "recorder.h"
#include "screen.h"
#include "scrcpy.h"
#include "session.h"
#include "util/timeout.h"
#ifdef HAVE_USB
# include "usb/aoa_hid.h"
# include "usb/keyboard_aoa.h"
# include "usb/mouse_aoa.h"
#endif

/* External-effect seams link the actual shipping owner and module code. */
void *
__wrap_calloc(size_t count, size_t size);
void
__wrap_free(void *pointer);
bool
__wrap_sc_mutex_init(sc_mutex *mutex);
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
__wrap_sc_machine_is_active(void);
bool
__wrap_sc_machine_stop_requested(void);
bool
__wrap_sc_machine_emit_lifecycle(const char *event, const char *subsystem,
                                  const char *attempt, const char *reason,
                                  const char *error);
SDL_JoystickID *
__wrap_SDL_GetJoysticks(int *count);
bool
__wrap_SDL_IsGamepad(SDL_JoystickID instance);
void
__wrap_SDL_free(void *pointer);
bool
__wrap_sc_controller_init(struct sc_controller *controller, sc_socket socket,
                           const struct sc_controller_callbacks *callbacks,
                           void *userdata);
bool
__wrap_sc_controller_start(struct sc_controller *controller);
void
__wrap_sc_controller_join(struct sc_controller *controller);
void
__wrap_sc_controller_destroy(struct sc_controller *controller);
bool
__wrap_sc_file_pusher_init(struct sc_file_pusher *pusher, const char *serial,
                           const char *target);
void
__wrap_sc_file_pusher_destroy(struct sc_file_pusher *pusher);
bool
__wrap_sc_screen_init(struct sc_screen *screen,
                       const struct sc_screen_params *params);
bool
__wrap_sc_recorder_init(struct sc_recorder *recorder, const char *filename,
                         enum sc_record_format format, bool video, bool audio,
                         enum sc_orientation orientation,
                         const struct sc_recorder_callbacks *callbacks,
                         void *userdata);
bool
__wrap_sc_recorder_start(struct sc_recorder *recorder);
void
__wrap_sc_recorder_destroy(struct sc_recorder *recorder);
bool
__wrap_sc_demuxer_start(struct sc_demuxer *demuxer);
void
__wrap_sc_demuxer_join(struct sc_demuxer *demuxer);
bool
__wrap_sc_timeout_init(struct sc_timeout *timeout);
bool
__wrap_sc_timeout_start(struct sc_timeout *timeout, sc_tick deadline,
                         const struct sc_timeout_callbacks *callbacks,
                         void *userdata);
void
__wrap_sc_timeout_destroy(struct sc_timeout *timeout);
#ifdef HAVE_USB
bool
__wrap_sc_acksync_init(struct sc_acksync *acksync);
void
__wrap_sc_acksync_destroy(struct sc_acksync *acksync);
bool
__wrap_sc_usb_init(struct sc_usb *usb);
bool
__wrap_sc_usb_select_device(struct sc_usb *usb, const char *serial,
                             struct sc_usb_device *device);
bool
__wrap_sc_usb_connect(struct sc_usb *usb, libusb_device *device,
                       const struct sc_usb_callbacks *callbacks, void *userdata);
void
__wrap_sc_usb_device_destroy(struct sc_usb_device *device);
void
__wrap_sc_usb_stop(struct sc_usb *usb);
void
__wrap_sc_usb_join(struct sc_usb *usb);
void
__wrap_sc_usb_disconnect(struct sc_usb *usb);
void
__wrap_sc_usb_destroy(struct sc_usb *usb);
bool
__wrap_sc_aoa_init(struct sc_aoa *aoa, struct sc_usb *usb,
                   struct sc_acksync *acksync);
bool
__wrap_sc_aoa_start(struct sc_aoa *aoa);
void
__wrap_sc_aoa_join(struct sc_aoa *aoa);
void
__wrap_sc_aoa_destroy(struct sc_aoa *aoa);
bool
__wrap_sc_keyboard_aoa_init(struct sc_keyboard_aoa *keyboard,
                            struct sc_aoa *aoa);
void
__wrap_sc_keyboard_aoa_destroy(struct sc_keyboard_aoa *keyboard);
bool
__wrap_sc_mouse_aoa_init(struct sc_mouse_aoa *mouse, struct sc_aoa *aoa);
void
__wrap_sc_mouse_aoa_destroy(struct sc_mouse_aoa *mouse);
#endif

enum failure_stage {
    FAIL_NONE,
    FAIL_SERVER_INIT,
    FAIL_SERVER_START,
    FAIL_CONTROLLER_INIT,
    FAIL_CONTROLLER_START,
    FAIL_FILE_PUSHER_INIT,
    FAIL_SCREEN_INIT,
    FAIL_RECORDER_INIT,
    FAIL_RECORDER_START,
    FAIL_VIDEO_START,
    FAIL_AUDIO_START,
    FAIL_TIMEOUT_INIT,
    FAIL_TIMEOUT_START,
    FAIL_USB_INIT,
    FAIL_USB_SELECT,
    FAIL_USB_CONNECT,
    FAIL_AOA_INIT,
    FAIL_AOA_START,
    FAIL_KEYBOARD_INIT,
    FAIL_MOUSE_INIT,
    STOP_BEFORE_SERVER,
    STOP_AFTER_READY,
    STOP_DURING_CONTROLLER_INIT,
    STOP_BEFORE_CREATE,
    FAIL_CONTROLLER_START_AND_STOP,
    QUIT_BEFORE_READY,
    QUIT_BEFORE_TERMINAL,
    TERMINAL_BEFORE_QUIT,
    JOYSTICK_OWNERSHIP,
    DUPLICATE_CONTROLLER_COMPLETION,
};

enum resource {
    RESOURCE_SERVER,
    RESOURCE_CONTROLLER,
    RESOURCE_FILE_PUSHER,
    RESOURCE_RECORDER,
    RESOURCE_TIMEOUT,
    RESOURCE_USB,
    RESOURCE_USB_DESCRIPTOR,
    RESOURCE_USB_CONNECTION,
    RESOURCE_AOA,
    RESOURCE_ACK,
    RESOURCE_KEYBOARD,
    RESOURCE_MOUSE,
    RESOURCE_VIDEO,
    RESOURCE_AUDIO,
    RESOURCE_COUNT,
};

struct counts {
    unsigned acquired;
    unsigned destroyed;
    unsigned started;
    unsigned joined;
};

static enum failure_stage current_failure;
static struct counts resources[RESOURCE_COUNT];
static bool machine_stop;
static unsigned graph_start_attempts;
static SDL_JoystickID *joystick_allocation;
static unsigned joystick_releases;
static struct sc_controller *current_controller;
static bool fail_owner_allocation;
static bool track_owner_allocation;
static bool fail_latch_mutex;
static void *tracked_owner;
static unsigned tracked_owner_releases;

void __real_SDL_free(void *pointer);
void *__real_calloc(size_t count, size_t size);
void __real_free(void *pointer);
bool __real_sc_mutex_init(sc_mutex *mutex);

bool __real_sc_controller_init(struct sc_controller *, sc_socket,
                                const struct sc_controller_callbacks *, void *);
void __real_sc_controller_destroy(struct sc_controller *);
bool __real_sc_file_pusher_init(struct sc_file_pusher *, const char *,
                                 const char *);
void __real_sc_file_pusher_destroy(struct sc_file_pusher *);
bool __real_sc_recorder_init(struct sc_recorder *, const char *,
                              enum sc_record_format, bool, bool,
                              enum sc_orientation,
                              const struct sc_recorder_callbacks *, void *);
void __real_sc_recorder_destroy(struct sc_recorder *);
bool __real_sc_timeout_init(struct sc_timeout *);
void __real_sc_timeout_destroy(struct sc_timeout *);
#ifdef HAVE_USB
bool __real_sc_acksync_init(struct sc_acksync *);
void __real_sc_acksync_destroy(struct sc_acksync *);
bool __real_sc_aoa_init(struct sc_aoa *, struct sc_usb *, struct sc_acksync *);
void __real_sc_aoa_destroy(struct sc_aoa *);
bool __real_sc_keyboard_aoa_init(struct sc_keyboard_aoa *, struct sc_aoa *);
void __real_sc_keyboard_aoa_destroy(struct sc_keyboard_aoa *);
bool __real_sc_mouse_aoa_init(struct sc_mouse_aoa *, struct sc_aoa *);
void __real_sc_mouse_aoa_destroy(struct sc_mouse_aoa *);
#endif

/** Assert exact acquisition ownership before releasing an external resource. */
static void
record_destroy(enum resource resource) {
    assert(resources[resource].destroyed < resources[resource].acquired);
    ++resources[resource].destroyed;
}

/** Assert that a production join corresponds to an acquired worker. */
static void
record_join(enum resource resource) {
    assert(resources[resource].joined < resources[resource].started);
    ++resources[resource].joined;
}

/** Fail only the owner allocation selected immediately before the real create. */
void *
__wrap_calloc(size_t count, size_t size) {
    if (fail_owner_allocation) {
        fail_owner_allocation = false;
        return NULL;
    }
    void *pointer = __real_calloc(count, size);

    if (track_owner_allocation) {
        track_owner_allocation = false;
        assert(pointer);
        tracked_owner = pointer;
    }
    return pointer;
}

/** Count exact owner rollback without changing ordinary release semantics. */
void
__wrap_free(void *pointer) {
    if (pointer && pointer == tracked_owner) {
        ++tracked_owner_releases;
        tracked_owner = NULL;
    }
    __real_free(pointer);
}

/** Inject only the next latch acquisition after dispatcher initialization. */
bool
__wrap_sc_mutex_init(sc_mutex *mutex) {
    if (fail_latch_mutex) {
        fail_latch_mutex = false;
        return false;
    }
    return __real_sc_mutex_init(mutex);
}

/** Supply a synthetic external server while retaining the real owner adapter. */
bool
__wrap_sc_server_init(struct sc_server *server,
                       const struct sc_server_params *params,
                       const struct sc_server_callbacks *callbacks,
                       void *userdata) {
    (void) params;

    if (current_failure == FAIL_SERVER_INIT) {
        return false;
    }
    memset(server, 0, sizeof(*server));
    server->serial = strdup("independent-owner-fixture");
    assert(server->serial);
    server->video_socket = (sc_socket) (uintptr_t) 101;
    server->audio_socket = (sc_socket) (uintptr_t) 102;
    server->control_socket = (sc_socket) (uintptr_t) 103;
    server->cbs = callbacks;
    server->cbs_userdata = userdata;
    ++resources[RESOURCE_SERVER].acquired;
    return true;
}

/** Inject external startup failure or publish actual connected notification. */
bool
__wrap_sc_server_start(struct sc_server *server) {
    if (current_failure == FAIL_SERVER_START) {
        return false;
    }
    ++resources[RESOURCE_SERVER].started;

    if (current_failure == QUIT_BEFORE_READY) {
        SDL_Event quit = {.type = SDL_EVENT_QUIT};
        assert(SDL_PushEvent(&quit));
    }
    server->cbs->on_connected(server, server->cbs_userdata);
    machine_stop = current_failure == STOP_AFTER_READY;
    return true;
}

/** Observe stop without operating ADB or any real socket. */
void
__wrap_sc_server_stop(struct sc_server *server) {
    (void) server;
    assert(resources[RESOURCE_SERVER].started);
}

/** Settle the synthetic external worker once, under the shipping owner. */
void
__wrap_sc_server_join(struct sc_server *server) {
    (void) server;
    record_join(RESOURCE_SERVER);
}

/** Release the owned synthetic serial without shared process effects. */
void
__wrap_sc_server_destroy(struct sc_server *server) {
    record_destroy(RESOURCE_SERVER);
    free(server->serial);
}

/** Test the live owned-machine sticky Stop boundary without starting IPC. */
bool
__wrap_sc_machine_is_active(void) {
    bool active = current_failure == STOP_AFTER_READY
        || current_failure == STOP_DURING_CONTROLLER_INIT
        || current_failure == STOP_BEFORE_CREATE
        || current_failure == FAIL_CONTROLLER_START_AND_STOP;
    return active;
}

/** Provide the command-reader fact independently of the owner's local flag. */
bool
__wrap_sc_machine_stop_requested(void) {
    return machine_stop;
}

/** Replace framed lifecycle output with a controlled successful external effect. */
bool
__wrap_sc_machine_emit_lifecycle(const char *event, const char *subsystem,
                                  const char *attempt, const char *reason,
                                  const char *error) {
    assert(event && subsystem && reason && error);
    (void) attempt;
    return true;
}

/** Supply an independently allocated enumeration without querying real devices. */
SDL_JoystickID *
__wrap_SDL_GetJoysticks(int *count) {
    assert(current_failure == JOYSTICK_OWNERSHIP);
    joystick_allocation = SDL_malloc(sizeof(*joystick_allocation));
    assert(joystick_allocation);
    joystick_allocation[0] = 1;
    *count = 1;
    return joystick_allocation;
}

/** Keep synthetic enumeration independent from any physical gamepad backend. */
bool
__wrap_SDL_IsGamepad(SDL_JoystickID instance) {
    assert(instance == 1);
    return false;
}

/** Observe exact enumeration release while retaining the actual allocator. */
void
__wrap_SDL_free(void *pointer) {
    if (pointer && pointer == joystick_allocation) {
        assert(!joystick_releases);
        ++joystick_releases;
    }
    __real_SDL_free(pointer);
}

/** Use real controller/receiver initialization except the injected acquisition. */
bool
__wrap_sc_controller_init(struct sc_controller *controller, sc_socket socket,
                           const struct sc_controller_callbacks *callbacks,
                           void *userdata) {
    ++graph_start_attempts;

    if (current_failure == FAIL_CONTROLLER_INIT) {
        return false;
    }
    bool initialized = __real_sc_controller_init(controller, socket, callbacks,
                                                  userdata);

    if (initialized) {
        ++resources[RESOURCE_CONTROLLER].acquired;
        current_controller = controller;
    }
    machine_stop = current_failure == STOP_DURING_CONTROLLER_INIT;
    return initialized;
}

/** Control worker creation independently from real queue/mutex acquisition. */
bool
__wrap_sc_controller_start(struct sc_controller *controller) {
    (void) controller;

    if (current_failure == FAIL_CONTROLLER_START
            || current_failure == FAIL_CONTROLLER_START_AND_STOP) {
        machine_stop = current_failure == FAIL_CONTROLLER_START_AND_STOP;
        return false;
    }
    ++resources[RESOURCE_CONTROLLER].started;
    return true;
}

/** Keep external threads absent in stage tests; real borrowers have another lane. */
void
__wrap_sc_controller_join(struct sc_controller *controller) {
    (void) controller;
    record_join(RESOURCE_CONTROLLER);
}

/** Check release ownership and destroy actual queues and receiver mutexes. */
void
__wrap_sc_controller_destroy(struct sc_controller *controller) {
    record_destroy(RESOURCE_CONTROLLER);
    __real_sc_controller_destroy(controller);
}

/** Exercise lazy file-pusher initialization without ever requesting a transfer. */
bool
__wrap_sc_file_pusher_init(struct sc_file_pusher *pusher, const char *serial,
                           const char *target) {
    if (current_failure == FAIL_FILE_PUSHER_INIT) {
        return false;
    }
    bool initialized = __real_sc_file_pusher_init(pusher, serial, target);

    if (initialized) {
        ++resources[RESOURCE_FILE_PUSHER].acquired;
    }
    return initialized;
}

/** Release actual lazy resources; their worker was never requested. */
void
__wrap_sc_file_pusher_destroy(struct sc_file_pusher *pusher) {
    record_destroy(RESOURCE_FILE_PUSHER);
    __real_sc_file_pusher_destroy(pusher);
}

/** Inject the only window boundary reached by these no-window tests. */
bool
__wrap_sc_screen_init(struct sc_screen *screen,
                       const struct sc_screen_params *params) {
    (void) screen;
    (void) params;
    assert(current_failure == FAIL_SCREEN_INIT);
    return false;
}

/** Preserve actual recorder queues and mutex rollback without opening files. */
bool
__wrap_sc_recorder_init(struct sc_recorder *recorder, const char *filename,
                         enum sc_record_format format, bool video, bool audio,
                         enum sc_orientation orientation,
                         const struct sc_recorder_callbacks *callbacks,
                         void *userdata) {
    if (current_failure == FAIL_RECORDER_INIT) {
        return false;
    }
    bool initialized = __real_sc_recorder_init(recorder, filename, format,
                                                video, audio, orientation,
                                                callbacks, userdata);

    if (initialized) {
        ++resources[RESOURCE_RECORDER].acquired;
    }
    return initialized;
}

/** Reject worker acquisition before any file I/O can happen. */
bool
__wrap_sc_recorder_start(struct sc_recorder *recorder) {
    (void) recorder;
    assert(current_failure == FAIL_RECORDER_START);
    return false;
}

/** Preserve real recorder destruction after exact failed-start unwind. */
void
__wrap_sc_recorder_destroy(struct sc_recorder *recorder) {
    record_destroy(RESOURCE_RECORDER);
    __real_sc_recorder_destroy(recorder);
}

/** Inject acquisition at the actual demux-start boundary without socket reads. */
bool
__wrap_sc_demuxer_start(struct sc_demuxer *demuxer) {
    bool video = !strcmp(demuxer->name, "video");
    enum resource resource = video ? RESOURCE_VIDEO : RESOURCE_AUDIO;
    enum failure_stage failure = video ? FAIL_VIDEO_START : FAIL_AUDIO_START;

    if (current_failure == failure) {
        return false;
    }
    ++resources[resource].started;

    if (current_failure == QUIT_BEFORE_TERMINAL) {
        SDL_Event quit = {.type = SDL_EVENT_QUIT};
        assert(SDL_PushEvent(&quit));
        demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_ERROR,
                               demuxer->cbs_userdata);
    }

    if (current_failure == TERMINAL_BEFORE_QUIT) {
        demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_ERROR,
                               demuxer->cbs_userdata);
        SDL_Event quit = {.type = SDL_EVENT_QUIT};
        assert(SDL_PushEvent(&quit));
    }
    return true;
}

/** Deliver the real terminal adapter during controlled external settlement. */
void
__wrap_sc_demuxer_join(struct sc_demuxer *demuxer) {
    enum resource resource = !strcmp(demuxer->name, "video")
        ? RESOURCE_VIDEO : RESOURCE_AUDIO;
    record_join(resource);
    demuxer->cbs->on_ended(demuxer, SC_DEMUXER_STATUS_EOS,
                           demuxer->cbs_userdata);
}

/** Acquire real timeout mutex/condition unless the exact boundary fails. */
bool
__wrap_sc_timeout_init(struct sc_timeout *timeout) {
    if (current_failure == FAIL_TIMEOUT_INIT) {
        return false;
    }
    bool initialized = __real_sc_timeout_init(timeout);

    if (initialized) {
        ++resources[RESOURCE_TIMEOUT].acquired;
    }
    return initialized;
}

/** Reject worker acquisition before a deadline thread is created. */
bool
__wrap_sc_timeout_start(struct sc_timeout *timeout, sc_tick deadline,
                         const struct sc_timeout_callbacks *callbacks,
                         void *userdata) {
    (void) timeout;
    (void) deadline;
    (void) callbacks;
    (void) userdata;
    assert(current_failure == FAIL_TIMEOUT_START);
    return false;
}

/** Preserve real timeout resource cleanup after startup failure. */
void
__wrap_sc_timeout_destroy(struct sc_timeout *timeout) {
    record_destroy(RESOURCE_TIMEOUT);
    __real_sc_timeout_destroy(timeout);
}

#ifdef HAVE_USB
/** Track the actual ACK primitive through each partial USB acquisition. */
bool
__wrap_sc_acksync_init(struct sc_acksync *acksync) {
    bool initialized = __real_sc_acksync_init(acksync);

    if (initialized) {
        ++resources[RESOURCE_ACK].acquired;
    }
    return initialized;
}

/** Assert exactly one ACK release after any successful acquisition. */
void
__wrap_sc_acksync_destroy(struct sc_acksync *acksync) {
    record_destroy(RESOURCE_ACK);
    __real_sc_acksync_destroy(acksync);
}

/** Inject USB library acquisition without invoking a physical backend. */
bool
__wrap_sc_usb_init(struct sc_usb *usb) {
    if (current_failure == FAIL_USB_INIT) {
        return false;
    }
    memset(usb, 0, sizeof(*usb));
    ++resources[RESOURCE_USB].acquired;
    return true;
}

/** Select an independent synthetic descriptor or fail its exact stage. */
bool
__wrap_sc_usb_select_device(struct sc_usb *usb, const char *serial,
                             struct sc_usb_device *device) {
    (void) usb;
    (void) serial;

    if (current_failure == FAIL_USB_SELECT) {
        return false;
    }
    memset(device, 0, sizeof(*device));
    ++resources[RESOURCE_USB_DESCRIPTOR].acquired;
    return true;
}

/** Inject USB connection failure without opening a real handle. */
bool
__wrap_sc_usb_connect(struct sc_usb *usb, libusb_device *device,
                       const struct sc_usb_callbacks *callbacks, void *userdata) {
    (void) usb;
    (void) device;
    (void) callbacks;
    (void) userdata;
    if (current_failure == FAIL_USB_CONNECT) {
        return false;
    }
    ++resources[RESOURCE_USB_CONNECTION].acquired;
    return true;
}

/** Retain the selected descriptor's independent no-resource ownership. */
void
__wrap_sc_usb_device_destroy(struct sc_usb_device *device) {
    (void) device;
    record_destroy(RESOURCE_USB_DESCRIPTOR);
}

/** No synthetic USB event publisher requires interruption. */
void
__wrap_sc_usb_stop(struct sc_usb *usb) {
    (void) usb;
}

/** No synthetic USB event thread exists. */
void
__wrap_sc_usb_join(struct sc_usb *usb) {
    (void) usb;
}

/** Synthetic connections do not own physical handles. */
void
__wrap_sc_usb_disconnect(struct sc_usb *usb) {
    (void) usb;
    record_destroy(RESOURCE_USB_CONNECTION);
}

/** Assert release of only successfully initialized USB resources. */
void
__wrap_sc_usb_destroy(struct sc_usb *usb) {
    (void) usb;
    record_destroy(RESOURCE_USB);
}

/** Retain real AOA queues and synchronization across injected init failure. */
bool
__wrap_sc_aoa_init(struct sc_aoa *aoa, struct sc_usb *usb,
                   struct sc_acksync *acksync) {
    if (current_failure == FAIL_AOA_INIT) {
        return false;
    }
    bool initialized = __real_sc_aoa_init(aoa, usb, acksync);

    if (initialized) {
        ++resources[RESOURCE_AOA].acquired;
    }
    return initialized;
}

/** Model only worker acquisition; the ACK target uses the actual AOA worker. */
bool
__wrap_sc_aoa_start(struct sc_aoa *aoa) {
    (void) aoa;

    if (current_failure == FAIL_AOA_START) {
        return false;
    }
    ++resources[RESOURCE_AOA].started;
    return true;
}

/** Validate that initialized-but-unstarted AOA is never joined. */
void
__wrap_sc_aoa_join(struct sc_aoa *aoa) {
    (void) aoa;
    record_join(RESOURCE_AOA);
}

/** Destroy actual AOA queues after the owner's external worker settlement. */
void
__wrap_sc_aoa_destroy(struct sc_aoa *aoa) {
    record_destroy(RESOURCE_AOA);
    __real_sc_aoa_destroy(aoa);
}

/** Exercise real keyboard queue admission unless its exact acquisition fails. */
bool
__wrap_sc_keyboard_aoa_init(struct sc_keyboard_aoa *keyboard,
                            struct sc_aoa *aoa) {
    if (current_failure == FAIL_KEYBOARD_INIT) {
        return false;
    }
    bool initialized = __real_sc_keyboard_aoa_init(keyboard, aoa);

    if (initialized) {
        ++resources[RESOURCE_KEYBOARD].acquired;
    }
    return initialized;
}

/** Preserve the keyboard's actual closing-queue publication before AOA free. */
void
__wrap_sc_keyboard_aoa_destroy(struct sc_keyboard_aoa *keyboard) {
    record_destroy(RESOURCE_KEYBOARD);
    __real_sc_keyboard_aoa_destroy(keyboard);
}

/** Inject mouse acquisition after the preceding keyboard has been acquired. */
bool
__wrap_sc_mouse_aoa_init(struct sc_mouse_aoa *mouse, struct sc_aoa *aoa) {
    if (current_failure == FAIL_MOUSE_INIT) {
        return false;
    }
    bool initialized = __real_sc_mouse_aoa_init(mouse, aoa);

    if (initialized) {
        ++resources[RESOURCE_MOUSE].acquired;
    }
    return initialized;
}

/** Preserve the mouse's real closing-queue ownership. */
void
__wrap_sc_mouse_aoa_destroy(struct sc_mouse_aoa *mouse) {
    record_destroy(RESOURCE_MOUSE);
    __real_sc_mouse_aoa_destroy(mouse);
}
#endif

/** Compose only the options needed to reach the selected production boundary. */
static struct scrcpy_options
options_for_failure(enum failure_stage failure) {
    struct scrcpy_options options = {
        .control = true,
        .keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_DISABLED,
        .mouse_input_mode = SC_MOUSE_INPUT_MODE_DISABLED,
        .gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_DISABLED,
    };

    if (failure == FAIL_FILE_PUSHER_INIT || failure == FAIL_SCREEN_INIT) {
        options.window = true;
    }

    if (failure == FAIL_RECORDER_INIT || failure == FAIL_RECORDER_START) {
        options.record_filename = "fixture-never-opened.mkv";
        options.video = true;
    }

    if (failure == FAIL_VIDEO_START || failure == FAIL_AUDIO_START) {
        options.video = true;
        options.audio = true;
    }

    if (failure == FAIL_TIMEOUT_INIT || failure == FAIL_TIMEOUT_START) {
        options.time_limit = SC_TICK_FROM_SEC(1);
    }

    if (failure == JOYSTICK_OWNERSHIP) {
        options.gamepad_input_mode = SC_GAMEPAD_INPUT_MODE_UHID;
    }
#ifdef HAVE_USB
    bool usb_case = failure >= FAIL_USB_INIT && failure <= FAIL_MOUSE_INIT;

    if (usb_case) {
        options.keyboard_input_mode = SC_KEYBOARD_INPUT_MODE_AOA;
        options.mouse_input_mode = SC_MOUSE_INPUT_MODE_AOA;
    }
#endif
    return options;
}

/** Dequeue queued owner wakes independently of SDL's per-pump poll boundary. */
static bool
take_connected(struct sc_session *session, struct sc_dispatcher *dispatcher,
                 uint32_t *outcome) {
    SDL_Event event;
    int count;

    while ((count = SDL_PeepEvents(&event, 1, SDL_GETEVENT,
                                   SC_EVENT_SESSION_NOTIFICATION,
                                   SC_EVENT_SESSION_NOTIFICATION)) > 0) {
        if (sc_session_handle_event(session, &event, outcome)) {
            return true;
        }
        sc_dispatcher_handle_event(dispatcher, &event);
    }
    assert(count >= 0);
    return sc_session_take_event(session, outcome);
}

/** Require settled fixture ownership before releasing the real dispatcher. */
static void
test_destroy_dispatcher(struct sc_dispatcher *dispatcher) {
    struct sc_dispatcher_stats stats = sc_dispatcher_get_stats(dispatcher);
    assert(!stats.queued_count);
    assert(!stats.retained_count);
    assert(!stats.retained_payload_bytes);
    assert(!stats.publishers);
    bool shutdown = sc_dispatcher_shutdown(dispatcher);
    assert(shutdown);
    bool destroyed = sc_dispatcher_destroy(dispatcher);
    fprintf(stderr, "Fixture dispatcher: queued=%zu retained=%zu bytes=%zu "
            "publishers=%zu shutdown=%s destroy=%s\n", stats.queued_count,
            stats.retained_count, stats.retained_payload_bytes, stats.publishers,
            shutdown ? "true" : "false", destroyed ? "true" : "false");
    assert(destroyed);
}

/** Verify real owner rollback without copying its lifecycle decisions. */
static void
test_failure_stage(enum failure_stage failure) {
    memset(resources, 0, sizeof(resources));
    current_failure = failure;
    machine_stop = false;
    graph_start_attempts = 0;
    current_controller = NULL;
    joystick_allocation = NULL;
    joystick_releases = 0;
    struct scrcpy_options options = options_for_failure(failure);
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    struct sc_screen screen;
    bool screen_initialized = false;
    struct sc_session *session = sc_session_create(&options, &dispatcher,
                                                  &screen, &screen_initialized);
    assert(session);

    if (failure == STOP_BEFORE_SERVER) {
        sc_session_request_stop(session);
    }
    struct sc_server_params params = {0};
    bool server_started = sc_session_start_server(session, &params);
    bool graph_started = false;

    if (server_started) {
        uint32_t event;
        assert(take_connected(session, &dispatcher, &event));
        assert(event == SC_EVENT_SERVER_CONNECTED);
        graph_started = sc_session_start(session);
    }
    bool expected_start = failure == FAIL_NONE || failure == JOYSTICK_OWNERSHIP
        || failure == DUPLICATE_CONTROLLER_COMPLETION;
    assert(graph_started == expected_start);

    if (failure == JOYSTICK_OWNERSHIP) {
        assert(joystick_allocation);
        assert(joystick_releases == 1);
    }

    if (failure == STOP_AFTER_READY || failure == STOP_BEFORE_SERVER) {
        assert(!graph_start_attempts);
    }

    if (failure == DUPLICATE_CONTROLLER_COMPLETION) {
        assert(current_controller);
        current_controller->cbs->on_ended(current_controller, false,
                                           current_controller->cbs_userdata);
        current_controller->cbs->on_ended(current_controller, true,
                                           current_controller->cbs_userdata);
        uint32_t outcome;
        assert(take_connected(session, &dispatcher, &outcome));
        assert(outcome == SC_EVENT_DEVICE_DISCONNECTED);
        assert(!take_connected(session, &dispatcher, &outcome));
    }
    sc_session_request_stop(session);

    if (failure == DUPLICATE_CONTROLLER_COMPLETION) {
        current_controller->cbs->on_ended(current_controller, true,
                                           current_controller->cbs_userdata);
        uint32_t outcome;
        assert(!take_connected(session, &dispatcher, &outcome));
    }
    sc_session_request_stop(session);
    sc_session_join(session);
    sc_session_join(session);
    assert(!sc_session_start(session));
    sc_session_destroy(session);

    for (unsigned resource = 0; resource < RESOURCE_COUNT; ++resource) {
        assert(resources[resource].acquired == resources[resource].destroyed);
        assert(resources[resource].started == resources[resource].joined);
    }
    assert(!screen_initialized);
    test_destroy_dispatcher(&dispatcher);
}

/** Preserve actual application FIFO semantics across the new scoped handoff. */
static void
test_application_outcome_order(enum failure_stage order) {
    memset(resources, 0, sizeof(resources));
    current_failure = order;
    graph_start_attempts = 0;
    machine_stop = false;
    struct scrcpy_options options = options_for_failure(FAIL_NONE);
    options.video = order != QUIT_BEFORE_READY;
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    enum scrcpy_exit_code expected = order == TERMINAL_BEFORE_QUIT
        ? SCRCPY_EXIT_FAILURE : SCRCPY_EXIT_SUCCESS;
    assert(scrcpy(&options, &dispatcher) == expected);

    if (order == QUIT_BEFORE_READY) {
        assert(!graph_start_attempts);
    }

    for (unsigned resource = 0; resource < RESOURCE_COUNT; ++resource) {
        assert(resources[resource].acquired == resources[resource].destroyed);
        assert(resources[resource].started == resources[resource].joined);
    }
    test_destroy_dispatcher(&dispatcher);
    SDL_FlushEvents(SDL_EVENT_FIRST, SDL_EVENT_LAST);
}

/** Verify allocation, latch rollback and rejection of a second current owner. */
static void
test_create_failures(void) {
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    struct scrcpy_options options = options_for_failure(FAIL_NONE);
    bool screen_initialized = false;
    fail_owner_allocation = true;
    assert(!sc_session_create(&options, &dispatcher, NULL, &screen_initialized));
    assert(!fail_owner_allocation && !dispatcher.last_generation);

    tracked_owner_releases = 0;
    track_owner_allocation = true;
    fail_latch_mutex = true;
    assert(!sc_session_create(&options, &dispatcher, NULL, &screen_initialized));
    assert(!fail_latch_mutex && !tracked_owner);
    assert(tracked_owner_releases == 1 && !dispatcher.last_generation);

    struct sc_session *first = sc_session_create(&options, &dispatcher, NULL,
                                                  &screen_initialized);
    assert(first);
    sc_dispatcher_generation first_generation = dispatcher.last_generation;
    tracked_owner_releases = 0;
    track_owner_allocation = true;
    assert(!sc_session_create(&options, &dispatcher, NULL, &screen_initialized));
    assert(!tracked_owner && tracked_owner_releases == 1);
    assert(dispatcher.last_generation == first_generation);
    sc_session_request_stop(first);
    sc_session_join(first);
    sc_session_destroy(first);
    test_destroy_dispatcher(&dispatcher);
}

/** Stop before creation acquires nothing; a later Stop cannot hide start failure. */
static void
test_application_startup_stop(enum failure_stage failure) {
    memset(resources, 0, sizeof(resources));
    current_failure = failure;
    graph_start_attempts = 0;
    machine_stop = failure == STOP_BEFORE_CREATE;
    struct scrcpy_options options = options_for_failure(FAIL_NONE);
    struct sc_dispatcher dispatcher;
    assert(sc_dispatcher_init(&dispatcher, SC_EVENT_DISPATCHER_WAKEUP,
                               NULL, NULL));
    enum scrcpy_exit_code expected = failure == STOP_BEFORE_CREATE
        ? SCRCPY_EXIT_SUCCESS : SCRCPY_EXIT_FAILURE;
    assert(scrcpy(&options, &dispatcher) == expected);

    if (failure == STOP_BEFORE_CREATE) {
        assert(!dispatcher.last_generation);
        assert(!graph_start_attempts);
    }

    for (unsigned resource = 0; resource < RESOURCE_COUNT; ++resource) {
        assert(resources[resource].acquired == resources[resource].destroyed);
        assert(resources[resource].started == resources[resource].joined);
    }
    test_destroy_dispatcher(&dispatcher);
    SDL_FlushEvents(SDL_EVENT_FIRST, SDL_EVENT_LAST);
}

/** Run nonzero production-owner stage coverage without devices or visible UI. */
int
main(int argument_count, char **arguments) {
    assert(SDL_Init(SDL_INIT_EVENTS));

    if (argument_count == 2) {
        if (!strcmp(arguments[1], "--stop-after-ready")) {
            test_failure_stage(STOP_AFTER_READY);
        } else if (!strcmp(arguments[1], "--joystick-ownership")) {
            test_failure_stage(JOYSTICK_OWNERSHIP);
        } else if (!strcmp(arguments[1], "--create-failures")) {
            test_create_failures();
        } else {
            enum failure_stage order = !strcmp(arguments[1], "--quit-before-ready")
                ? QUIT_BEFORE_READY : QUIT_BEFORE_TERMINAL;
            test_application_outcome_order(order);
        }
        puts("Session lifetime: 1 targeted production-path case passed");
        SDL_Quit();
        return 0;
    }
    static const enum failure_stage cases[] = {
        FAIL_NONE, FAIL_SERVER_INIT, FAIL_SERVER_START,
        FAIL_CONTROLLER_INIT, FAIL_CONTROLLER_START,
        FAIL_FILE_PUSHER_INIT, FAIL_SCREEN_INIT,
        FAIL_RECORDER_INIT, FAIL_RECORDER_START,
        FAIL_VIDEO_START, FAIL_AUDIO_START,
        FAIL_TIMEOUT_INIT, FAIL_TIMEOUT_START,
        STOP_BEFORE_SERVER, STOP_AFTER_READY, STOP_DURING_CONTROLLER_INIT,
        JOYSTICK_OWNERSHIP, DUPLICATE_CONTROLLER_COMPLETION,
#ifdef HAVE_USB
        FAIL_USB_INIT, FAIL_USB_SELECT, FAIL_USB_CONNECT,
        FAIL_AOA_INIT, FAIL_AOA_START, FAIL_KEYBOARD_INIT, FAIL_MOUSE_INIT,
#endif
    };

    for (unsigned index = 0; index < ARRAY_LEN(cases); ++index) {
        test_failure_stage(cases[index]);
    }
    test_application_outcome_order(QUIT_BEFORE_READY);
    test_application_outcome_order(QUIT_BEFORE_TERMINAL);
    test_application_outcome_order(TERMINAL_BEFORE_QUIT);
    test_application_startup_stop(STOP_BEFORE_CREATE);
    test_application_startup_stop(FAIL_CONTROLLER_START_AND_STOP);
    test_create_failures();
    SDL_Quit();
    printf("Session lifetime: %zu production-owner stage cases passed\n",
           ARRAY_LEN(cases) + 8);
    return 0;
}
