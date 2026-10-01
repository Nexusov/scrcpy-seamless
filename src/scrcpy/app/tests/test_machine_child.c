#ifdef _WIN32

#include <inttypes.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>
#include <SDL3/SDL.h>

#include "ipc/machine.h"

#define SC_FIXTURE_PATH_CAPACITY 1024
#define SC_FIXTURE_STARTUP_MS 10000u
#define SC_FIXTURE_FOCUS_WINDOW_WIDTH 480
#define SC_FIXTURE_FOCUS_WINDOW_HEIGHT 320

/** Convert one FILETIME into the exact unsigned Windows creation tick value. */
static uint64_t
sc_fixture_filetime(FILETIME value) {
    return ((uint64_t) value.dwHighDateTime << 32) | value.dwLowDateTime;
}

/** Read a test-only process-local environment field without truncation. */
static bool
sc_fixture_environment(const wchar_t *name, wchar_t *value, DWORD capacity) {
    DWORD length = GetEnvironmentVariableW(name, value, capacity);
    return length && length < capacity;
}

/** Publish a complete PID/creation-time marker before signalling readiness. */
static bool
sc_fixture_publish_identity(const wchar_t *marker, uint64_t created) {
    wchar_t directory[SC_FIXTURE_PATH_CAPACITY];
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_DIRECTORY", directory,
                                SC_FIXTURE_PATH_CAPACITY)) {
        return false;
    }
    wchar_t temporary[SC_FIXTURE_PATH_CAPACITY];
    wchar_t final[SC_FIXTURE_PATH_CAPACITY];
    int temporary_length = swprintf(temporary, SC_FIXTURE_PATH_CAPACITY,
                                   L"%ls\\%ls.json.tmp", directory, marker);
    int final_length = swprintf(final, SC_FIXTURE_PATH_CAPACITY,
                               L"%ls\\%ls.json", directory, marker);
    if (temporary_length < 0 || final_length < 0 ||
            temporary_length >= SC_FIXTURE_PATH_CAPACITY ||
            final_length >= SC_FIXTURE_PATH_CAPACITY) {
        return false;
    }
    char json[128];
    int length = snprintf(json, sizeof(json),
                          "{\"Pid\":%lu,\"CreatedFileTime\":%" PRIu64 "}",
                          (unsigned long) GetCurrentProcessId(), created);
    if (length < 0 || (size_t) length >= sizeof(json)) {
        return false;
    }
    HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, NULL,
                              CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        return false;
    }
    DWORD written = 0;
    bool complete = WriteFile(file, json, (DWORD) length, &written, NULL) &&
                    written == (DWORD) length && FlushFileBuffers(file);
    CloseHandle(file);
    if (!complete || !MoveFileExW(temporary, final,
                                  MOVEFILE_REPLACE_EXISTING |
                                  MOVEFILE_WRITE_THROUGH)) {
        DeleteFileW(temporary);
        return false;
    }
    return true;
}

/** Remain alive as an escaped, synthetic daemon-like process until test cleanup. */
static int
sc_fixture_daemon_main(void) {
    wchar_t ready_name[SC_FIXTURE_PATH_CAPACITY];
    wchar_t release_name[SC_FIXTURE_PATH_CAPACITY];
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_DAEMON_READY_EVENT",
                                ready_name, SC_FIXTURE_PATH_CAPACITY) ||
            !sc_fixture_environment(L"SCRCPY_IPC_TEST_DAEMON_RELEASE_EVENT",
                                    release_name, SC_FIXTURE_PATH_CAPACITY)) {
        return 2;
    }
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, ready_name);
    HANDLE release = OpenEventW(SYNCHRONIZE, FALSE, release_name);
    if (!ready || !release) {
        if (ready) {
            CloseHandle(ready);
        }
        if (release) {
            CloseHandle(release);
        }
        return 3;
    }
    FILETIME creation, exit_time, kernel, user;
    bool valid = GetProcessTimes(GetCurrentProcess(), &creation, &exit_time,
                                 &kernel, &user) &&
                 sc_fixture_publish_identity(L"daemon-ready",
                     sc_fixture_filetime(creation));
    if (!valid || !SetEvent(ready)) {
        CloseHandle(ready);
        CloseHandle(release);
        return 4;
    }
    DWORD result = WaitForSingleObject(release, INFINITE);
    CloseHandle(ready);
    CloseHandle(release);
    return result == WAIT_OBJECT_0 ? 0 : 5;
}

/** Hold the child before installing the guard to prove a dead parent is rejected. */
static bool
sc_fixture_before_guard(void) {
    wchar_t ready_name[SC_FIXTURE_PATH_CAPACITY];
    wchar_t release_name[SC_FIXTURE_PATH_CAPACITY];
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_PRE_GUARD_READY_EVENT",
                                ready_name, SC_FIXTURE_PATH_CAPACITY)) {
        return true;
    }
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_PRE_GUARD_RELEASE_EVENT",
                                release_name, SC_FIXTURE_PATH_CAPACITY)) {
        return false;
    }
    HANDLE ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, ready_name);
    HANDLE release = OpenEventW(SYNCHRONIZE, FALSE, release_name);
    if (!ready || !release) {
        if (ready) {
            CloseHandle(ready);
        }
        if (release) {
            CloseHandle(release);
        }
        return false;
    }
    FILETIME creation, exit_time, kernel, user;
    bool valid = GetProcessTimes(GetCurrentProcess(), &creation, &exit_time,
                                 &kernel, &user) &&
                 sc_fixture_publish_identity(L"pre-guard-ready",
                     sc_fixture_filetime(creation)) && SetEvent(ready);
    DWORD result = valid ? WaitForSingleObject(release, INFINITE) : WAIT_FAILED;
    CloseHandle(ready);
    CloseHandle(release);
    return result == WAIT_OBJECT_0;
}

/** Spawn a new descendant without inheriting protocol or parent-guard handles. */
static bool
sc_fixture_spawn_daemon(void) {
    wchar_t ready_name[SC_FIXTURE_PATH_CAPACITY];
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_DAEMON_READY_EVENT",
                                ready_name, SC_FIXTURE_PATH_CAPACITY)) {
        return false;
    }
    HANDLE ready = OpenEventW(SYNCHRONIZE, FALSE, ready_name);
    if (!ready) {
        return false;
    }
    wchar_t executable[SC_FIXTURE_PATH_CAPACITY];
    DWORD length = GetModuleFileNameW(NULL, executable,
                                      SC_FIXTURE_PATH_CAPACITY);
    if (!length || length >= SC_FIXTURE_PATH_CAPACITY) {
        CloseHandle(ready);
        return false;
    }
    wchar_t command[SC_FIXTURE_PATH_CAPACITY + 32];
    int command_length = swprintf(command, SC_FIXTURE_PATH_CAPACITY + 32,
                                  L"\"%ls\" --daemon", executable);
    if (command_length < 0 || command_length >= SC_FIXTURE_PATH_CAPACITY + 32) {
        CloseHandle(ready);
        return false;
    }
    STARTUPINFOW startup = {0};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process = {0};
    bool started = CreateProcessW(executable, command, NULL, NULL, FALSE,
                                  DETACHED_PROCESS, NULL, NULL,
                                  &startup, &process);
    if (!started) {
        CloseHandle(ready);
        return false;
    }
    CloseHandle(process.hThread);
    DWORD result = WaitForSingleObject(ready, SC_FIXTURE_STARTUP_MS);
    bool alive = WaitForSingleObject(process.hProcess, 0) == WAIT_TIMEOUT;
    if (result != WAIT_OBJECT_0 && alive) {
        TerminateProcess(process.hProcess, 1);
        WaitForSingleObject(process.hProcess, SC_FIXTURE_STARTUP_MS);
    }
    CloseHandle(process.hProcess);
    CloseHandle(ready);
    return result == WAIT_OBJECT_0 && alive;
}

/** Extract only the managed bootstrap contract; ignore ordinary mirror options. */
static bool
sc_fixture_parse_bootstrap(int argc, char *argv[],
                           struct sc_machine_config *config) {
    memset(config, 0, sizeof(*config));
    bool machine = false;
    for (int index = 1; index < argc; ++index) {
        const char *argument = argv[index];
        if (!strcmp(argument, "--seamless-machine")) {
            machine = true;
        } else if (!strncmp(argument, "--seamless-session-id=", 22)) {
            config->session_id = argument + 22;
        } else if (!strncmp(argument, "--seamless-parent-pid=", 22)) {
            char *end = NULL;
            unsigned long value = strtoul(argument + 22, &end, 10);
            if (!end || *end || !value || value > UINT32_MAX) {
                return false;
            }
            config->parent_pid = (uint32_t) value;
        } else if (!strncmp(argument, "--seamless-parent-created=", 26)) {
            char *end = NULL;
            unsigned long long value = strtoull(argument + 26, &end, 10);
            if (!end || *end || !value) {
                return false;
            }
            config->parent_created = (uint64_t) value;
        }
    }
    return machine && config->session_id && config->parent_pid &&
           config->parent_created;
}

/** Interrupt the main SDL wait after Stop, EOF or parent loss. */
static void
sc_fixture_request_stop(void *userdata) {
    HANDLE stop = userdata;
    SetEvent(stop);
    SDL_Event event = {0};
    event.type = SDL_EVENT_USER;
    SDL_PushEvent(&event);
}

/** Publish only this synthetic window's identity before accepting UI requests. */
static bool
sc_fixture_publish_focus_window(SDL_Window *window) {
    wchar_t directory[SC_FIXTURE_PATH_CAPACITY];
    if (!sc_fixture_environment(L"SCRCPY_IPC_TEST_DIRECTORY", directory,
                                SC_FIXTURE_PATH_CAPACITY)) {
        return false;
    }
    wchar_t temporary[SC_FIXTURE_PATH_CAPACITY];
    wchar_t final[SC_FIXTURE_PATH_CAPACITY];
    int temporary_length = swprintf(temporary, SC_FIXTURE_PATH_CAPACITY,
                                   L"%ls\\focus-window-ready.json.tmp", directory);
    int final_length = swprintf(final, SC_FIXTURE_PATH_CAPACITY,
                               L"%ls\\focus-window-ready.json", directory);
    if (temporary_length < 0 || final_length < 0 ||
            temporary_length >= SC_FIXTURE_PATH_CAPACITY ||
            final_length >= SC_FIXTURE_PATH_CAPACITY) {
        return false;
    }
    const char *revision = SDL_GetRevision();
    // The loaded library's build revision is a token, not arbitrary user text.
    for (const char *cursor = revision; *cursor; ++cursor) {
        bool token_character = (*cursor >= 'a' && *cursor <= 'z') ||
                               (*cursor >= 'A' && *cursor <= 'Z') ||
                               (*cursor >= '0' && *cursor <= '9') ||
                               *cursor == '-' || *cursor == '_' ||
                               *cursor == '.' || *cursor == '+';
        if (!token_character) {
            return false;
        }
    }
    HWND hwnd = SDL_GetPointerProperty(SDL_GetWindowProperties(window),
                                       SDL_PROP_WINDOW_WIN32_HWND_POINTER, NULL);
    SDL_WindowFlags flags = SDL_GetWindowFlags(window);
    char json[1024];
    int length = snprintf(json, sizeof(json),
        "{\"Pid\":%lu,\"Hwnd\":%" PRIuPTR ",\"MainThreadId\":%lu,"
        "\"IsSdlMainThread\":%s,\"SdlVersion\":%d,\"SdlRevision\":\"%s\","
        "\"Hidden\":%s,\"Minimized\":%s,\"AlwaysOnTop\":%s,\"Focusable\":%s,"
        "\"ActivateWhenRaised\":%s,\"ForceRaise\":%s}",
        (unsigned long) GetCurrentProcessId(), (uintptr_t) hwnd,
        (unsigned long) GetCurrentThreadId(), SDL_IsMainThread() ? "true" : "false",
        SDL_GetVersion(), revision,
        flags & SDL_WINDOW_HIDDEN ? "true" : "false",
        flags & SDL_WINDOW_MINIMIZED ? "true" : "false",
        flags & SDL_WINDOW_ALWAYS_ON_TOP ? "true" : "false",
        flags & SDL_WINDOW_NOT_FOCUSABLE ? "false" : "true",
        SDL_GetHintBoolean(SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED, true) ? "true" : "false",
        SDL_GetHintBoolean(SDL_HINT_FORCE_RAISEWINDOW, false) ? "true" : "false");
    if (!hwnd || length < 0 || (size_t) length >= sizeof(json)) {
        return false;
    }
    HANDLE file = CreateFileW(temporary, GENERIC_WRITE, 0, NULL,
                              CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        return false;
    }
    DWORD written = 0;
    bool complete = WriteFile(file, json, (DWORD) length, &written, NULL) &&
                    written == (DWORD) length && FlushFileBuffers(file);
    CloseHandle(file);
    if (!complete || !MoveFileExW(temporary, final, MOVEFILE_WRITE_THROUGH)) {
        DeleteFileW(temporary);
        return false;
    }
    return true;
}

/** Exercise the same production dispatcher and parent watcher without ADB. */
int
main(int argc, char *argv[]) {
    if (argc == 2 && !strcmp(argv[1], "--daemon")) {
        return sc_fixture_daemon_main();
    }
    if (!sc_machine_prepare_stdio()) {
        return 10;
    }
    wchar_t focus_mode[2];
    bool focus_window_enabled = sc_fixture_environment(
        L"SCRCPY_IPC_TEST_FOCUS_WINDOW", focus_mode, 2) && focus_mode[0] == L'1';
    struct sc_machine_config config;
    if (!sc_fixture_parse_bootstrap(argc, argv, &config) ||
            !sc_fixture_before_guard() ||
            !sc_machine_init(&config) || !sc_machine_handshake() ||
            !SDL_Init(focus_window_enabled ? SDL_INIT_VIDEO | SDL_INIT_EVENTS :
                                            SDL_INIT_EVENTS)) {
        return 11;
    }
    HANDLE stop = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!stop || !sc_machine_start(sc_fixture_request_stop, stop)) {
        if (stop) {
            CloseHandle(stop);
        }
        sc_machine_destroy();
        SDL_Quit();
        return 12;
    }
    SDL_Window *window = NULL;
    if (focus_window_enabled) {
        // Do not enable SDL's input-thread/topmost workaround in this experiment.
        if (!SDL_GetHintBoolean(SDL_HINT_FORCE_RAISEWINDOW, false)) {
            window = SDL_CreateWindow("Seamless synthetic focus target",
                SC_FIXTURE_FOCUS_WINDOW_WIDTH, SC_FIXTURE_FOCUS_WINDOW_HEIGHT,
                SDL_WINDOW_RESIZABLE);
        }
        sc_machine_set_window(window);
        if (!window || !sc_fixture_publish_focus_window(window)) {
            sc_machine_close_commands();
            sc_machine_set_window(NULL);
            if (window) {
                SDL_DestroyWindow(window);
            }
            sc_machine_destroy();
            CloseHandle(stop);
            SDL_Quit();
            return 14;
        }
    }
    if ((!focus_window_enabled && !sc_fixture_spawn_daemon()) ||
            !sc_machine_emit_lifecycle("NativeReady", "native", NULL,
                                       "none", "none")) {
        sc_machine_close_commands();
        sc_machine_set_window(NULL);
        if (window) {
            SDL_DestroyWindow(window);
        }
        sc_machine_destroy();
        CloseHandle(stop);
        SDL_Quit();
        return 13;
    }
    while (!sc_machine_stop_requested()) {
        SDL_Event event;
        if (!SDL_WaitEvent(&event)) {
            break;
        }
    }
    sc_machine_close_commands();
    sc_machine_set_window(NULL);
    if (window) {
        SDL_DestroyWindow(window);
    }
    if (WaitForSingleObject(stop, 0) == WAIT_OBJECT_0) {
        sc_machine_emit_lifecycle("SessionStopped", "native", NULL,
                                   "userStop", "none");
    }
    sc_machine_destroy();
    CloseHandle(stop);
    SDL_Quit();
    return 0;
}

#endif
