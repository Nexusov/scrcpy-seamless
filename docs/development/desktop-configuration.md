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
changes. USB identity, mDNS identity, pairing endpoint, connection endpoint,
alias and connection preference are separate fields. IDs serialize as invariant
GUID strings; endpoints serialize as `host:port`, including bracketed IPv6.
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

`LegacyConfigurationMigrator` is pure: it accepts the legacy
`app/phone.json` and `app/scrcpy-settings.json` contents and returns a v2
candidate or structured problems. It checks the actual 1.x phone shape,
including its serial/mDNS restrictions, and reports unknown fields or
unrepresentable values. A missing phone file does not create a profile;
missing optional phone mode is inferred by the legacy rule. Settings remain
independent of device setup. An initial profile ID is derived deterministically
from the v1 serial; after a v2 document is stored, that ID is saved and no
longer recomputed from transport data.

`LegacyMigrationCoordinator` first checks for existing v2 authority, snapshots
both v1 files, computes/validates a proposal, then rechecks both exact byte
revisions under the legacy configuration mutex before the v2 atomic commit.
It reconstructs the candidate from locked source bytes at commit, so mutating
a preview cannot change the saved result. It never deletes or rewrites either
v1 file. Existing valid v2 wins on later runs; an invalid v2 file blocks
migration for explicit recovery. A leftover temporary file has no authority.

This is intentionally one-way. If the 1.x launcher changes its intact v1
files after v2 migration, the new v2 document does not automatically absorb
those changes. Phase 5 must avoid concurrent editing between old and new
control centers during cutover or provide an explicit user-mediated import.
The `.bak` file backs up the previous v2 revision on replacement; the original
v1 files are the separate migration recovery source.

## ADB, activation and native process boundaries

Core defines semantic ADB results, discovery and pairing use cases. The
Infrastructure adapter executes ADB directly with `ArgumentList`, no shell,
owned cancellation/timeout and bounded output. Device and mDNS text is
parsed as untrusted input. The pairing code is sent through redirected stdin,
not a process command-line argument. Pairing codes are transient and absent from saved
profiles, result/error objects and diagnostics. A verified connection can
compare the connected device serial with the selected USB serial; pairing
alone does not invent a connection endpoint.

`ConnectionPlan` is an ordered description of preferred/fallback transport,
capabilities and bounded retry/failback policy. It does not run reconnection;
the native ConnectionManager belongs to Phase 8. Core also defines a
same-user, single-control-center activation contract and an owned native-host
start/stop/completion contract. The Windows activation channel, Desktop window
focus behavior and native process adapter remain for later Desktop/IPC phases.
No Win32, named-pipe, HWND or legacy environment-variable detail enters Core.
