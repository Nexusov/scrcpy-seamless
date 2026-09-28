#include "ipc/ipc_frame.h"
#include "ipc/ipc_json.h"

#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

struct golden_case {
    const char *name;
    size_t payload_length;
    uint8_t header[4];
};

static const struct golden_case golden[] = {
    {"hello", 182, {0xb6, 0, 0, 0}},
    {"hello-result", 167, {0xa7, 0, 0, 0}},
    {"stop", 128, {0x80, 0, 0, 0}},
    {"stop-result", 154, {0x9a, 0, 0, 0}},
    {"focus", 116, {0x74, 0, 0, 0}},
    {"focus-result", 142, {0x8e, 0, 0, 0}},
    {"lifecycle", 294, {0x26, 1, 0, 0}},
    {"fatal", 308, {0x34, 1, 0, 0}},
    {"unicode-unknown-command", 115, {0x73, 0, 0, 0}},
};

/** Reads a bounded binary fixture without text-mode translation. */
static uint8_t *
read_file(const char *path, size_t *length) {
    FILE *file = fopen(path, "rb");
    assert(file);
    assert(!fseek(file, 0, SEEK_END));
    long size = ftell(file);
    assert(size > 0 && (unsigned long) size <= SC_IPC_MAX_PAYLOAD + 4u);
    assert(!fseek(file, 0, SEEK_SET));
    uint8_t *data = malloc((size_t) size);
    assert(data);
    assert(fread(data, 1, (size_t) size, file) == (size_t) size);
    assert(!fclose(file));
    *length = (size_t) size;
    return data;
}

/** Writes an exact binary frame for the cross-language harness. */
static void
write_file(const char *path, const uint8_t *bytes, size_t length) {
    FILE *file = fopen(path, "wb");
    assert(file);
    assert(fwrite(bytes, 1, length, file) == length);
    assert(!fclose(file));
}

/** Verifies one boundary split and completed-frame ownership. */
static void
check_split(const uint8_t *frame, size_t frame_length, size_t split,
            const uint8_t *payload, size_t payload_length) {
    struct sc_ipc_frame_reader reader;
    sc_ipc_frame_reader_init(&reader);
    size_t consumed, output_length;
    uint8_t *output;
    enum sc_ipc_frame_status status = sc_ipc_frame_reader_feed(&reader,
        frame, split, &consumed, &output, &output_length);
    assert(consumed == split);
    assert(status == SC_IPC_FRAME_MORE);
    status = sc_ipc_frame_reader_feed(&reader, frame + split,
        frame_length - split, &consumed, &output, &output_length);
    assert(status == SC_IPC_FRAME_READY);
    assert(consumed == frame_length - split);
    assert(output_length == payload_length);
    assert(!memcmp(output, payload, payload_length));
    free(output);
    assert(sc_ipc_frame_reader_eof(&reader) == SC_IPC_FRAME_READY);
    sc_ipc_frame_reader_destroy(&reader);
}

/** Runs framing and semantic checks against one authored vector. */
static void
check_golden(const char *directory, const char *output_directory,
             const struct golden_case *item) {
    char path[1024];
    int count = snprintf(path, sizeof(path), "%s/%s.json", directory,
                         item->name);
    assert(count > 0 && count < (int) sizeof(path));
    size_t payload_length;
    uint8_t *payload = read_file(path, &payload_length);
    assert(payload_length == item->payload_length);
    struct sc_ipc_message decoded;
    assert(sc_ipc_json_decode(payload, payload_length, &decoded) ==
           SC_IPC_JSON_OK);
    if (!strcmp(item->name, "stop")) {
        assert(decoded.request_id == UINT64_MAX);
        assert(!strcmp(decoded.command, "Stop"));
    }
    if (!strcmp(item->name, "fatal")) {
        assert(decoded.sequence == UINT64_MAX);
        assert(decoded.monotonic_microseconds == UINT64_MAX);
        assert(decoded.connection_attempt_null);
    }
    if (!strcmp(item->name, "unicode-unknown-command")) {
        assert(!strcmp(decoded.command, "Фокус"));
    }
    if (!strcmp(item->name, "hello")) {
        struct sc_ipc_message result;
        assert(sc_ipc_hello_evaluate(&decoded, &result) == SC_IPC_JSON_OK);
        assert(!strcmp(result.status, "accepted"));
        decoded.protocol_major = 2;
        assert(sc_ipc_hello_evaluate(&decoded, &result) == SC_IPC_JSON_OK);
        assert(!strcmp(result.status, "majorMismatch"));
        decoded.protocol_major = 1;
        strcpy(decoded.required_capabilities.values[0], "future-required");
        strcpy(decoded.supported_capabilities.values[0], "future-required");
        assert(sc_ipc_hello_evaluate(&decoded, &result) == SC_IPC_JSON_OK);
        assert(!strcmp(result.status, "requiredCapabilityMissing"));
        strcpy(decoded.required_capabilities.values[0], "stop");
        strcpy(decoded.supported_capabilities.values[0], "focus-window");
        decoded.supported_capabilities.count = 0;
        assert(sc_ipc_hello_evaluate(&decoded, &result) ==
               SC_IPC_JSON_INVALID_MESSAGE);
        decoded.supported_capabilities.count = 3;
    }
    uint8_t *encoded;
    size_t encoded_length;
    assert(sc_ipc_json_encode(&decoded, &encoded, &encoded_length) ==
           SC_IPC_JSON_OK);
    assert(encoded_length == payload_length);
    assert(!memcmp(encoded, payload, payload_length));
    uint8_t *frame;
    size_t frame_length;
    assert(sc_ipc_frame_encode(encoded, encoded_length, &frame,
                               &frame_length) == SC_IPC_FRAME_READY);
    assert(frame_length == payload_length + 4);
    assert(!memcmp(frame, item->header, 4));
    for (size_t split = 1; split < frame_length; ++split) {
        check_split(frame, frame_length, split, payload, payload_length);
    }
    if (output_directory) {
        count = snprintf(path, sizeof(path), "%s/%s.frame",
                         output_directory, item->name);
        assert(count > 0 && count < (int) sizeof(path));
        write_file(path, frame, frame_length);
    }
    free(frame);
    free(encoded);
    free(payload);
}

/** Verifies zero/oversized lengths and distinct EOF states. */
static void
check_frame_edges(void) {
    const uint8_t invalid[][4] = {
        {0, 0, 0, 0},
        {1, 0, 0x10, 0},
        {0xff, 0xff, 0xff, 0xff},
    };
    for (size_t index = 0; index < 3; ++index) {
        struct sc_ipc_frame_reader reader;
        sc_ipc_frame_reader_init(&reader);
        size_t consumed, length;
        uint8_t *payload;
        assert(sc_ipc_frame_reader_feed(&reader, invalid[index], 4,
            &consumed, &payload, &length) == SC_IPC_FRAME_INVALID_LENGTH);
        assert(!payload);
        const uint8_t next = 'x';
        assert(sc_ipc_frame_reader_feed(&reader, &next, 1, &consumed,
            &payload, &length) == SC_IPC_FRAME_INVALID_LENGTH);
        assert(consumed == 0);
        assert(sc_ipc_frame_reader_eof(&reader) == SC_IPC_FRAME_INVALID_LENGTH);
        sc_ipc_frame_reader_destroy(&reader);
    }
    struct sc_ipc_frame_reader reader;
    sc_ipc_frame_reader_init(&reader);
    assert(sc_ipc_frame_reader_eof(&reader) == SC_IPC_FRAME_READY);
    size_t consumed, length;
    uint8_t *payload;
    const uint8_t partial_header[] = {1, 0};
    assert(sc_ipc_frame_reader_feed(&reader, partial_header, 2,
        &consumed, &payload, &length) == SC_IPC_FRAME_MORE);
    assert(sc_ipc_frame_reader_eof(&reader) == SC_IPC_FRAME_TRUNCATED_HEADER);
    sc_ipc_frame_reader_destroy(&reader);
    const uint8_t partial_body[] = {2, 0, 0, 0, 'x'};
    assert(sc_ipc_frame_reader_feed(&reader, partial_body, 5,
        &consumed, &payload, &length) == SC_IPC_FRAME_MORE);
    assert(sc_ipc_frame_reader_eof(&reader) == SC_IPC_FRAME_TRUNCATED_PAYLOAD);
    sc_ipc_frame_reader_destroy(&reader);

    uint8_t *maximum = calloc(SC_IPC_MAX_PAYLOAD, 1);
    assert(maximum);
    uint8_t *frame;
    size_t frame_length;
    assert(sc_ipc_frame_encode(maximum, SC_IPC_MAX_PAYLOAD, &frame,
                               &frame_length) == SC_IPC_FRAME_READY);
    assert(!memcmp(frame, (uint8_t[]) {0, 0, 0x10, 0}, 4));
    free(frame);
    free(maximum);
    assert(sc_ipc_frame_encode((const uint8_t *) "x",
        SC_IPC_MAX_PAYLOAD + 1u, &frame, &frame_length) ==
        SC_IPC_FRAME_INVALID_LENGTH);

    const uint8_t two_frames[] = {1, 0, 0, 0, 'a', 1, 0, 0, 0, 'b'};
    sc_ipc_frame_reader_init(&reader);
    assert(sc_ipc_frame_reader_feed(&reader, two_frames, sizeof(two_frames),
        &consumed, &payload, &length) == SC_IPC_FRAME_READY);
    assert(consumed == 5 && length == 1 && payload[0] == 'a');
    free(payload);
    assert(sc_ipc_frame_reader_feed(&reader, two_frames + consumed,
        sizeof(two_frames) - consumed, &consumed, &payload, &length) ==
        SC_IPC_FRAME_READY);
    assert(consumed == 5 && length == 1 && payload[0] == 'b');
    free(payload);
    sc_ipc_frame_reader_destroy(&reader);
}

/** Confirms strict semantic and JSON rejection without echoing input. */
static void
check_invalid_messages(void) {
    const char *invalid[] = {
        "{}",
        "[]",
        "{\"messageType\":\"unknown\"}",
        "{\"messageType\":\"command\",\"requestId\":null}",
        "{\"messageType\":\"command\",\"requestId\":\"01\"}",
        "{\"messageType\":\"command\",\"requestId\":\"18446744073709551616\"}",
        "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
            "\"11111111-2222-3333-4444-555555555555\",\"command\":null}",
        "{\"messageType\":\"command\",\"messageType\":\"command\"}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\",\"nested\":{}}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\"} trailing",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\\u0000extra\"}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"\\uD800\"}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\",\"future\":1,\"future\":2}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\",\"future\":[]}",
        "{\"messageType\":\"command\",\"requestId\":\"1\","
            "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            "\"command\":\"Stop\",\"status\":\"accepted\"}",
        "{\"messageType\":\"hello\",\"product\":\"scrcpy-seamless\","
            "\"protocolMajor\":1,\"protocolMinor\":0,"
            "\"requiredCapabilities\":[\"stop\"],\"supportedCapabilities\":[]}",
        "{\"messageType\":\"lifecycle\",\"sequence\":\"1\","
            "\"utc\":\"2026-02-30T00:00:00.000Z\"}",
    };
    struct sc_ipc_message message;
    for (size_t index = 0; index < sizeof(invalid) / sizeof(invalid[0]);
         ++index) {
        assert(sc_ipc_json_decode((const uint8_t *) invalid[index],
            strlen(invalid[index]), &message) != SC_IPC_JSON_OK);
    }
    const uint8_t bad_utf8[] = {'{', '"', 'x', '"', ':', '"', 0xc3, 0x28, '"', '}'};
    assert(sc_ipc_json_decode(bad_utf8, sizeof(bad_utf8), &message) !=
           SC_IPC_JSON_OK);
    const char *optional = "{\"messageType\":\"command\",\"requestId\":\"3\","
        "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
        "\"command\":\"Фокус\",\"futureNote\":\"supported later\"}";
    assert(sc_ipc_json_decode((const uint8_t *) optional, strlen(optional),
                              &message) == SC_IPC_JSON_OK);
    assert(!strcmp(message.command, "Фокус"));
}

/** Normalizes one foreign frame with the native decoder and encoder. */
static void
normalize(const char *source, const char *target) {
    size_t input_length;
    uint8_t *input = read_file(source, &input_length);
    struct sc_ipc_frame_reader reader;
    sc_ipc_frame_reader_init(&reader);
    size_t consumed, payload_length;
    uint8_t *payload;
    assert(sc_ipc_frame_reader_feed(&reader, input, input_length,
        &consumed, &payload, &payload_length) == SC_IPC_FRAME_READY);
    assert(consumed == input_length);
    struct sc_ipc_message message;
    assert(sc_ipc_json_decode(payload, payload_length, &message) ==
           SC_IPC_JSON_OK);
    uint8_t *encoded;
    size_t encoded_length;
    assert(sc_ipc_json_encode(&message, &encoded, &encoded_length) ==
           SC_IPC_JSON_OK);
    uint8_t *frame;
    size_t frame_length;
    assert(sc_ipc_frame_encode(encoded, encoded_length, &frame,
                               &frame_length) == SC_IPC_FRAME_READY);
    write_file(target, frame, frame_length);
    free(frame);
    free(encoded);
    free(payload);
    free(input);
    sc_ipc_frame_reader_destroy(&reader);
}

/** Runs pure contract tests or the test-only cross-language normalizer. */
int
main(int argc, char **argv) {
    if (argc == 4 && !strcmp(argv[1], "--normalize")) {
        normalize(argv[2], argv[3]);
        return 0;
    }
    assert(argc == 3 || argc == 4);
    assert(!strcmp(argv[1], "--golden"));
    for (size_t index = 0; index < sizeof(golden) / sizeof(golden[0]);
         ++index) {
        check_golden(argv[2], argc == 4 ? argv[3] : NULL, &golden[index]);
    }
    check_frame_edges();
    check_invalid_messages();
    return 0;
}
