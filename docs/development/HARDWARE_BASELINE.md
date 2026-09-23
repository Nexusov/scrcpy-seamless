# Manual 1.x hardware baseline

Status: owner-accepted Phase 0 hardware baseline on 2026-09-24, with the
documented measurement limits and remaining 2.0 validation risks.
Automated coverage and current restrictions are in the
[baseline](../architecture/SEAMLESS_1_BASELINE.md).
This procedure characterizes 1.x and must not silently apply future
[2.0 semantics](../architecture/SEAMLESS_2_TARGET.md).

## Preconditions and isolation gate

- Use a named source commit and checksum-verified artifact; record whether each
  component was source-built or imported.
- Earlier OFFLINE bootstrap artifacts reject device operations. The owner has
  since explicitly approved shared ADB trust/server for the new DEV artifact;
  its files, settings, temporary data, shortcuts and activation remain separate.
- Live tests require a separately reviewed build and an owner-approved dedicated
  test environment with isolated Windows/ADB identity, or explicit acceptance of
  shared ADB trust. A separate ADB port is not key isolation.
- Keep the personal installed copy read-only. Never borrow its JSON, keys,
  shortcuts, binaries or logs. Use disposable package-local settings, a test
  phone, synthetic profile labels and a private shortcut directory.
- Do not kill a shared ADB server or unrelated application process. Record owned
  process identities, and define cleanup before pairing or changing device state.
- Keep pairing codes, key material, raw serials, IP addresses and personal screen
  content out of committed evidence. Use local aliases such as device-A.

Report only the portions actually observed. A missing phone or approved
environment does not prevent the Phase 0 source inventory.

## Owner-reported Phase 0 observations

The owner tested the unpacked DEV package based on commit `ea193f21c0b179081909a18d2bda4f404036530e`.
This is manual visual/interaction evidence, not a synchronized transport trace.

| Observation | Evidence level and limit |
| --- | --- |
| Start.vbs launches; mirroring video and PC control work | Owner observed on the DEV build |
| With Wi-Fi disabled during USB operation, video and PC control continue | Owner observed; no measured resource/lifecycle data |
| After USB disconnection, visual USB-to-Wi-Fi failover works, the window remains visible, and mirroring resumes or continues | Owner observed; post-failover PC control was not separately measured |
| No obvious window replacement or failure | Owner observed visually; subsequent endpoint samples also matched PID/HWND |
| Audio is audible with USB and Wi-Fi, after USB removal, after Wi-Fi restoration, and after USB reattachment | Owner observed; occasional perceived volume jumps after USB removal; recovery timing and stable quality were not measured |
| After Wi-Fi was disabled with USB reattached following Wi-Fi failover, screen and audio were unavailable | Owner observed; consistent with the current lack of automatic Wi-Fi-to-USB failback, not proof of a fresh USB connection failure |
| A fresh DEV launch with Wi-Fi disabled and USB connected provided video and audio | Owner observed in a separate USB-only startup; confirms the USB startup path worked in this run, not automatic failback in the earlier session |

The package contains `app/scrcpy.exe`; `scripts/package.ps1` copies it there and
`launcher/launch-runtime.ps1` starts that exact executable. A read-only Windows
process query on 2026-09-24 found an active process at this path named
`scrcpy.exe`, with a mirror window. The owner captured PID `28600`, HWND
`594730`, and title `Phone-Seamless` during USB operation with Wi-Fi disabled.
After the reported recovery and further transport changes, a read-only query
of the same exact executable path found PID `28600` and HWND `594730` again.
Thus native PID/HWND matched at two checkpoints of this manual run; intermediate
states and repeated-cycle reliability were not sampled. The owner's attempted
second shell command failed because copied PowerShell prompts, continuation
markers, Markdown fences, and prior output were submitted as commands. The
independent after-query supplies the second sample. An earlier
`Get-Process scrcpy` no-match remains inconclusive: the executable name really
is `scrcpy.exe`, and a no-match at one instant is not a Seamless failure.

For future repeatable cycles on this exact DEV build, run only the commands
inside the following block while USB mirroring is active and again after
unplugging USB and waiting for Wi-Fi video recovery. Do not paste shell prompts
(`PS ...>` or `>>`), Markdown fence markers, or displayed results:

```powershell
$nativeExecutable = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-win64-dev-gea193f2\app\scrcpy.exe'
Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -ieq $nativeExecutable } |
    ForEach-Object {
        $windowProcess = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        [pscustomobject]@{
            ProcessName = $_.Name
            ExecutablePath = $_.ExecutablePath
            PID = $_.ProcessId
            MainWindowTitle = $windowProcess.MainWindowTitle
            MainWindowHandle = $windowProcess.MainWindowHandle
        }
    } | Format-List
```

Compare `PID` before and after to confirm native process continuity. Compare
`MainWindowHandle` to check window identity as well; the title alone is not an
identity. If the query returns nothing at either point, record the timing and
do not count it as proof of process replacement.

Audio return was observed, but stable audio quality and recovery timing remain
unverified. The native log contains repeated audio buffer sample-skip messages;
without timestamps correlating them to the reported volume jumps, they do not
establish a cause. For a repeatable audio check, use combined USB + Wi-Fi mode
with Seamless reconnect enabled, ordinary audio playback enabled, and no
recording or positive time limit (legacy reconnect restrictions). Disable
neither audio nor audio playback. On a supported phone, play a local test file
with speech or repeated tones. First confirm sound reaches the PC
speakers/headphones during USB mirroring. Keep Wi-Fi available, unplug USB
while playback continues, wait for Wi-Fi video recovery, and confirm sound
resumes through the PC. Record whether
the sound stopped, how long recovery took, and whether manual action was needed.
Check PC control separately after failover. A moving video frame does not prove
audio recovery. If audio was unavailable before unplugging, mark recovery NOT
APPLICABLE for that run and record why; some Android versions and apps restrict
audio capture. With both USB and Wi-Fi unavailable, absent audio is expected.
After fallback to Wi-Fi, reattaching USB does not automatically move the current
native session back to USB: retry retains the Wi-Fi serial and does not reselect
USB in `src/scrcpy/app/src/scrcpy.c`. The owner subsequently performed a fresh
USB-only launch with Wi-Fi off and observed video and audio. This confirms that
USB startup worked in that separate run; Wi-Fi-to-USB failback remains absent.

## Local DEV observation without changing the build

The repository-only `scripts/observe-dev.ps1` observes the named package's
native process, its existing log files and local Wi-Fi adapter status. Optional
`-IncludeAdb` polls `adb devices` through the package-local client every three
seconds. That command can start the approved shared ADB server if it is not
already running; it does not pair, connect or change device configuration.
Run the observer in a separate PowerShell terminal before a
controlled transition; it stops after the requested duration:

```powershell
Set-Location -LiteralPath 'D:\My Projects\scrcpy-seamless'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\observe-dev.ps1 -PackageDirectory '.\dist\dev\scrcpy-seamless-win64-dev-gea193f2' -DurationSeconds 180 -IncludeAdb -InteractiveMarkers
```

Perform the desired DEV actions while it runs. Its final line gives the local
`work/phase0/observations/.../trace.jsonl` path. That directory is ignored by
Git. The JSONL contains the native executable SHA-256, UTC collection times,
exact-package PID/window handle,
resource counts, categorized native events and, when selected, aggregate ADB
endpoint counts.
It omits raw native messages, device identifiers, IP addresses, pairing codes,
keys, screen and audio content. The original package logs are not sanitized;
inspect them privately and share only reviewed diagnostic traces.

The observer skips prior log content by default. `-IncludeExistingLogs` reads
it as historical content, but cannot recover actual emission times. Even live
native-event timestamps are when the collector reads a line: the launcher
buffers file output and stdout/stderr are separate. ADB counts show endpoint
availability, not which transport the native session uses; a
`selected_transport` event is recorded only when that native message reaches a
log file. Wi-Fi adapter counts can include virtual adapters. Continue recording
manual video, sound and control outcomes separately.

With `-InteractiveMarkers`, focus the observer terminal and press one key
immediately after each action or observation: `1`/`2` for USB removed/attached,
`3`/`4` for phone Wi-Fi off/on, `5`/`6` for video lost/restored, `7`/`8` for
audio lost/restored, `9`/`0` for control failed/working, and `g` for an audible
glitch. These predefined labels contain no free-form device or media data.
They record when the collector processes the key, not the exact physical
transition instant. This mode requires an interactive console; omit the flag
for unattended capture.

### First collected DEV trace (2026-09-24)

The owner captured a completed 180-second run with the exact DEV executable
hash verified against the package. Its native process was already 22 minutes
old when collection began. All 103 process samples reported the same PID
`38868`, HWND `2494172` and responding state. ADB availability samples changed
from one `usbOrOther` plus one `networkLike` endpoint to network-only, then
both, then USB/other-only, and finally both again; one intermediate sample
reported an offline endpoint. These categories are availability hints, not
proof of the active transport or exact physical action.

The trace contains, in native-log order, one selected USB transport followed
by two selected TCP/IP transports, two stream-resume events, 69 reconnect
messages and 62 audio-buffer sample-skip messages. The native output arrived
in five large batches, so its collection times do not establish when those
events happened. The initial USB selection may predate collection entirely.
No owner action times or video/audio/control outcomes were recorded for this
particular trace, and their order cannot be reconstructed reliably from memory.
Do not infer audible recovery or the cause of volume jumps from it alone.
The owner accepted the combined manual baseline and trace as sufficient for
Phase 0 closure. The 62 sample-skip messages are observations, not a proven
audio defect. No further Phase 0 audio investigation is required.

## Capture method

Record Windows/Android versions, device model, display/audio capabilities, build
identity, selected options and transport mode. Use a monotonic stopwatch and
timestamped sanitized transitions. Capture native PID and HWND before and after
each disconnect; window title alone is not identity. Note video, audio and control
recovery independently. Use an approved inspector/test harness and semantic
readiness signals; a fixed sleep is not proof of readiness.

Record memory/handles/threads at comparable idle/streaming points if measuring
growth. Do not infer a leak from one noisy sample. Avoid recording personal phone
content; use a known test screen and a harmless local audio sample.

## Cases and expected 1.x behavior

| Case | Action | Expected observation / boundary |
| --- | --- | --- |
| USB setup | Launch with one authorized USB device, then with unauthorized/no device and multiple candidates | Clear selection/readiness behavior; no silent selection of an ambiguous device |
| Wi-Fi setup | Pair in approved environment, connect, close/reopen disposable settings | Pairing and connection endpoints remain distinct; no pairing code in saved settings/logs |
| Combined initial selection | Make USB and configured Wi-Fi available, then start | USB selected initially; wireless fallback target available |
| USB loss | Unplug during active combined-mode video/audio/control | Retain last frame/window; observe transition to Wi-Fi; same native PID/HWND; new valid frame restores input |
| Repeated recovery | Repeat controlled loss/recovery at least five times for this baseline | Record every attempt and separate channel outcomes; do not label this the later 100-cycle soak |
| Wi-Fi outage | Interrupt Wi-Fi after failover, then restore it | Record retries, retained presentation and recovery; current fixed/unbounded retry is a limitation |
| USB returns | Reattach USB after Wi-Fi recovery | Record actual behavior; automatic failback is not a current promise |
| Disconnect/close races | Close native window or request launcher stop while connecting/retrying | Owned child terminates; record any delay/forced fallback, hang or stale callback symptom |
| Input and presentation | Rotate/resize before loss, hold a harmless input action during loss, then recover | Record geometry/capture behavior and any replay; recovery is not accepted solely because a frame appeared |
| Retry/settings | While waiting/failed, change disposable settings, Retry, cancel old operation | New generation uses current settings; late old result does not launch a session |
| Reset isolation | Reset disposable phone config with mirroring preferences present; repeat while its native session is active | Only allowed phone config removed; active-session refusal; unrelated package/preferences untouched |
| Legacy option limits | Enable reconnect with recording, positive time limit, no-window/no-video/no-video-playback/no-playback | Validation rejection is expected in 1.x; zero deadline and disabled switches remain valid |
| Non-reconnect modes | Separately test recording, positive deadline and supported headless modes with reconnect disabled | Record normal lifetime/file finalization; do not infer reconnect support |
| Multiple installations | Use two disposable roots with approved isolated activation strategy | Record boundaries; unmodified 1.x global mutex/shortcut behavior is a known limitation |

On failure, preserve a sanitized local reproduction before changing code. Record
trigger, expected/actual behavior, earliest relevant event, owned process state,
and competing explanations. Follow [debugging.md](debugging.md). Stop any case
that would affect an unapproved personal installation or shared daemon.

## Sanitized report template

```text
Source commit / branch:
Artifact filename / SHA-256:
Native / server / ADB origins and versions:
Windows / Android / test device model:
Isolation strategy approved by owner:
Case ID / options / transport mode:
Result: PASS | FAIL | NOT RUN
PID/HWND retained: yes | no | not applicable | not observed
Video / audio / control outcome:
Elapsed transition / cancellation time:
Sanitized evidence path:
Failure details / uncertainty / follow-up Phase:
Cleanup complete / unrelated state unchanged:
```

Phase 8 introduces recording segments, overall reconnect deadlines and headless
recovery. Phase 12 hardens those semantics with comprehensive transport fault
injection, 100-cycle soak/resource budgets, accessibility and multi-monitor
coverage. These future gates are not implied by this limited baseline procedure.
