#ifndef SC_MACHINE_STREAM_H
#define SC_MACHINE_STREAM_H

#include <stdbool.h>

enum sc_machine_stream_event {
    SC_MACHINE_STREAM_NONE,
    SC_MACHINE_STREAM_STARTED,
    SC_MACHINE_STREAM_RESUMED,
};

struct sc_machine_stream {
    bool presented_this_attempt;
    bool presented_ever;
    bool reconnecting;
};

void
sc_machine_stream_begin(struct sc_machine_stream *stream, bool reconnecting);

enum sc_machine_stream_event
sc_machine_stream_presented(struct sc_machine_stream *stream);

#endif
