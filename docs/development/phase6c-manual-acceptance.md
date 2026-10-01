# Phase 6C DEV manual acceptance proposal

The original run below is frozen against `gd9d12a12`. The separately authorized
[two-session corrective retest](#supervised-corrective-retest-on-2026-10-01)
used the existing `g3a99a1ed` artifact and completed its bounded sequence.
Phase 6C is accepted for engineering integration and continued development by
the [maintainer decision](https://github.com/Nexusov/scrcpy-seamless/pull/12#issuecomment-5939687829)
and integrated through PR #12 merge
`f08e603f0a4187b227c67a1eec4d845b814e095e`. This is not public prerelease, RC
or stable acceptance. Further phone-backed checks require separate authorization.
Do not resume the historical commands or transfer their results to another artifact.
The first [corrected Focus attempt](#supervised-focus-retest-blocked-before-native-launch-on-2026-10-01)
on g81fba9b1 acquired no native session because of a pre-launch profile/endpoint
comparison. A separately authorized endpoint-only Save/Apply enabled the
[completed USB/Focus/Stop session](#supervised-corrected-focus-session-on-2026-10-01)
on those same frozen bytes. Configuration remained unchanged relative to that
authorized Apply, not the pre-update profile. The bounded foreground observation
supports closing the observed Focus finding for this normal USB session;
Windows foreground activation is still best effort. The accepted integration
preserves all artifact-specific coverage limits and unavailable observations.

The merge's ordered parents are
`85197957352c1606ce4d96970bf7a20d3ee1f531` and
`9f0dd98d8395fc20f6708cd2d1882268b6cd28c3`; its reviewed tree is
`fbdd7bca5e99c0d32e76fcf3f13196f1c5d68813`. All 20 work commits remain in
its ancestry. The earlier checkpoints below retain their pending-at-the-time
status; this current decision does not retroactively change their observations.

## Current artifact evidence and review gates

| Category | Evidence and current boundary |
| --- | --- |
| gd9d12a12 | [Original observations](#supervised-observations-on-2026-09-30): startup, USB media/control, hidden-page recovery and explicit Stop; disabled fallback ended the child but exposed misleading automatic Stop-failure presentation. Historical incident remains preserved; subsequent corrections do not relabel its results. |
| g3a99a1ed | [Corrective retest](#supervised-corrective-retest-on-2026-10-01) and [window continuation](#supervised-focus-activation-and-close-continuation-on-2026-10-01): disabled fallback, USB media, recovery with Settings visible, ordered attempts/exact identity, explicit Stop, same-root activation, WindowClosed cleanup and active-Desktop closure. First visual Focus was unobserved; the separately authorized second did not raise the mirror. |
| g81fba9b1 | [Completed targeted session](#supervised-corrected-focus-session-on-2026-10-01): one USB session, three media channels confirmed before/after one ordinary Focus, exact-native foreground transfer, unchanged session/child, explicit UserStop/ExplicitStop/Succeeded cleanup and idle closure. Native bytes differ from g3a99a1ed; recovery, disabled-fallback, activation and distinct active-close results are not transferred. Frozen source is 81fba9b17843fae03b18812bb7bae831e13d540a; ZIP/component hashes remain in the [artifact table](#corrected-focus-artifact-and-validation-boundary). |
| Observation/review sources | Maintainer confirmed video/control/audible PC audio; the operator's bounded read-only trace measured foreground (300 samples over about 18.9 seconds) and a later screenshot showed the raised mirror. No separate maintainer visual Focus report exists. The external reviewer read the acceptance record and previously inspected the correction, but did not execute this hardware run or independently inspect its raw snapshots/foreground trace. Permission return, per-request wire acknowledgement, exit code, escalation and phone-side cleanup remain unavailable. |
| Previous local automated checks | [Initial composition validation, checkpoint 6C.4](../exec-plans/active/seamless-2.md) and [Focus correction validation](#bounded-no-phone-focus-investigation-on-2026-10-01) retain source/input-specific results. The latter reports 527 solution tests, eight separate process/parent-death tests and 18 Meson tests; the subsequent frozen-package check reran eight process cases against its rebuilt native entry point. These are earlier executions, not fresh runs for this source-only compatibility task. The interactive synthetic Focus experiment is separate from unattended CI and phone validation. |
| Hosted validation | PR [run 36912915271](https://github.com/Nexusov/scrcpy-seamless/actions/runs/36912915271), pull_request, attempt 1: test, native, android-server and desktop succeeded on synthetic checkout `1d6c2828242dd36529a0c7cd39846ac5c6afc863`, with head `9f0dd98d8395fc20f6708cd2d1882268b6cd28c3` and base `85197957352c1606ce4d96970bf7a20d3ee1f531`. Separately observed post-merge [run 36920087526](https://github.com/Nexusov/scrcpy-seamless/actions/runs/36920087526), push, attempt 1, head `f08e603f0a4187b227c67a1eec4d845b814e095e`: all four jobs completed successfully. These are baseline executions, not fresh validation of issue #13's changed Core source. CI's native-backed legacy ZIP is not the frozen Desktop DEV ZIP. |
| Remaining coverage | Screen-reader, multi-monitor DPI, cold-start discovery on this machine artifact, unavailable-Wi-Fi, stress/soak and phone-backed abnormal-parent-death behavior remain unverified. [R05/R06/R07/R12/R15](../architecture/SEAMLESS_2_RISK_REGISTER.md) retain Phase 7 lifetime, Phase 8 reconnect/channel-readiness and Phase 12 hardening work; shared ADB and intermittent fixture/observer observations remain bounded risks. |
| Separate compatibility finding | [Issue #13](https://github.com/Nexusov/scrcpy-seamless/issues/13) remains open: at the accepted merge, the inherited preflight checked string presence instead of the effective numeric deadline. The maintainer made it temporarily non-blocking only for PR #12's merge, without accepting that behavior as correct. The separately authorized `2.0/fix-time-limit-zero` source correction is being prepared for independent review; Phase 7 implementation and any public 2.0 prerelease remain blocked until the correction is reviewed and separately integrated. No Phase 7/8 reassignment or issue closure is implied. |

This current-status reconciliation does not alter the historical hardware
evidence or runtime ownership/contracts. All granular commits, historical
packages and local raw evidence remain preserved. The DEV ZIP stays local and
is neither rebuilt nor uploaded; its frozen source still contains the earlier
preflight behavior and does not acquire the later issue #13 correction. Hardware
checks are not repeated for the source-only compatibility task.

This procedure is prepared for the local package built from source commit
`d9d12a12bc94bd5b272824631d38e0cb6be1a4f0`. Supervised validation stopped
after an unexpected disabled-fallback terminal status; the observations below
do not constitute final acceptance. The package is
`dist/dev/scrcpy-seamless-desktop-p06c-gd9d12a12/`; its ZIP is
`dist/dev/scrcpy-seamless-desktop-p06c-gd9d12a12.zip` with SHA-256
`e33c68d863dae2e700a1839e9bcb0df0546ce6affc1bc9535522bdc117533f00`.
The runtime manifest in that directory records the exact machine-contract
claim and every native/server/ADB/SDL/FFmpeg hash and origin. ZIP extraction
and complete inventory verification passed from outside the repository.

Use only the isolated `.dev-data/p06c` root. Do not copy profiles or ADB keys
from another installation. An already paired device need not be paired again.
Do not stop/restart the shared ADB server for this procedure.

From PowerShell, after setting the repository path:

```powershell
$repository = 'D:\My Projects\scrcpy-seamless'
$package = Join-Path $repository 'dist\dev\scrcpy-seamless-desktop-p06c-gd9d12a12'
$data = Join-Path $repository '.dev-data\p06c'
$desktop = Join-Path $package 'ScrcpySeamless.Desktop.exe'
& $desktop --preview --page=settings
& $desktop "--dev-data-dir=$data" --page=settings
& $desktop "--dev-data-dir=$data" "--device-runtime=$package\runtime" --page=devices
```

Close each window before launching the next mode. Preview must show simulated
data and leave the selected data root unchanged; settings-only must remain
editable without a native child or ADB action. Device-enabled startup should
show the machine host but must not refresh, pair, connect or mirror until an
explicit action.

For the supervised device run, connect the already paired phone by USB with
Wi-Fi debugging enabled. Refresh, select the eligible USB row and create or
select a profile in the isolated root. When creating one, use the selected USB
transport in the profile draft, enter the connection endpoint from the main
Wireless debugging screen if fallback is intended, then **Save to draft** and
**Apply**. Confirm the machine host is selected. Mirror and independently
check updating video, usable PC control and audible PC output. A video
lifecycle event alone establishes none of the latter two.

Use the exact package path below before USB removal and after Wi-Fi recovery
to record native PID, creation time, window handle and title. Compare all
three identity values; a new process or window is a failure of the proposed
same-child transition.

```powershell
$nativeExecutable = Join-Path $package 'runtime\scrcpy.exe'
Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -ieq $nativeExecutable } |
    ForEach-Object {
        $windowProcess = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        [pscustomobject]@{
            PID = $_.ProcessId
            Created = $_.CreationDate
            HWND = $windowProcess.MainWindowHandle
            Title = $windowProcess.MainWindowTitle
        }
    } | Format-List
```

With a continuous local audio source on the phone, unplug USB and check video,
control and PC audio separately after Wi-Fi recovery. Record any sound on the
phone and whether its playback timer advances if audio fails. Check that the
SessionId remains fixed and that the connection attempt changes in ordered
machine lifecycle observations. These IDs and the full observation order are
currently internal to the Desktop's bounded projection, not visible in the
normal UI; obtain them through a supervised read-only debug observation or
mark this part **unverified**. Do not infer attempt identity from PID, elapsed
time or a displayed video frame. Stop and confirm exact-child exit.

In a separate run, leave the saved network endpoint but disable fallback in
the committed profile. Start on USB, disconnect USB and verify that it does
not switch to Wi-Fi. A selected network route may still retry on that same
route. Then compare an ordinary native-window close with Desktop Stop and
normal Desktop close: record reported reason, exact child exit, retained
configuration and no surprise relaunch. Test the explicit **Focus mirror**
button separately from launching a second Desktop instance with the same data
root; `Applied` reports a focus attempt, not guaranteed foreground activation.

The production native parent-death guard has synthetic process coverage. An
abnormal Desktop-death experiment on a phone-backed mirror requires separate
authorization and is outside this proposal. Preserve sanitized diagnostics
before retrying a new failure. No result from the Phase 5 artifact transfers
to this machine-route artifact.

## Supervised observations on 2026-09-30

Computer-use observations apply only to the package identified above. Its ZIP,
Desktop executable and native executable hashes were rechecked against the
recorded values. Validation started at repository HEAD
`13056d74be8bdf544d1c805e7eb55a2b94312aaa`; the changes after the package source
commit affect documentation only. No rebuild was performed.

| Check | Observed result | Evidence and limits |
| --- | --- | --- |
| Visible preview startup and normal closure | Passed | The simulated-data banner and Settings appeared. Clicking the window close button exited the exact Desktop process; no native or ADB process appeared and the empty p06c data root remained empty. |
| Visible settings-only startup and normal closure | Passed | Settings and Shortcuts navigation responded. Normal window close exited the exact Desktop process, with no native or ADB process. Draft persistence was not exercised here. |
| Visible device-enabled idle startup and normal closure | Passed | Devices showed the machine IPC host and an explicit Refresh prompt. Before Refresh, no native or ADB process appeared. Normal window close exited the exact Desktop process. |
| Explicit discovery and USB profile association | Passed | After reopening the same device-enabled package, explicit Refresh found an eligible USB route. The association action filled only USB serial; the name and network fields stayed empty. The maintainer entered a DEV alias and network endpoint, then computer-use Save to draft and Apply showed Configuration saved. Read-only persisted-state inspection confirmed USB identity, endpoint, Automatic, AllowFallback and Reconnect. |
| Live handshake | Indirectly confirmed | After one Mirror action, Desktop displayed Owned native PID 45608 and first-video-frame status. Source ownership transfers only after accepted helloResult and NativeReady. Their actual payloads and the full lifecycle stream were not independently inspected. Host selection and metadata alone were not treated as handshake evidence. |
| SessionId inspection | Observed from exact-child metadata | Only the `--seamless-session-id` argument was extracted from the native child's process metadata; the rest of its command line was not published. This does not read protocol stdout. |
| Structured lifecycle order and ConnectionAttemptId | Unverified; access gap identified | `DeviceSessionViewModel.RecentLifecycleEvents` provides a bounded in-process snapshot, but this package has no UI binding, exporter or external diagnostic endpoint. No second consumer or debugger suspension was used. |
| USB video, PC control and audible PC audio | Passed in the primary run | Computer-use saw the mirrored Wireless debugging screen. Clicking its back arrow changed the phone page. The maintainer independently confirmed live video, PC control and audible PC audio before USB removal, and kept playback running. |
| Enabled fallback while Desktop shows Settings | Passed within observed scope | Desktop was left on Settings during cable removal. The maintainer reported video, PC control and audible PC audio recovered. PID, creation time, HWND and command-line SessionId stayed fixed. Returning to Devices showed First video frame observed after reconnect. Full event order and attempt identity remain separately unverified. |
| Disabled fallback | No cross-transport recovery observed; terminal status requires review | Only AllowFallback was disabled and committed, retaining endpoint, USB identity, Automatic and Reconnect=true. Refresh showed USB and network available; USB was explicitly selected. After cable removal the maintainer reported the window closed and never returned. The exact child exited, with no packaged native replacement. Desktop unexpectedly reported Native Stop failed even though no explicit Stop was pressed. |
| Desktop Stop | Exact-child exit observed | After the primary Wi-Fi recovery, Desktop Stop showed Native session stopped. PID 45608 exited, no packaged native child remained, and Desktop and the same ADB server stayed alive. Graceful-versus-escalated termination is not externally exposed in this artifact. |
| Native-window close and Desktop close with mirror | Pending after terminal-status observation | Idle closure and Desktop Stop do not validate these distinct active-session paths. No additional Mirror was launched after the unexpected result. |
| Focus and same-root activation | Pending after terminal-status observation | A command acknowledgment and actual foreground activation must be recorded separately. |

No adb.exe process was observed before explicit Refresh; afterward the packaged
ADB server was running. Port 5037 absence was not independently checked before
startup. No shared-server stop or restart was performed, and this is not a
completed cold-start wireless-discovery check. No pairing was repeated. Exact child
metadata before USB removal: PID `45608`, UTC creation
`2026-09-30T16:26:30.9768600Z`, HWND `18157668`, title `Phone-Seamless`,
SessionId `f4964068-e7a9-40c7-bda5-63bc3b1f2fc2`, parent Desktop PID `61140`.
Exactly one native window from the package was observed. Read-only snapshots
after recovery retained all four identity values above. Android version,
device-side audio behavior and graceful-versus-forced session cleanup were not
independently observed. The audio source's offline/local status was not inspected.
The unchanged packaged ADB server had PID `46976`, UTC creation
`2026-09-30T16:19:35.2533670Z`.

### Unexpected status after disabled-fallback disconnect

The second native session had PID `59400`, UTC creation
`2026-09-30T16:36:17.5469780Z`, HWND `4134484`, and SessionId
`65ed19b4-482d-428d-9481-a9d6b155b625`. A native video window appeared before
cable removal. After removal, the exact process disappeared, the packaged
native process count was zero, and the same ADB server remained alive. Desktop
reported:

> Native Stop failed; the exact child exited and was released. Review the failure before another launch.

Computer-use did not press Stop during this run. No Wi-Fi recovery or
replacement native child was observed; the terminal/cleanup error status remains an
unexplained finding. It is not evidence of unauthorized fallback, nor does it
establish whether the cause is product teardown, protocol settlement, native
exit or presentation. Actual typed terminal reason, command outcome, exception
and escalation were unavailable externally and are not invented here.

Before another launch or cleanup, a sanitized bounded record was saved locally
at `work/phase6c/manual-20260930/disabled-fallback-observation.json`; it contains
artifact identity, stage, expected/observed behavior, process identities and
the access limitation, without device addresses, serials, pairing codes or raw
command lines. No retry, source fix, rebuild or additional phone experiment
followed. The original AllowFallback=true choice was restored through Save to
draft and Apply, retaining the endpoint, USB identity and Reconnect=true.
The idle Desktop then closed normally and its exact process exited; the same
ADB server remained alive. This is not normal Desktop-close-with-live-mirror
evidence.

The remaining Focus/activation and active window-close checks require a later
bounded continuation after this finding is reviewed. If production changes
are needed, obtain a separately scoped corrective task with deterministic
regression coverage and a traceable artifact for the targeted retest. A
read-only sanitized developer accessor for the existing bounded snapshot is
the proposed observation-only follow-up; it must not add a second lifecycle
consumer. No such accessor was implemented in this validation.

AGENTS/docs impact: this task changes only validation evidence and progress.
Architecture, protocol, ownership and package bytes are unchanged; no AGENTS
change or rebuild is needed. Full screen-reader, multi-monitor DPI, abnormal
parent-death-on-phone and cold-start wireless discovery claims are outside this
run. Phase 6C is not published or finally accepted; Phases 7/8 remain unstarted.

## Bounded terminal-status correction

The corrective investigation starts at local HEAD
`3fbb4635be0b3d4f60a63eaeed909ad84322402a` on `2.0/p06c-desktop-ipc`, with
integration base `85197957352c1606ce4d96970bf7a20d3ee1f531`. Its delta from
the frozen package source is documentation-only. The original ZIP, Desktop,
native and both manifests remain unchanged. The retained incident record's
SHA-256 is `50f75b07963d17ed07a78b48cacc025515371f0c1a03c5ea5c1e4c4b2d1b86b7`.
The original manual run remains stopped; this correction is not final acceptance.

### Cause and evidence boundaries

Source tracing identifies two paths. Completion-only cleanup already presents
the observed session reason after disposal. Lifecycle-first cleanup handles
`FatalError` or `SessionStopped` by starting an internal shared Stop, reserving
ownership against the completion observer. A completed `NativeFailure` makes
`MachineNativeSession.StopAsync` throw typed `TerminalFailure`. The old
Desktop catch then successfully disposes the session but presents the error as
failed Stop. The first incorrect boundary is that presentation consumption:
an unsuccessful session result is mislabeled as unsuccessful automatic cleanup
and implies a Stop action that was not requested.

The enabling issue is reuse of the explicit Stop result contract without
retaining automatic-cleanup intent. Existing Desktop terminal tests used a
successful `WindowClosed` fake; infrastructure tests checked typed fatal exit
and disposal without the automatic Desktop presentation path. Neither covered
their combination. Native failure mapping, reconnect policy and exit codes
are unchanged. No precise `TransportLost` cause is fabricated.

The production-backed regression uses a synthetic redirected PowerShell child,
the production machine adapter and the actual ViewModel. Named gates produce
accepted handshake/`NativeReady`, `FatalError`, an internally issued wire Stop
read by the child and acknowledged, `SessionStopped(nativeFailure)`, then
controlled nonzero exit. It invokes no UI Stop. On the old implementation,
typed `NativeFailure`, zero pending requests/deadlines and successful repeated
real disposal passed; final presentation failed with the original Stop-failure
message instead of `Native session ended: NativeFailure.` The retained local
red output is `work/phase6c/terminal-status-correction/red.txt`.

Independent review also reproduced the completion-first order: real completion
entered shared disposal before a forwarding observation gate delivered terminal
lifecycle. After terminal cleanup joined that disposal, the old null-session
branch incorrectly presented `Native session stopped.` The retained red is
`work/phase6c/terminal-status-correction/red-completion-first.txt`. Capturing the
original terminal session preserves its completed reason after the shared
cleanup releases it; no duplicate Stop/disposal or replacement launch is needed.

This proves a contributing product defect, not the full historical USB event
sequence. The incident lacks native event ordering, exit code, typed exception,
wire-command evidence and escalation outcome. In production, `FatalError`
precedes `SessionStopped`; only the latter closes command admission, so an
internal Stop may or may not reach the wire in that interval. No newly passing
synthetic test retroactively supplies those historical facts.

The accepted wire Stop in the primary fixture is a controlled managed-contract
scenario, not an exact reproduction of native `main.c`: ordinary native teardown
closes command processing before publishing `FatalError` and `SessionStopped`.
The completion-first fixture emits unsolicited terminal lifecycle and exits,
without a wire Stop. Neither fixture proves real transport failure mapping or
the original incident's native timing.

### Correction and validation scope

Only Desktop's automatic terminal-cleanup consumption changes. After successful
exact-session disposal and observer/focus settlement, a matching typed
`TerminalFailure` preserves `NativeFailure` in final status. That status is
not a successful-streaming claim. Explicit Stop behavior is unchanged;
automatic escalation and other failures remain visible as cleanup errors with
typed failure detail. Disposal failure after process exit retains ownership
and a separate cleanup error. No native, server, protocol, configuration,
fallback or ADB behavior changes.

The focused real-pipe cases also cover explicit terminal failure, normal
`WindowClosed`, injected resource-disposal failure after exact exit and rejected
internal Stop requiring exact-child escalation. Existing tests cover successful
Stop, missing acknowledgement/lifecycle, fixed budgets, concurrent Stop,
terminal/Stop/disposal ordering, close decisions, old callbacks, cleanup-gated
restart and disabled/enabled fallback launch snapshots. These tests do not
validate a phone's media or transport behavior.

Corrective validation on 2026-09-30 passed: all six new production-backed
regressions (machine filter 7/7 including one existing check), ViewModel class
29/29, locked .NET 10.0.401 solution restore, Release build with zero warnings
and errors, and solution tests 505/505 with no skips. The separate real-process
IPC suite passed 8/8, including exact-child parent-death isolation and production
pre-device rejection; it is not included in the solution total. SpecGen verified
113 native entries and six current outputs; DocsCheck passed 507 links in 81
tracked Markdown files; build metadata and `git diff --check` passed. Independent
source review found and tested the completion-first race before its correction.

Native/server/protocol/build/packaging inputs are unchanged. The retained 18
native tests and 9 golden/24 cross-language conformance frames are earlier
evidence, not newly executed results. Native and Android binaries are reused
only after current staging provenance checks; Desktop is rebuilt below. No
phone operation, pairing, shared-ADB restart or real DEV configuration edit
was performed.

### Corrective artifact identity

The corrective package was staged from clean committed source
`1eafa0cfbbfe697af0ff70a33d4a1878852c3035`:
`dist/dev/scrcpy-seamless-desktop-p06c-g1eafa0cf/`, with ZIP at the same path
plus `.zip`. Self-contained Release `win-x64` Desktop publish, staging provenance,
complete ZIP inventory and independent extraction verification passed. A
read-only verification was repeated from outside the repository. Documentation
follow-ups do not relabel or rebuild this artifact.
Final artifact documentation passed DocsCheck (508 links in 81 tracked Markdown
files) and diff validation; this documentation-only follow-up requires no rebuild.

| Item | SHA-256 |
| --- | --- |
| Corrective ZIP | `517064f9224efadbc73a398249165bf3889e3c27566e477cd4ffbcd51d9bd0d9` |
| Desktop executable | `774996f245a4637d9655104f6c19857a245a99440e5067b75aaad956404e591d` |
| Desktop assembly | `0f1620ac6e7c6ced976d996dc14ea8f6cc18ad9cd9747c083c4948f42e24695b` |
| Native executable | `4e5d085f9cec0816af1ef1ff3e1d207260642f4b6223d5bff6f53b1d90a98306` |
| Android server | `a5e307a072dac91a766e929733d187c531d30271eb3a7b19cbd6923349685c47` |

The package's `runtime/runtime-dev-manifest.json` records all 12 runtime hashes,
origins and the unchanged v1 compatibility claim. All 12 hashes match the frozen
`gd9d12a12` package. Native and server are reused source-built binaries, not
newly rebuilt; staging verified native input fingerprint
`4af64848c4b349810f3953b036dd52279f4e74d1a18d97be494bdccfb6944dc8` and server
fingerprint `c731943be22482bfa4c19a1cd48d3aa2199621fbc5796cf3ba46f11c092a33e6`
plus server build-source ancestry/unchanged inputs. ADB and its two DLLs, SDL,
the four FFmpeg DLLs and two images remain reviewed imports. This consistency
claim is not an observed handshake or new hardware evidence.

The original ZIP also passed read-only verification and its executable,
manifest and incident hashes remain the original values. Neither package
contains DEV configuration or private incident evidence. Hardware retest of
the corrective artifact remains pending.

### Targeted retest and observation access

After review and separate authorization, use the corrective package recorded
above with the existing `.dev-data/p06c` root; do not reset saved settings or
restart shared ADB. Do not execute this proposal as part of corrective validation.

1. Explicitly Apply disabled fallback while retaining USB identity, network
   endpoint and reconnect preference. Refresh, select USB and Mirror. Record
   exact-package PID, creation time, HWND and launch-argument SessionId.
2. Remove USB without pressing Stop. Capture final Desktop Status and exact-child
   absence before another launch. A failed session may end with `NativeFailure`;
   cleanup/escalation errors must remain visible, and no network replacement
   should appear. Do not infer successful resource settlement from PID absence.
3. Restore fallback explicitly and Apply. Check one USB-to-Wi-Fi recovery with
   video, control and PC audio independently, plus exact-child identity. Then
   Desktop Stop must report truthfully and settle that child without replacement.

Read-only external evidence in `g1eafa0cf` is the final Desktop `Status` and
exact-package process metadata. `RecentLifecycleEvents` is the existing bounded
in-process read-only snapshot, retained after successful cleanup until another
launch; it has no external accessor. `NativeExit` exposes typed reason/SessionId
inside the owned session, but no exit code or escalation field. After disposal,
the adapter and its typed Stop exception are not externally retrievable. The
corrective UI distinguishes typed automatic cleanup errors, but is not a full
structured terminal/cleanup record. Capture visible status before another action;
full lifecycle order, native-echoed IDs and independent settlement/escalation
proof remain unverified in a hardware run.

The smallest separate observation-only follow-up is a sanitized read-only
snapshot of the existing bounded projection plus the retained exact-session
terminal result and typed cleanup outcome, captured by the current owner before
release and accessible without another stdout/lifecycle reader. It requires
separate review; no exporter, protocol extension or dashboard is added here.
Do not substitute legacy JSONL or suspend a live failover under a debugger.

Focus mirror, same-root activation, native-window close and Desktop close with
an active mirror remain distinct pending scenarios. The new package inherits
none of the original hardware passes; unrelated pairing, cold-start discovery,
accessibility and DPI campaigns are not repeated by this targeted correction.

AGENTS/docs impact: the intent-aware consumption and regression coverage update
this acceptance record, the runtime guide and checkpoint 6C.7. Protocol,
ownership, paths, build commands and phase boundaries are unchanged. The
Infrastructure friend assembly extends an existing internal process-test seam
to Desktop tests; it adds no runtime policy. AGENTS files need no change.

## Bounded DEV session evidence

The observation-only follow-up adds Devices → Mirror session → **Copy session
evidence** in explicit device-enabled DEV mode. This changes observation access,
not native termination, IPC, Stop, ownership, budgets or reconnect behavior.
Phase 6C remains unaccepted; phone-backed validation is paused. The earlier
`gd9d12a12` and `g1eafa0cf` packages and private incident remain unchanged.
The historical incident is not reconstructed by these synthetic tests.

The [runtime guide](desktop-native-runtime.md#explicit-dev-session-evidence)
defines provenance, privacy and unavailable fields. The adapter retains actual
accepted handshake data; the existing single consumer projects ordered events;
the Desktop owner retains exact-session NativeExit and typed Stop/cleanup
outcomes before release. NativeFailure, cleanup failure and initiating intent
remain separate. Disposal/owned-work settlement is distinct from child absence.
Copy captures immutable values without consuming history, limits retention to
64 events and refuses output above 64 KiB UTF-8. It omits unknown capabilities
and all private configuration/free-form payloads. Wire acknowledgement,
receipt timestamps, native scope and process exit code remain NotRecorded.
Escalation is unknown unless typed Escalated was actually observed. StopCallState
describes ViewModel Stop calls, not every possible internal adapter call.

Tests extend the corrected production managed path with actual fixture
handshake, two attempts in one session, lifecycle-first and completion-first
termination, a closed-command terminal sequence without successful Stop
acknowledgement, explicit Stop, WindowClosed, escalation and disposal failure.
They retain earlier failure/ownership assertions. The closed-command fixture
models that terminal condition; it is not a phone run or exact native timing.
Existing history-flood and delayed-old-callback tests now assert snapshots.
DEV binding tests use an injected clipboard sink, including failure/retry;
formatter tests cover immutability, redaction, missing values and output refusal.
Fresh validation: locked restore; Release build with zero warnings/errors;
520/520 solution tests; 8/8 separate IPC/process tests; SpecGen verifies 113
entries and six outputs; build metadata, DocsCheck and diff validation pass.
Focused runs passed 31 owner-path cases, 17 adapter cases and 10 evidence
formatter/action cases. Existing native 18-test, nine golden/24 conformance
frame and 29-suite legacy results are reused for unchanged inputs, not freshly
executed here. Raw test output stays in ignored local scratch. The new artifact
identity is recorded after clean-source staging below.

### Proposed consolidated supervised retest

Do not execute until independent source review and separate hardware
authorization. Use the [cleanup-initiator corrective artifact](#cleanup-initiator-source-review-correction)
with the existing isolated DEV root; do not reset profiles, repeat pairing or
restart shared ADB.

1. Confirm visible idle startup and DEV copy action. Copy before Mirror:
   absent session/handshake must remain unavailable; copying must not start a
   child or change settings. Save copied text explicitly if needed; the product
   writes no evidence file automatically.
2. Explicitly Apply disabled fallback, retaining USB identity/network endpoint.
   Select USB and Mirror. Capture snapshot and exact-package PID, creation time
   and HWND. Remove USB without pressing UI Stop. Capture final Status, snapshot
   and exact-child exit before another launch. Read NativeExit and cleanup
   separately; do not infer TransportLost or successful settlement from absence.
3. Restore fallback explicitly and Apply. USB start, then one USB→Wi-Fi recovery
   while Settings is visible. Independently confirm video, control and audible
   PC output. Copy evidence after recovery: compare same SessionId/PID and
   distinct observed attempts; unavailable audio/control lifecycle remains
   unavailable. Use explicit Desktop Stop; copy final result before relaunch.
4. Keep Focus, same-root activation, native-window close and active-Desktop-close
   as separate pending checks. Capture before Desktop close and observe the
   exact child externally afterwards. In-memory copy cannot provide final
   post-Desktop-exit evidence. Foreground focus and audible audio still require
   direct observation.

No cold-start discovery, accessibility, multi-monitor DPI, stress or abnormal
parent-death campaign is repeated. Old hardware passes do not validate this
new artifact. AGENTS/docs impact: the Desktop guide now directs contributors
to the bounded owner snapshot; canonical runtime access and this retest proposal
are updated with checkpoint 6C.8. Protocol/configuration and build workflow
remain unchanged. The application ZIP stays local; independent source review
uses the authorized work branch, not a source-review archive.

### Evidence artifact and source review identity

The earlier observation package was staged from clean committed source
`e8ed35653f2ebaa54cda894b6a7ef345f3664587` at
`dist/dev/scrcpy-seamless-desktop-p06c-ge8ed3565/`; the adjacent `.zip` remains
local. Staging rebuilt the self-contained Desktop and validated the unchanged
native/server source fingerprints before reusing their source-built binaries.
Native fingerprint is
`4af64848c4b349810f3953b036dd52279f4e74d1a18d97be494bdccfb6944dc8`;
server fingerprint is
`c731943be22482bfa4c19a1cd48d3aa2199621fbc5796cf3ba46f11c092a33e6`.
Package creation and a separate VerifyOnly invocation outside the repository
both passed, including independent extraction in a path with spaces. No phone,
shared ADB or real DEV configuration was used. Manifest claims do not establish
a live handshake or hardware pass. This artifact record is a documentation-only
follow-up; it does not require rebuilding or relabelling the source artifact.

| Artifact/component | SHA-256 | Origin |
| --- | --- | --- |
| ZIP | `b5c0dad9c379a286ca6451062255eb981b1f5f369e64b147e162e7f118cf9654` | Verified local development archive |
| Desktop EXE | `a188125502f5e259847a2a1a9ff37d37724ca694800c5435b7da866c89c47dba` | Rebuilt self-contained publish |
| Desktop assembly | `6dfa1a811d1a4be9bde707ab373f3a4ae2cd3045cc8c96e6decdb4d916c0e0eb` | Rebuilt managed source |
| Package inventory | `a4a2968e37f10fe1c76139f23a3a376cd432414ede8a3688f155d0366c4ec0f7` | Complete generated file inventory |
| Runtime manifest | `5eeb096fe1752a5862f0a6a6b750b7c40081fcb17562a52156bc5035e6f61e3d` | Generated provenance/compatibility claim |
| scrcpy.exe | `4e5d085f9cec0816af1ef1ff3e1d207260642f4b6223d5bff6f53b1d90a98306` | Reused source-built native |
| scrcpy-server | `a5e307a072dac91a766e929733d187c531d30271eb3a7b19cbd6923349685c47` | Reused source-built Android server |
| adb.exe | `58765259a349cce392fbb2f15dab75fed3b7c0b40cc68a7653278b9850602a2f` | Reviewed import |
| AdbWinApi.dll | `689e4263252c734ee40d748f0e5a911801c6083a8e81b5040fd9c49dff3bfdce` | Reviewed import |
| AdbWinUsbApi.dll | `e6141805bb19eeafac6ab2d0fb50aa098b8c27149dc8ed73739cc40436274748` | Reviewed import |
| SDL3.dll | `f8bb1698f618949498ac517a0766eaa91972ecc818d80deb00634e197ee923cf` | Reviewed import |
| avcodec-62.dll | `893237890f744ea1eb447f56e8ac9d803deb48170bb456a11a10edf8c08a1eaa` | Reviewed import |
| avformat-62.dll | `d46e9b99b27c743b8ae73eacfd76004fba218b65eba5a1ce9cdf494a7030a4a4` | Reviewed import |
| avutil-60.dll | `2933c61bd5c3f0c2bec22310de8b9a22969030fb1ee204eae1a4948e7599f59d` | Reviewed import |
| swresample-6.dll | `91c2595781581c61144262fc596dcdbdca7f72b6315d28e31becc67d35b2cf59` | Reviewed import |

The runtime inventory also includes the two previously reviewed PNG assets;
their hashes remain in the complete package/runtime manifests. Old ZIPs retain
their documented hashes, and the frozen incident's hash remains
`50f75b07963d17ed07a78b48cacc025515371f0c1a03c5ea5c1e4c4b2d1b86b7`.

The earlier observation artifact's launch is retained for identity; use the
cleanup-initiator corrective artifact for the pending supervised retest:

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p06c-ge8ed3565'
& "$package\ScrcpySeamless.Desktop.exe" '--dev-data-dir=D:\My Projects\scrcpy-seamless\.dev-data\p06c' "--device-runtime=$package\runtime" --page=devices
```

Independent source review uses
[the work branch](https://github.com/Nexusov/scrcpy-seamless/tree/2.0/p06c-desktop-ipc).
Review ranges are the terminal correction `3fbb4635..1eafa0cf`, observation
source `507b1d77..e8ed3565`, and full Phase 6C from
`85197957352c1606ce4d96970bf7a20d3ee1f531` to the independently verified remote
tip reported in the handoff. All nine commits and 63 changed blob versions
through the observation source were reviewed for private material, including
intermediate history; no blocker was found. The documentation-only follow-up
requires its own final review before the authorized single-branch push. This
targeted review is not an exhaustive secret audit or a hosted CI result.

### Cleanup-initiator source-review correction

Independent review of `90c6ce92629f47bb968c6f4960536bcc5547302e` identified
an evidence-label defect. Completion-owned disposal recorded CompletionCleanup;
a terminal observer joining that pending operation overwrote it with
AutomaticTerminal. The original test expected both labels at different stages,
despite zero forwarded Stop calls and one disposal. This finding does not prove
a new native/USB failure or reconstruct the historical incident.

The existing controlled real-pipe completion-first regression now asserts the
first initiator in pending, joined and released snapshots. Before the production
change it failed at the joined capture: expected CompletionCleanup, actual
AutomaticTerminal. The same test passes after the evidence-only correction.
The terminal NativeFailure, successful cleanup, exact ownership, zero forwarded
Stop/one disposal, immutable captures and no-relaunch assertions remain.
Raw red/green output is retained only in ignored local scratch.

The production change is confined to SetCleanupIntent in
DeviceSessionViewModel.Evidence.cs: a non-None initiator is preserved while
cleanup is InProgress; Failed cleanup permits the incoming route of a new
attempt. Existing gated natural-exit coverage now checks both Stop and shutdown
joining CompletionCleanup without another Stop or disposal. A fail-once
synthetic disposal test covers Failed → new explicit Stop/shutdown retry →
Succeeded, preserving NativeExit and detached failed snapshots, with one actual
ViewModel Stop invocation, two disposal invocations and no replacement launch.
Lifecycle-first, explicit Stop, WindowClosed, escalation, disposal failure,
cancelled launch and shutdown contracts retain their existing coverage.

No native, IPC, host Stop/Dispose policy, cancellation budget, configuration,
ADB or reconnect input changed. No new evidence fields were added. The earlier
packages/manifests and incident remain frozen. AGENTS already directs bounded
owner snapshot retention; no instruction/path/workflow change needs another
AGENTS edit. This guide, the runtime invariant and checkpoint 6C.8 are updated.
Phase 6C remains unaccepted; hardware validation stays paused. Fresh validation:
direct red (expected initiating-intent assertion failure) and corresponding green;
34/34 owner tests; 10/10 evidence tests; locked restore; zero-warning Release build; 523/523 solution
tests; 8/8 separate IPC/process tests; SpecGen (113 entries/six outputs), build
metadata, DocsCheck and diff validation. The unchanged native/server/conformance
and legacy test results remain reused evidence, not new runs. Artifact identity
is recorded below after clean-source staging.

The corrective source commit is
`3a99a1ed27fc4439d0c616182f9e8199563f0b5e`. Staging from its clean tree rebuilt
self-contained Desktop into
`dist/dev/scrcpy-seamless-desktop-p06c-g3a99a1ed/`; the adjacent ZIP stays local.
The existing canonical provenance checks permitted native/server reuse. Both
source fingerprints and all 12 runtime file hashes/origins match `ge8ed3565`;
their exact component hashes remain in the earlier inventory above. No expected
hash was adjusted. Creation verification and independent VerifyOnly extraction
from outside the repository passed. Sixteen retained ZIP/Desktop/manifest/incident
hashes remained unchanged. No phone-backed validation occurred.

| Corrective artifact | SHA-256 |
| --- | --- |
| ZIP | `6f2a457dde377c1e3c97b0df3018a2e12cefc26c56bd4fe5e3a4056b062d480b` |
| Desktop EXE | `fd3cb9c61c68f3cc65283c211341993d33ef33d55e391fd36b91bd797d53061a` |
| Desktop assembly | `6e5a54a9b24eca449119f307740b66395a2c6170a88861c7b61c2d6227852c09` |
| Package inventory | `4ba923ff9ebeec28de2c6d9ed0fd4183d8a7fa7adaca317aa3c691c02029cbb9` |
| Runtime manifest | `f753e9792f199807dbb5a77aaa1a83a08b1212259b366b3b380c9fc2e4b795cc` |

The following command identifies the package proposed at that source-review
handoff. The separately authorized run is recorded below; this historical
command is not permission to repeat phone-backed testing:

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p06c-g3a99a1ed'
& "$package\ScrcpySeamless.Desktop.exe" '--dev-data-dir=D:\My Projects\scrcpy-seamless\.dev-data\p06c' "--device-runtime=$package\runtime" --page=devices
```

Capture before another launch or Desktop exit. Compare initiating intent only
with actually observed operation ordering; the hardware run need not reproduce
the deterministic completion-first test ordering. Retain separate media,
exact-child and terminal/cleanup observations. Focus, same-root activation and
the two close paths remain distinct pending checks; wire/exit-code/receipt-time
and post-Desktop-exit retention limits are unchanged.

The artifact-identity follow-up is documentation-only, so the package remains
bound to corrective source `3a99a1ed`, not the subsequent documentation HEAD.
Source review compares reviewed tip `90c6ce92629f47bb968c6f4960536bcc5547302e`
with the full independently verified remote tip in that handoff. Its upload
permission applied only to that task and that work branch; it grants no remote
write during this retest. Upload is not acceptance, hosted CI success, a PR or
integration. No source-review archive was prepared.

### Supervised corrective retest on 2026-10-01

This run used only the existing `g3a99a1ed` package and `.dev-data/p06c` root,
after maintainer readiness confirmation and authorization of computer-use for
the intended DEV stages. Starting branch was `2.0/p06c-desktop-ipc`, clean at
`1bb4f4d54451078c51a264b3e468a0f547d6142b`. Its difference from artifact source
`3a99a1ed27fc4439d0c616182f9e8199563f0b5e` was documentation-only. Before launch,
independent VerifyOnly extraction and actual file hashing verified the complete
package/runtime inventories and the seven specified ZIP, Desktop EXE/DLL,
manifest, native and server hashes. Both manifests identified the exact source
above. No rebuild or artifact relabelling occurred.

Observation sources were computer-use actions/screenshots and UI text, explicit
version-1 Copy session evidence snapshots from the existing single consumer,
read-only exact-executable CIM/process observations, and maintainer confirmation
of physical USB actions, live video, usable PC control and audible PC output.
No autonomous phone input, pairing or shared-ADB restart occurred. No second
lifecycle/stdout reader was used. The six snapshots were saved without overwrite
in ignored local `work/phase6c/supervised-g3a99a1ed-20261001T142204Z-2952f1c1/`;
only this sanitized summary is committed. Each snapshot is below 64 KiB.

| Checkpoint | Observed result and evidence |
| --- | --- |
| `01-idle` | Passed: visible responsive device-enabled startup; Copy left Status unchanged, retained NoSession, null SessionId, no accepted handshake and zero lifecycle entries. Exact-package native count was zero. Known configuration bytes were unchanged. No Refresh/Pair/Connect/Mirror action was performed before capture; an exhaustive process audit of idle ADB activity was not performed. |
| `02-disabled-usb-running` | Passed: Apply changed only profile fallback to false; Reconnect remained true. USB route and saved profile were selected. Actual helloResult accepted product `scrcpy-seamless`, protocol 1.0 and `focus-window`, `lifecycle-v1`, `stop`, with zero omitted capabilities. NativeReady and events 1–3 were retained. The maintainer confirmed video/control/PC audio before unplugging. |
| `03-disabled-terminal` | Passed within the stated native-classification boundary: no user Stop, no observed Wi-Fi recovery; window closed. Status was `Native session ended: NativeFailure.` Same-session NativeExit remained NativeFailure. Cleanup was Succeeded, intent AutomaticTerminal, StopCallState Failed with typed TerminalFailure/StopOperation, ExactChildExitObserved true and OwnershipReleased true. Independent CIM found no exact-package native child and no original PID. This is successful managed cleanup of an unsuccessful session, not successful mirroring or a disposal failure. |
| `04-enabled-usb-running` | Passed: fallback enabled through Save to draft → Apply, existing identity/endpoint and mirroring settings retained. A new explicitly selected USB session accepted the same actual handshake and retained events 1–3. The maintainer independently confirmed all three media/control channels. Desktop was then switched to Settings without edits. |
| `05-enabled-recovered` | Passed: after physical USB removal with Wi-Fi retained, the maintainer confirmed recovered video/control/audible PC audio. Settings remained visible during recovery. Returning to Devices showed `First video frame observed after reconnect; audio and control remain unknown.` The snapshot retained the same SessionId/native child and events 1–7 with two distinct attempt IDs. Process creation time and nonzero HWND were unchanged. |
| `06-enabled-stopped` | Passed: one Desktop Stop click; Status `Native session stopped.` Same-session completion retained UserStop, intent ExplicitStop, StopCallState Succeeded, Cleanup Succeeded, ExactChildExitObserved true and OwnershipReleased true. Independent CIM confirmed original-child absence and zero replacement children. Desktop remained responsive and the saved profile remained present. |

The disabled session ID was `7cd09eb0-a000-4069-9522-db5ccee0e8cc`, PID 40780,
creation `2026-10-01T14:34:57.8575660Z`, HWND 6360774. Its terminal retained range
was 1–6: NativeReady → Connecting → StreamStarted → TransportLost → FatalError
(NativeFailure/InternalFailure) → SessionStopped (NativeFailure).

The enabled session ID was `efcbc8ad-c5bb-4a7e-b8fc-808afe463e6e`, PID 53896,
creation `2026-10-01T14:42:37.6800700Z`, HWND 26480946 before and after recovery.
Attempt `52aa909d-9303-4e30-ba63-d93cea8d65d8` preceded transport loss; recovery
used `e5a190da-357b-46aa-9324-7ddb2cc06707`. Its retained order was NativeReady →
Connecting → StreamStarted → TransportLost → ReconnectScheduled → Reconnecting
→ StreamResumed → SessionStopped (UserStop), sequences 1–8. Both final snapshots
retained all observed events with OmittedCount `0`; exact integer sequence and
monotonic values were checked without floating-point conversion. Attempt events
do not independently name the physical transport; the recovery conclusion also
uses the supervised USB removal and observed continuing media.

An external checkpoint comparison initially reported an identity mismatch
because a timestamp string was compared with a deserialized date using different
fractional-second formatting. The run paused before Stop. Inspection of the
unchanged original checkpoints, invariant-culture parsing of their original ISO
timestamps and the current exact DEV window established equal PID, creation
instant and HWND. An intermediate local diagnostic also used culture-sensitive
date conversion; `05-identity-validated.json` retains the definitive comparison.
No product identity failure was observed, no additional Mirror was launched,
and no production code or diagnostic instrument was changed to continue the
existing session's authorized Stop step.

The original fallback choice was true and remains committed true. A final UI
inspection found no staged profile edit; the final configuration file hash
matches its pre-launch hash, including retained profile and mirroring values.
Desktop is left idle; no active-window-close test was performed. All six captures
remain available independently of the in-memory projection.

NativeFailure, managed cleanup and initiating intent remain distinct. Neither
terminal capture records wire Stop queued/written/acknowledged, process exit
code, local receipt timestamps, native scope or phone-side cleanup. Escalation
is null/unknown, not a proved absence. No audio/control-ready lifecycle event is
manufactured from user-heard audio or video events. AutomaticTerminal is the
observed initiator; this run does not exercise completion-first hardware ordering
or independently rerun the earlier Windows regression tests.

Focus, same-root activation, ordinary native-window close, active-Desktop-close
and Phase 6C hosted checks remain pending. No cold-start discovery, accessibility,
multi-monitor DPI, stress/soak, unavailable-Wi-Fi or abnormal-parent-death campaign
was added. Historical packages and incident evidence remain unchanged. This
bounded result is not final Phase 6C acceptance, publication or integration.
AGENTS/docs impact: validation evidence and current plan status changed; no
architecture, lifecycle invariant, protocol, configuration, path or workflow
changed, so no AGENTS or runtime-guide edit is required. Documentation-only
validation uses DocsCheck and diff checks; the prior automated totals remain
historical evidence rather than fresh test runs.

### Supervised Focus, activation and close continuation on 2026-10-01

This continuation reused the exact `g3a99a1ed` package and isolated
`.dev-data/p06c` root. Starting local branch `2.0/p06c-desktop-ipc` was clean at
`ba7be25761a62c272eb369c21638611c5319c65f`. Only the acceptance record and
execution plan differed from frozen artifact source
`3a99a1ed27fc4439d0c616182f9e8199563f0b5e`. Fresh independent VerifyOnly
extraction passed; actual file hashing matched all seven prescribed artifact
hashes above, all 248 package entries and all 12 runtime entries. No artifact
was rebuilt or relabelled. The local tracking ref remained `1bb4f4d`; this task
did not independently query the remote or perform any remote operation.

The verified primary Desktop was reused with PID 7752, creation
`2026-10-01T14:20:50.0048660Z` and HWND 4325912. Initially it retained the prior
completed session, with no owned native child; no fresh NoSession result was
required. The maintainer confirmed readiness and computer-use authorization,
kept USB attached, and confirmed live video, PC control and audible PC audio
in both new sessions. Profile, fallback=true and mirroring settings were not
edited. The completed USB-disconnect/recovery sequence was not repeated.

| Action | Observed result and limits |
| --- | --- |
| Focus command | The existing FocusStatus reported `Focus request ran; Windows foreground activation is not guaranteed.` Session A retained its SessionId, native PID/creation/HWND, one connection attempt and lifecycle sequences 1–3. No additional child or configuration change was observed. This UI outcome reports a focus attempt; it is not a retained wire acknowledgement. |
| Actual Focus foreground | Not obtained in the observed second invocation. The maintainer did not watch the first invocation, so its visual result remains unverified. The maintainer explicitly requested one additional invocation and confirmed readiness before that second click, then reported that the mirror did not come above the other windows. Native captures were partly occluded and do not independently establish foreground success. No external native focus helper was used and no further retry was performed. The cause is not established; visual Focus is not marked passed. |
| Same-root activation | After ordinary primary minimization, one secondary Desktop was launched from the same package with the same canonical data root and device-enabled arguments. A nonactivating computer-use capture showed the restored primary before another UI action. Secondary PID 39856, creation `2026-10-01T15:49:21.4892723Z`, exited within the finite ten-second observation. Exact-package process observations found one original Desktop and one unchanged native child. The retained snapshot still had the same session, attempt and sequences 1–3. This checks ShowControlCenter; it does not require mirror focus. |
| Ordinary native-window close | One native title-bar close, with no Desktop Stop click, produced Status `Native session ended: WindowClosed.` The same-session snapshot retained WindowClosed, Cleanup Succeeded, initiating intent AutomaticTerminal, StopCallState Succeeded with requested reason WindowClosed, ExactChildExitObserved true and OwnershipReleased true. Independent observations found the original native PID absent and zero replacement children; Desktop remained alive and responsive. Internal cleanup success is separate from user Stop intent. |
| Active Desktop close | After maintainer review of session A, one new USB session B was started and its running snapshot saved. One ordinary primary title-bar close, without Stop or native-window close first, closed both windows. Within a finite twenty-second observation the exact primary and native processes were absent, with no persistent secondary or replacement exact-package child. The shared ADB server retained its PID, exact executable and creation instant. Configuration bytes were unchanged. No Desktop was reopened to obtain a final snapshot. |

Session A was `4326fc8a-ae65-4d0e-9a51-4f7c9342df4b`, native PID 62364,
creation `2026-10-01T15:34:10.3509380Z`, HWND 23794216, and attempt
`1c8d721d-8abc-4aac-8879-ef28be2f042e`. Its final retained sequence range 1–4
was NativeReady → Connecting → StreamStarted → SessionStopped (WindowClosed),
with zero omissions. Session B was `52217f42-422a-401d-9fef-88b90b30427f`,
native PID 56988, creation `2026-10-01T15:53:01.0411290Z`, HWND 6226470,
and attempt `788a955b-3f46-4d2d-bf5e-3a06275ced2d`. Its pre-close snapshot
retained sequences 1–3. Both snapshots recorded accepted actual handshakes for
product `scrcpy-seamless`, protocol 1.0 and capabilities `focus-window`,
`lifecycle-v1`, `stop`, with zero omitted capabilities. Session identity is
preserved within A; B is a deliberately new session, not a continuity claim.

Five explicit version-1 snapshots and separate process/observation records are
retained without overwrite in ignored local
`work/phase6c/windows-g3a99a1ed-20261001T153212Z-22e8eb0d/`. All snapshots are
below 64 KiB. Original process timestamp text was preserved; comparisons used
typed UTC instants with invariant offset-aware parsing and full available
precision. Hash checks confirmed that the prior six snapshots and definitive
`05-identity-validated.json` remained unchanged. A computer-use input guard
interrupted preparation of B's Copy action; a refreshed observation and explicit
successful Copy retained the same B session, without another Mirror or product
retry. The stale clipboard identity was rejected instead of being accepted as
B evidence.

After Desktop exit, B's final typed reason, cleanup outcome and initiating
intent are unavailable. Exact process exit ordering was not observed. Exit
codes, wire Stop queued/written/acknowledged, escalation and phone-side cleanup
remain unknown; process absence does not independently establish all owned
resource settlement. A's typed managed cleanup success comes from its retained
owner snapshot, not from process absence alone. Ordinary Desktop closure is not
abnormal-parent-death coverage. Media evidence remains the maintainer's channel
confirmation, separate from native lifecycle readiness.

Desktop and Mirror are left closed. Shared ADB was not restarted, paired or
stopped. The original configuration hash still matches. Historical packages,
manifests and incident evidence remain preserved. Focus foreground behavior and
its cause require review; fresh Phase 6C hosted checks and final acceptance
remain pending. No additional hardware campaign, production change, publication,
integration or Phase 7/8 work occurred. AGENTS/docs impact is limited to recorded
validation and current checkpoint status; no architecture, protocol, lifecycle
invariant, path or workflow changed, so AGENTS and the runtime guide need no
edit. DocsCheck, diff and privacy review validate this documentation change;
earlier automated totals are not represented as rerun here.

### Bounded no-phone Focus investigation on 2026-10-01

Starting checkout was clean on `2.0/p06c-desktop-ipc` at
`a694b485d302342f9412a44a29f59f642b37da6d`. An independent remote query found
`1bb4f4d54451078c51a264b3e468a0f547d6142b`. Changes from frozen package source
`3a99a1ed` to that starting HEAD affected only this record and the execution
plan. The historical ZIP passed fresh independent extraction/VerifyOnly;
previous packages and observation records remain unchanged.

The production path is DeviceSessionViewModel's exact-session/generation-guarded
Focus → MachineNativeSession's correlated ordered command → native machine
dispatcher → SDL main-thread callback → registered live screen window →
SDL_RaiseWindow. The inspected path lacked an explicit Windows foreground
permission handoff. SDL's Windows release-3.4.8 backend calls
SetForegroundWindow on the normal raise route without propagating its BOOL;
Applied therefore cannot establish foreground success. This is consistent with
the [SDL contract](https://wiki.libsdl.org/SDL3/SDL_RaiseWindow) and the
[Windows restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).
The exact historical hardware cause remains unproven: the first visual result
was unobserved, the second was negative, and unchanged FocusStatus text alone
does not prove a fresh second response.

One separately approved visible experiment used a human-clicked isolated
WinForms parent, production MachineNativeHost/MachineNativeSession and the
production-linked test_machine_child with an optional real SDL window. Neither
ADB nor a phone was used. The experiment executable was compiled before the
production correction; later rebuilt fixtures include the corrected production
route on both buttons and label the second probe as an extra grant. This
optional fixture is outside unattended CI and is not a public product mode.

The actual loaded SDL DLL matched the frozen runtime SHA-256
`f8bb1698f618949498ac517a0766eaa91972ecc818d80deb00634e197ee923cf` and reported
version 3.4.8, revision `SDL-3.4.8-HEAD-HASH-NOTFOUND`. Release-3.4.8 source
correspondence is version-based, not an attested bit-identical DLL rebuild.
The tested native executable hash was
`84cb891e843e2dfe39a5d9c73579a01f1670abb7d202718957e81d8d683c44dd`;
the parent EXE hash was
`99b8eac2bdb5f444640b2f43d2e4d8221d0428899ee231daadd7a165f2690654`.
These identify test binaries, not a new hardware-tested product package.

| One-shot human action | Correlated result | Independent read-only foreground observation | Maintainer visual report |
| --- | --- | --- | --- |
| Original production Focus | Applied; no explicit grant | parent → parent during the bounded 2029 ms observation | Target did not come forward |
| Exact-child AllowSetForegroundWindow then the same production Focus | Grant returned true; Applied | parent → child at the first post-result observation | Target came forward |

Both cases retained child PID 62080, creation `2026-10-01T16:43:06.9430593Z`
and HWND 13830518; parent PID was 42632. HWND owner/thread matched the owned
fixture and SDL main thread. The window was visible, focusable, not minimized
or always-on-top, activate-when-raised true and force-raise false. The session
remained healthy. Foreground measurement never activated a window; unrelated
foreground was mapped only to other. SDL's native SetForegroundWindow return
was not exposed and remains unknown. The second observation proves an observed
transfer, not sustained foreground ownership or behavior under every OS policy.
Normal parent closure ended both synthetic windows; subsequent exact-PID
observations found both absent. No real DEV application was launched.

This controlled difference demonstrates a missing supported parent/child
permission boundary in the no-phone route. It does not reconstruct the old
phone run or prove that all future raises succeed. Wrong-target/main-thread,
hidden/minimized/non-focusable and force-raise alternatives were excluded for
the synthetic case; historical elevation, input interference and occlusion
remain unknown. The [supported exact-child delegation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow)
is the smallest justified correction. Infrastructure now attempts it under the
existing admission gate after bounded checks and before Focus frame publication.
The retained process handle prevents PID lookup/replacement targeting. Denial
does not fabricate success, fail the channel or stop a healthy session. No
native production code, wire contract, budgets, media, cleanup intent,
ShowControlCenter, configuration or ADB policy changed.

The controlled regression failed twice before the correction (expected one
permission attempt, observed zero), then the same two cases passed. It protects
exact-child delegation, denied-grant/native-result separation, startup and
Stop exclusion, pre-cancellation and terminal/disposed rejection. The pipe
marker verifies the observed grant precedes receipt in these runs; strict
before-publication ordering is additionally established by code inspection,
not by the marker's absence alone. Added NoWindow/Failed cases preserve native
outcomes, and the existing 16-pending pressure case verifies no extra grant for
rejected work. Original neighboring concurrency, late-result and cleanup tests
remain intact. Red/green outputs and raw synthetic observations are retained
locally under ignored `work/phase6c/`; the experiment directory is
`focus-experiment-20261001T164304Z-64fc9ead`.

Fresh checks passed: locked restore, warning-free Release build, 527/527 full
.NET tests, 8/8 separate real-process/parent-death checks, 18/18 native Meson
tests including the default no-window fixture, SpecGen (113 entries/six outputs)
and build metadata. These do not establish phone foreground success or hosted
CI. Earlier phone/activation/close results remain attached only to g3a99a1ed.
AGENTS/docs impact adds only the Infrastructure handoff invariant and opt-in
fixture guidance; architecture ownership and protocol remain unchanged.

After source review and separate hardware authorization, the minimal proposed
retest is one USB session on the separately identified corrected package:
confirm video/control/audio independently, capture running evidence and exact
identity, bring Desktop forward by normal user interaction, then click Focus
once. Record FocusStatus separately from actual foreground behavior and retain
the same session/child identity. Preserve evidence and pause on an unexpected
result. No recovery, pairing, activation or close campaign is proposed; normal
cleanup after the single session remains required. Phase 6C is unaccepted and
phone-backed validation is paused.

#### Corrected Focus artifact and validation boundary

The clean committed corrective source is
`81fba9b17843fae03b18812bb7bae831e13d540a`. The separately staged package is
`dist/dev/scrcpy-seamless-desktop-p06c-g81fba9b1/`, with local application ZIP
`dist/dev/scrcpy-seamless-desktop-p06c-g81fba9b1.zip`. Source review uses the
work branch, not a source archive. Independent ZIP extraction/VerifyOnly and
actual hashing of all 248 package inventory entries and 12 runtime entries
passed. A subsequent documentation-only commit does not relabel this source.

| File | SHA-256 |
| --- | --- |
| Application ZIP | `cc31ca51a770266bbed79d2be1ab69ae1fd8a2ae1acc4f2af3dfcd6bb4805660` |
| Desktop EXE | `8e1eee84525f509e73f6577f8c3bbeb6633a40e4500c396e65eda67636e72ded` |
| Desktop DLL | `48c5f7e4063972e32093d622a20f642205ecbc2bc244e4b507edd748f7a2c46f` |
| Infrastructure DLL | `2f3633bfdc241ef35a57effb0090baa7341b34f415129e1c30420b037a4fc915` |
| Package inventory | `3e4e1b59ea4ee31b6d3ab78b209f672651b91106f7e750226c6f92cc0a0d10b2` |
| Runtime inventory | `55e44dd5132a408ce745c06ec97dcc95a8302f641b3f77a1491d8f7791830aff` |
| Native executable | `42f511431b07b8cb27ccaedcf46c80543d2a5e8f99e56c712ec7cb2896cdc0f5` |
| Android server | `a5e307a072dac91a766e929733d187c531d30271eb3a7b19cbd6923349685c47` |

Desktop was freshly published. The optional native test-window input changed
the canonical native fingerprint, so the production executable was rebuilt
from the same clean corrective source even though production native sources
remain identical to g3a99a1ed. Its bytes/hash differ; no byte-identical rebuild
or historical hardware coverage is claimed. Canonical native provenance passed.
Server source history/fingerprint/hash and reviewed imported components passed
the existing staging provenance checks. Comparing runtime inventories against
g3a99a1ed found only scrcpy.exe changed: server, ADB, SDL, FFmpeg and both image
assets remain byte-identical. No shared ADB process was operated.

Eight separate process tests passed again using the new package's production
native entry point and DLLs plus the separately linked no-device fixture.
This distinguishes malformed-bootstrap entry checks from fixture handshake,
Focus/Stop and parent-death tests; it is not a device-enabled Mirror run.
SpecGen, build metadata, warning-free builds, DocsCheck and diff checks passed.
Unchanged cross-language codec and legacy-suite results remain earlier evidence,
not fresh executions in this task. The new package was not launched against a
phone; its foreground and media behavior still require the single proposed
authorized USB Focus session. All earlier artifacts/evidence remain preserved.

After source review and separate readiness approval, use this exact package
with the existing isolated root, without changing the profile or fallback:

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p06c-g81fba9b1'
& "$package\ScrcpySeamless.Desktop.exe" '--dev-data-dir=D:\My Projects\scrcpy-seamless\.dev-data\p06c' "--device-runtime=$package\runtime" --page=devices
```

This is a proposed future command, not an executed phone operation. Do not
repeat pairing, recovery, same-root activation or the close campaign. Capture
running evidence before the single Focus, separate the fresh command outcome
from observed foreground behavior, preserve exact identity and settle the one
session normally. A platform denial remains best effort and requires explicit
review disposition rather than retries until success. Phase 6C remains unaccepted.

### Supervised Focus retest blocked before native launch on 2026-10-01

Independent static review found no blocking issue in the published exact-child
permission correction. It did not provide an independently executed Windows
test run, phone-backed Focus evidence or final acceptance. This supervised task
started with clean branch `2.0/p06c-desktop-ipc` at reviewed HEAD
`833988560a2d43dfbb4125799869a7807b7325f5`. Only this record and the execution
plan differed from frozen package source
`81fba9b17843fae03b18812bb7bae831e13d540a`.

The existing g81fba9b1 ZIP SHA-256 remained
`cc31ca51a770266bbed79d2be1ab69ae1fd8a2ae1acc4f2af3dfcd6bb4805660`.
Fresh independent VerifyOnly extraction passed. Actual intended-run-directory
hashing matched every prescribed EXE/DLL, including Infrastructure and the
newly rebuilt native, both inventory documents, server and all 248 package/12
runtime entries. This is external artifact verification; the copied production
snapshot's BuildIdentity verification field was not changed to manufacture it.
Nothing was rebuilt, relabelled or substituted. Earlier packages, manifests,
incident records, hardware snapshots and synthetic Focus evidence remain intact.

The maintainer confirmed readiness and computer-use approval for the bounded
USB sequence, including one ordinary pointer Focus click by the operator.
USB remained attached; the existing committed profile was selected without
editing settings or fallback. One visible responsive idle Desktop started from
the exact package with the existing isolated root and no native child. Its
PID was 61712, creation `2026-10-01T18:09:47.0341293Z`, HWND 8265980.

| Checkpoint | Actual observation and limit |
| --- | --- |
| Idle startup | Correct explicit machine host and local data scope were visible; no automatic native child was found. |
| Route/profile selection | One explicit Refresh completed; the USB route with Device state was explicitly selected, then the existing saved DEV profile. Configuration files were not edited. |
| One Mirror click | UI reported `Selected Wi-Fi endpoint differs from the saved profile; Apply the intended endpoint first.` The selected observation still described a USB route. No second Mirror was attempted. |
| Acquired native session | None. The native PID field remained empty, and the read-only exact-package process query found zero native processes. |
| Video, PC control, audible PC audio | Not tested on this artifact in this run; no running mirror existed. Earlier g3a99a1ed results are not transferred. |
| Focus | Zero invocations. No fresh FocusStatus, foreground measurement or visual Focus result exists. Grant return, wire acknowledgement and native platform result remain unknown. |
| Desktop Stop | Zero invocations; no acquired session existed. No successful Stop or session-cleanup result is claimed. |

Source inspection located the emitted message in
DeviceSessionViewModel's committed-profile comparison: a non-null selected
connection endpoint unequal to the committed profile endpoint returns before
NativeLaunchPreflight and host startup. This establishes the observed pre-launch
boundary, not a native/IPC/Focus defect or a complete cause for the mismatch.
Configuration contents and private endpoint values were not published. The
task stopped before another Mirror, profile change or retry; resumption needs a
separate decision about the intended endpoint/profile preparation.

One explicit Copy session evidence action reported success. Its fresh 1,639-byte
version-1 snapshot, captured `2026-10-01T18:13:31.1487317+00:00`, retained
ObservationStage NoSession, null SessionId/ProcessId, handshake NotRecorded,
NativeReadyObserved false, lifecycle total/retained/omitted counts zero,
SessionResult NotRecorded, Cleanup NotStarted/intent None and Stop NotRequested.
No old clipboard result or terminal result was substituted. Snapshot SHA-256:
`52aa6a4fd078ec9ece602ac22bb743e6dcb0b2bb970753c414bcaa187a9000b0`.

New bounded local records are retained without overwrite under ignored
`work/phase6c/focus-retest-g81fba9b1-20261001T180902Z-7d48e43a/`:
preflight, exact Desktop identity, separate blocked-launch UI/process
observation, explicit NoSession snapshot and preservation checks. Known
configuration and backup bytes matched preflight. The shared ADB retained PID
16592, the exact original DEV executable and creation instant
`2026-10-01T14:33:40.2774010Z`; the read-only listener query still identified that
PID on port 5037. No independent ADB command or shared-server restart occurred.
A preliminary preservation flag was invalid because JSON DateTime coercion
followed by implicit string formatting lost fractional precision. Its original
record remains retained; `06-preservation-identity-validated.json` supersedes
that flag using DateKind String and invariant offset-aware full UTC-tick
comparison. Original timestamps were not rounded or overwritten.

Desktop remains idle with no acquired child while normal closure is awaiting
the maintainer's decision. This is not an active-mirror close test. No phone
recovery, pairing, activation, close campaign, stress, new test run or production
change occurred. Focus and the requested USB/Focus/Stop sequence remain pending;
Phase 6C remains unaccepted. AGENTS/docs impact is limited to observed validation
and current status; no architecture, protocol, ownership, path or workflow changed,
so no AGENTS/runtime-guide edit is required. Documentation checks do not establish
hosted CI or hardware acceptance. This task permits only local documentation
commits; no remote publication is authorized.

### Supervised corrected Focus session on 2026-10-01

The maintainer separately authorized updating only the existing DEV profile's
connection endpoint and resuming the blocked retest. The same primary Desktop
and frozen g81fba9b1 package were reused. Starting local HEAD was
`e2dbb516404a7c0aa3dc486faede10e65e447816`; its difference from package source
`81fba9b17843fae03b18812bb7bae831e13d540a` remained documentation-only.
The preceding independent ZIP extraction and actual 248-package/12-runtime
inventory checks still identify these bytes, including Infrastructure and the
rebuilt native executable. No rebuild, relabelling or DLL substitution occurred;
the ZIP hash remains the value in the corrected artifact table above.

Save to draft followed by Apply reported Configuration saved. A read-only
comparison with the preceding configuration backup found exactly one changed
field: Profiles[0].ConnectionEndpoint. The private value is not published.
Fallback, device identity and unrelated settings were unchanged. One explicit
Refresh and USB-route selection preceded one new Mirror click. This acquired
one session; the earlier blocked click acquired none and remains recorded above.
USB stayed attached throughout, with no independent ADB commands or pairing.

| Identity | Running and post-Focus observation |
| --- | --- |
| Desktop | PID 61712; Get-Process creation `2026-10-01T18:09:47.0341293Z`; HWND 8265980; exact package Desktop EXE |
| Native child | PID 61604; Get-Process creation `2026-10-01T18:34:47.9035425Z`; HWND 2954026; exact package runtime/scrcpy.exe; parent PID 61712 |
| Session | `ea75bedc-a7ef-4de2-a67b-2335c390777d` |
| Connection attempt | `394b7351-0987-46e2-be9f-83cb01fd98de` |

The maintainer independently confirmed updating video, usable PC control and
audible PC audio before Focus and reconfirmed all three afterward. Production
evidence reports the first video frame, not audio/control readiness. Running
and post-Focus snapshots retained an accepted actual handshake (protocol 1.0;
focus-window, lifecycle-v1, stop; zero omitted capabilities), NativeReady,
Connecting and StreamStarted, sequences 1–3 with zero omitted lifecycle entries.
SessionId, native PID, exact path, full-precision creation instant and HWND
remained unchanged. Creation comparisons used invariant DateTimeOffset parsing
and full UTC ticks; original source timestamps were retained without rounding.

The operator brought the original Desktop forward with ordinary pointer input,
leaving the normal visible, non-minimized native window behind it. A hidden,
bounded read-only observer sampled only foreground classification
desktop/native/other and exact-native owner/visibility/minimized checks; it
collected no unrelated titles, selectors or command lines and called no
activation, permission or input API. The maintainer explicitly directed the
operator to click and measure Focus rather than supplying a separate visual
Focus report.

Exactly one ordinary Computer Use pointer click targeted Focus mirror. The
pre-click sample at `2026-10-01T18:42:15.9469287Z` identified Desktop foreground
and the exact visible, non-minimized child. The click call was bounded by
`2026-10-01T18:42:16.0439715Z` and `2026-10-01T18:42:16.9716513Z`; first native
foreground was observed at `2026-10-01T18:42:16.4877480Z`. Of 300 samples,
the first eight were desktop and the remaining 292 native, through
`2026-10-01T18:42:34.8402489Z`. The nominal 50-ms polling interval took about
18.9 seconds overall; no strict 15-second deadline or continuous observation
between samples is claimed. All samples retained exact-native ownership,
visibility and non-minimized state. No other app activation, input or screenshot
capture occurred during measurement. A later screenshot separately showed the
mirror above Desktop; it is not the basis for the earlier foreground result.

FocusStatus changed from empty to `Focus request ran; Windows foreground
activation is not guaranteed.` This is a fresh attempt outcome, separate from
the measured foreground transfer. Permission-call return, per-request wire
acknowledgement and native platform return remain unknown. Returning to Desktop
for the explicit post-Focus Copy action occurred after measurement and is not
a failure of the transfer. No repeated Focus, external permission grant,
simulated Alt, input-thread attachment or topmost change was used.

One Desktop Stop ended the same session. The UI reported Native session stopped.
The retained Released snapshot recorded Completed/UserStop, terminal sequence
4 SessionStopped/UserStop with no error, Cleanup Succeeded, first initiating
intent ExplicitStop, StopCallState Succeeded, requested reason UserStop,
ExactChildExitObserved true and OwnershipReleased true. These typed managed
results accompany the external absence of PID 61604 and zero replacement native
processes; absence alone is not the cleanup proof. Wire Stop queued/written/
acknowledged, process exit code, escalation and phone-side cleanup remain
NotRecorded/unknown as actually exported. Final evidence was saved before
ordinary closure of idle Desktop; PID 61712 then exited with no replacement.

New bounded records remain ignored and preserved under
`work/phase6c/focus-retest-resumed-g81fba9b1-20261001T182754Z-d22594e7/`.
Only the existing explicit Copy action supplied session evidence; there was
no second lifecycle/stdout consumer or unrelated clipboard read.

| Explicit snapshot | Original CapturedUtc | Bytes | SHA-256 |
| --- | --- | --- | --- |
| Running, 04-running-session-snapshot.json | `2026-10-01T18:41:17.7222436+00:00` | 2922 | `14e120ed4f8f5fc76294a134e5ff4ffe5a233f4cf83e34b54ff452c783a4a6a4` |
| Post-Focus, 06-post-focus-session-snapshot.json | `2026-10-01T18:43:31.7838268+00:00` | 2922 | `fe9b30c95867e348b869d18720a22b625d7380ddb13fa37e2a7f7fdf3fcd6179` |
| Final, 09-final-session-snapshot.json | `2026-10-01T18:44:56.8017778+00:00` | 3353 | `71b6c6e580a4458995f81775149b198fed9bbcbf0c5fc1bf751ce22b5ea45a2d` |

The 62,837-byte foreground record has SHA-256
`53396bce34d828a5c2aa3bff7659d9160918dfc973c1bba551a335d5e63d6db4`.
Separate records retain profile-change paths, both process timestamp sources,
before/after media confirmations, fresh FocusStatus, identity comparisons,
exact-child exit, preservation checks and idle closure. Configuration bytes
matched the authorized Apply throughout the subsequent session. The original
shared ADB retained PID 16592, its exact executable and original CIM creation
instant `2026-10-01T14:33:40.2774010Z`, with listener 5037 unchanged. A preliminary
check compared that CIM timestamp with Get-Process's higher-precision StartTime
and produced an invalid negative flag; record 11 supersedes it with same-source,
full-tick equality while preserving both original timestamps and record 10.
No shared-server stop/restart or personal-installation operation occurred.

This supports the corrected production Focus route and normal USB/Stop behavior
in this exact session and environment. It does not reconstruct every detail of
the historical failure or guarantee Windows foreground permission in every
window/input/elevation state. Older recovery, disabled-fallback, activation and
distinct close results remain attached only to g3a99a1ed; they were not repeated
or transferred to the rebuilt g81fba9b1 native. Hosted CI and final Phase 6C
acceptance remain separate pending gates. All previous packages and evidence
remain intact. AGENTS/docs impact is validation/current status only: architecture,
protocol, runtime inputs and workflow did not change, so AGENTS/runtime-guide
edits, rebuilding and a blanket automated test rerun are unnecessary. Only local
documentation commits are authorized; stop for review with no remote operations
or Phase 7/8 work.
