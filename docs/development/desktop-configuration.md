# Phase 3 desktop configuration and application boundaries

Phase 3 provides a headless C# foundation. The current WinForms/PowerShell
launcher and canonical 1.x package do not consume these classes yet; no user
configuration is migrated merely by building or testing the solution. The
Avalonia project remains a placeholder. Phase 5 composition must choose the
storage mode and explicitly invoke migration before using v2 state.

## Storage and authority

`ApplicationDataPaths.Resolve` takes explicit roots. Portable mode stores
`configuration.v2.json` at `<application>/data/`; installed mode stores it at
`%LOCALAPPDATA%/scrcpy-seamless/`. The path resolver does not probe for a
`portable.flag`; callers select a mode explicitly. Tests use synthetic roots
and isolated temporary directories, never the owner's portable installation.

The v2 document has `SchemaVersion: 2`, a list of `Profiles`, and global
`Mirroring` preferences (`Reconnect` and `Options`). A profile has a stable
`Id` that does not change when its USB serial, mDNS service, or network address
changes. `ProfileId` identifies a saved profile; optional `DeviceId` is an
application-assigned reference, not physical-device attestation. It is created
only when a caller explicitly sets it (for example with `DeviceId.New()`),
persists in v2 JSON and remains stable while that saved value is retained;
Phase 3 never derives it from ADB discovery or a hardware property. Two
profiles may describe the same handset. `UsbSerial` selects a USB ADB
transport, mDNS identifies an advertised service, and a network endpoint is a
host/port address that may change or be reused. USB identity, mDNS identity,
pairing endpoint, connection endpoint, alias and connection preference are
separate fields. IDs serialize as invariant GUID strings; endpoints serialize
as `host:port`, including bracketed IPv6.
Option values remain legacy booleans or strings; Phase 4 will define the
canonical option catalogue and rules. Unknown option names are preserved as
data rather than silently discarded.

`VersionedConfigurationStore` reads a validated snapshot and an opaque SHA-256
revision of the exact bytes. A save validates the new document, acquires a
short cross-process writer mutex, compares the expected revision, writes a
flushed temporary file in the same directory and atomically moves or replaces
it. Replacement writes `configuration.v2.json.bak` as the previous complete
v2 state. Invalid or unsupported v2 files are reported, not overwritten.
The store holds no lock across ADB, network, process launch or callbacks.

## One-way legacy migration

Configuration authority changes exactly once for the new 2.x architecture:
before a successful migration, v1 is the migration input; after a successful
migration, a valid v2 document is its only canonical configuration. The original
v1 files remain untouched for rollback/reference, but are never automatically
re-imported or synchronized with v2. A valid existing v2 always wins, including
when the legacy launcher subsequently edits v1. A corrupt v2 requires an
explicit recovery decision (`InvalidV2`); it never silently falls back to v1.
There are no two canonical writers and no bidirectional synchronization.

`LegacyConfigurationMigrator` is pure: it accepts the legacy
`app/phone.json` and `app/scrcpy-settings.json` contents and returns a v2
candidate or structured problems. It checks the actual 1.x phone shape,
including its serial/mDNS restrictions, and reports unknown fields or
unrepresentable values. A missing phone file does not create a profile;
missing optional phone mode is inferred by the legacy rule. Settings remain
independent of device setup. An initial profile ID is derived deterministically
from the v1 serial solely to make initial migration repeatable, not to prove
physical identity. After a v2 document is stored, that ID is saved and no
longer recomputed from transport data. A profile containing only `DeviceId` is
valid saved data but cannot form a `ConnectionPlan` until a USB or network
candidate is supplied.

`LegacyMigrationCoordinator` first checks for existing v2 authority, snapshots
both v1 files, computes/validates a proposal, then rechecks both exact byte
revisions under the legacy configuration mutex before the v2 atomic commit.
It reconstructs the candidate from locked source bytes at commit, so mutating
a preview cannot change the saved result. It never deletes or rewrites either
v1 file. Existing valid v2 wins on later runs; an invalid v2 file blocks
migration for explicit recovery. A leftover temporary file has no authority.

During migration commit the lock order is **legacy configuration mutex → v2
writer mutex**; the v2 lock is released before the legacy lock. Future code
requiring both must never acquire them in reverse order. Local file reads and
atomic replacement are part of this short critical section; ADB, network I/O,
process launch and UI callbacks are not. Contention returns `Busy`, and the v2
store still checks the expected byte revision before replacement.

The 1.x launcher remains a temporary compatibility path until Phase 11. When
the new Desktop becomes active, Phase 5 must isolate legacy compatibility so
simultaneous v1/v2 editing cannot silently diverge. An isolated generated
legacy view, unavailable legacy editing in the new flow, or another explicit
boundary may be considered there; Phase 3 chooses none of these mechanisms.
The `.bak` file backs up the previous v2 revision on replacement; the original
v1 files are the separate migration recovery source.

## ADB, activation and native process boundaries

Core defines semantic ADB results, discovery and pairing use cases. The
Infrastructure adapter executes ADB directly with `ArgumentList`, no shell,
owned cancellation/timeout and bounded output. Both redirected pipes keep
draining after their diagnostic capture caps, so additional output cannot
block the command on a full pipe. Cancellation or timeout terminates only the
invoked command process, then waits for it with a bounded cleanup deadline;
the persistent shared ADB daemon is not application-owned. A cancelled call
propagates cancellation while an internal timeout reports `TimedOut` through
the gateway. Device and mDNS text is parsed as untrusted input. The pairing
code is sent through redirected stdin, not a process command-line argument.
The generic runner inherits its process environment and does not select an
ADB mDNS backend. A future bundled-ADB compatibility override belongs to an
explicit runtime/composition policy, after reviewing that toolchain version.
Pairing codes are transient and absent from saved profiles, result/error
objects and diagnostics. After a network connection,
`adb -s <network-endpoint> shell getprop ro.serialno` reports an observed
device property, not a `UsbSerial` or proof that two transports reach the same
physical device.
A caller may compare it with an independently observed expected property as a
consistency check; a USB transport serial is never implicitly accepted as that
expectation. Pairing alone does not invent a connection endpoint.
The network endpoint used for pairing and the one used for connection remain
separate even when advertised by the same handset.

`ConnectionPlan` is an ordered description of preferred/fallback transport,
capabilities and bounded retry/failback policy. It does not run reconnection;
the native ConnectionManager belongs to Phase 8. Core also defines a
same-user, single-control-center activation contract and an owned native-host
start/stop/completion contract. The Windows activation channel, Desktop window
focus behavior and native process adapter remain for later Desktop/IPC phases.
No Win32, named-pipe, HWND or legacy environment-variable detail enters Core.
