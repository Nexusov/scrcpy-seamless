# Phase 6C DEV manual acceptance proposal

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
