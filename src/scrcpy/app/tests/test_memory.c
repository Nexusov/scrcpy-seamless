#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>

#include "util/memory.h"

// Confirm the allocator rejects multiplication overflow before malloc.
int
main(void) {
    errno = 0;
    assert(!sc_allocarray(SIZE_MAX, 2));
    assert(errno == ENOMEM);

    void *buffer = sc_allocarray(1024, 8);
    assert(buffer);
    free(buffer);
    return 0;
}
