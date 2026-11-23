#include <assert.h>
#include <stdio.h>
#include "ipc/machine_stream.h"

int main(void) {
    struct sc_machine_stream stream = {0};
    sc_machine_stream_begin(&stream, false);
    /* Two earlier attempts acquired no presented video. */
    sc_machine_stream_begin(&stream, true);
    sc_machine_stream_begin(&stream, true);
    assert(sc_machine_stream_presented(&stream) == SC_MACHINE_STREAM_STARTED);
    assert(sc_machine_stream_presented(&stream) == SC_MACHINE_STREAM_NONE);
    sc_machine_stream_begin(&stream, true);
    assert(sc_machine_stream_presented(&stream) == SC_MACHINE_STREAM_RESUMED);
    assert(sc_machine_stream_presented(&stream) == SC_MACHINE_STREAM_NONE);
    struct sc_machine_stream initial = {0};
    sc_machine_stream_begin(&initial, false);
    assert(sc_machine_stream_presented(&initial) == SC_MACHINE_STREAM_STARTED);
    assert(sc_machine_stream_presented(&initial) == SC_MACHINE_STREAM_NONE);
    puts("Machine stream: first presentation, resumed presentation and coalescing passed");
    return 0;
}
