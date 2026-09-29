#include "ipc/machine.h"

#ifdef _WIN32

#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <io.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>
#include <rpc.h>
#include <SDL3/SDL.h>

#include "ipc/ipc_frame.h"
#include "ipc/ipc_json.h"

#define SC_MACHINE_QUEUE_ITEMS 64
#define SC_MACHINE_QUEUE_BYTES (2u * 1024u * 1024u)
#define SC_MACHINE_HANDSHAKE_MS 8000u
#define SC_MACHINE_WRITE_MS 5000u
#define SC_MACHINE_PARENT_STOP_MS 3000u
#define SC_MACHINE_JOIN_MS 6000u

struct sc_machine_frame {
    uint8_t *data;
    size_t length;
};

struct sc_machine_state {
    HANDLE input;
    HANDLE output;
    HANDLE parent;
    HANDLE shutdown_event;
    HANDLE reader_thread;
    HANDLE writer_thread;
    HANDLE guard_thread;
    CRITICAL_SECTION mutex;
    CRITICAL_SECTION stop_publication_mutex;
    CONDITION_VARIABLE queue_changed;
    struct sc_machine_frame queue[SC_MACHINE_QUEUE_ITEMS];
    size_t queue_head;
    size_t queue_count;
    size_t queue_bytes;
    size_t in_flight_bytes;
    size_t focus_pending;
    uint64_t max_request_id;
    uint64_t sequence;
    LARGE_INTEGER process_started;
    LARGE_INTEGER frequency;
    ULONGLONG handshake_deadline;
    ULONGLONG write_started;
    LONG stop_requested;
    LONG stop_cause; // 1 command, 2 protocol, 3 parent exit
    LONG terminal;
    bool closing;
    bool commands_closing;
    bool initialized;
    bool handshake_written;
    LONG handshake_complete;
    char session_id[37];
    char attempt_id[37];
    bool attempt_reconnecting;
    bool frame_presented;
    SDL_Window *window; // SDL main thread only
    void (*request_stop)(void *);
    void *stop_userdata;
};

static struct sc_machine_state machine;
static HANDLE protocol_output;
static LARGE_INTEGER machine_process_started;

/** Isolate the original redirected stdout handle from all text and children. */
bool
sc_machine_prepare_stdio(void) {
    LARGE_INTEGER current_counter, frequency;
    FILETIME creation, exit_time, kernel, user, wall_now;
    if (!QueryPerformanceCounter(&current_counter) ||
            !QueryPerformanceFrequency(&frequency) ||
            !GetProcessTimes(GetCurrentProcess(), &creation, &exit_time,
                             &kernel, &user)) {
        return false;
    }
    GetSystemTimeAsFileTime(&wall_now);
    uint64_t created_ticks = ((uint64_t) creation.dwHighDateTime << 32) |
                             creation.dwLowDateTime;
    uint64_t wall_ticks = ((uint64_t) wall_now.dwHighDateTime << 32) |
                          wall_now.dwLowDateTime;
    if (wall_ticks < created_ticks) {
        return false;
    }
    uint64_t elapsed_100ns = wall_ticks - created_ticks;
    // Correlate the Windows creation timestamp with the monotonic QPC epoch.
    int64_t elapsed_counter = (int64_t) (elapsed_100ns / 10000000u) *
                              frequency.QuadPart +
                              (int64_t) (elapsed_100ns % 10000000u) *
                              frequency.QuadPart / 10000000u;
    machine_process_started.QuadPart = current_counter.QuadPart -
                                       elapsed_counter;
    HANDLE process = GetCurrentProcess();
    HANDLE original = GetStdHandle(STD_OUTPUT_HANDLE);
    HANDLE diagnostics = GetStdHandle(STD_ERROR_HANDLE);
    HANDLE input = GetStdHandle(STD_INPUT_HANDLE);
    if (!original || original == INVALID_HANDLE_VALUE ||
            !diagnostics || diagnostics == INVALID_HANDLE_VALUE ||
            !input || input == INVALID_HANDLE_VALUE) {
        return false;
    }

    if (!DuplicateHandle(process, original, process, &protocol_output, 0,
                         FALSE, DUPLICATE_SAME_ACCESS)) {
        return false;
    }
    if (!SetHandleInformation(protocol_output, HANDLE_FLAG_INHERIT, 0) ||
            !SetHandleInformation(original, HANDLE_FLAG_INHERIT, 0) ||
            !SetHandleInformation(input, HANDLE_FLAG_INHERIT, 0) ||
            _setmode(_fileno(stdin), _O_BINARY) == -1 ||
            _dup2(_fileno(stderr), _fileno(stdout)) == -1 ||
            !SetStdHandle(STD_OUTPUT_HANDLE, diagnostics)) {
        CloseHandle(protocol_output);
        protocol_output = NULL;
        return false;
    }
    setbuf(stdout, NULL);
    setbuf(stderr, NULL);
    return true;
}

/** Fail the channel once and request the existing native shutdown path. */
static void
sc_machine_fail(void) {
    if (InterlockedExchange(&machine.terminal, 1)) {
        return;
    }
    InterlockedExchange(&machine.stop_requested, 1);
    InterlockedCompareExchange(&machine.stop_cause, 2, 0);
    EnterCriticalSection(&machine.mutex);
    void (*callback)(void *) = machine.request_stop;
    void *userdata = machine.stop_userdata;
    LeaveCriticalSection(&machine.mutex);
    if (callback) {
        callback(userdata);
    }
    WakeAllConditionVariable(&machine.queue_changed);
}

/** Read a complete bounded frame without reading bytes of the next frame. */
static bool
sc_machine_read_exact(void *buffer, size_t length) {
    uint8_t *cursor = buffer;
    while (length) {
        EnterCriticalSection(&machine.mutex);
        bool closing = machine.commands_closing;
        LeaveCriticalSection(&machine.mutex);
        if (closing) {
            return false;
        }
        DWORD count = 0;
        DWORD requested = length > 65536 ? 65536 : (DWORD) length;
        if (!ReadFile(machine.input, cursor, requested, &count, NULL) || !count) {
            return false;
        }
        cursor += count;
        length -= count;
    }
    return true;
}

/** Decode a single frame, abandoning this channel on every partial failure. */
static bool
sc_machine_read_message(struct sc_ipc_message *message) {
    uint8_t header[4];
    if (!sc_machine_read_exact(header, sizeof(header))) {
        return false;
    }
    uint32_t length = (uint32_t) header[0] |
        ((uint32_t) header[1] << 8) |
        ((uint32_t) header[2] << 16) |
        ((uint32_t) header[3] << 24);
    if (!length || length > SC_IPC_MAX_PAYLOAD) {
        return false;
    }
    uint8_t *payload = malloc(length);
    if (!payload) {
        return false;
    }
    bool ok = sc_machine_read_exact(payload, length) &&
              sc_ipc_json_decode(payload, length, message) == SC_IPC_JSON_OK;
    free(payload);
    return ok;
}

/** Queue a complete encoded frame under both count and total-byte budgets. */
static bool
sc_machine_queue_message(const struct sc_ipc_message *message) {
    uint8_t *payload = NULL;
    size_t payload_length = 0;
    if (sc_ipc_json_encode(message, &payload, &payload_length) != SC_IPC_JSON_OK) {
        sc_machine_fail();
        return false;
    }
    uint8_t *frame = NULL;
    size_t frame_length = 0;
    enum sc_ipc_frame_status status =
        sc_ipc_frame_encode(payload, payload_length, &frame, &frame_length);
    free(payload);
    if (status != SC_IPC_FRAME_READY) {
        sc_machine_fail();
        return false;
    }
    EnterCriticalSection(&machine.mutex);
    bool capacity = machine.queue_count < SC_MACHINE_QUEUE_ITEMS &&
                    frame_length <= SC_MACHINE_QUEUE_BYTES -
                                    machine.queue_bytes - machine.in_flight_bytes;
    bool accepted = !machine.closing && !machine.terminal && capacity;
    if (accepted) {
        size_t tail = (machine.queue_head + machine.queue_count) %
                      SC_MACHINE_QUEUE_ITEMS;
        machine.queue[tail] = (struct sc_machine_frame) {frame, frame_length};
        machine.queue_count++;
        machine.queue_bytes += frame_length;
        WakeConditionVariable(&machine.queue_changed);
    }
    LeaveCriticalSection(&machine.mutex);
    if (!accepted) {
        free(frame);
        sc_machine_fail();
    }
    return accepted;
}

/** Only this thread writes framed bytes to the saved protocol stdout. */
static DWORD WINAPI
sc_machine_writer(void *userdata) {
    (void) userdata;
    for (;;) {
        EnterCriticalSection(&machine.mutex);
        while (!machine.queue_count && !machine.closing && !machine.terminal) {
            SleepConditionVariableCS(&machine.queue_changed, &machine.mutex,
                                     INFINITE);
        }
        if (!machine.queue_count || machine.terminal) {
            LeaveCriticalSection(&machine.mutex);
            return 0;
        }
        struct sc_machine_frame frame = machine.queue[machine.queue_head];
        machine.queue_head = (machine.queue_head + 1) % SC_MACHINE_QUEUE_ITEMS;
        machine.queue_count--;
        machine.queue_bytes -= frame.length;
        machine.in_flight_bytes = frame.length;
        machine.write_started = GetTickCount64();
        LeaveCriticalSection(&machine.mutex);

        size_t written = 0;
        while (written < frame.length) {
            DWORD count = 0;
            DWORD requested = frame.length - written > 65536 ? 65536 :
                              (DWORD) (frame.length - written);
            if (!WriteFile(machine.output, frame.data + written, requested,
                           &count, NULL) || !count) {
                free(frame.data);
                sc_machine_fail();
                return 1;
            }
            written += count;
        }
        free(frame.data);
        EnterCriticalSection(&machine.mutex);
        machine.write_started = 0;
        machine.in_flight_bytes = 0;
        if (!machine.handshake_written) {
            machine.handshake_written = true;
            WakeAllConditionVariable(&machine.queue_changed);
        }
        LeaveCriticalSection(&machine.mutex);
    }
}

/** An independent guard survives blocked protocol and rendering workers. */
static DWORD WINAPI
sc_machine_guard(void *userdata) {
    (void) userdata;
    HANDLE watched[] = {machine.shutdown_event, machine.parent};
    for (;;) {
        DWORD result = WaitForMultipleObjects(2, watched, FALSE, 100);
        if (result == WAIT_OBJECT_0) {
            return 0;
        }
        if (result == WAIT_OBJECT_0 + 1) {
            InterlockedExchange(&machine.stop_requested, 1);
            InterlockedCompareExchange(&machine.stop_cause, 3, 0);
            EnterCriticalSection(&machine.mutex);
            void (*callback)(void *) = machine.request_stop;
            void *callback_userdata = machine.stop_userdata;
            LeaveCriticalSection(&machine.mutex);
            if (callback) {
                callback(callback_userdata);
            }
            if (WaitForSingleObject(machine.shutdown_event,
                                    SC_MACHINE_PARENT_STOP_MS) != WAIT_OBJECT_0) {
                TerminateProcess(GetCurrentProcess(), 3);
            }
            return 0;
        }
        if (result == WAIT_FAILED) {
            TerminateProcess(GetCurrentProcess(), 4);
            return 4;
        }
        ULONGLONG now = GetTickCount64();
        ULONGLONG writer_started;
        EnterCriticalSection(&machine.mutex);
        writer_started = machine.write_started;
        LeaveCriticalSection(&machine.mutex);
        if ((!InterlockedCompareExchange(&machine.handshake_complete, 0, 0) &&
             now > machine.handshake_deadline) ||
                (writer_started && now - writer_started > SC_MACHINE_WRITE_MS)) {
            TerminateProcess(GetCurrentProcess(), 5);
            return 5;
        }
    }
}

/** Validate the parent identity before trusting its PID as an owner. */
static HANDLE
sc_machine_open_parent(uint32_t pid, uint64_t created) {
    HANDLE parent = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                                FALSE, pid);
    if (!parent) {
        return NULL;
    }
    FILETIME creation, exit_time, kernel, user;
    bool valid = GetProcessTimes(parent, &creation, &exit_time, &kernel, &user);
    if (!valid) {
        CloseHandle(parent);
        return NULL;
    }
    uint64_t actual = ((uint64_t) creation.dwHighDateTime << 32) |
                      creation.dwLowDateTime;
    if (!created || actual != created ||
            WaitForSingleObject(parent, 0) != WAIT_TIMEOUT ||
            !SetHandleInformation(parent, HANDLE_FLAG_INHERIT, 0)) {
        CloseHandle(parent);
        return NULL;
    }
    return parent;
}

/** Establish exact parent ownership before waiting for protocol bytes. */
bool
sc_machine_init(const struct sc_machine_config *config) {
    if (!config || !protocol_output || !config->session_id ||
            strlen(config->session_id) != 36 || !config->parent_pid) {
        return false;
    }
    bool nonzero_guid = false;
    for (size_t index = 0; index < 36; ++index) {
        char digit = config->session_id[index];
        bool hyphen = index == 8 || index == 13 || index == 18 || index == 23;
        if (hyphen ? digit != '-' : !((digit >= '0' && digit <= '9') ||
                                      (digit >= 'a' && digit <= 'f'))) {
            return false;
        }
        nonzero_guid |= !hyphen && digit != '0';
    }
    if (!nonzero_guid) {
        return false;
    }
    memset(&machine, 0, sizeof(machine));
    machine.output = protocol_output;
    machine.input = GetStdHandle(STD_INPUT_HANDLE);
    machine.parent = sc_machine_open_parent(config->parent_pid,
                                             config->parent_created);
    if (!machine.parent) {
        return false;
    }
    strcpy(machine.session_id, config->session_id);
    if (!QueryPerformanceFrequency(&machine.frequency) ||
            machine.frequency.QuadPart <= 0) {
        CloseHandle(machine.parent);
        return false;
    }
    machine.process_started = machine_process_started;
    InitializeCriticalSection(&machine.mutex);
    InitializeCriticalSection(&machine.stop_publication_mutex);
    InitializeConditionVariable(&machine.queue_changed);
    machine.shutdown_event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!machine.shutdown_event) {
        CloseHandle(machine.parent);
        DeleteCriticalSection(&machine.mutex);
        DeleteCriticalSection(&machine.stop_publication_mutex);
        return false;
    }
    machine.handshake_deadline = GetTickCount64() + SC_MACHINE_HANDSHAKE_MS;
    machine.writer_thread = CreateThread(NULL, 0, sc_machine_writer,
                                         NULL, 0, NULL);
    machine.guard_thread = CreateThread(NULL, 0, sc_machine_guard,
                                        NULL, 0, NULL);
    if (!machine.writer_thread || !machine.guard_thread) {
        machine.closing = true;
        WakeAllConditionVariable(&machine.queue_changed);
        SetEvent(machine.shutdown_event);
        if (machine.writer_thread) {
            WaitForSingleObject(machine.writer_thread, SC_MACHINE_JOIN_MS);
            CloseHandle(machine.writer_thread);
        }
        if (machine.guard_thread) {
            WaitForSingleObject(machine.guard_thread, SC_MACHINE_JOIN_MS);
            CloseHandle(machine.guard_thread);
        }
        CloseHandle(machine.shutdown_event);
        CloseHandle(machine.parent);
        DeleteCriticalSection(&machine.mutex);
        DeleteCriticalSection(&machine.stop_publication_mutex);
        return false;
    }
    machine.initialized = true;
    return true;
}

/** Require exactly one valid hello before any native device operation. */
bool
sc_machine_handshake(void) {
    struct sc_ipc_message hello;
    struct sc_ipc_message result;
    if (!machine.initialized || !sc_machine_read_message(&hello) ||
            sc_ipc_hello_evaluate(&hello, &result) != SC_IPC_JSON_OK ||
            !sc_machine_queue_message(&result)) {
        sc_machine_fail();
        return false;
    }
    EnterCriticalSection(&machine.mutex);
    while (!machine.handshake_written && !machine.terminal &&
            GetTickCount64() < machine.handshake_deadline) {
        SleepConditionVariableCS(&machine.queue_changed, &machine.mutex, 100);
    }
    bool delivered = machine.handshake_written && !machine.terminal;
    LeaveCriticalSection(&machine.mutex);
    if (!delivered) {
        sc_machine_fail();
        return false;
    }
    if (strcmp(result.status, "accepted")) {
        return false;
    }
    InterlockedExchange(&machine.handshake_complete, 1);
    return true;
}

/** Queue one correlated terminal result without claiming process completion. */
static bool
sc_machine_send_result(const struct sc_ipc_message *command,
                       const char *status) {
    struct sc_ipc_message result = {0};
    result.type = SC_IPC_COMMAND_RESULT;
    result.request_id = command->request_id;
    strcpy(result.session_id, machine.session_id);
    strcpy(result.command, command->command);
    strcpy(result.status, status);
    return sc_machine_queue_message(&result);
}

struct sc_machine_focus_request {
    struct sc_ipc_message command;
};

/** Execute focus only on the SDL main thread, then report the attempted action. */
static void SDLCALL
sc_machine_focus_on_main(void *userdata) {
    struct sc_machine_focus_request *focus = userdata;
    const char *status = "noWindow";
    if (machine.window) {
        status = SDL_RaiseWindow(machine.window) ? "applied" : "failed";
    }
    sc_machine_send_result(&focus->command, status);
    EnterCriticalSection(&machine.mutex);
    machine.focus_pending--;
    LeaveCriticalSection(&machine.mutex);
    free(focus);
}

/** Handle only the two reviewed commands; request IDs never repeat. */
static bool
sc_machine_dispatch(const struct sc_ipc_message *command) {
    EnterCriticalSection(&machine.mutex);
    bool commands_closing = machine.commands_closing;
    LeaveCriticalSection(&machine.mutex);
    if (commands_closing) {
        return false;
    }
    if (command->type != SC_IPC_COMMAND ||
            strcmp(command->session_id, machine.session_id) ||
            command->request_id <= machine.max_request_id) {
        return false;
    }
    machine.max_request_id = command->request_id;
    if (!strcmp(command->command, "Stop")) {
        // SessionStopped cannot overtake the correlated acceptance frame.
        EnterCriticalSection(&machine.stop_publication_mutex);
        InterlockedExchange(&machine.stop_requested, 1);
        InterlockedCompareExchange(&machine.stop_cause, 1, 0);
        EnterCriticalSection(&machine.mutex);
        void (*callback)(void *) = machine.request_stop;
        void *userdata = machine.stop_userdata;
        LeaveCriticalSection(&machine.mutex);
        if (callback) {
            callback(userdata);
        }
        bool accepted = sc_machine_send_result(command, "accepted");
        LeaveCriticalSection(&machine.stop_publication_mutex);
        return accepted;
    }
    if (machine.stop_requested) {
        return sc_machine_send_result(command, "invalidState");
    }
    if (strcmp(command->command, "FocusWindow")) {
        return sc_machine_send_result(command, "unsupportedCommand");
    }
    struct sc_machine_focus_request *focus = malloc(sizeof(*focus));
    if (!focus) {
        return sc_machine_send_result(command, "failed");
    }
    focus->command = *command;
    EnterCriticalSection(&machine.mutex);
    bool capacity = machine.focus_pending < SC_MACHINE_QUEUE_ITEMS;
    if (capacity) {
        machine.focus_pending++;
    }
    LeaveCriticalSection(&machine.mutex);
    if (!capacity) {
        free(focus);
        return sc_machine_send_result(command, "failed");
    }
    if (!SDL_RunOnMainThread(sc_machine_focus_on_main, focus, false)) {
        EnterCriticalSection(&machine.mutex);
        machine.focus_pending--;
        LeaveCriticalSection(&machine.mutex);
        free(focus);
        return sc_machine_send_result(command, "failed");
    }
    return true;
}

/** A protocol error or pipe EOF is terminal and requests exact-session stop. */
static DWORD WINAPI
sc_machine_reader(void *userdata) {
    (void) userdata;
    for (;;) {
        struct sc_ipc_message message;
        bool valid = sc_machine_read_message(&message) &&
                     sc_machine_dispatch(&message);
        if (!valid) {
            EnterCriticalSection(&machine.mutex);
            bool closing = machine.commands_closing || machine.closing;
            LeaveCriticalSection(&machine.mutex);
            if (closing) {
                return 0;
            }
            sc_machine_fail();
            return 1;
        }
    }
}

/** Start command dispatch only after the accepted handshake. */
bool
sc_machine_start(void (*request_stop)(void *), void *userdata) {
    if (!InterlockedCompareExchange(&machine.handshake_complete, 0, 0) ||
            machine.reader_thread) {
        return false;
    }
    // Published before the reader starts; the guard may already be active.
    EnterCriticalSection(&machine.mutex);
    machine.request_stop = request_stop;
    machine.stop_userdata = userdata;
    LeaveCriticalSection(&machine.mutex);
    machine.reader_thread = CreateThread(NULL, 0, sc_machine_reader,
                                         NULL, 0, NULL);
    return machine.reader_thread != NULL;
}

/** Machine route stays opt-in; legacy calls remain unchanged. */
bool
sc_machine_is_active(void) {
    return machine.initialized &&
           InterlockedCompareExchange(&machine.handshake_complete, 0, 0);
}

/** Main-thread attempt identity changes only when a new worker is launched. */
void
sc_machine_set_attempt_id(const char attempt_id[37], bool reconnecting) {
    if (!sc_machine_is_active()) {
        return;
    }
    strcpy(machine.attempt_id, attempt_id);
    machine.attempt_reconnecting = reconnecting;
    machine.frame_presented = false;
}

/** A decoded byte or opened window is not the first presented video frame. */
void
sc_machine_on_frame_presented(void) {
    if (!sc_machine_is_active() || machine.frame_presented ||
            !machine.attempt_id[0]) {
        return;
    }
    machine.frame_presented = true;
    sc_machine_emit_lifecycle(machine.attempt_reconnecting ? "StreamResumed" :
                              "StreamStarted", "video", machine.attempt_id,
                              "none", "none");
}

/** The window pointer is owned and changed exclusively by the SDL main thread. */
void
sc_machine_set_window(SDL_Window *window) {
    machine.window = window;
}

/** Format one real process-relative timestamp and a UTC millisecond timestamp. */
static void
sc_machine_timestamp(char utc[25], uint64_t *elapsed_us) {
    SYSTEMTIME now;
    GetSystemTime(&now);
    snprintf(utc, 25, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ",
             now.wYear % 10000u, now.wMonth % 100u, now.wDay % 100u,
             now.wHour % 100u, now.wMinute % 100u, now.wSecond % 100u,
             now.wMilliseconds % 1000u);
    LARGE_INTEGER counter;
    QueryPerformanceCounter(&counter);
    uint64_t elapsed = counter.QuadPart - machine.process_started.QuadPart;
    *elapsed_us = elapsed / machine.frequency.QuadPart * 1000000u +
        (elapsed % machine.frequency.QuadPart) * 1000000u /
        machine.frequency.QuadPart;
}

/** Assign sequence and enqueue under one lock so publication order is stable. */
bool
sc_machine_emit_lifecycle(const char *event_type, const char *subsystem,
                          const char *attempt_id, const char *reason,
                          const char *error) {
    if (!InterlockedCompareExchange(&machine.handshake_complete, 0, 0) ||
            !event_type || !subsystem ||
            !reason || !error) {
        return false;
    }
    struct sc_ipc_message message = {0};
    message.type = SC_IPC_LIFECYCLE;
    strcpy(message.session_id, machine.session_id);
    if (attempt_id) {
        strcpy(message.connection_attempt_id, attempt_id);
    } else {
        message.connection_attempt_null = true;
    }
    strcpy(message.event_type, event_type);
    strcpy(message.subsystem, subsystem);
    strcpy(message.reason, reason);
    strcpy(message.error, error);
    sc_machine_timestamp(message.utc, &message.monotonic_microseconds);
    bool session_stopped = !strcmp(event_type, "SessionStopped");
    if (session_stopped) {
        EnterCriticalSection(&machine.stop_publication_mutex);
    }
    EnterCriticalSection(&machine.mutex);
    message.sequence = ++machine.sequence;
    // Sequencing and encoding are serialized with the queue owner.
    uint8_t *payload = NULL;
    size_t payload_length = 0;
    bool encoded = sc_ipc_json_encode(&message, &payload,
                                      &payload_length) == SC_IPC_JSON_OK;
    uint8_t *frame = NULL;
    size_t frame_length = 0;
    bool framed = encoded &&
        sc_ipc_frame_encode(payload, payload_length, &frame,
                            &frame_length) == SC_IPC_FRAME_READY;
    free(payload);
    bool capacity = framed && machine.queue_count < SC_MACHINE_QUEUE_ITEMS &&
                    frame_length <= SC_MACHINE_QUEUE_BYTES -
                                    machine.queue_bytes - machine.in_flight_bytes;
    bool accepted = !machine.closing && !machine.terminal && capacity;
    if (accepted) {
        size_t tail = (machine.queue_head + machine.queue_count) %
                      SC_MACHINE_QUEUE_ITEMS;
        machine.queue[tail] = (struct sc_machine_frame) {frame, frame_length};
        machine.queue_count++;
        machine.queue_bytes += frame_length;
        WakeConditionVariable(&machine.queue_changed);
    }
    LeaveCriticalSection(&machine.mutex);
    if (session_stopped) {
        LeaveCriticalSection(&machine.stop_publication_mutex);
    }
    if (!accepted) {
        free(frame);
        sc_machine_fail();
    }
    return accepted;
}

/** Use Windows' UUID generator for per-attempt identity. */
bool
sc_machine_new_attempt_id(char attempt_id[37]) {
    UUID uuid;
    RPC_STATUS status = UuidCreate(&uuid);
    if (status != RPC_S_OK && status != RPC_S_UUID_LOCAL_ONLY) {
        return false;
    }
    snprintf(attempt_id, 37,
             "%08" PRIx32 "-%04" PRIx16 "-%04" PRIx16 "-%04" PRIx16
             "-%02" PRIx8 "%02" PRIx8 "%02" PRIx8 "%02" PRIx8
             "%02" PRIx8 "%02" PRIx8,
             (uint32_t) uuid.Data1, (uint16_t) uuid.Data2,
             (uint16_t) uuid.Data3,
             (uint16_t) (((uint16_t) uuid.Data4[0] << 8) | uuid.Data4[1]),
             uuid.Data4[2], uuid.Data4[3], uuid.Data4[4], uuid.Data4[5],
             uuid.Data4[6], uuid.Data4[7]);
    return true;
}

/** Stop is sticky from a command, channel failure or parent death. */
bool
sc_machine_stop_requested(void) {
    return InterlockedCompareExchange(&machine.stop_requested, 0, 0) != 0;
}

/** Distinguish user Stop from pipe failure and abnormal parent death. */
const char *
sc_machine_stop_reason(void) {
    LONG cause = InterlockedCompareExchange(&machine.stop_cause, 0, 0);
    if (cause == 1) {
        return "userStop";
    }
    if (cause == 2) {
        return "protocolError";
    }
    return "unknown";
}

/** End the read direction before sending terminal lifecycle observations. */
void
sc_machine_close_commands(void) {
    if (!machine.initialized || !machine.reader_thread) {
        return;
    }
    EnterCriticalSection(&machine.mutex);
    machine.commands_closing = true;
    LeaveCriticalSection(&machine.mutex);
    ULONGLONG deadline = GetTickCount64() + SC_MACHINE_JOIN_MS;
    for (;;) {
        // Repeat cancellation until the worker has observed its closing flag;
        // an I/O may begin in the interval around the first cancellation.
        CancelSynchronousIo(machine.reader_thread);
        if (WaitForSingleObject(machine.reader_thread, 100) == WAIT_OBJECT_0) {
            break;
        }
        if (GetTickCount64() >= deadline) {
            TerminateProcess(GetCurrentProcess(), 6);
        }
    }
    CloseHandle(machine.reader_thread);
    machine.reader_thread = NULL;
    // SDL runs pending Focus callbacks on this same main thread.
    ULONGLONG focus_deadline = GetTickCount64() + 1000u;
    for (;;) {
        EnterCriticalSection(&machine.mutex);
        size_t pending = machine.focus_pending;
        LeaveCriticalSection(&machine.mutex);
        if (!pending) {
            break;
        }
        if (GetTickCount64() >= focus_deadline) {
            TerminateProcess(GetCurrentProcess(), 8);
        }
        SDL_PumpEvents();
    }
}

/** Settle workers before releasing buffers and the exact parent handle. */
void
sc_machine_destroy(void) {
    if (!machine.initialized) {
        return;
    }
    sc_machine_close_commands();
    EnterCriticalSection(&machine.mutex);
    machine.closing = true;
    WakeAllConditionVariable(&machine.queue_changed);
    LeaveCriticalSection(&machine.mutex);
    if (WaitForSingleObject(machine.writer_thread,
                            SC_MACHINE_JOIN_MS) != WAIT_OBJECT_0) {
        CancelSynchronousIo(machine.writer_thread);
        TerminateProcess(GetCurrentProcess(), 7);
    }
    SetEvent(machine.shutdown_event);
    WaitForSingleObject(machine.guard_thread, SC_MACHINE_JOIN_MS);
    CloseHandle(machine.writer_thread);
    CloseHandle(machine.guard_thread);
    CloseHandle(machine.shutdown_event);
    CloseHandle(machine.parent);
    CloseHandle(machine.output);
    for (size_t index = 0; index < machine.queue_count; ++index) {
        size_t slot = (machine.queue_head + index) % SC_MACHINE_QUEUE_ITEMS;
        free(machine.queue[slot].data);
    }
    DeleteCriticalSection(&machine.mutex);
    DeleteCriticalSection(&machine.stop_publication_mutex);
    memset(&machine, 0, sizeof(machine));
    protocol_output = NULL;
}

#endif
