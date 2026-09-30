# Phase 6C DEV manual acceptance proposal

The original run below is frozen against `gd9d12a12`. Current phone validation
remains paused. The pending [consolidated evidence retest](#proposed-consolidated-supervised-retest)
uses the separately identified cleanup-initiator corrective artifact below after
independent source review and separate hardware authorization; do not resume
the old commands.

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

The new package was staged from clean committed source
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
