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
the unbounded lifetime of a healthy mirror session and are injectable in
deterministic tests. The outgoing queue retains at most 32 frames and
2,097,152 encoded bytes, with at most 16 pending requests and 128 waiting
lifecycle events. Stderr is drained continuously and retains at most 65,536
bytes of diagnostics. Native stdout is bounded to 64 queued frames and
2,097,152 encoded bytes including an in-flight frame, with at most 64 pending
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
