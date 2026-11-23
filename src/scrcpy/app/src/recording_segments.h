#ifndef SC_RECORDING_SEGMENTS_H
#define SC_RECORDING_SEGMENTS_H

#include "common.h"

#include <stdbool.h>
#include <stdint.h>

/* Invocation owns the immutable filename; sequential retirement owns commits. */
struct sc_recording_segments {
    const char *filename;
    uint64_t next_number;
    bool exhausted;
};

struct sc_recording_segment {
    char *filename;
    uint64_t number;
};

bool
sc_recording_segments_init(struct sc_recording_segments *segments,
                            const char *filename);

/* Preparation does not advance numbering; caller releases the allocated path. */
bool
sc_recording_segments_prepare(const struct sc_recording_segments *segments,
                               struct sc_recording_segment *segment);

/* Call after the exact recorder joins, using its actual output-open receipt. */
bool
sc_recording_segments_commit(struct sc_recording_segments *segments,
                              const struct sc_recording_segment *segment,
                              bool output_opened);

void
sc_recording_segment_destroy(struct sc_recording_segment *segment);

#endif
