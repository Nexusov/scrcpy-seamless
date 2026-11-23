#include "common.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "control_msg.h"
#include "device_msg.h"
#include "util/utf8.h"

#ifdef NDEBUG
# error Device protocol fixtures require active assertions
#endif

enum {
    WIRE_MAX = 262144,
    CASE_LIMIT = 64,
    ID_CAPACITY = 64,
    PATH_CAPACITY = 4096,
    LINE_CAPACITY = WIRE_MAX * 2 + ID_CAPACITY + 4,
    CONTROL_CASES = 30,
    DEVICE_CASES = 10,
    FULL_PREFIX_LIMIT = 4096,
};

static const uint64_t sequence_bits = UINT64_C(0xfedcba9876543210);
static const char unicode_text[] = "A\xe2\x82\xac";
static const uint8_t hid_data[] = {0, 0xff, 0x80};
static const uint8_t hid_descriptor[] = {5, 1, 9};

struct frame {
    char id[ID_CAPACITY];
    uint8_t *data;
    size_t size;
    size_t prefix_size;
};

struct vectors {
    struct frame items[CASE_LIMIT];
    size_t count;
};

static unsigned
hex_digit(char character) {

    if (character >= '0' && character <= '9') {
        return character - '0';
    }

    if (character >= 'a' && character <= 'f') {
        return character - 'a' + 10;
    }

    if (character >= 'A' && character <= 'F') {
        return character - 'A' + 10;
    }
    assert(!"Invalid fixture hexadecimal digit");
    return 0;
}

static struct frame *
find_frame(struct vectors *vectors, const char *id) {
    for (size_t index = 0; index < vectors->count; ++index) {

        if (!strcmp(vectors->items[index].id, id)) {
            return &vectors->items[index];
        }
    }
    return NULL;
}

/** Expand literal fixture bytes, never bytes produced by a codec under test. */
static void
add_frame(struct vectors *vectors, const char *id, const char *hex,
          const char *repeat_hex, size_t repeat_count) {
    assert(vectors->count < CASE_LIMIT);
    assert(strlen(id) < ID_CAPACITY && !find_frame(vectors, id));
    size_t hex_size = strlen(hex);
    assert(!(hex_size % 2));
    size_t prefix = hex_size / 2;
    assert(prefix <= WIRE_MAX && repeat_count <= WIRE_MAX - prefix);
    struct frame *frame = &vectors->items[vectors->count++];
    memcpy(frame->id, id, strlen(id) + 1);
    frame->size = prefix + repeat_count;
    frame->prefix_size = prefix;
    assert(frame->size);
    frame->data = malloc(frame->size);
    assert(frame->data);

    for (size_t index = 0; index < prefix; ++index) {
        frame->data[index] = (hex_digit(hex[index * 2]) << 4)
                          | hex_digit(hex[index * 2 + 1]);
    }

    if (repeat_count) {
        assert(repeat_hex && strlen(repeat_hex) == 2);
        unsigned value = (hex_digit(repeat_hex[0]) << 4)
                       | hex_digit(repeat_hex[1]);
        memset(frame->data + prefix, value, repeat_count);
    }
}

/** Read only the bounded TSV fixture transport, not the device wire protocol. */
static void
load_file(struct vectors *vectors, const char *directory, const char *name,
          const char *direction, bool boundary) {
    char path[PATH_CAPACITY];
    int count = directory ? snprintf(path, sizeof(path), "%s/%s", directory, name)
                          : snprintf(path, sizeof(path), "%s", name);
    assert(count >= 0 && (size_t) count < sizeof(path));
    FILE *input = fopen(path, "rb");
    assert(input);
    char *line = malloc(LINE_CAPACITY);
    assert(line);

    while (fgets(line, LINE_CAPACITY, input)) {
        line[strcspn(line, "\r\n")] = '\0';

        if (!*line || *line == '#') {
            continue;
        }
        char *fields[5];
        size_t field_count = 0;
        char *field = strtok(line, "\t");

        while (field) {
            assert(field_count < ARRAY_LEN(fields));
            fields[field_count++] = field;
            field = strtok(NULL, "\t");
        }
        assert(field_count == (boundary ? 5 : 2));

        if (!boundary) {
            add_frame(vectors, fields[0], fields[1], NULL, 0);
            continue;
        }

        if (!strcmp(fields[0], direction)) {
            char *end;
            unsigned long repetitions = strtoul(fields[4], &end, 10);
            assert(!*end && repetitions <= WIRE_MAX);
            add_frame(vectors, fields[1], fields[2], fields[3], repetitions);
        }
    }
    assert(!ferror(input));
    free(line);
    fclose(input);
}

static struct vectors
load_vectors(const char *directory, const char *direction) {
    struct vectors vectors = {0};
    char name[ID_CAPACITY];
    int count = snprintf(name, sizeof(name), "%s.tsv", direction);
    assert(count > 0 && (size_t) count < sizeof(name));
    load_file(&vectors, directory, name, direction, false);
    load_file(&vectors, directory, "boundary.tsv", direction, true);
    return vectors;
}

static void
destroy_vectors(struct vectors *vectors) {
    for (size_t index = 0; index < vectors->count; ++index) {
        free(vectors->items[index].data);
    }
}

static char *
repeated_text(size_t count, bool append_euro) {
    size_t suffix = append_euro ? 3 : 0;
    char *text = malloc(count + suffix + 1);
    assert(text);
    memset(text, 'x', count);

    if (append_euro) {
        memcpy(text + count, "\xe2\x82\xac", suffix);
    }
    text[count + suffix] = '\0';
    return text;
}

static enum sc_control_msg_type
control_type(const char *id) {
    static const struct {
        const char *id;
        enum sc_control_msg_type type;
    } cases[] = {
        {"keycode", SC_CONTROL_MSG_TYPE_INJECT_KEYCODE},
        {"text", SC_CONTROL_MSG_TYPE_INJECT_TEXT},
        {"text-empty", SC_CONTROL_MSG_TYPE_INJECT_TEXT},
        {"text-max", SC_CONTROL_MSG_TYPE_INJECT_TEXT},
        {"text-utf8-cut", SC_CONTROL_MSG_TYPE_INJECT_TEXT},
        {"touch", SC_CONTROL_MSG_TYPE_INJECT_TOUCH_EVENT},
        {"touch-pressure-one", SC_CONTROL_MSG_TYPE_INJECT_TOUCH_EVENT},
        {"scroll", SC_CONTROL_MSG_TYPE_INJECT_SCROLL_EVENT},
        {"back", SC_CONTROL_MSG_TYPE_BACK_OR_SCREEN_ON},
        {"expand-notifications", SC_CONTROL_MSG_TYPE_EXPAND_NOTIFICATION_PANEL},
        {"expand-settings", SC_CONTROL_MSG_TYPE_EXPAND_SETTINGS_PANEL},
        {"collapse-panels", SC_CONTROL_MSG_TYPE_COLLAPSE_PANELS},
        {"get-clipboard", SC_CONTROL_MSG_TYPE_GET_CLIPBOARD},
        {"set-clipboard", SC_CONTROL_MSG_TYPE_SET_CLIPBOARD},
        {"set-clipboard-empty", SC_CONTROL_MSG_TYPE_SET_CLIPBOARD},
        {"set-clipboard-max", SC_CONTROL_MSG_TYPE_SET_CLIPBOARD},
        {"set-clipboard-utf8-cut", SC_CONTROL_MSG_TYPE_SET_CLIPBOARD},
        {"display-power", SC_CONTROL_MSG_TYPE_SET_DISPLAY_POWER},
        {"display-power-off", SC_CONTROL_MSG_TYPE_SET_DISPLAY_POWER},
        {"rotate", SC_CONTROL_MSG_TYPE_ROTATE_DEVICE},
        {"uhid-create", SC_CONTROL_MSG_TYPE_UHID_CREATE},
        {"uhid-input", SC_CONTROL_MSG_TYPE_UHID_INPUT},
        {"uhid-destroy", SC_CONTROL_MSG_TYPE_UHID_DESTROY},
        {"keyboard-settings", SC_CONTROL_MSG_TYPE_OPEN_HARD_KEYBOARD_SETTINGS},
        {"start-app", SC_CONTROL_MSG_TYPE_START_APP},
        {"reset-video", SC_CONTROL_MSG_TYPE_RESET_VIDEO},
        {"camera-torch", SC_CONTROL_MSG_TYPE_CAMERA_SET_TORCH},
        {"camera-zoom-in", SC_CONTROL_MSG_TYPE_CAMERA_ZOOM_IN},
        {"camera-zoom-out", SC_CONTROL_MSG_TYPE_CAMERA_ZOOM_OUT},
        {"resize-display", SC_CONTROL_MSG_TYPE_RESIZE_DISPLAY},
    };

    for (size_t index = 0; index < ARRAY_LEN(cases); ++index) {

        if (!strcmp(id, cases[index].id)) {
            return cases[index].type;
        }
    }
    assert(!"Unknown semantic control vector");
    return 0;
}

static struct sc_control_msg
control_message(const char *id) {
    struct sc_control_msg message = {.type = control_type(id)};
    switch (message.type) {
        case SC_CONTROL_MSG_TYPE_INJECT_KEYCODE:
            message.inject_keycode.action = AKEY_EVENT_ACTION_UP;
            message.inject_keycode.keycode = AKEYCODE_N;
            message.inject_keycode.repeat = UINT32_C(0x01020304);
            message.inject_keycode.metastate = (enum android_metastate) 0x11223344;
            break;
        case SC_CONTROL_MSG_TYPE_INJECT_TEXT:

            if (!strcmp(id, "text-max")) {
                message.inject_text.text = repeated_text(300, false);
            } else if (!strcmp(id, "text-utf8-cut")) {
                message.inject_text.text = repeated_text(299, true);
            } else {
                message.inject_text.text = strdup(!strcmp(id, "text-empty") ? "" : unicode_text);
            }
            assert(message.inject_text.text);
            break;
        case SC_CONTROL_MSG_TYPE_INJECT_TOUCH_EVENT:
            message.inject_touch_event.action = AMOTION_EVENT_ACTION_MOVE;
            message.inject_touch_event.pointer_id = sequence_bits;
            message.inject_touch_event.position = (struct sc_position) {
                .point = {-123, 456}, .screen_size = {1080, 1920},
            };
            message.inject_touch_event.pressure = !strcmp(id, "touch") ? 0.5f : 1.0f;
            message.inject_touch_event.action_button = AMOTION_EVENT_BUTTON_PRIMARY;
            message.inject_touch_event.buttons = (enum android_motionevent_buttons) 5;
            break;
        case SC_CONTROL_MSG_TYPE_INJECT_SCROLL_EVENT:
            message.inject_scroll_event.position = (struct sc_position) {
                .point = {42, 99}, .screen_size = {800, 600},
            };
            message.inject_scroll_event.hscroll = -16.0f;
            message.inject_scroll_event.vscroll = 8.0f;
            message.inject_scroll_event.buttons = AMOTION_EVENT_BUTTON_PRIMARY;
            break;
        case SC_CONTROL_MSG_TYPE_GET_CLIPBOARD:
            message.get_clipboard.copy_key = SC_COPY_KEY_CUT;
            break;
        case SC_CONTROL_MSG_TYPE_SET_CLIPBOARD:
            message.set_clipboard.sequence = !strcmp(id, "set-clipboard-empty") ? 0 : sequence_bits;
            message.set_clipboard.paste = !!message.set_clipboard.sequence;

            if (!strcmp(id, "set-clipboard-max")) {
                message.set_clipboard.text = repeated_text(262130, false);
            } else if (!strcmp(id, "set-clipboard-utf8-cut")) {
                message.set_clipboard.text = repeated_text(262129, true);
            } else {
                message.set_clipboard.text = strdup(message.set_clipboard.sequence ? unicode_text : "");
            }
            assert(message.set_clipboard.text);
            break;
        case SC_CONTROL_MSG_TYPE_SET_DISPLAY_POWER:
            message.set_display_power.on = !strcmp(id, "display-power");
            break;
        case SC_CONTROL_MSG_TYPE_UHID_CREATE:
            message.uhid_create.id = 0x1234;
            message.uhid_create.vendor_id = 0x0abc;
            message.uhid_create.product_id = 0x00ff;
            message.uhid_create.name = "U";
            message.uhid_create.report_desc = hid_descriptor;
            message.uhid_create.report_desc_size = sizeof(hid_descriptor);
            break;
        case SC_CONTROL_MSG_TYPE_UHID_INPUT:
            message.uhid_input.id = 0x1234;
            message.uhid_input.size = sizeof(hid_data);
            memcpy(message.uhid_input.data, hid_data, sizeof(hid_data));
            break;
        case SC_CONTROL_MSG_TYPE_UHID_DESTROY:
            message.uhid_destroy.id = 0x1234;
            break;
        case SC_CONTROL_MSG_TYPE_START_APP:
            message.start_app.name = strdup("org.example.demo");
            assert(message.start_app.name);
            break;
        case SC_CONTROL_MSG_TYPE_CAMERA_SET_TORCH:
            message.camera_set_torch.on = true;
            break;
        case SC_CONTROL_MSG_TYPE_RESIZE_DISPLAY:
            message.resize_display.width = 2560;
            message.resize_display.height = 1440;
            break;
        default:
            // The remaining audited types have no payload, or the zero BACK action.
            break;
    }
    return message;
}

static void
write_frame(FILE *output, const char *id, const uint8_t *data, size_t size) {
    assert(fprintf(output, "%s\t", id) > 0);

    for (size_t index = 0; index < size; ++index) {
        assert(fprintf(output, "%02x", data[index]) == 2);
    }
    assert(fputc('\n', output) != EOF);
}

static void
check_controls(struct vectors *vectors, FILE *output) {
    assert(vectors->count == CONTROL_CASES);
    uint8_t *wire = malloc(SC_CONTROL_MSG_MAX_SIZE);
    assert(wire);

    for (size_t index = 0; index < vectors->count; ++index) {
        struct frame *expected = &vectors->items[index];
        struct sc_control_msg message = control_message(expected->id);
        size_t size = sc_control_msg_serialize(&message, wire);
        assert(size == expected->size && !memcmp(wire, expected->data, size));

        if (output) {
            write_frame(output, expected->id, wire, size);
        }
        sc_control_msg_destroy(&message);
    }
    struct sc_control_msg unknown = {.type = (enum sc_control_msg_type) 255};
    assert(sc_control_msg_serialize(&unknown, wire) == 0);
    free(wire);
}

static void
assert_repeated(const char *text, size_t size) {
    assert(strlen(text) == size);

    for (size_t index = 0; index < size; ++index) {
        assert(text[index] == 'x');
    }
}

static void
assert_device(const char *id, const struct sc_device_msg *message) {

    if (!strncmp(id, "clipboard", strlen("clipboard"))) {
        assert(message->type == DEVICE_MSG_TYPE_CLIPBOARD);

        if (!strcmp(id, "clipboard")) {
            assert(!strcmp(message->clipboard.text, unicode_text));
        } else if (!strcmp(id, "clipboard-empty")) {
            assert(!strcmp(message->clipboard.text, ""));
        } else {
            assert_repeated(message->clipboard.text, !strcmp(id, "clipboard-max") ? 262139 : 262138);
        }
        return;
    }

    if (!strncmp(id, "ack", strlen("ack"))) {
        assert(message->type == DEVICE_MSG_TYPE_ACK_CLIPBOARD);
        uint64_t expected = !strcmp(id, "ack") ? sequence_bits
                          : !strcmp(id, "ack-zero") ? 0 : UINT64_MAX;
        assert(message->ack_clipboard.sequence == expected);
        return;
    }
    assert(message->type == DEVICE_MSG_TYPE_UHID_OUTPUT);
    bool maximum = !strcmp(id, "uhid-output-max");
    size_t size = !strcmp(id, "uhid-output-empty") ? 0 : maximum ? 65535 : sizeof(hid_data);
    assert(message->uhid_output.id == (maximum ? 65535 : 0x1234));
    assert(message->uhid_output.size == size);

    for (size_t index = 0; index < size; ++index) {
        assert(message->uhid_output.data[index] == (maximum ? 0xa5 : hid_data[index]));
    }
}

static void
check_device_frame(const struct frame *frame) {
    struct sc_device_msg message;
    ssize_t consumed = sc_device_msg_deserialize(frame->data, frame->size, &message);
    assert(consumed > 0 && (size_t) consumed == frame->size);
    assert_device(frame->id, &message);
    sc_device_msg_destroy(&message);

    if (frame->size < FULL_PREFIX_LIMIT) {

        for (size_t length = 0; length < frame->size; ++length) {
            assert(sc_device_msg_deserialize(frame->data, length, &message) == 0);
        }
        return;
    }
    size_t prefixes[] = {0, 1, frame->prefix_size - 1, frame->prefix_size, frame->size - 1};

    for (size_t index = 0; index < ARRAY_LEN(prefixes); ++index) {
        assert(sc_device_msg_deserialize(frame->data, prefixes[index], &message) == 0);
    }
}

static void
check_device_errors(void) {
    static const uint8_t unknown[] = {255};
    static const uint8_t oversized[] = {0, 0, 4, 0, 0};
    static const uint8_t unsigned_max[] = {0, 255, 255, 255, 255};
    struct sc_device_msg message;
    assert(sc_device_msg_deserialize(unknown, sizeof(unknown), &message) < 0);
    assert(sc_device_msg_deserialize(oversized, sizeof(oversized), &message) < 0);
    assert(sc_device_msg_deserialize(unsigned_max, sizeof(unsigned_max), &message) < 0);
}

static void
check_devices(struct vectors *vectors) {
    assert(vectors->count == DEVICE_CASES);
    size_t total = 0;

    for (size_t index = 0; index < vectors->count; ++index) {
        check_device_frame(&vectors->items[index]);
        total += vectors->items[index].size;
    }
    uint8_t *joined = malloc(total);
    assert(joined);
    size_t position = 0;

    for (size_t index = 0; index < vectors->count; ++index) {
        struct frame *frame = &vectors->items[index];
        memcpy(joined + position, frame->data, frame->size);
        position += frame->size;
    }
    position = 0;

    for (size_t index = 0; index < vectors->count; ++index) {
        struct sc_device_msg message;
        ssize_t consumed = sc_device_msg_deserialize(joined + position, total - position, &message);
        assert(consumed > 0 && (size_t) consumed == vectors->items[index].size);
        assert_device(vectors->items[index].id, &message);
        sc_device_msg_destroy(&message);
        position += consumed;
    }
    assert(position == total);
    free(joined);
    check_device_errors();
}

static void
consume_java(struct vectors *goldens, const char *path) {
    struct vectors actual = {0};
    load_file(&actual, NULL, path, "device", false);
    assert(actual.count == goldens->count);

    for (size_t index = 0; index < actual.count; ++index) {
        struct frame *frame = &actual.items[index];
        struct frame *expected = find_frame(goldens, frame->id);
        assert(expected && frame->size == expected->size);
        assert(!memcmp(frame->data, expected->data, frame->size));
        frame->prefix_size = expected->prefix_size;
        check_device_frame(frame);
    }
    destroy_vectors(&actual);
}

static void
check_utf8(const char *directory, bool check_parser) {
    char path[PATH_CAPACITY];
    int path_size = snprintf(path, sizeof(path), "%s/utf8.tsv", directory);
    assert(path_size > 0 && (size_t) path_size < sizeof(path));
    FILE *input = fopen(path, "rb");
    assert(input);
    char line[PATH_CAPACITY];
    size_t cases = 0;

    while (fgets(line, sizeof(line), input)) {
        line[strcspn(line, "\r\n")] = '\0';

        if (!*line || *line == '#') {
            continue;
        }
        char *outcome = strtok(line, "\t");
        char *id = strtok(NULL, "\t");
        char *hex = strtok(NULL, "\t");
        assert(outcome && id && hex && !strtok(NULL, "\t"));
        assert(!strcmp(outcome, "accept") || !strcmp(outcome, "reject"));
        bool accepted = !strcmp(outcome, "accept");
        uint8_t payload[ID_CAPACITY];
        size_t size = !strcmp(hex, "-") ? 0 : strlen(hex) / 2;
        assert(size <= sizeof(payload));

        for (size_t index = 0; index < size; ++index) {
            payload[index] = (hex_digit(hex[index * 2]) << 4)
                           | hex_digit(hex[index * 2 + 1]);
        }
        assert(sc_utf8_validate_text(payload, size) == accepted);

        if (check_parser) {
            uint8_t wire[5 + sizeof(payload)] = {0};
            wire[4] = size;
            memcpy(wire + 5, payload, size);
            struct sc_device_msg message;
            ssize_t consumed = sc_device_msg_deserialize(wire, size + 5, &message);

            if (accepted) {
                assert(consumed == (ssize_t) (size + 5));
                assert(!memcmp(message.clipboard.text, payload, size));
                assert(message.clipboard.text[size] == '\0');
                sc_device_msg_destroy(&message);
            } else {
                fprintf(stderr, "Checking rejected UTF-8/NUL wire case: %s\n", id);
                assert(consumed < 0);
            }
        }
        ++cases;
    }
    assert(!ferror(input) && cases == 30);
    fclose(input);
    printf("C UTF-8 profile: %zu shared scalar cases%s\n", cases,
           check_parser ? " through actual device parser" : " through validator");
}

int
main(int argument_count, char **arguments) {
    assert(argument_count >= 3);
    const char *mode = arguments[1];
    struct vectors control = load_vectors(arguments[2], "control");
    struct vectors device = load_vectors(arguments[2], "device");
    assert(SC_CONTROL_MSG_MAX_SIZE == WIRE_MAX && DEVICE_MSG_MAX_SIZE == WIRE_MAX);

    if (!strcmp(mode, "--utf8")) {
        assert(argument_count == 3);
        check_utf8(arguments[2], false);
    } else if (!strcmp(mode, "--selftest")) {
        assert(argument_count == 3);
        check_controls(&control, NULL);
        check_devices(&device);
        check_utf8(arguments[2], true);
        puts("C device protocol: 30 control + 10 device goldens; finite prefixes and malformed declarations passed");
    } else if (!strcmp(mode, "--emit-control")) {
        assert(argument_count == 4);
        FILE *output = fopen(arguments[3], "wb");
        assert(output);
        check_controls(&control, output);
        assert(!fclose(output));
        puts("Actual C serializer: 30 control frames emitted");
    } else {
        assert(!strcmp(mode, "--consume-device") && argument_count == 4);
        consume_java(&device, arguments[3]);
        puts("Actual Java -> C parser: 10 device frames passed");
    }
    destroy_vectors(&control);
    destroy_vectors(&device);
    return 0;
}
