#ifndef SC_UTF8_H
#define SC_UTF8_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/** Strict Unicode scalar UTF-8 without embedded NUL; empty text is valid. */
bool
sc_utf8_validate_text(const uint8_t *data, size_t size);

#endif
