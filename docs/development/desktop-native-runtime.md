# Desktop/native machine runtime

The [v1 wire contract](../../spec/desktop-native/PROTOCOL.md) defines message
syntax and meaning. Phase 6B adds an explicitly selected process route beside
the existing `LegacyNativeHost`; normal Desktop composition still uses the
legacy route until Phase 6C. The machine route is Windows x64 only at this
stage. Its synthetic process tests do not establish Android video, control or
audible output behavior.

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
native work. Request IDs increase within one connection and are never reused;
late results cannot complete a later command. Queue saturation fails the
channel and settles the owned child instead of dropping lifecycle/results.

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

## Validation boundary

Deterministic tests use real Windows redirected pipes and a native test target
linked to the production channel/dispatcher modules. They can prove framing,
handshake, correlation, cleanup, exact-process parent-death handling and
synthetic lifecycle identity. Phase 6C still must select this host in normal
Desktop composition, validate package capabilities and perform integrated
device acceptance with a new artifact.

### Source-to-test evidence

The [contract check](../../scripts/test-ipc-contract.ps1) exchanges nine authored
golden vectors and 24 complete conformance frames between C and C#. The
[runtime check](../../scripts/test-ipc-runtime.ps1) runs its isolated .NET
process project separately from the solution suite. The three parent-death
and Focus/Stop process cases are **not** part of the 461 solution tests or the
17 Meson tests. Meson builds `test_machine_child.exe` but does not register it
as a Meson test. That target links production `machine.c`, frame/json codecs
and yyjson, while using a test-only `main`; it does not prove the ordinary
`scrcpy.exe` entry point. The check also exercises the actual native executable
only on pre-device paths, with no ADB or phone access.

| Boundary | Production source | Existing check and assertion | Evidence limit |
| --- | --- | --- | --- |
| Entry and bootstrap | `src/scrcpy/app/src/main.c` selects machine mode before banner/argument errors; `src/scrcpy/app/src/cli.c` validates the internal arguments; `src/scrcpy/app/src/ipc/machine.c` installs the exact-parent guard before handshake. | Native `test_cli` checks option/help compatibility; the real-executable no-device tests check ordinary CLI exit and pre-device machine rejection. | Successful media startup, ADB subprocess output and real-window behavior remain Phase 6C checks. |
| Framing and negotiation | `ipc_frame.c`, `ipc_json.c`, `MachineNativeSession` and native `machine.c`. | `ProtocolContractTests.FramingHandlesEverySplitAndSeveralFrames`, `FramingRejectsMalformedBoundariesBeforeBodyAllocation` and `StreamCancellationRemainsDistinctFromEof` use in-process streams; `MachineNativeSessionTests.RejectedHandshakeDoesNotLeakChild` and `PartialNativeFrameFailsTheExactChannel` use a redirected PowerShell process double. | These do not exercise a partial native pipe write or a healthy full media session. |
| Retained work and cancellation | `MachineNativeSession` owns one writer, at most 32 queued/in-flight frames, 2 MiB encoded bytes, 16 pending commands, 128 lifecycle entries and a continuously drained 64 KiB stderr prefix; native `machine.c` bounds its queue, in-flight bytes and pending Focus work. | `MachineNativeSessionTests.UnterminatedStderrCannotBlockHandshakeOrStop`, `MissingCommandResultFailsWithinTheFiniteBudget` and `PostSendCancellationKeepsLateResultCorrelated` exercise a process double. | Queue saturation, pre-enqueue cancellation, request-ID exhaustion and interrupted actual pipe writes have no dedicated test. These limits are not a total process-memory bound. |
| Stop and Focus | `MachineNativeSession.StopCoreAsync` distinguishes acknowledgement, terminal event and process exit; native `machine.c` dispatches Focus on the SDL main thread and settles commands before `SessionStopped`. | `MachineNativeSessionTests.RealProcessNegotiatesFocusesAndStops`, `StopWithoutAcknowledgementReportsEscalation` and `StopAcknowledgedWithoutTerminalLifecycleReportsFailure` use a process double; `ParentDeathTests.FocusAndGracefulStopSettleWithoutWaitingForDaemonDescendant` uses the production-linked native target. | `Applied` means an attempted focus, not Windows foreground permission. The fixture has no real mirror window. Repeated/concurrent machine Stop and blocked real-native teardown are not directly exercised. |
| Lifecycle identity | `main.c` emits `NativeReady` and terminal events; `scrcpy.c` records connection/reconnect decisions; `screen.c` reports the first newly presented frame; `machine.c` assigns sequence and attempt IDs. | Process-double and linked-target tests observe `NativeReady`/`SessionStopped`; `RealProcessNegotiatesFocusesAndStops` checks their order and sequence. | Device connection, loss, video presentation and late callback attribution have not been exercised by these no-device tests; no audio/control readiness is claimed. |
| Abnormal parent death | `machine.c` retains a non-inheritable parent handle identified by PID and creation time, requests cooperative stop, then terminates only its own process after a finite bound. | `ParentDeathTests.KilledSupervisorEndsNativeChildButNotDaemonLikeDescendant` and `ParentDeathBeforeGuardInstallationRejectsStaleBootstrap` kill an independent supervisor and assert native exit before test cleanup, while a synthetic daemon-like descendant and unrelated process survive. | These run the production-linked native target, not the full scrcpy media process; the legacy host has no new crash-parent guarantee. |

The focused source and process checks establish a bounded opt-in machine route.
They do not establish full native reconnect or audio/control behavior, and they
do not switch normal Desktop composition away from `LegacyNativeHost`.
