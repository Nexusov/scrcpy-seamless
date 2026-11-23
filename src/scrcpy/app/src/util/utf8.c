#include "utf8.h"

/** RFC 3629 scalar restrictions, with the device protocol's NUL-free profile. */
bool
sc_utf8_validate_text(const uint8_t *data, size_t size) {
    size_t position = 0;

    while (position < size) {
        uint8_t first = data[position++];

        if (first && first <= 0x7f) {
            continue;
        }
        unsigned remaining;
        uint8_t second_min = 0x80;
        uint8_t second_max = 0xbf;

        if (first >= 0xc2 && first <= 0xdf) {
            remaining = 1;
        } else if (first >= 0xe0 && first <= 0xef) {
            remaining = 2;

            if (first == 0xe0) {
                second_min = 0xa0;
            }

            if (first == 0xed) {
                second_max = 0x9f;
            }
        } else if (first >= 0xf0 && first <= 0xf4) {
            remaining = 3;

            if (first == 0xf0) {
                second_min = 0x90;
            }

            if (first == 0xf4) {
                second_max = 0x8f;
            }
        } else {
            return false;
        }

        if (remaining > size - position || data[position] < second_min || data[position] > second_max) {
            return false;
        }
        ++position;

        for (unsigned continuation = 1; continuation < remaining; ++continuation) {
            uint8_t value = data[position++];

            if (value < 0x80 || value > 0xbf) {
                return false;
            }
        }
    }
    return true;
}
