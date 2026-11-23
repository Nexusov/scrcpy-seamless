#include "common.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include <SDL3/SDL.h>
#include "transport_resolver.h"
#include "adb/adb.h"

/* Only external command execution is replaced; worker/intr/settlement are real. */
static SDL_Semaphore *entered;
static SDL_Semaphore *release_query;
static unsigned query_count;
static unsigned connect_count;
static bool gated;
static const char *mdns_output;
static const char *usb_output;
static const char *usb_path = "unknown";
static const char *expected_connect;

const char *sc_adb_get_executable(void) {
    return "owned-fixture-adb";
}

bool sc_adb_query(struct sc_intr *intr, const char *const argv[], char *output,
                   size_t capacity) {
    assert(!strcmp(argv[0], "owned-fixture-adb"));
    ++query_count;

    if (gated) {
        SDL_SignalSemaphore(entered);
        SDL_WaitSemaphore(release_query);
    }

    if (sc_intr_is_interrupted(intr)) {
        return false;
    }
    const char *answer;

    if (!strcmp(argv[1], "-s")) {
        assert(!strcmp(argv[2], "opaque-device") && !argv[4]);
        answer = !strcmp(argv[3], "get-devpath") ? usb_path : "device\n";
    } else if (!strcmp(argv[1], "-d")) {
        assert(!strcmp(argv[2], "get-serialno") && !argv[3]);
        answer = usb_output;
    } else if (!strcmp(argv[1], "mdns")) {
        assert(!strcmp(argv[2], "services") && !argv[3]);
        answer = mdns_output;
    } else {
        assert(!strcmp(argv[1], "connect") && !argv[3]);
        assert(!strcmp(argv[2], expected_connect));
        ++connect_count;
        answer = "connected to synthetic-route\n";
    }

    if (!answer) {
        return false;
    }
    assert(strlen(answer) < capacity);
    strcpy(output, answer);
    return true;
}

/* No handle is registered by the controlled query, so these must never run. */
bool net_interrupt(sc_socket socket) {
    (void) socket;
    assert(!"unexpected external socket effect");
    return false;
}

bool sc_process_terminate(sc_pid process) {
    (void) process;
    assert(!"unexpected external process effect");
    return false;
}

static struct sc_transport_result
run(const struct sc_connection_target *target) {
    struct sc_transport_resolver resolver = {0};
    assert(sc_transport_resolver_start(&resolver, 42, target));
    struct sc_transport_result result;
    Uint64 deadline = SDL_GetTicks() + 2000;

    while (!sc_transport_resolver_take(&resolver, &result)) {
        assert(SDL_GetTicks() < deadline);
        SDL_Delay(0); /* Yield only; elapsed time is not the test's ordering gate. */
    }
    assert(!resolver.started && result.attempt == 42);
    return result;
}

static void test_mdns(void) {
    char target[SC_TRANSPORT_IDENTITY_MAX + 1] = "untouched";
    const char *output = "List of discovered mdns services\n"
        "phone _adb-tls-pairing._tcp 127.0.0.1:1000\n"
        "phone _adb-tls-connect._tcp 127.0.0.1:2000\n";
    assert(sc_transport_parse_mdns(output, "phone", target));
    assert(!strcmp(target, "127.0.0.1:2000"));
    assert(sc_transport_parse_mdns(output, "phone._adb-tls-connect._tcp", target));
    assert(!sc_transport_parse_mdns(output, "other", target));
    assert(!sc_transport_parse_mdns("phone _adb-tls-connect._tcp x\n"
                                    "phone _adb-tls-connect._tcp y\n",
                                    "phone", target));
    assert(!sc_transport_parse_mdns("phone _adb-tls-connect._tcp x extra\n",
                                    "phone", target));
    assert(!sc_transport_parse_mdns("phone _adb-tls-pairing._tcp x\n",
                                    "phone", target));
    assert(sc_transport_parse_mdns(
        "phone. _adb-tls-connect._tcp. 127.0.0.1:2000\n",
        "phone._adb-tls-connect._tcp.", target));
}

static void test_usb(void) {
    struct sc_connection_target candidate = {
        .transport = SC_CONNECTION_USB,
        .serial = "opaque-device",
    };
    usb_output = "opaque-device\r\n";
    struct sc_transport_result result = run(&candidate);
    assert(result.failure == SC_CONNECTION_FAILURE_NONE);
    assert(!strcmp(result.serial, "opaque-device"));
    usb_output = "another-device\n";
    result = run(&candidate);
    assert(result.failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
    assert(!result.serial[0]);
    usb_output = "opaque-device\nextra\n";
    assert(run(&candidate).failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
    usb_output = NULL;
    assert(run(&candidate).failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
    /* An exact USB devpath remains positive evidence with multiple USB phones;
     * the global -d selector is deliberately ambiguous in this schedule. */
    usb_path = "1-2.3\n";
    assert(run(&candidate).failure == SC_CONNECTION_FAILURE_NONE);
    usb_path = "unknown";
}

static void test_network(void) {
    struct sc_connection_target candidate = {
        .transport = SC_CONNECTION_NETWORK,
        .service = "phone._adb-tls-connect._tcp",
        .endpoint = "127.0.0.1:3000",
    };
    mdns_output = "phone _adb-tls-connect._tcp 127.0.0.1:4000\n";
    expected_connect = "127.0.0.1:4000";
    struct sc_transport_result result = run(&candidate);
    assert(result.failure == SC_CONNECTION_FAILURE_NONE);
    assert(!strcmp(result.serial, expected_connect));
    mdns_output = NULL;
    expected_connect = candidate.endpoint;
    result = run(&candidate);
    assert(result.failure == SC_CONNECTION_FAILURE_NONE);
    assert(!strcmp(result.serial, candidate.endpoint));
    candidate.endpoint[0] = '\0';
    unsigned before = connect_count;
    assert(run(&candidate).failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
    assert(connect_count == before);
}

static void test_cancel_settlement(void) {
    struct sc_connection_target candidate = {
        .transport = SC_CONNECTION_USB,
        .serial = "opaque-device",
    };
    struct sc_transport_resolver resolver = {0};
    gated = true;
    assert(sc_transport_resolver_start(&resolver, 91, &candidate));
    assert(SDL_WaitSemaphoreTimeout(entered, 2000));
    struct sc_transport_result result;
    sc_transport_resolver_cancel(&resolver);
    assert(!sc_transport_resolver_take(&resolver, &result));
    assert(resolver.started);
    SDL_SignalSemaphore(release_query);
    Uint64 deadline = SDL_GetTicks() + 2000;

    while (!sc_transport_resolver_take(&resolver, &result)) {
        assert(SDL_GetTicks() < deadline);
        SDL_Delay(0);
    }
    assert(result.attempt == 91);
    assert(result.failure == SC_CONNECTION_FAILURE_TRANSIENT_CONNECT);
    gated = false;
}

int main(int argc, char **argv) {
    assert(SDL_Init(0));
    entered = SDL_CreateSemaphore(0);
    release_query = SDL_CreateSemaphore(0);
    assert(entered && release_query);
    if (argc == 2 && !strcmp(argv[1], "--usb")) {
        test_usb();
        SDL_DestroySemaphore(release_query);
        SDL_DestroySemaphore(entered);
        SDL_Quit();
        return 0;
    }
    test_mdns();
    test_usb();
    test_network();
    test_cancel_settlement();
    assert(query_count > 0 && connect_count == 2);
    SDL_DestroySemaphore(release_query);
    SDL_DestroySemaphore(entered);
    SDL_Quit();
    puts("Transport resolver: 4 production-linked groups passed (no ADB)");
    return 0;
}
