#include "machine_stream.h"

void
sc_machine_stream_begin(struct sc_machine_stream *stream, bool reconnecting) {
    stream->presented_this_attempt = false;
    stream->reconnecting = reconnecting;
}

enum sc_machine_stream_event
sc_machine_stream_presented(struct sc_machine_stream *stream) {

    if (stream->presented_this_attempt) {
        return SC_MACHINE_STREAM_NONE;
    }
    stream->presented_this_attempt = true;
    enum sc_machine_stream_event event = stream->presented_ever
        ? SC_MACHINE_STREAM_RESUMED : SC_MACHINE_STREAM_STARTED;
    stream->presented_ever = true;
    return event;
}
