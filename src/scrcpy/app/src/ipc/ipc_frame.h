#ifndef SC_IPC_FRAME_H
#define SC_IPC_FRAME_H

#include <stddef.h>
#include <stdint.h>

#define SC_IPC_MAX_PAYLOAD 1048576u

enum sc_ipc_frame_status {
    SC_IPC_FRAME_MORE,
    SC_IPC_FRAME_READY,
    SC_IPC_FRAME_INVALID_LENGTH,
    SC_IPC_FRAME_TRUNCATED_HEADER,
    SC_IPC_FRAME_TRUNCATED_PAYLOAD,
    SC_IPC_FRAME_NO_MEMORY,
};

struct sc_ipc_frame_reader {
    uint8_t header[4];
    size_t header_size;
    uint8_t *payload;
    size_t payload_size;
    size_t payload_length;
    enum sc_ipc_frame_status failure;
};

/** Initializes a pure frame reader without touching process I/O. */
void sc_ipc_frame_reader_init(struct sc_ipc_frame_reader *reader);

/** Frees an incomplete payload and resets the reader. */
void sc_ipc_frame_reader_destroy(struct sc_ipc_frame_reader *reader);

/** Consumes up to one frame and transfers completed payload ownership. */
enum sc_ipc_frame_status
sc_ipc_frame_reader_feed(struct sc_ipc_frame_reader *reader,
                         const uint8_t *data, size_t data_size,
                         size_t *consumed, uint8_t **payload,
                         size_t *payload_length);

/** Reports clean EOF or a truncated header/body. */
enum sc_ipc_frame_status
sc_ipc_frame_reader_eof(const struct sc_ipc_frame_reader *reader);

/** Allocates one header plus payload; caller frees the output. */
enum sc_ipc_frame_status
sc_ipc_frame_encode(const uint8_t *payload, size_t payload_length,
                    uint8_t **frame, size_t *frame_length);

#endif
