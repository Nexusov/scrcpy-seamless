# Phase 3 desktop configuration and application boundaries

Phase 3 provides a headless C# foundation. The current WinForms/PowerShell
launcher and canonical 1.x package do not consume these classes yet; no user
configuration is migrated merely by building or testing the solution. Phase 5A
introduced the Avalonia shell and an explicitly simulated preview. Phase 5B
adds an opt-in normal settings composition with real local files. Phase 5C
adds device operations only for `--dev-data-dir=<absolute>` together with
`--device-runtime=<absolute>`. Preview still constructs no persistent, ADB,
activation or native adapters.

## Storage and authority

`ApplicationDataPaths.Resolve` takes explicit roots. Portable mode stores
`configuration.v2.json` at `<application>/data/`; installed mode stores it at
`%LOCALAPPDATA%/scrcpy-seamless/`. The path resolver does not probe for a
`portable.flag`; callers select a mode explicitly. Tests use synthetic roots
and isolated temporary directories, never the owner's portable installation.
Desktop normal startup requires exactly one of `--portable`, `--installed`, or
`--dev-data-dir=<absolute directory>`. The development override uses its exact
directory without appending `data`. Paths derive from the executable directory
or an explicit selected root, never the current working directory. A missing
document stays absent until Apply. Malformed or ambiguous launch arguments are
rejected. `--preview` accepts no storage selection and remains in memory even
when a real configuration exists nearby.

## Phase 5C DEV device boundary

An explicit device runtime is validated against its separate DEV hash manifest
before ADB services or a native host are attached. An absent or invalid runtime
leaves normal settings editable and blocks Mirror. Startup does not probe ADB:
Refresh, Pair, Connect and Mirror each require a user action. Opening Wireless
setup is an explicit action that runs one discovery snapshot; use Refresh after
opening the phone's pairing-code dialog if that first snapshot was too early.
The interface distinguishes not searched, searching, empty, failed and cancelled
pairing discovery from USB transport observations. One fresh pairing service is
visibly preselected, multiple services require selection, and manual pairing
address entry is an explicit fallback. Pair eligibility and its visible reason
use the same validation; changing or losing a target clears the pairing code.
Pair never needs a connection endpoint and never connects automatically.
Refresh distinguishes a
failed/stale observation from an authoritative empty result; selecting a saved
profile does not select an ADB transport. Pairing and connection endpoints have
different purposes. An already paired endpoint can be connected manually when
mDNS discovery is unavailable. Pairing and Connect neither save a profile nor
start mirroring; use the Profiles editor and its existing revision-checked Apply
to persist changes.

Mirror requires a selected saved profile, a fresh explicitly selected eligible
ADB transport and no pending profile/mirroring edits. It rereads the committed
v2 document and checks its byte revision against the loaded editor before
building an immutable native request. A selected Wi-Fi endpoint that differs
from the saved profile blocks launch until the profile is explicitly updated.
Unknown/managed options and currently
unsupported legacy reconnect combinations block execution without deleting
their stored values. Appearance edits do not block Mirror. One active mirror
is permitted per control center. Process existence reports only process
evidence; video, audio and control remain unverified until hardware observation.

The compatibility adapter owns only its native child, stop event and bounded
sanitized lifecycle JSONL in the selected DEV data root. It does not stop the
shared ADB daemon or replace the native client's existing in-process reconnect.
Same-user normal Desktop activation is scoped to the selected data root;
preview creates no activation endpoint. Accepted exit decisions cancel/settle
live work and stop the owned child; a failed native stop keeps the window open.
Abnormal parent death is not yet a proven orphan-prevention mechanism.

## Phase 5B editing and Desktop preferences

`ConfigurationEditSession` loads the exact v2 byte revision and deep-clones the
baseline and working draft. A valid v2 file is authoritative; invalid or
inaccessible v2 remains an error, not an empty session or a migration trigger.
Apply validates the changed known options, captures an immutable candidate and
commits it against the loaded revision on a worker thread. A semantic no-op
does not rewrite JSON or rotate its backup. A conflict, busy writer, invalid
draft or I/O error leaves the draft for review; a failed reload blocks stale
edits and writes until a successful explicit retry. Cancel restores the latest
loaded or applied baseline without file I/O. Resetting an option removes only
that draft override. Unknown stored options and raw string spelling remain in
the complete dictionary, even when the visible editor is filtered.

Saved profiles share the configuration draft and keep stable `ProfileId` values.
The editor stages changes before Apply; a profile's displayed fields are saved
identities, not evidence of an available phone. Global mirroring options do
not become per-profile preferences. Explicit `--legacy-dev-dir=<absolute app
directory>` enables inspection of synthetic/selected DEV legacy files; the UI
shows counts for a prepared proposal and requires confirmation. Migration uses
`LegacyMigrationCoordinator`, rechecks v1 snapshots under its established lock
order and reloads committed v2 before further editing. It never rewrites v1.
Preparation and confirmation both refuse an unstaged profile edit, including
invalid input. A prepared proposal remains available while that edit is
pending; after the edit is explicitly cancelled, confirmation may proceed.

The separate profile editor keeps unstaged input across page navigation and
blocks profile switching until that input is staged or cancelled. Settings
Apply cannot omit an unstaged profile edit, and Settings Reload is unavailable
while either the configuration draft or profile editor has pending changes.
After a failed authority reload, a clearly labelled "Discard edits and reload"
retry remains available so repaired files can be reopened. Apply or Cancel the
relevant edit before an ordinary reload. Closing the normal window
asks about both dirty save groups; a successful save advances each group's
baseline independently. If the second save fails, the first remains committed
and the failed draft stays open for review. Save and close owns editing across
both sequential writes: configuration, profile, preference and sidebar theme
edits stay unavailable until both writes settle. The window rechecks its dirty
and busy state before closing; a failed save releases edit ownership. Closing
the confirmation dialog with Escape or its title-bar close keeps the drafts.

Appearance and supported control-center shortcuts live in separate versioned
`desktop-preferences.json` in the selected data directory. Its read and atomic
write use a separate exact-byte revision and mutex. There is no cross-file
transaction. Missing preferences use in-memory defaults; invalid or future
versions keep their original bytes and render safe defaults until explicitly
reloaded. Appearance can preview before Apply and Cancel restores the committed
rendering; shortcut edits become active only after successful Apply. The
requested font remains saved even when the local font is unavailable and a
system font renders instead. Only implemented local command IDs can be bound;
disabled shortcuts have an explicit empty binding.

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
The generic runner accepts explicit child-environment settings but does not
select an ADB mDNS backend. The reviewed DEV runtime packages ADB Platform Tools
34.0.5-10900879. Its server selects the mDNS backend at server startup;
changing a later client environment does not change an already-running shared
server. A legacy runner explicitly selected Openscreen. During a subsequent
owner-run check with the phone pairing dialog open, `adb mdns services` returned
an empty list while `adb mdns check` returned `ERROR: mdns daemon unavailable`
with process exit zero. In [ADB 34.0.5's Bonjour implementation](https://android.googlesource.com/platform/packages/modules/adb/+/refs/tags/platform-tools-34.0.5/client/mdnsresponder_client.cpp),
that reply means the server's DNSService daemon query failed. The current
server is therefore on the Bonjour path at check time, but whether it selected
Bonjour initially or fell back from Openscreen is unknown. An empty service
list in this state is not evidence that the phone stopped advertising. The
gateway now checks mDNS health when the service list is empty and reports
unavailability separately from a healthy empty result. A controlled restart
with the exact same ADB binary hash and `ADB_MDNS_OPENSCREEN=1` selected
`Openscreen discovery 0.0.0`; with the phone's pairing-code screen open, the
Desktop displayed its pairing service immediately. This establishes that the
observed empty ADB result preceded UI parsing, and that Openscreen discovery
works in the tested setup. It does not establish why the earlier Bonjour
daemon query failed or whether a restart alone would have recovered it.
The validated runtime policy matches the reviewed ADB 34.0.5 executable by
SHA-256 and passes `ADB_MDNS_OPENSCREEN=1` to Desktop ADB children and the
legacy native child. The policy can select the backend only if one of these
children starts a server; it never changes or restarts an existing shared
server. Other ADB binaries inherit their normal environment.

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
start/stop/completion contract. Phase 5C wires the Windows same-user activation
channel and an isolated legacy native process adapter. Phase 6 still owns the
new Desktop/native machine protocol and crash-lifetime contract.
No Win32, named-pipe, HWND or legacy environment-variable detail enters Core.
