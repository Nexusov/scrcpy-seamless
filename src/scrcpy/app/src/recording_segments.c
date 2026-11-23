#include "recording_segments.h"

#include <inttypes.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { SC_SEGMENT_SUFFIX_CAPACITY = sizeof(".part") + 20 };

static const char *
sc_recording_basename(const char *filename) {
    const char *basename = filename;

    for (const char *cursor = filename; *cursor; ++cursor) {
        if (*cursor == '/' || *cursor == '\\') {
            basename = cursor + 1;
        }
    }

    return basename;
}

bool
sc_recording_segments_init(struct sc_recording_segments *segments,
                            const char *filename) {
    if (!filename) {
        return false;
    }

    const char *basename = sc_recording_basename(filename);

    if (!*basename || !strcmp(basename, ".") || !strcmp(basename, "..")) {
        return false;
    }

    *segments = (struct sc_recording_segments) {
        .filename = filename,
        .next_number = 1,
    };
    return true;
}

bool
sc_recording_segments_prepare(const struct sc_recording_segments *segments,
                               struct sc_recording_segment *segment) {
    if (segments->exhausted) {
        return false;
    }

    const char *basename = sc_recording_basename(segments->filename);
    const char *extension = strrchr(basename, '.');

    if (!extension || extension == basename) {
        extension = segments->filename + strlen(segments->filename);
    }

    char suffix[SC_SEGMENT_SUFFIX_CAPACITY];
    int suffix_length = snprintf(suffix, sizeof(suffix), ".part%02" PRIu64,
                                  segments->next_number);

    if (suffix_length < 0 || (size_t) suffix_length >= sizeof(suffix)) {
        return false;
    }

    size_t filename_length = strlen(segments->filename);

    if (filename_length > SIZE_MAX - (size_t) suffix_length - 1) {
        return false;
    }

    char *path = malloc(filename_length + suffix_length + 1);

    if (!path) {
        return false;
    }

    size_t prefix_length = extension - segments->filename;
    memcpy(path, segments->filename, prefix_length);
    memcpy(path + prefix_length, suffix, suffix_length);
    memcpy(path + prefix_length + suffix_length, extension,
             filename_length - prefix_length + 1);
    *segment = (struct sc_recording_segment) {
        .filename = path,
        .number = segments->next_number,
    };
    return true;
}

bool
sc_recording_segments_commit(struct sc_recording_segments *segments,
                              const struct sc_recording_segment *segment,
                              bool output_opened) {
    if (!output_opened || segments->exhausted
            || segment->number != segments->next_number) {
        return false;
    }

    if (segments->next_number == UINT64_MAX) {
        segments->exhausted = true;
        return true;
    }

    ++segments->next_number;
    return true;
}

void
sc_recording_segment_destroy(struct sc_recording_segment *segment) {
    free(segment->filename);
    segment->filename = NULL;
}
