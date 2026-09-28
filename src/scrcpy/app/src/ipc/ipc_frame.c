#include "ipc_frame.h"

#include <stdlib.h>
#include <string.h>

/** Initializes a pure frame reader without touching process I/O. */
void
sc_ipc_frame_reader_init(struct sc_ipc_frame_reader *reader) {
    memset(reader, 0, sizeof(*reader));
}

/** Frees an incomplete payload and resets the reader. */
void
sc_ipc_frame_reader_destroy(struct sc_ipc_frame_reader *reader) {
    free(reader->payload);
    sc_ipc_frame_reader_init(reader);
}

/** Consumes up to one frame and transfers completed payload ownership. */
enum sc_ipc_frame_status
sc_ipc_frame_reader_feed(struct sc_ipc_frame_reader *reader,
                         const uint8_t *data, size_t data_size,
                         size_t *consumed, uint8_t **payload,
                         size_t *payload_length) {
    *consumed = 0;
    *payload = NULL;
    *payload_length = 0;

    if (reader->failure != SC_IPC_FRAME_MORE) {
        return reader->failure;
    }

    if (data_size && !data) {
        reader->failure = SC_IPC_FRAME_INVALID_LENGTH;
        return reader->failure;
    }

    while (*consumed < data_size) {
        if (reader->header_size < sizeof(reader->header)) {
            size_t remaining = sizeof(reader->header) - reader->header_size;
            size_t available = data_size - *consumed;
            size_t count = remaining < available ? remaining : available;
            memcpy(reader->header + reader->header_size, data + *consumed,
                   count);
            reader->header_size += count;
            *consumed += count;

            if (reader->header_size < sizeof(reader->header)) {
                return SC_IPC_FRAME_MORE;
            }

            uint32_t length = (uint32_t) reader->header[0]
                    | ((uint32_t) reader->header[1] << 8)
                    | ((uint32_t) reader->header[2] << 16)
                    | ((uint32_t) reader->header[3] << 24);
            if (!length || length > SC_IPC_MAX_PAYLOAD) {
                reader->failure = SC_IPC_FRAME_INVALID_LENGTH;
                return reader->failure;
            }

            reader->payload = malloc(length);
            if (!reader->payload) {
                reader->failure = SC_IPC_FRAME_NO_MEMORY;
                return reader->failure;
            }
            reader->payload_length = length;
        }

        size_t remaining = reader->payload_length - reader->payload_size;
        size_t available = data_size - *consumed;
        size_t count = remaining < available ? remaining : available;
        memcpy(reader->payload + reader->payload_size, data + *consumed,
               count);
        reader->payload_size += count;
        *consumed += count;

        if (reader->payload_size == reader->payload_length) {
            *payload = reader->payload;
            *payload_length = reader->payload_length;
            reader->payload = NULL;
            reader->payload_size = 0;
            reader->payload_length = 0;
            reader->header_size = 0;
            return SC_IPC_FRAME_READY;
        }
    }

    return SC_IPC_FRAME_MORE;
}

/** Reports clean EOF or a truncated header/body. */
enum sc_ipc_frame_status
sc_ipc_frame_reader_eof(const struct sc_ipc_frame_reader *reader) {
    if (reader->failure != SC_IPC_FRAME_MORE) {
        return reader->failure;
    }

    if (!reader->header_size) {
        return SC_IPC_FRAME_READY;
    }

    return reader->header_size < sizeof(reader->header)
            ? SC_IPC_FRAME_TRUNCATED_HEADER : SC_IPC_FRAME_TRUNCATED_PAYLOAD;
}

/** Allocates one header plus payload; caller frees the output. */
enum sc_ipc_frame_status
sc_ipc_frame_encode(const uint8_t *payload, size_t payload_length,
                    uint8_t **frame, size_t *frame_length) {
    *frame = NULL;
    *frame_length = 0;

    if (!payload || !payload_length || payload_length > SC_IPC_MAX_PAYLOAD) {
        return SC_IPC_FRAME_INVALID_LENGTH;
    }

    size_t length = payload_length + 4;
    uint8_t *bytes = malloc(length);
    if (!bytes) {
        return SC_IPC_FRAME_NO_MEMORY;
    }

    uint32_t payload_size = (uint32_t) payload_length;
    bytes[0] = (uint8_t) payload_size;
    bytes[1] = (uint8_t) (payload_size >> 8);
    bytes[2] = (uint8_t) (payload_size >> 16);
    bytes[3] = (uint8_t) (payload_size >> 24);
    memcpy(bytes + 4, payload, payload_length);
    *frame = bytes;
    *frame_length = length;
    return SC_IPC_FRAME_READY;
}
