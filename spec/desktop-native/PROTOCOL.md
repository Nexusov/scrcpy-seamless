# Desktop/native machine protocol v1

Status: Phase 6A wire contract and codec tests. No application process uses
this route yet. This contract is separate from the Android device protocol,
saved configuration and product version.

## Transport and limits

The future Desktop parent writes commands to redirected native stdin. Native
stdout contains only frames after explicit machine-mode activation; human
diagnostics go to stderr, drained independently. Each frame is a four-byte
**unsigned little-endian** payload byte length followed by that many UTF-8
JSON bytes. The header is not part of the length. The default and v1 maximum
payload is 1,048,576 bytes; zero and larger lengths are invalid. A reader
validates the header before allocating the body. A complete frame may be split
at any byte; consecutive frames may be coalesced. EOF between frames is clean;
EOF in a header or payload is truncated input. Oversized input is a protocol
failure, not an instruction to allocate or skip its claimed length. Stream
cancellation is separate from EOF and from the future child-stop policy.

JSON is strict RFC 8259 UTF-8, with no BOM or trailing non-whitespace data.
The root is an object of at most 32 unique properties, with at most two levels
(the root and flat capability arrays), 256 UTF-8 bytes per ordinary string,
64 bytes per capability, and at most 16 distinct capabilities per array.
Unknown optional root fields with bounded scalar values are ignored for a
same-major peer. Nested unknown objects/arrays and duplicate keys, including
unknown duplicates, are rejected. Known fields with missing, null or wrong
types are rejected unless the table explicitly permits null. Parsers must
reject embedded NUL and invalid UTF-8; diagnostics contain codes and bounded
field names, never a payload dump or secrets.

All names and enum tokens below are case-sensitive. Canonical encoders emit
compact JSON in table order and lowercase GUIDs; decoders accept property
reordering. `requestId`, `sequence` and `monotonicMicroseconds` are canonical
base-10 **strings** with no sign or leading zeros. They represent unsigned
64-bit integers; `requestId` and `sequence` start at 1, while monotonic time
may be 0. This preserves all 64 bits across C and C# regardless of a JSON
library's floating-point number representation. `sessionId` and
`connectionAttemptId` are lowercase canonical nonzero GUID D strings.
`utc` is exactly `YYYY-MM-DDTHH:mm:ss.fffZ`, a valid UTC calendar timestamp.
Monotonic microseconds are relative to the emitting native process start;
UTC alone cannot order events across processes. Sequence orders only that
process's machine stream and must not reset on reconnect.

## Messages

| Type/direction | Required fields in canonical order | Nullable fields | Meaning |
| --- | --- | --- | --- |
| `hello` Desktop → native, first frame | `messageType`, `product`, `protocolMajor`, `protocolMinor`, `requiredCapabilities`, `supportedCapabilities` | none | Parent proposes protocol and capability requirements. |
| `helloResult` native → Desktop, first response | `messageType`, `product`, `protocolMajor`, `protocolMinor`, `status`, `capabilities` | none | Native accepts or states a typed mismatch. |
| `command` Desktop → native | `messageType`, `requestId`, `sessionId`, `command` | none | One named request. |
| `commandResult` native → Desktop | `messageType`, `requestId`, `sessionId`, `command`, `status` | none | Correlates with exactly one request; not a process-exit receipt. |
| `lifecycle` native → Desktop | `messageType`, `sequence`, `utc`, `monotonicMicroseconds`, `sessionId`, `connectionAttemptId`, `subsystem`, `eventType`, `reason`, `error` | `connectionAttemptId` only | An observation at the stated native emission boundary. |

`product` is `scrcpy-seamless`; protocol version is major 1, minor 0. Version
fields are nonnegative JSON integers in 0..65535, independent of product and
configuration versions. Capabilities in v1 are `stop`, `focus-window` and
`lifecycle-v1`; strings are sorted ordinally in canonical output. Native
accepts the same major if all required capabilities are supported, ignores
unknown *optional* supported capabilities, and negotiates the smaller minor.
Every required capability must also appear in `supportedCapabilities`;
otherwise the `hello` is malformed and is rejected before negotiation.
`helloResult.status` is one of `accepted`, `productMismatch`,
`majorMismatch`, `requiredCapabilityMissing`; on rejection `capabilities` is
empty and no command may be executed. A major/product/required-capability
mismatch is explicit, not a silent fallback. Before successful handshake only
`hello` is legal. A duplicate `hello` afterward is a protocol error.
Unexpected `helloResult`, command or lifecycle direction is a protocol error.

`command` is `Stop` or `FocusWindow`. An unknown but bounded command token is
decoded so the native dispatcher can return `unsupportedCommand`; no generic
shell/ADB/CLI tunnel is implied. `requestId` is unique among in-flight
commands and a duplicate in-flight ID fails. Result `requestId`, `sessionId`
and `command` must match the original. `commandResult.status` is `accepted`,
`applied`, `unsupportedCommand`, `invalidState`, `noWindow` or `failed`.
`accepted` means a Stop request was accepted for work, **not** that all workers
or the process have exited. `applied` means the FocusWindow attempt ran on
the native main thread, **not** that Windows granted foreground focus.
`Stop` is valid in ready/connecting/active/reconnecting states and becomes
idempotent while stopping; `FocusWindow` is valid only while a presentation
window exists, otherwise `noWindow`. Commands after terminal stop fail with
`invalidState`. The concrete state dispatcher is Phase 6B.

Lifecycle `eventType` is one of `NativeReady`, `Connecting`, `StreamStarted`,
`TransportLost`, `ReconnectScheduled`, `Reconnecting`, `StreamResumed`,
`CapabilityDegraded`, `SessionStopped`, `FatalError`. `subsystem` is one of
`protocol`, `native`, `connection`, `video`, `audio`, `control`. `reason` is
one of `none`, `userStop`, `windowClosed`, `transportLost`, `protocolError`,
`nativeFailure`, `unknown`. `error` is one of `none`, `unknown`,
`invalidMessage`, `unsupported`, `internalFailure`. `unknown` or
`unsupported` is an explicit limit of evidence, never a success assertion.
`Connecting`, `StreamStarted`, `TransportLost`, `Reconnecting` and
`StreamResumed` require a real `connectionAttemptId`; for other events it may
be null when no attempt exists. A reconnect attempt is distinct from the
application-lifetime `sessionId` and the protocol connection.

| Event | Intended native emission boundary and strongest claim |
| --- | --- |
| `NativeReady` | Machine handshake and command dispatcher ready; no media claim. |
| `Connecting` | A concrete transport connection attempt begins. |
| `StreamStarted` | First valid video frame reaches presentation; process creation is insufficient. |
| `TransportLost` | Active transport loss observed for the stated attempt. |
| `ReconnectScheduled` | Recovery was scheduled, not yet connected. |
| `Reconnecting` | Replacement attempt begins with its own attempt ID. |
| `StreamResumed` | First valid replacement video frame reaches presentation. |
| `CapabilityDegraded` | Named native channel capability is unavailable; not a blanket media failure. |
| `SessionStopped` | Native session teardown completed; process exit remains independently observable. |
| `FatalError` | Native terminal error with typed reason/error; does not imply cleanup completed. |

Audio packet receipt or decoding never proves audible Windows output.
`SessionStopped` and observed process exit may arrive in either order on a
failure path. Phase 7 and 8 may add honest evidence and event fields without
redefining these names. Do not emit a stronger event when the native runtime
only knows `unknown` or `unsupported`.

## Compatibility and later process integration

Unknown `messageType` is a typed protocol error, not ignored. Unknown optional
root fields may be ignored after all size/depth/duplicate checks; missing
semantic fields are never optional. Neither generated native `OPT_*` ordinals,
ViewModels, arbitrary configuration files, CLR names nor native pointers are
wire values. Breaking field semantics requires a new major version.

Phase 6B must explicitly enable machine mode, reroute native banner/library/
child output away from framed stdout, serialize all stdout frames through one
ordered writer, flush critical lifecycle events immediately, drain stderr
concurrently, and bound queue count/bytes as well as frame size. It must
handle deadlines, cancellation, broken pipes, duplicate requests, EOF and
abnormal parent death with exact owned-child cleanup. A Job Object or other
guard must not capture or kill shared ADB. FocusWindow must run on the native
main thread and report no-window/failure truthfully. A partial machine-route
failure must not silently launch a second legacy session. Phase 6C owns
Desktop/package compatibility and synthetic/process/hardware acceptance of
the integrated route. The current `LegacyNativeHost` remains the runtime path
through Phase 6A.
