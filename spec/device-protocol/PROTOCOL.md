# Android device wire contract

This document describes the shipping Seamless Android control channel inherited
from scrcpy 4.0, including the bounded text validation corrections. It is separate
from the framed Desktop/native IPC under `spec/desktop-native`. It does not
introduce a wire version, negotiate capabilities, or identify a device session.
Protocol SessionId, ConnectionAttemptId, native connection generation, server
scid, media stream identity and OS process/window identity remain distinct.

## Representation and limits

All multibyte integers use big-endian wire order. Signed coordinates retain
their two's-complement bits; 64-bit pointer IDs and clipboard sequences preserve
all bits, including values above signed `long` range. There is no outer control
frame header: the type, fixed fields and payload lengths determine boundaries.
A complete device message consumes its positive byte count; incomplete prefixes
consume zero. Negative C parser results are local malformed/allocation outcomes,
not bytes or status fields sent over the wire.

Text is strict RFC 3629 UTF-8 containing Unicode scalar values and no U+0000.
Empty text is valid. Overlong sequences, surrogate encodings, values above
U+10FFFF, malformed/truncated sequences and embedded NUL are rejected. There is
no normalization or BOM stripping. These restrictions match C string consumers;
binary UHID payloads are unrestricted octets. Java rejects unpaired UTF-16
surrogates and invalid text even beyond a truncated output prefix. Output limits
truncate only at complete scalar boundaries. Rejection precedes writer emission,
so a following valid message is not poisoned by a buffered partial type byte.

The common message ceiling is 262144 bytes. Injected text is at most 300 UTF-8
bytes; set-clipboard text is at most 262130; device clipboard is at most 262139.
Length declarations exceeding these type-specific limits are malformed before
allocation or waiting for that payload. Shorter valid declarations await all
payload bytes. Producer-owned source strings and transport buffering are not
covered by these codec allocation limits.

## Control messages: C serializer to Java reader

Each row starts with a one-byte type. Sizes include that byte. `u8/u16/u32/u64`
are unsigned wire widths, `i32` is a signed coordinate, and `n` is payload size.
The canonical positive vectors cover every type 0 through 21.

| Type | Message and fields after type | Total bytes |
| --- | --- | --- |
| 0 | key: action u8, keycode u32, repeat u32, metastate u32 | 14 |
| 1 | text: length u32, UTF-8 bytes | 5+n |
| 2 | touch: action u8, pointer u64, x/y i32, width/height u16, pressure u16, action-button/buttons u32 | 32 |
| 3 | scroll: x/y i32, width/height u16, horizontal/vertical i16, buttons u32 | 21 |
| 4 | back/screen-on: action u8 | 2 |
| 5 | expand notifications | 1 |
| 6 | expand settings | 1 |
| 7 | collapse panels | 1 |
| 8 | get clipboard: copy-key u8 | 2 |
| 9 | set clipboard: sequence u64, paste u8, length u32, UTF-8 bytes | 14+n |
| 10 | display power: on u8 | 2 |
| 11 | rotate device | 1 |
| 12 | UHID create: id/vendor/product u16, name length u8, UTF-8 name, descriptor length u16, binary descriptor | 10+name+descriptor |
| 13 | UHID input: id u16, length u16, binary report | 5+n |
| 14 | UHID destroy: id u16 | 3 |
| 15 | open hard keyboard settings | 1 |
| 16 | start application: name length u8, UTF-8 name | 2+n |
| 17 | reset video | 1 |
| 18 | torch: enabled u8 | 2 |
| 19 | zoom in | 1 |
| 20 | zoom out | 1 |
| 21 | resize display: width/height u16 | 5 |

Pressure uses unsigned 16-bit fixed point with 1.0 saturated to 65535. Scroll
uses signed 16-bit fixed point scaled by 32768 after the control serializer's
division by 16. The golden cases independently assert endpoint bit patterns.
Native control UHID input storage admits at most 15 report bytes; the Java wire
reader supports a u16 length. The C UHID name producer limit is 127 UTF-8 bytes;
the reader's field is u8. Start-application names are at most 255 UTF-8 bytes.
These asymmetric producer bounds are explicit, not a reason to narrow the wire.

## Device messages: Java writer to C parser

| Type | Message and fields after type | Total bytes |
| --- | --- | --- |
| 0 | clipboard: length u32, UTF-8 bytes | 5+n |
| 1 | clipboard acknowledgement: sequence u64 | 9 |
| 2 | UHID output: id u16, length u16, binary report | 5+n |

UHID output IDs and data must fit u16 before any bytes are emitted. Empty UHID
output is valid; bytes 00/ff/80 remain binary data. Unknown message types are
rejected before writer emission and rejected by readers. The shared vectors
include maximum unsigned sequence values, empty clipboard/report and maximum
payload sizes, not just printable ASCII samples.

## Independent vectors and executed boundary

`golden/control.tsv` and `golden/device.tsv` contain independently authored
literal bytes with semantic IDs. `boundary.tsv` encodes large independent byte
expectations as literal prefix plus repeated octet/count; it is not generated
by either codec. `utf8.tsv` is one shared accepted/rejected scalar list (30
cases). Test-side semantic constructors/assertions are independent of the
production serializers. Expected bytes must not be regenerated from a codec.

`scripts/test-device-protocol.ps1` compiles the actual C `control_msg.c` and
`device_msg.c` with a pure external-effect harness. It emits 30 real C frames
consumed by the real Java ControlMessageReader; Java emits 10 real frames through
DeviceMessageWriter and the actual C parser consumes them. JUnit also checks
finite prefixes, type/length rejection, binary preservation and shared UTF-8.
Both sides check every prefix of ordinary small vectors and representative
header/body/final-byte prefixes of large boundary vectors, plus malformed
declarations, using actual codec buffers. Three Java codec classes must be discovered
with nonzero tests, no failures and no skips; artifact presence is required.
The ordinary native CI job invokes the lane after pinned server bootstrap.

The integrated native parser initially accepted the rejected NUL clipboard
vector: the real harness failed its `consumed < 0` assertion before the guard
was added. Complete bounded clipboard text is now validated before allocation;
incomplete payload still requests more input, and allocation failure remains a
distinct local outcome. The fresh combined lane then passed 30 C-to-Java and
10 Java-to-C frames, 21 Java codec tests and the 30 shared UTF-8 cases. This
establishes codec behavior, not a historical device failure or a hardware pass.

Each invocation uses a new retained evidence directory, hashes direct source,
fixture, tool/config and golden inputs before/after, and refuses overwrite.
An explicit NativeSourceRoot override records a mixed-source codec experiment;
that experiment is not a canonical full build or evidence for unlinked modules.
Gradle tracks golden/actual-frame inputs and fresh output path but does not own
or delete retained cross-language evidence as stale build output.

This lane exercises codecs, not Android controller side effects, physical
devices, clipboard/HID APIs, ADB, media decoding, startup handshakes or actual
transport recovery. JVM EOF exceptions and C incomplete-prefix results are
different API representations, not a demand for identical exception classes.

## Related startup/media source audit

The enabled channel order remains video, audio, then control. Forward setup's
dummy byte is sent only on the first socket; device metadata is a 64-byte,
NUL-padded UTF-8 name (maximum 63 bytes). Codec IDs use four bytes. The current
session video header includes flags plus width/height; encoded media packets
carry timestamp/flag and length fields. These are separate startup/media
contracts and are source-audited rather than exercised by this control-channel
lane. Codec unavailability/fatal-disable markers and media size admission must
retain their own tests. A passing control lane is not complete media-wire or
runtime compatibility certification.

Sources: shipping `app/src/control_msg.c`, `app/src/device_msg.c`,
`server/.../control/ControlMessageReader.java`, `DeviceMessageWriter.java`,
`DesktopConnection.java`, `Streamer.java` and `app/src/demuxer.c`.
The UTF-8 profile follows [RFC 3629](https://www.rfc-editor.org/rfc/rfc3629#section-4).
