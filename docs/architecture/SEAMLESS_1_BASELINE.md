# Seamless 1.x baseline and Phase 0 freeze

Evidence date: 2026-09-23. Source baseline:
`98c613019049ef3e97ae6644f6f14a2cef7ed627` on `seamless-2.0`.
Phase 0 work is local on `2.0/p00-baseline-freeze`; production source and reviewed
release metadata are unchanged. The [target](SEAMLESS_2_TARGET.md) describes future
behavior. The [risk register](SEAMLESS_2_RISK_REGISTER.md) and
[execution plan](../exec-plans/active/seamless-2.md) describe work still required.

## What freeze means

Freeze the observable 1.x contracts and record limitations before replacing their
implementation. It does not mean preserve every defect forever or claim complete
native/hardware coverage. Legacy reconnect incompatibilities are explicitly
scheduled for replacement in Phase 8. Historical tests in [CHANGES](../CHANGES.md)
remain historical; this run did not connect a phone.

## Repository inventory and sources of truth

| Area | Current owner / entry point | Phase 0 status |
| --- | --- | --- |
| Launch and setup | [launcher/](../../launcher/), Start.vbs / Settings.vbs, launch.ps1 / setup.ps1 | PowerShell 5.1, WinForms and Windows Script Host; no C# Desktop project |
| Application decisions | launch-session.ps1 / setup-session.ps1 | Explicit state and injected effects; retain as characterization reference |
| Effects/presentation | *-runtime.ps1 / *-view.ps1 | Runspaces/processes separated from WinForms controls |
| Persistence/ADB | configuration-store.ps1, options-store.ps1, adb-process.ps1, connection-core.ps1, launcher-core.ps1 | Portable app-relative settings; bounded owned ADB subprocesses |
| Options | [option-catalog.json](../../launcher/option-catalog.json) and native cli.c | Native-name/kind parity exists; basic metadata is still duplicated |
| Native/Android | [src/scrcpy/](../../src/scrcpy/) | Modified scrcpy 4.0 C client plus Java server source; no source-tree move |
| Native builds | [build.ps1](../../scripts/build.ps1), Meson/Ninja | Local-build executable plus fingerprint/hash sidecar |
| Runtime identity | [release-manifest.json](../../release-manifest.json) | Reviewed 1.x metadata and 11 runtime-file hashes |
| Packaging | [package.ps1](../../scripts/package.ps1), [provenance.ps1](../../scripts/provenance.ps1) | Explicit launcher file lists; recursive docs/licenses; fail-closed selection |
| Tests | [tests/](../../tests/), [app/meson.build](../../src/scrcpy/app/meson.build) | 23 original PowerShell suites; Phase 0 adds one; 14 debug C tests |
| CI | [.github/workflows/test.yml](../../.github/workflows/test.yml) | Windows PowerShell suites; see CI limitations below |
| Release publication | [publish.ps1](../../scripts/publish.ps1) | Legacy mutable-tag workflow; prohibited for 2.0 |
| Policies | Root plus five scoped AGENTS; [development policies](../development/README.md) | Already supplied; reviewed without replacing them |

## Current launcher behavior and architecture

```text
Start.vbs -> app/launch.vbs -> launch.ps1
  desktop-session mutex/activation event
  launch-session: Waiting -> Probing -> Starting -> Streaming/Running
                             | retry/settings/failure/close transitions
  launch-runtime: discovery workers + native child + graceful stop event
  launch-view: waiting/progress/failure controls

Settings.vbs -> app/setup.ps1
  setup-session: draft, saved snapshot, pairing state, one owned operation
  setup-runtime: discovery/pairing/config/shortcut effects
  setup-view + options-view: connection, mirroring, advanced and diagnostics

native scrcpy.exe -> bundled adb.exe -> Android scrcpy-server
  native itself owns reconnect after the launcher starts the process
```

`launch-session.ps1` owns Phase, retry generation, detached probe snapshots and
one native lifetime. States are Waiting, Probing, Starting, Streaming, Running,
Settings, Failed and Closing. Retry cancels/invalidate old work, reloads saved
configuration and prevents old results from launching a replacement session.
Config is reread under the same lock around final validation and process creation;
this deliberately broad 1.x critical section is future debt, not the 2.0 model.
Only native startup has a deadline (30 seconds); discovery can keep waiting.
Window readiness is inferred from HWND, while supported no-window sessions use
process lifetime. Normal stop signals a unique SCRCPY_STOP_EVENT; native emits SDL
quit; the launcher waits ten seconds before forcing only its owned child down.

`setup-session.ps1` keeps input, saved snapshot, pairing state, one PendingWork and
Open/Saved/Cancelled outcome. Cancellation discards late results; a pairing
already accepted by the phone is not undone. Config stores validate and replace
atomically with expected-snapshot checks, distinguishing absent/empty files.
Reset removes only the matching runtime's phone.json, refuses its active native
session, and retains mirroring preferences, logs, shortcuts and shared ADB trust.

Settings automatically starts discovery on Shown. Opening the unmodified Settings
entry point is therefore not an offline operation. Informational help/version
run without a phone; device diagnostics require configured, ready target discovery.
Option strings are data: the native boundary uses Windows quoting; ADB uses its
own restrictive argument validation and joined argv string, not a shell launch.
This is not yet the typed ArgumentList/IPv6/hostname architecture of Phase 3.

## Behavior-to-evidence matrix

A green row below means the named automated boundary is covered, not that its
Android hardware outcome has been observed in this run.

| Observable 1.x contract | Automated evidence | Remaining boundary |
| --- | --- | --- |
| USB, Wi-Fi and combined setup; ambiguity/identity checks; retained pairing | launcher.Tests.ps1, connection.Tests.ps1, pairing-recovery.Tests.ps1, setup-session.Tests.ps1 | Real authorization, mDNS/network/device variation |
| USB priority at initial connection; Wi-Fi fallback target handoff | connection.Tests.ps1, launch-runtime.Tests.ps1; owner-reported visual failover, matched endpoint PID/HWND and audible return | Intermediate transport trace, audio quality/timing, post-failover control and repeated hardware recovery remain pending |
| Retry generations reject stale config/results; reset blocks stale restore | launch-session.Tests.ps1, configuration-store.Tests.ps1, reset.Tests.ps1 | Native replacement-session callbacks are not covered by these tests |
| Atomic snapshots and package-local independent phone/mirroring stores | configuration-store.Tests.ps1, options-store.Tests.ps1, **baseline-contract.Tests.ps1** | v2 schema/migration does not exist yet |
| Literal option values survive save/load/native argument construction | options-store.Tests.ps1, launch-runtime.Tests.ps1, **baseline-contract.Tests.ps1** | Device capability validity remains native/device authority |
| Reconnect rejects six enabled recording/deadline/playback options; zero deadline/false switches preserve intent | option-catalog.Tests.ps1, options-store.Tests.ps1, **baseline-contract.Tests.ps1** | Characterize legacy restrictions; Phase 8 changes semantics |
| Native headless lifetime and graceful stop ownership | launch-runtime.Tests.ps1, launch-session.Tests.ps1, test_launcher_stop.c | Full native teardown/recording finalization under device loss |
| Instance activation and settings/controller/view event behavior | instance.Tests.ps1, *-view.Tests.ps1, setup-session.Tests.ps1 | Default lock is shared across installations; full real-desktop E2E not established |
| Informational diagnostics and cancellation | diagnostics.Tests.ps1, diagnostics-view.Tests.ps1 | Device actions require hardware |
| Exact ZIP roots/privacy/checksum/links; VBS wrappers and private shortcut fixtures | package-layout.Tests.ps1 | Wrapper tests use sentinel scripts; not mirroring E2E |
| Stale/incomplete builds and runtime drift fail packaging | provenance.Tests.ps1, package-provenance.Tests.ps1 | Hash identity does not establish reproducibility or eliminate vulnerabilities |
| Owned test-process timeout and descendant cleanup | test-runner.Tests.ps1 | No native parent-death product contract yet |
| Legacy publication and failed-main-push ordering | publish.Tests.ps1 against disposable local remotes | Existing tests also preserve unsafe mutable-tag behavior; not 2.0 policy approval |

New [baseline-contract.Tests.ps1](../../tests/baseline-contract.Tests.ps1) calls
imported production stores/validators directly, creates synthetic fixtures in
repository-local TEMP and never invokes ADB. It checks independent roots,
phone-vs-mirroring preservation, six incompatible reconnect options, disabled
switches/zero time limit, optional bare flags/literal values, and rejected-save
byte preservation. It does not extract functions from source or recreate the
production algorithm.

## Native ownership and reconnect map

| Owner | Current resources / evidence anchors |
| --- | --- |
| Process | main.c:main_scrcpy initializes network/main-thread dispatch/SDL and the stop monitor; monitor retirement precedes dispatcher/SDL shutdown |
| Cross-session orchestration | scrcpy.c:struct scrcpy and scrcpy contain the static aggregate, initialization flags and restart_session label |
| Retained presentation | screen.h:sc_screen; screen.c:sc_screen_prepare_reconnect, sc_screen_rebind, sc_screen_update_frame retain window/texture/last frame but also store controller/input pointers |
| Attempt transport/server | server.c:sc_server_init/run_server/stop/join/destroy own serial, sockets, interrupt state, server worker and server-process observer |
| Media | demuxer.c:run_demuxer owns reader threads; decoder/delay-buffer/audio-player sink close cascades retire downstream resources |
| Control | controller.c:stop/join/destroy; sender plus receiver share the server-owned control socket and queue |
| Dispatch | events.c:sc_run_on_main_thread/stop/resume use a process-wide gate and raw-pointer callback payloads, not connection generations |
| Buffer | frame_buffer.c latest-frame mailbox uses screen-owned synchronization; screen.c:sc_screen_clear_pending_frames directly resets its internals |

With SCRCPY_RECONNECT_SERIAL enabled, initial connection failures and selected
stream/controller failures can retry. Initialization/setup failures are not all
retryable. Subsequent attempts use the supplied wireless target; there is no
automatic USB failback promise. server.c knows the private environment contract
and requests ADB reconnection when the selected serial matches it.

Current reconnect requires a visible video-playback window, no recording, no
positive time limit and no AOA input. Retry uses a fixed one-second delay and no
attempt budget. Server-await and retry-delay loops service SDL events, but
synchronous stop/join intervals run on the main thread; full responsiveness is
not proven by the responsive wait loop.

On retry, source ordering is:

1. Gate input, detach old controller and retain last presentation frame.
2. Stop/drain the global main-thread callback gate.
3. Stop timeout/control/file-push/recording and interrupt server/socket I/O.
4. Join media producers and close their sinks before releasing dependent state.
5. Retire controller/receiver/recorder/file-pusher and server; final exit also
   retires retained screen, while retry preserves it.
6. Flush a specific SDL user-event range, clear pending frames, pump retry wait,
   resume dispatcher and restart the next attempt.

`sc_screen_rebind` installs replacement input/control while reconnecting remains
set. Only a successfully applied replacement frame clears the gate, updates
geometry/title and restores capture when appropriate and focused. Old-work safety
currently depends on drain/join/flush ordering. scid and stream metadata are not
callback generation tokens. The flush does not cover every SDL event or ordinary
queued input. These are maintenance risks; this audit did not establish a new
use-after-free or input-replay bug.

No dedicated deterministic reconnect lifecycle harness exists. Current C tests
cover thread waits, ADB parser, launcher stop, binary, audio buffer, CLI, Windows
command quoting, control serialize, device deserialize, orientation, strings,
string buffer, vector and vector deque. They compile only with buildtype=debug;
the canonical debugoptimized build does not itself run those 14 tests.

## Android server and device protocol

Server.java owns DesktopConnection, controller and audio/video processors. On
shutdown it requests processor/cleanup stop, shuts sockets, joins workers and
closes the connection. Android server lifetime is per native attempt.
Options.java checks exact client/server version against BuildConfig.VERSION_NAME.
DesktopConnection opens enabled video/audio/control channels; Streamer writes
codec and big-endian frame/session metadata. Native demuxer.c is its counterpart.
control_msg.c / ControlMessageReader.java and DeviceMessageWriter.java /
device_msg.c form the bidirectional control/device message boundary.

C vectors and Java unit-test sources already exist. Java tests cover control
reader/device writer, Binary, CommandParser, StringUtils, Size and CodecOptions;
they were not run because the canonical Windows client build imports the server.
No exhaustive cross-language golden/fuzz, bounded Desktop/native machine IPC or
handshake suite exists yet. Do not confuse device protocol with private launcher
Windows-event/environment contracts.

## Build, package, licenses and provenance

Canonical commands remain [BUILD](../BUILD.md) and [PACKAGING](../PACKAGING.md).
The local toolchain is w64devkit 2.9.1 / GCC 16.2.0 / binutils 2.47.20260726,
pkg-config 0.34.0, Meson 1.12.0, Ninja 1.13.2, existing Python 3.12.14 plus local
venv, Windows PowerShell 5.1.26100.3912. No global tool installation/PATH change.
SDL headers/runtime are 3.4.8. FFmpeg 8.1.1 public headers were generated from the
repository-pinned source; local .pc files link reviewed DLLs directly using
MinGW ld. The compiled/linked versions match in native --version.

| Packaged component | Origin / license evidence | What this run proves |
| --- | --- | --- |
| Modified native client | This checkout; Apache-2.0 [LICENSE](../../LICENSE) and upstream notices | Compiles from source, local-build sidecar and C tests |
| PowerShell/VBS launcher | This checkout, same project license | Source tests; DEV staging overlay separately identified |
| Android server 4.0 | Official upstream reviewed binary; source vendored, Apache-2.0 | Manifest hash; not rebuilt or Java-tested here |
| SDL 3.4.8 | Official scrcpy runtime + SDL development headers; [SDL notice](../../licenses/SDL-LICENSE.txt) | Reviewed runtime hash and matching linked version |
| FFmpeg 8.1.1 | Official scrcpy DLLs; [LGPL](../../licenses/FFmpeg-LGPL-2.1.txt), [license details](../../licenses/FFmpeg-LICENSE.md) | Hash and API-version match; not reproducible dependency rebuild |
| Embedded dav1d 1.5.3 / zlib 1.3.1 | [THIRD_PARTY](../../THIRD_PARTY.md), respective notices under licenses | Declared dependency/source obligations retained; no new binary composition audit |
| ADB 34.0.5-10900879 | Google Platform Tools input; [Android notice](../../licenses/Android-Platform-Tools-NOTICE.txt) | All three reviewed hashes; no device/trust operations |
| Compiler runtime | GCC/MinGW license texts and runtime exception under licenses | Notices retained; exact upstream runtime-build reproducibility not established |

THIRD_PARTY is the current licensing/provenance inventory authority. No competitor
code/assets were copied. A public dependency source companion remains an explicit
release obligation; Phase 0 does not publish or attest the local DEV archive.

The packager chooses dist/scrcpy.exe plus its sidecar whenever either exists and
fails if incomplete/stale. Imported baseline fallback is conditional, never a
silent recovery. Every runtime-file hash is checked before staging. The native
fingerprint includes **all files recursively under src/scrcpy**, including scoped
AGENTS.md; adding it invalidated the older local sidecar and the imported-baseline
fingerprint. Rebuild rather than changing expected hashes. Keeping fingerprints
free of generated server/build outputs is a Phase 2 concern.

ZIP roots are exactly app/, Start.vbs, Settings.vbs, README.md, THIRD_PARTY.md and
LICENSE. Native provenance and reviewed manifest live inside app/. The runtime
file set comes from the manifest; launcher modules are manually listed in two
loops; docs/licenses are copied recursively. Package tests use a synthetic
runtime by default; -ArchivePath validates the supplied real ZIP/checksum. The
recursive docs copy also carries source-oriented developer links whose targets
are outside the runtime package: these are repository navigation, not bundled
source claims. Phase 1/2 should settle package-vs-source docs/link policy.

## Existing CI, policy drift and limitations

CI already uses contents:read, an immutable checkout Action SHA and
persist-credentials:false. It runs the PowerShell suites for main pushes, PRs and
manual dispatch. It does not currently build/test native or Android, validate a
real artifact, run DocsCheck, or create SBOM/attestations. No remote ruleset state
was queried or modified; recommendations are not evidence of enabled settings.

The supplied six AGENTS files accurately identify the current directories and
future migration responsibilities. The active-plan link now resolves. Retain them
for the Phase 1 governance review instead of replacing them.

Concrete drift to route forward:

- publish.ps1 force-moves a release tag; its tests assert tag-following behavior,
  and PACKAGING/CONTRIBUTING still describe that workflow. New policy says tags
  are immutable. Do not use that publisher on the real repository.
- Test-PublicSourcePath rejects phone.json and adbkey but omits
  scrcpy-settings.json; a force-staged file under an allowed source prefix can
  pass that predicate. This is a source-observed privacy gap, not a claim that
  personal data was published. Current tests cover force-staged phone.json only.
- The publisher's root-file allowlist omits the supplied root AGENTS.md.
- BUILD lists FFmpeg 8.1 whereas the pinned script/runtime/THIRD_PARTY identify
  8.1.1; preserve exact matching inputs until Phase 2 documents one toolchain.
- README/THIRD_PARTY retain old Nexusov/scrcpy GitHub links. Their current external
  redirects were not audited; do not silently replace them in this freeze.
- Default launcher mutex is shared across installations. Shortcut matching can
  reuse a same-description personal shortcut. App-local JSON isolation does not
  isolate those resources or shared ADB profile keys.

## Local DEV isolation and validation boundary

Workspace: `D:\My Projects\scrcpy-seamless`. Tools/cache/scratch are under ignored
work/, canonical intermediates in .build/ and dist/, final artifacts in dist/dev/.
The personal `D:\Portable\scrcpy-seamless-win64` directory is read-only and never
an input binary/config source. Read-only before/after metadata comparison is
recorded locally; no private paths/content are copied into committed fixtures.

ADB 34.0.5 Windows source uses SHGetFolderPathW(CSIDL_PROFILE) and appends .android;
ADB_VENDOR_KEYS supplements rather than replaces the default key. Supported
per-process environment alone does not establish full key isolation. The owner
explicitly approved shared ADB server/trust on 2026-09-23 to enable live DEV use.
The new generated DEV staging removes the earlier offline-only restriction while
retaining a separate mutex, local shortcuts/TEMP and independent app-local JSON.
It targets the shared local ADB server on port 5037. Production launcher sources
and release manifest remain unchanged. This is file/settings isolation, not
ADB-trust, daemon-state or physical-device isolation. Do not manipulate a shared
daemon to test recovery, or claim simultaneous sessions cannot affect each other.

Baseline PowerShell result: 23/23 PASS. Native clean rebuild: 73/73 steps. Separate
debug native suite: 14/14 PASS. New characterization and final artifact results
are captured in the Phase 0 handoff evidence under work/phase0/ and
dist/test-results/. Exact HEAD/artifact hashes are recorded after local commits,
not duplicated as self-referential constants in this document.

The previous external HWND/CloseMainWindow smoke harness was inconsistent. It is
not accepted as full interactive Start-to-mirroring E2E evidence. Deterministic
in-process UI lifecycle checks and source wrapper tests are separate evidence.
See the [hardware baseline procedure](../development/HARDWARE_BASELINE.md) for
pending real-device acceptance and reporting. Comprehensive native reconnect,
server tests and hardware acceptance remain explicit gaps; later Phases must not
replace this boundary with a blanket claim that green launcher tests prove them.

## Hardware addendum (2026-09-24)

After the original artifact handoff, the owner reported that DEV Start.vbs opens,
video and PC control work, and both remain functional with Wi-Fi disabled during
USB operation. The owner visually observed USB-to-Wi-Fi failover with the window
still visible and mirroring resumed or continuing. No obvious window replacement
or failure was observed. At this initial report, the observations did not
prove identical native PID/HWND, post-failover audio/control, repeated-cycle
stability or every failure stage. Audio recovery had not yet been tested.

The native executable is `app/scrcpy.exe`: the packager copies that file and the
launcher starts it by exact path. The initial process query found that
executable and a mirror window, but at that point there was no paired
pre/post-failover PID sample. The owner's earlier `Get-Process scrcpy` returned
no process. That is an inconclusive point-in-time observation, not evidence that
the process has a different name or
that Seamless failed. The [hardware procedure](../development/HARDWARE_BASELINE.md)
contains the path-based PID/HWND command for future repeatable cycles.

### Extended owner sample (2026-09-24)

The owner captured the DEV native executable at PID `28600`, HWND `594730`,
title `Phone-Seamless` during USB operation with Wi-Fi disabled. Following the
reported USB-to-Wi-Fi recovery and additional transport changes, an independent
read-only query of that exact executable path found the same PID and HWND.
This confirms identity at those two checkpoints, not every intermediate state
or repeated-cycle reliability. The owner's attempted after-query failed because
PowerShell prompts, Markdown fences and displayed output were pasted as code;
it is not a failed native-process check.

The owner heard audio with USB and Wi-Fi, after USB removal, after Wi-Fi
restoration and after USB reattachment. Occasional perceived volume jumps
followed USB removal. Neither timing nor stable audio quality was measured;
post-failover PC control was not separately reported. With both links absent,
audio was absent as expected. After Wi-Fi failover and USB reattachment, turning
off Wi-Fi left screen and audio unavailable. Current native retry retains the
Wi-Fi serial and disables USB selection (`src/scrcpy/app/src/scrcpy.c`), so this
observation is consistent with missing automatic Wi-Fi-to-USB failback; it does
not establish a defect in fresh USB startup.

The owner subsequently launched DEV afresh with Wi-Fi off and USB connected;
video and audio worked. This is a separate successful USB startup observation,
not evidence of automatic failback in the earlier Wi-Fi session.

A subsequent three-minute source-tree observer run sampled one already-running
DEV native process 103 times. PID, HWND and responsiveness remained stable.
The native log contained USB selection followed by two TCP/IP selections and
two video-resume messages, but its lines reached the collector in five batches.
Because the process started 22 minutes before collection and output is buffered,
the trace establishes event order but not exact event times or that every native
event occurred during the three-minute capture. Its ADB counts indicate endpoint
availability changes, not native transport choice or audio outcome.
No contemporaneous owner action/outcome markers were captured, so the manual
sequence cannot be aligned retrospectively to this trace.

## Post-freeze Phase 1 note

The CI and publisher gaps listed above describe the Phase 0 source snapshot.
Phase 1 later added DocsCheck to CI, rejected existing release tags before
committing, used a non-forced atomic branch/tag push for new releases, blocked
force-staged `scrcpy-settings.json`, admitted root `AGENTS.md` as public source,
corrected the FFmpeg version, and updated the old repository links. See the
[risk register](SEAMLESS_2_RISK_REGISTER.md) for current status. These changes
do not revise the original Phase 0 hardware observations.
