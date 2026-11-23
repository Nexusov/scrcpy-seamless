# Native connection policy and effect ownership

This document records the autonomous Phase 8 implementation contract. Source
validation and final device acceptance remain separate. The application owns one
native process, persistent presentation and last frame; it admits at most one
connection-generation graph, after the previous graph has actually retired.

## Immutable plan and authority

Desktop captures the committed profile, explicit observed route and option
snapshot once. The bounded [connection-plan carrier](../../spec/desktop-native/CONNECTION_PLAN.md)
transports its ordered candidates and retry policy explicitly. It is separate
from the framed Desktop/native v1 channel. The native parser rejects malformed,
oversized, duplicate and unknown fields before device work. Native never edits
a profile or reads a later Desktop draft. The first attempt retains the selected
network selector; a retry may resolve the same saved service to a changed endpoint.

`connection_manager` is application-thread policy with supplied monotonic time,
copied values and no session pointers, allocation or I/O. `transport_resolver`
owns one worker, interrupt context and bounded result. `app` executes effects;
`session` owns one generation; `session_retirement` settles its joins and external
release off the SDL thread. Desktop does not run a competing reconnect loop.

SessionId, ConnectionAttemptId, manager attempt token, dispatcher generation,
stream metadata, server scid and process/window identities remain distinct.
No manager token validates a stale owner pointer. Server parameters borrow
resolved strings only while the exact resolver result remains live through
server join. A shallow parameter copy does not acquire string ownership.

## Policy and settlement

The plan has at most two candidates. Explicit selection starts first; fallback
permission controls switching, while an explicitly selected network may retry
with switching disabled. Default maximum attempts is three per recovery episode,
including its first attempt; default backoff is one second, doubled and capped
at eight seconds. All additions saturate. Eight uninterrupted active seconds
reset the episode budget; a fleeting successful connection does not. A healthy
network route does not switch back merely because USB returns. Automatic
failback is currently rejected rather than silently ignoring its policy.

Actions are Resolve, Start, Retire, WaitUntil and Finish. Each effect action is
emitted once. Resolver failure can enter bounded waiting without acquiring a
graph. Once Start is admitted, replacement requires the matching retirement
receipt. Duplicate or old results cannot admit another graph. Callback outcomes
are evaluated outside dispatcher drain; no callback revokes its own execution
lease. A new attempt number is not proof that SDL was initialized: video, audio
and gamepad initialization have independent application-lifetime stage flags.

Stop is sticky and independently sampled even when an SDL wake fails. Intent,
the first terminal failure and cleanup settlement remain separate. A later Quit
must not turn an already selected fatal outcome into success. Cancellation does
not mean a resolver or owner has joined. Resolver completion requires its actual
thread to finish before reaping; graph retirement retains live contexts until
all borrowers settle. No deadline authorizes freeing live state.

Only typed transport loss and positively classified transient connection effects
may retry. Configuration, protocol, decoder/controller and recording failures
remain distinguishable and are not retried as a generic disconnect. Unknown
server push/launch/handshake failures remain conservative server-start failures;
an error string is not reachability evidence.

## Resolver effects and bounds

Queries use discrete argv entries, no shell. Output is bounded to 64 KiB; overflow
terminates and reaps only the owned query child before returning rejection.
Already cancelled work creates no child. The application observes an eight-second
resolution deadline and interrupts that exact operation while continuing its SDL
pump. It still waits for actual settlement before releasing the context. Server startup
has a separate named 30-second attempt bound; expiry requests typed transient
retirement rather than blocking the SDL loop or freeing a live server.

USB selection requires positive ADB evidence, not serial spelling. An exact
`get-devpath` result can identify the selected USB transport with several attached
phones. Legacy backends lacking a path may use the USB-scoped single-device query
and exact serial comparison; ambiguity remains a safe rejection. Device state is
checked separately. The source audit of the reviewed ADB implementation and the
synthetic multi-device/cancellation tests are not a physical USB validation.

Network resolution matches only connection services, with exact instance/type
comparison after trailing-dot normalization. Pairing services never supply a
connection endpoint. Ambiguous matches reject; the immutable saved endpoint may
be used when service discovery is unavailable. Resolved endpoints pass the same
bounded native syntax validator before command admission. Neither discovery nor
connect restarts or kills a pre-existing shared ADB server.

## Deadline, recording and channel evidence

The invocation deadline is computed once from monotonic application start and
includes resolution, startup, backoff and retirement. Numeric zero disables it.
Generation option copies suppress the old per-generation timer without changing
the committed raw spelling. Expiry prevents replacement, but teardown still waits
for real settlement. Meaningful headless paths do not wait for a video frame.

With recording recovery, the application prepares deterministic part01/part02
paths while preserving the original immutable filename and extension. The
generation borrows its prepared path until retirement. Atomic exclusive creation
refuses existing files; no existence-check race or automatic deletion is used.
The recorder owns its custom seekable AVIO buffer and file, reports flush/close
errors and finalizes before the next segment. Numbering advances only from the
exact joined recorder's actual-open receipt, even when a later allocation/header
fails; failure before open leaves the number unchanged. Ordinary non-segmented
CLI recording retains its prior output path behavior.

The recorder publishes its output context under its existing mutex/condition;
sink open waits for publication or Stop/failure. A worker having started is not
proof that its context is available. Packet references, queue backing, file and
AVIO lifetime all remain part of graph retirement.

Optional capability loss may be reported through the existing bounded lifecycle
path while required failure retires the generation. Local graph-acquisition failures
(such as allocation or thread creation) still fail closed; optional device-channel
unavailability does not turn local resource failure into success. Requested
recorded video is required because the destination depends on its header. Video presentation, audio
pipeline facts and control facts remain distinct; no packet, thread, PID or HWND
proves audible output or usable phone-side input. Missing wire/platform evidence
remains unknown. Final physical-media checks are deferred to the joint candidate
acceptance, together with inherited platform and backend coverage limits.

The first successfully presented frame emits StreamStarted even when earlier
resolution or generation attempts failed before presentation. StreamResumed
requires a previously presented frame in this invocation. Attempt count alone
cannot prove that a video stream existed. Per-attempt duplicate presentations
remain coalesced. This changes no wire message shape or acknowledgement claim.

The joined recorder result is retained before releasing its owner, independently
of revoked notifications. A failed trailer/flush/close prevents replacement and
makes the invocation unsuccessful even when a prior Stop intent remains. A
cancelled pre-media recorder is a separate outcome; it does not prove a valid
recording or hide a concurrent real output error. Actual opened files, including
failed or cancelled segments, are retained and consume their prepared number.

## Validation boundaries

Pure fake-clock manager tests cover retry budgets, saturation, hysteresis,
stale/duplicate results, Stop and one invocation deadline. Production-linked
coordinator tests use the real owner, manager, resolver and retirement paths,
with controlled external transport/module effects. Resolver tests retain the
trailing-dot, multiple-USB and cancellation findings independently. Recording
tests exercise actual FFmpeg references, exclusive files and WAV muxer trailers;
the reviewed FFmpeg runtime lacks a WAV demuxer, so final RIFF/data bytes are
checked directly. These lanes do not establish historical hardware causality or
transfer a frozen package's hardware pass to changed bytes.

Fresh frozen-input Windows debug validation executed 46/46 Meson checks; a
separate USB-enabled configuration executed 48/48, including ACK/OTG ownership.
Internal cases are not extra checks: manager nine fake-clock groups, demuxer
18 worker cases, controller/receiver nine worker cases, owner18 capability cases,
and coordinator17 cases including one application with 100 replacements/101
owners. Recording fixtures retain publication, optional-suffix readiness,
exclusive-file and reference/error outcomes. The first full run's app timeout
was a fixture assumption: optional audio EOS was incorrectly expected to end a
required-control session. Explicit Audio|Control restored that case's intended
mandatory-audio check. The original timeout and observed mask red remain evidence;
no timeout, retry or product optional-channel rule was weakened.

Those module/configuration runs are distinct from the source-identified
production native, bidirectional codec, real-process/parent-death, normal managed
and hosted lanes. Each later lane must record its actual source and executed
results; neither compilation nor an older successful snapshot substitutes for it.
