# Desktop/native machine runtime

## Canonical portable composition

The 2.0 application starts in portable mode when no storage selector is given.
Portable and installed modes resolve only the adjacent `runtime` directory;
an explicit development root may select its isolated runtime. Validation still
precedes all device effects, and missing or incompatible inputs leave launch
unavailable. Working-directory search, PATH-native lookup and a legacy process
fallback are not allowed. Preview never resolves a runtime or persistent root.
Startup displays its loading window while same-root activation and runtime
validation settle. Component hashing uses fixed-size reads off the UI thread.
Closing during startup requests cancellation and releases the activation lease
before closing the loading window. Normal close waits for owned device and
application operations, including activation and explicit release checks.
The canonical source-built package must carry the compatible runtime manifest,
full inventories and source/build evidence; no-argument startup is not proof
of a successful device handshake.

The [v1 wire contract](../../spec/desktop-native/PROTOCOL.md) defines message
syntax and meaning. Phase 6B provides the owned process route; Phase 6C selects
`MachineNativeHost` in normal device-enabled Desktop composition when the
explicit DEV bundle advertises a compatible machine contract and passes the
existing source/hash validation. A hash-valid older bundle without that claim
is incompatible with this route, not an implicit legacy fallback. Preview and
settings-only startup do not launch native. The machine route is Windows x64
only at this stage. Synthetic process tests do not establish Android video,
control or audible output behavior.

The production executable has no private launcher Stop event or environment
recovery adapter. Machine Stop uses the framed channel and recovery uses the
validated launch plan. Ordinary CLI invocation preserves its command-line device
selectors, including the standard ANDROID_SERIAL fallback. That invocation has
one attempt without a machine plan. Historical launcher contracts are kept
under the test directories for regression evidence.

The build-side machine capability claim is
[`runtime-contract.json`](../../spec/desktop-native/runtime-contract.json).
Staging and archive validation read it; a managed parity test checks its
product, protocol version and required capabilities against
`ProtocolCompatibility`. The runtime manifest remains schema 1 with optional
machine metadata so previously built bundles remain readable. Full file/hash
checks run first; an otherwise valid old manifest without machine metadata
returns `IncompatibleMachineContract`. A current machine runtime also claims
`ConnectionPlanVersion: 1` for the bounded immutable launch argument. A missing
or different version is incompatible before child creation even if its framed
IPC version and capabilities match. This bootstrap version does not change the
version-1 wire codec. Read-only archive verification still accepts historical
four-field machine claims; creating a new package requires the current claim.
Neither verification path modifies an older package or grants it new runtime
features. This local manifest is consistency
metadata, not a signature. The child must still pass the real negotiated
handshake on Mirror, and a failed handshake never starts a legacy child.

## Bootstrap and ownership

`MachineNativeHost` starts one exact native child from a committed
`NativeStartRequest` with absolute native, server and ADB paths, an explicit
working directory, redirected binary stdin/stdout and independent stderr.
Arguments select machine mode and provide the captured session GUID, parent
PID and the parent's creation FILETIME. The native process validates this
metadata, opens a non-inheritable handle to that exact parent process and
installs its parent-death watcher before any ADB or media work. A missing,
already exited or differently created parent fails closed.

The native machine mode reserves stdout for complete framed messages. Human
diagnostics and subprocess output go to stderr or another safe destination;
subprocesses must not inherit protocol pipe endpoints. A dedicated writer owns
frame order. The first parent frame is `hello`; native responds with
`helloResult`, then enables command dispatch and emits `NativeReady`. Neither
handshake nor `NativeReady` claims that a device is connected. Normal CLI
invocation does not wait for a handshake or treat terminal stdin EOF as Stop.

The parent owns the concrete `Process`, its pipe tasks and correlation state.
Every error path that actually created a child keeps that ownership through
graceful shutdown or exact-child escalation. Neither process-tree termination
nor ADB-server shutdown is part of cleanup. The native parent watcher exits
only the machine child if the parent dies without managed cleanup; its scope
does not include pre-existing or newly spawned daemon-like processes.

## Finite channel policy

The managed route uses one reader and one ordered writer. Its initial budgets
are 8 seconds for handshake, 3 seconds for a frame write, 5 seconds for an
ordinary command result, 8 seconds for graceful Stop, 5 seconds for exact-child
escalation and 5 seconds for stream-task settlement. Budgets are separate from
the unbounded lifetime of a healthy mirror session. Tests exercise the fixed
budgets; an internal budget-injection seam is not yet present. The outgoing
queue retains at most 32 frames and
2,097,152 encoded bytes, with at most 16 pending requests and 128 waiting
lifecycle events. Stderr is drained continuously and retains at most 65,536
bytes of diagnostics. Native stdout is bounded to 64 waiting frames plus one
in-flight frame and 2,097,152 encoded bytes in total, with at most 64 pending
main-thread focus callbacks. Native budgets are 8 seconds for handshake,
5 seconds for a stalled write, 3 seconds for cooperative shutdown after
parent death and 6 seconds for worker joins. A blocked writer or reader
cannot hold a rendering or teardown lock indefinitely.

An interrupted or failed actual frame read/write terminates its channel;
the stateless codec cannot resume at an assumed frame boundary. Caller
cancellation before enqueue can prevent a command from being sent. After a
complete send, cancellation only ends that caller's wait: it cannot undo
native work. Request IDs increase within one connection and are never reused.
Reservation, bounded publication and pending-result registration share one
admission gate without waiting for pipe I/O, so concurrent callers cannot
publish ID 2 ahead of ID 1. Late results cannot complete a later command.
The 16-pending-command limit is reached by valid commands before the 32-frame
outgoing limit. An unread 128-entry lifecycle queue fails the channel and
settles the owned child instead of dropping events; a seventeenth valid
command is rejected at admission without failing the sixteen already admitted.
A valid `SessionStopped` closes admission and fails unanswered commands without
inventing success. The reader drains remaining stdout and owned command
deadlines settle before process and cancellation-source disposal. Internal
test hooks pause admission and wrap the write stream; Phase 6C keeps the
production process budgets unchanged while selecting this host in normal
device-enabled Desktop composition.

Stop acceptance means native shutdown was scheduled; `SessionStopped` waits
for teardown, and process exit is observed separately. The managed Stop API
reports a typed failure if exact-child escalation was needed or clean terminal
lifecycle was not observed. `FocusWindow` executes
on the native presentation thread and reports the attempt, not foreground
permission from Windows. Connection and stream events require their actual
native observation seams; audio packet receipt is not audible-output proof.
Native sequence numbers are assigned with queue publication. Native UTC comes
from `GetSystemTime`; native monotonic microseconds use QueryPerformanceCounter
anchored approximately to the process creation time. These timestamps do not
imply synchronized clocks across processes.

The implemented native seams are `NativeReady` after dispatcher startup,
`Connecting`/`Reconnecting` after the corresponding server worker starts,
`TransportLost` when the active native loop reports disconnection,
`ReconnectScheduled` when existing native policy chooses another attempt,
and `StreamStarted`/`StreamResumed` after a valid frame is textured and its SDL
render/present call succeeds. A fresh attempt GUID is allocated before each
server start while the application SessionId stays fixed. `SessionStopped`
follows the existing session teardown, and process exit remains an independent
observation. `FatalError` indicates a terminal native failure. The route does
not yet emit `CapabilityDegraded` or claim first audio packet, audio sink
readiness or audible output; those require later channel-specific seams.

## Phase 6C Desktop ownership

The normal device-enabled Desktop composes `MachineNativeHost` only after
explicit bundle validation; settings-only mode constructs no native child and
preview never constructs the runtime validator. Mirror still captures a
revision-checked committed v2 request and selected ADB route. The machine
host receives this immutable request; profile drafts or navigation cannot
alter a running session. USB-to-network fallback remains a committed profile
decision implemented by the current native reconnect path, not a second C#
reconnect loop.

`DeviceSessionViewModel` owns exactly one lifecycle reader for each acquired
interactive session, regardless of the visible page. It processes ordered
observations off the UI dispatcher, retains at most 64 typed recent entries
and queues at most one pending presentation update. Session identity,
increasing sequence and current attempt identity guard the projection and
queued UI work. A terminal event starts the shared bounded Stop/cleanup path
without awaiting it from the reader; exact process exit and disposal remain
separate facts. Focus uses the explicit machine command for the current or
exactly named session. `Applied` acknowledges an attempted focus, not actual
Windows foreground activation.

For an admitted user-requested `FocusWindow`, Infrastructure attempts
`AllowSetForegroundWindow` for the retained, still-live owned process after
bounded admission and before publishing the command to the single writer.
The original process handle remains retained; no PID lookup or HWND operation
selects a replacement target. Only Windows uses this platform adapter. A denied
grant leaves the native command/result and healthy session unchanged. Cancellation
before admission, terminal rejection, startup, Stop and ShowControlCenter do not
grant permission. Native raising stays on the SDL main thread. This supported
handoff neither guarantees foreground ownership nor changes `Applied` semantics;
another input event or Windows policy may still prevent activation.

Automatic terminal cleanup is distinct from an explicit Stop request. The
machine adapter retains its typed `TerminalFailure` when the session result is
`NativeFailure`. Desktop presents that result as `Native session ended:
NativeFailure` only when the exception's exact-session exit matches completed
`Completion` and disposal plus owned observer/focus settlement succeeds. This
means cleanup succeeded, not that mirroring ended successfully. Escalation,
other typed Stop failures and resource-settlement failures remain errors;
automatic errors are identified as cleanup failures. Explicit Stop retains its
existing failure contract. `FatalError` can start an internal Stop before
`SessionStopped` closes command admission; a cleanup call alone is not evidence
that a wire Stop was sent.
If completion already started shared disposal before terminal consumption,
automatic cleanup retains the captured original session result even after that
disposal releases ownership. It does not relabel the result as explicit Stop.

The projection marks video observed only after `StreamStarted` or
`StreamResumed` for the current attempt; transport loss clears that evidence.
Audio and control readiness, and the physical active transport, remain
unknown without channel-specific native events. It does not poll HWND or
derive media readiness from process existence. The retained bounded attempt
history prevents current native late-stream observations from promoting an
older attempt; arbitrary reuse of an evicted GUID plus a fabricated scheduled
transition is outside the v1 event evidence and requires stronger native
generation semantics in Phase 7.

## Explicit DEV session evidence

Devices → Mirror session exposes **Copy session evidence** only in explicit
device-enabled Development composition. The injected clipboard action runs
only on request. Preview and settings-only composition expose no such action.
The copy result has its own message and cannot replace session Status.

The production owner captures detached values under its existing short gate;
serialization runs off the lifecycle path, and clipboard I/O runs outside the
gate. There is still one lifecycle consumer. The adapter retains the actual
accepted helloResult only after NativeReady completes negotiation. This is
distinct from the manifest's runtime compatibility declaration. The projection
records NativeReady only when its existing consumer applies that observation.

The version-1 JSON snapshot retains at most the existing 64 lifecycle entries
and refuses output above 64 KiB UTF-8; it reports total observed, retained range
and omitted count. Reading/copying never consumes history. Exact uint64 sequence
and native monotonic values are decimal strings. Accepted known capabilities
are detached; arbitrary extension capabilities are omitted with a count.
The explicit allowlist exports correlation GUIDs/PID, actual metadata source
SHA, native timestamps and typed semantic values. It excludes physical device
identity, endpoints, names, paths, command lines, settings, ADB output, raw
stderr, exception text, keys and pairing material.

NativeExit and typed Stop outcomes are retained before exact-owner release.
Cleanup Succeeded means adapter disposal and owned Desktop observer/focus work
settled, not that mirroring or every phone-side resource succeeded. Intent names
describe the first non-None initiating route of the active cleanup operation.
While cleanup is InProgress, a terminal observer, explicit Stop or shutdown
caller joining it cannot rewrite that initiator. Completion-first cleanup
retains CompletionCleanup through late terminal delivery and successful release,
with no additional forwarded Stop. Lifecycle-first cleanup retains
AutomaticTerminal. After Failed cleanup, a genuinely new Stop/shutdown retry
may record ExplicitStop/ApplicationShutdown and InProgress; this is not a
permanent session-wide intent or an accumulating retry history. Detached prior
snapshots keep their failed outcome. Only actual owner Stop calls update
StopCallState and StopRequestedReason. A failed Stop call and
successful resource cleanup can coexist. Disposal failure retains ownership
and Failed cleanup. StopCallState covers the ViewModel owner's invocation only;
NotRequested does not exclude an internal Stop inside adapter disposal.
Escalation is true only for an observed typed Escalated outcome; otherwise it is
unknown, including completion-path escalation not retained as that typed outcome.

Wire Stop queued/written/acknowledged, receipt timestamps, native scope and
process exit code are explicitly NotRecorded: the existing projection/result
does not retain them. Handshake absence never becomes success from metadata.
Metadata identity is not independent binary verification. Copy failures leave
observations and session operations unchanged and allow an explicit retry.
Final evidence survives successful session cleanup and rejected/failed launch,
until another exact session is acquired. It does not survive Desktop exit.
Capture before another launch or normal Desktop exit; see the
[consolidated retest proposal](phase6c-manual-acceptance.md#bounded-dev-session-evidence).

## Validation boundary

Deterministic tests use real Windows redirected pipes and a native test target
linked to the production channel/dispatcher modules. They can prove framing,
handshake, correlation, cleanup, exact-process parent-death handling and
synthetic lifecycle identity. Phase 6C adds normal Desktop selection and bundle
preflight. The manifest claim does not replace the child's actual handshake;
integrated device acceptance still requires the new artifact.

### Source-to-test evidence

The [contract check](../../scripts/test-ipc-contract.ps1) exchanges nine authored
golden vectors and 24 complete conformance frames between C and C#. The
[runtime check](../../scripts/test-ipc-runtime.ps1) runs its isolated .NET
process project separately from the solution suite. The three parent-death
and Focus/Stop process cases are **not** part of solution or Meson test totals.
Meson builds `test_machine_child.exe` but does not register it
as a Meson test. That target links production `machine.c`, frame/json codecs
and yyjson, while using a test-only `main`; it does not prove the ordinary
`scrcpy.exe` entry point. The check also exercises the actual native executable
only on pre-device paths, with no ADB or phone access.

| Boundary | Production source | Existing check and assertion | Evidence limit |
| --- | --- | --- | --- |
| Entry and bootstrap | `src/scrcpy/app/src/main.c` selects machine mode before banner/argument errors; `src/scrcpy/app/src/cli.c` validates the internal arguments; `src/scrcpy/app/src/ipc/machine.c` installs the exact-parent guard before handshake. | Native `test_cli` checks option/help compatibility; the real-executable no-device tests check ordinary CLI exit and pre-device machine rejection. | Successful media startup, ADB subprocess output and real-window behavior remain Phase 6C checks. |
| Framing and negotiation | `ipc_frame.c`, `ipc_json.c`, `MachineNativeSession` and native `machine.c`. | `ProtocolContractTests.FramingHandlesEverySplitAndSeveralFrames`, `FramingRejectsMalformedBoundariesBeforeBodyAllocation` and `StreamCancellationRemainsDistinctFromEof` use in-process streams; `MachineNativeSessionTests.RejectedHandshakeDoesNotLeakChild` and `PartialNativeFrameFailsTheExactChannel` use a redirected PowerShell process double. | These do not exercise a partial native pipe write or a healthy full media session. |
| Retained work and cancellation | `MachineNativeSession.RequestAsync` admits request IDs in wire order; the class owns one writer, at most 32 queued/in-flight frames, 2 MiB encoded bytes, 16 pending commands, 128 lifecycle entries and a continuously drained 64 KiB stderr prefix. Native `machine.c` bounds its queue, in-flight bytes and pending Focus work. | `ConcurrentFocusRequestsPublishInRequestIdOrder`, `ConcurrentFocusAndStopPublishInRequestIdOrder` and `CancellationBeforeAdmissionLeavesNoPendingRequest` use a controlled gate and real-pipe recorder. `MachineNativePressureTests` exercises the seventeenth valid command and full lifecycle queue. `MachineNativeTerminalTests` interrupts an outgoing header and blocks a write through the production writer. | The stricter 16-command limit prevents valid public commands from reaching the 32-frame queue cap. Request-ID exhaustion and a full native media-session write remain untested. These limits are not a total process-memory bound. |
| Terminal settlement | `MachineNativeSession.HandleInbound`, `CompleteAsync` and `SettleDeadlinesAsync` close admission, drain stdout, settle unanswered commands and join deadline work before owned resources are disposed. A write error after an observed terminal event fails that frame without replacing the native exit reason. | `MachineNativeTerminalTests.TerminalLifecycleSettlesUnansweredFocusBeforeDisposal` uses a real-pipe child that receives Focus, emits `SessionStopped` without a result and exits; both active and already-cancelled callers leave zero pending requests/deadlines. `OutgoingWriteFailureAfterTerminalPreservesWindowClose` holds a queued write until the terminal event, then fails it. `MissingCommandResultFailsWithinTheFiniteBudget` checks timeout failure. | A terminal event never claims that an unanswered command succeeded; no real media teardown is exercised. |
| Stop and Focus | `MachineNativeSession.StopCoreAsync` distinguishes acknowledgement, terminal event and process exit; a terminal event winning before or during Stop still has a finite exact-child exit budget and does not fabricate a channel failure. Native `machine.c` dispatches Focus on the SDL main thread and settles commands before `SessionStopped`. | `MachineNativeSessionTests.RealProcessNegotiatesFocusesAndStops`, `StopWithoutAcknowledgementReportsEscalation` and `StopAcknowledgedWithoutTerminalLifecycleReportsFailure` use a process double; `MachineNativePressureTests.OverlappingStopCallsShareTheSingleNativeCommand` verifies one result/child across concurrent and later Stop calls. `MachineNativeTerminalTests.DisposeAfterTerminalBeforeExitPreservesWindowClose` and `TerminalBeforeStopResultPreservesWindowClose` hold the exact child between terminal event and process exit; `TerminalWithoutChildExitEscalatesTheExactProcess` verifies bounded escalation if the child never exits. `ParentDeathTests.FocusAndGracefulStopSettleWithoutWaitingForDaemonDescendant` uses the production-linked native target. | `Applied` means an attempted focus, not Windows foreground permission. The fixtures have no real mirror window; blocked full-native teardown is not directly exercised. |
| Lifecycle identity | `main.c` emits `NativeReady` and terminal events; `scrcpy.c` records connection/reconnect decisions; `screen.c` reports the first newly presented frame; `machine.c` assigns sequence and attempt IDs. | Process-double and linked-target tests observe `NativeReady`/`SessionStopped`; `RealProcessNegotiatesFocusesAndStops` checks their order and sequence. | Device connection, loss, video presentation and late callback attribution have not been exercised by these no-device tests; no audio/control readiness is claimed. |
| Abnormal parent death | `machine.c` retains a non-inheritable parent handle identified by PID and creation time, requests cooperative stop, then terminates only its own process after a finite bound. | `ParentDeathTests.KilledSupervisorEndsNativeChildButNotDaemonLikeDescendant` and `ParentDeathBeforeGuardInstallationRejectsStaleBootstrap` kill an independent supervisor and assert native exit before test cleanup, while a synthetic daemon-like descendant and unrelated process survive. | These run the production-linked native target, not the full scrcpy media process; the legacy host has no new crash-parent guarantee. |
| Native close reason | `scrcpy.c` records an actual SDL quit in machine mode and `main.c` selects the reason through `machine_exit.c`; `MachineNativeSession` maps `windowClosed` separately from unknown/fatal exit. | Native `test_machine_exit` pushes and consumes a real SDL quit, then tests Stop, protocol error, fatal and unknown precedence. `MachineNativeTerminalTests` verifies managed `windowClosed` and conservative unknown/fatal mapping over real pipes. | The SDL-linked test does not run a phone-backed mirror window; successful exits without an observed quit remain `unknown`. |

The focused source and process checks establish a bounded machine route.
Normal Desktop selects it only with an explicit compatible DEV bundle. They do
not establish full native reconnect or audio/control behavior.

## Phase 7 P7.1 receiver dispatcher boundary

Native `main` now owns an explicit `sc_dispatcher` after SDL event initialization.
`scrcpy` allocates a fresh value before each legacy connection attempt starts,
binds initialized UHID storage before controller/receiver start, and revokes
admission before target release. Receiver clipboard/UHID work transfers an owned
device-message record through this dispatcher. Its destructor needs no target;
the run callback obtains a target only after validating the captured generation.
Clipboard byte accounting uses the actual decoded wire allocation, including
embedded NUL content and terminator, rather than its displayed string length.

The [implemented contract](../architecture/PHASE_7_NATIVE_LIFETIME_PLAN.md#p71-authorized-implementation-contract)
defines 64 retained slots, 1 MiB owned payload, eight items per drain, separate
caller/operation settlement, main-thread execution lease, coalesced wake tickets
and accepted-but-wakeup-failed ownership. SDL wakeups are outside the legacy
session flush and are handled in server wait, playback, reconnect wait and the
conditional OTG loop. Final destruction removes only exact-instance wakeups
after producers and completion users quiesce.

Machine commands, framed stdio, managed Stop/Focus/cleanup and parent-death
contracts remain unchanged on their application-scoped route. P7.2 moves screen
open/frame/metadata work to a captured-generation video bridge and app ingress,
with frame-associated metadata and a first-successful-presentation input gate.
The dispatcher binding is a typed presentation/UHID bundle, not a replacement
UHID pointer. Server/controller/demux status and screen icon events retain their
existing join/drain/flush containment. Direct receiver ACK is not generation
protected; the conditional ACK-before-receiver-join risk remains open. This is
not full app/session ownership, asynchronous retirement or responsive joins.
Native debug tests link actual dispatcher and receiver/deserializer code with
controlled clipboard/HID/network doubles. A Windows `usb=false` pass does not
validate physical devices, AOA/OTG or V4L2 runtime behavior. No frozen Desktop
package receives these changed native bytes.

The [P7.2 contract](../architecture/PHASE_7_NATIVE_LIFETIME_PLAN.md#p72-implementation-contract-recorded-before-production-changes)
defines detached input and mailbox ownership. Its production-linked debug targets
are `test_frame_buffer`, `test_video_ingress`, `test_input_binding` and
`test_screen_presentation`; the latter replaces only external effects, creates
no real window and performs no actual clipboard/HID/ADB operation. FFmpeg frame
references remain real. Controller/receiver join now also precedes ingress
destruction because a failed receiver-thread wake can retire a video record.
The pre-existing direct ACK teardown risk remains open.
Buffered video now forwards metadata and every frame through one delay-worker
FIFO. The first ASAP frame skips its timing wait after predecessor callbacks;
subsequent frame deadlines are unchanged. `test_delay_buffer_ingress` exercises
this real composition with gated sink effects and actual FFmpeg references.
Admission is separate from later downstream acceptance. Worker join precedes
queue release and sink close; the upstream producer still owns publication/close
serialization. This does not bound the existing growable delay queue or validate
hardware latency. See the [ordering correction](../architecture/PHASE_7_NATIVE_LIFETIME_PLAN.md#p72-delayed-first-frame-ordering-correction).

## Explicit local JSONL diagnostics

Diagnostics provides a user-initiated Save action over the existing detached,
single-consumer session projections. This exporter is separate from the DEV
Copy evidence format above: it excludes source/profile/device/path values and
raw session, attempt and process identifiers. Session and attempt correlations
are export-local ordinals, reset for every export. Resource text, commands,
stderr, clipboard content, exception text and error payloads are never inputs
to the serializer. The Copy action retains its existing scope and may append
negotiated metrics with local generation ordinals; historical files remain
unchanged.

[`DiagnosticExportFormatter`](../../src/desktop/ScrcpySeamless.Desktop/Presentation/DiagnosticExportFormatter.cs)
defines append-only JSONL format version 1. Every line is one JSON object with
a `Record` discriminator: one `Export` header, at most eight `Session` summaries
and at most 64 `Lifecycle` rows per summary. The total bound is 521 records,
512 KiB UTF-8 and six JSON container levels. A fixed-capacity output buffer
rejects the complete export rather than returning a truncated document. Keep
existing field meaning/type stable; compatible new fields may be appended.
Incompatible semantics require a new format version. There is no background
log, rotation service or automatic export; every saved file is an explicit
new bounded snapshot.

Only actual projection counts are aggregated: total observed, retained and
omitted events, retained channel-event counts and retained attempt count.
Sequences and native monotonic microseconds are invariant decimal strings;
UTC values preserve the observed instant and available precision. Per-event
intervals and retained span require nondecreasing native monotonic samples.
Loss-to-resume intervals run from the first pending **retained** TransportLost
to the next retained StreamResumed; another loss before a resume does not reset
the start. These intervals are not complete outage measurements when earlier
history was omitted. Non-monotonic samples remain exported, with derived
durations unavailable.

An optional `generation-metrics-v1` negotiation appends `GenerationRetired`
observations without changing the three required capabilities or protocol 1.0.
The strict two-level `generationMetrics` wire object has exactly two mandatory
and ten optional recognized fields, using the existing exact unsigned decimal
string syntax. Missing optional fields are unknown; explicit wire nulls,
duplicates and unknown fields are rejected. This event requires a nonnull
attempt, native subsystem and none reason/error. It is not session termination.

Only measured values from a joined/released generation are projected: complete
audio packets read, decoded audio/video frames, submitted PCM sample frames
(including silence), underflow/overflow sample frames, highest producer PCM
queue observation before trimming and complete control writes. Software SDL
submission is not audible output or device-side control acceptance.
`retirementMicroseconds` covers the retirement request through main-thread
settlement, including dispatcher receipt and handoff. Optional Windows working
set/private-byte values are post-retirement samples after resource release,
not peaks or a universal RSS certificate. Raw native generation remains typed
internal data; both formats expose only export-local generation ordinals,
independent of session and attempt correlation.

Generation metrics are optional best-effort observations. Native publication
and managed lifecycle admission require their bounded queues to be idle;
admission/encoding or writer pressure, or later history eviction, can omit them. They
add no queue, reader, periodic file or larger capacity. A later critical burst
still has the existing finite backpressure policy. Sequence validation still
includes a received optional frame even if it is omitted from the projection;
projection totals count delivered observations, not every wire frame.

Retirement rows carry exact measured counters and nulls for absent counters.
The legacy summary can report submitted PCM samples from its latest retained
retired generation, with explicit scope and field availability. It does not
map sample underflows to underflow-event counts, decoder frames to presented
frames, or working/private samples to ambiguous generic memory. Older senders
remain compatible and expose unknown counters rather than manufactured zeros.
Audible readiness, native queue/retained bytes, video presentation counts,
process exit code, wire Stop acknowledgement and permission return remain
explicitly unavailable. Channel lifecycle events alone establish none of those
measurements or physical transport truth.
Terminal reason, initiating cleanup intent, managed cleanup outcome and actual
exact-child observations remain distinct typed fields.

[`LocalDiagnosticWriter`](../../src/desktop/ScrcpySeamless.Infrastructure/Diagnostics/LocalDiagnosticWriter.cs)
uses atomic `FileMode.CreateNew`, never an existence-check admission or
overwrite. Windows device namespaces, alternate-stream selectors, invalid
components and lossy trailing-dot/space names are rejected before opening;
both the original selection and normalized path are checked. Non-local
providers without provable CreateNew semantics are unsupported. The UI never
renders the selected absolute destination or raw I/O exception.

Cancellation does not prove that no file was created. A failed/cancelled new
file is retained with a typed partial-output receipt; its name is not deleted
after releasing ownership, and an existing destination is never replaced.
Successful write/flush/dispose is a managed I/O result, not a crash-durability
or signature claim. The composition awaits picker/write settlement through
`DiagnosticExportViewModel.CloseAsync()` before releasing adapters. An actual
completed save remains Saved even if a later cancellation was requested.

Synthetic filesystem/stream, immutable-format/privacy and headless UI tests
exercise the real formatter and writer, including CreateNew races, injected
write/flush/dispose failures, cancellation, actual NTFS stream rejection and
retained-only timing. These checks do not supply missing hardware/audio/backend
measurements or close the historical geometry/activation observations.
