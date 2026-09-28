# Phase 5D manual acceptance (in progress)

## Targeted USB route retest

The corrected local DEV package is
`D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-gda35a1bd`
from code `da35a1bdffb5d90fe02d81e9e965a4195dc1b692`. Its verified ZIP
SHA-256 is `5a14f48b83b507aa19398a34c84fcc3063cdcd651495b4a62721e0a7ffea3cb5`.
All 12 bundled native/server/ADB/SDL/FFmpeg/image files match the earlier
`geb3de1c5` package. Do not apply that package's phone or cold-start results
to this build. Keep the existing `.dev-data\p05d` root and its saved profile
untouched; this retest uses a separate empty DEV root. Do not restart the shared
ADB server, pair again or use the personal installation.

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-gda35a1bd'
$devData = 'D:\My Projects\scrcpy-seamless\.dev-data\p05d-usb-route-da35a1bd'
& "$package\ScrcpySeamless.Desktop.exe" "--dev-data-dir=$devData" "--device-runtime=$package\runtime" --page=devices
```

1. With the USB cable attached, click Refresh and explicitly select the intended
   USB observation. Report whether its row says `USB route` rather than `Route
   unknown`. Do not copy the selector, IP address or pairing code into a report.
2. Open Profiles → New profile with a clean editor. Click **Use selected ADB
   transport in this profile draft**. Confirm that only USB serial fills; the
   pairing and connection endpoints remain empty and no profile/file has been
   saved yet. Enter a friendly alias, then click Save to draft → Apply. Confirm
   the saved profile has that exact USB selector. Do not transcribe it manually.
3. Return to Devices, explicitly select the USB observation and the new saved
   profile, then click Mirror once. Confirm changing video, usable PC control
   and audible PC sound independently. Click Stop and confirm the native window
   closes and Desktop reports stopped.

Report the package suffix and these three outcomes. If the row is still
Unknown, stop before saving or launching and retain the visible error/status.
This fix changes only discovery classification; it does not change launch
selection or fallback policy, so the prior enabled/disabled fallback checks
need not be repeated automatically. Add a separate supervised fallback check
only if the corrected selected-route workflow produces a new launch target or
fallback behavior inconsistent with the saved profile. This targeted retest
does not close the separate screen-reader, multi-monitor DPI or compact-window
vertical-density acceptance items.

## Earlier exact-artifact acceptance procedure and evidence

The earlier Phase 5D checks used source `eb3de1c55b159b980643ee6235e000770134a1b3`
and DEV package `D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-geb3de1c5`.
The ZIP at the same path plus `.zip` has SHA-256
`cad4bcba0b4139ec13bfc36d9010fc6a4209343a19d0ba5ad8d152dccc49888f`.
The earlier `g9a02818b` package is retained as historical local evidence,
not as a hardware pass for this one. Its 12 runtime files have identical
SHA-256 hashes in the current package; the managed Desktop build changed.
The isolated data root is `D:\My Projects\scrcpy-seamless\.dev-data\p05d`;
it was created empty and must not be reseeded from earlier DEV or personal
installations. Keep one active mirror at a time. Do not share pairing codes,
device addresses, serials or private traces.

From PowerShell, these are the exact package launches. Preview is isolated;
settings-only has no device adapter; only the last command enables explicit
device actions. Close one window before launching a different mode on the same
data root.

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-geb3de1c5'
$devData = 'D:\My Projects\scrcpy-seamless\.dev-data\p05d'
& "$package\ScrcpySeamless.Desktop.exe" --preview --scenario=fallback
& "$package\ScrcpySeamless.Desktop.exe" --preview --page=settings --ui-scale=1.5
& "$package\ScrcpySeamless.Desktop.exe" "--dev-data-dir=$devData" --page=settings
& "$package\ScrcpySeamless.Desktop.exe" "--dev-data-dir=$devData" "--device-runtime=$package\runtime" --page=devices
```

The ZIP was verified, extracted into a clean path containing spaces and its
preview opened from a working directory outside the checkout. Actual Windows
preview captures for compact Settings and light/dark Shortcuts are under
`work/phase5d/shortcuts-visual-review/geb3de1c5/`. The preview did not use
ADB or the p05d data root. The later partial manual results on this same
artifact are recorded in the [integrated matrix](phase5-acceptance.md#current-artifact-manual-observations).

## Cold-start discovery precondition and separate permission

On 2026-09-28 a read-only check found the existing DEV `adb.exe` PID 34800
listening on `127.0.0.1:5037`, with no established client connection in that
snapshot. The owner separately authorized one controlled stop. The exact
package's `adb.exe kill-server` exited 0; subsequent checks showed no ADB
process or port-5037 listener before Desktop launch. Desktop PID 32500 then
started from this package; its UI discovery request started the package's
`adb.exe` PID 49044, and `mdns check` reported Openscreen discovery. While the
phone advertised its pairing service, the DEV UI displayed that service and
`adb mdns services` counted one pairing service. No pairing was attempted.
The Desktop window was closed normally afterward; ADB PID 49044 remained
available. See the [current artifact observations](phase5-acceptance.md#current-artifact-manual-observations).

For any future repeat, stopping a shared server still requires new separate
authorization at a safe moment. It can temporarily interrupt ADB connections
held by Android Studio and other apps. Do not delete ADB keys, unpair the
phone, change global environment variables or manually prestart a replacement
server.

At an agreed safe moment, close other ADB-using tools. Inspect the process,
executable/start time and port-5037 listener with these read-only commands:

```powershell
Get-CimInstance Win32_Process -Filter "Name='adb.exe'" | Select-Object ProcessId,ExecutablePath,CreationDate
Get-NetTCPConnection -LocalPort 5037 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess
```

**Do not run the next command without separate authorization for that run.**
It is a shared-server stop and may temporarily disconnect Android
Studio and other ADB clients:

```powershell
& "$package\runtime\adb.exe" kill-server
```

After an approved stop, re-run the read-only checks to prove absence before
launching Desktop. Do not use an unscoped process-name kill.

If another tool restarts the server first, record the case as contaminated.
Launch the final device-enabled Desktop command above and request discovery
from its UI; do not prestart ADB manually. The reviewed ADB SHA-256 is
`58765259a349cce392fbb2f15dab75fed3b7c0b40cc68a7653278b9850602a2f`
(bundled version 34.0.5). For that hash the application sets
`ADB_MDNS_OPENSCREEN=1` only in its ADB/native child process environment; it
does not change the user's global environment or restart an already running
shared server. Only after the product starts the new server, inspect its
identity/start time and run `& "$package\runtime\adb.exe" mdns check` and
`& "$package\runtime\adb.exe" mdns services` (supported by this bundled
version). Confirm whether the intended pairing service appears while the
phone advertises it.
An already paired phone need not be paired again. Preserve the current
non-destructive manual-address fallback if discovery is unavailable.

## Real-window and hardware checks

1. Open the final package's isolated settings-only and device-enabled modes.
   Check keyboard Tab/Shift+Tab, focus after dialogs, shortcut behavior in
   text fields, visible disabled reasons, and reachability at 660×460 and
   150% application scale. In Settings, confirm the compact category selector
   sits above full-width search/results; check the read-only Shortcuts tab,
   its scrollable native conditions, MOD link and the existing control-center
   editor link. Confirm those reference actions do not start a mirror or change
   drafts. Record whether inspection used Windows UI
   Automation, a real screen reader, or only a visual check.
2. In Devices, Refresh and explicitly select the USB row whose route and
   **USB serial** are labeled; the model is only a friendly name. In Profiles,
   select the intended profile or choose New profile. Before entering other
   unsaved values, use **Use selected ADB transport in this profile draft**;
   check that only USB serial changed. Then enter a friendly display name,
   Save to draft and Apply. If an existing editor contains unstaged input,
   save or cancel that input first; the action must refuse to overwrite it.
   Return to Devices and explicitly choose both the saved profile and fresh
   USB transport for Mirror. An opaque mDNS network selector has no concrete
   host:port, so enter the connection endpoint manually from the phone's
   Wireless debugging screen when needed. Keep the pairing endpoint separate;
   do not pair again merely to complete profile setup.
3. With USB and Wi-Fi available and cross-transport fallback enabled, launch
   Mirror from the explicitly selected USB route. Confirm changing video,
   usable PC control and audible PC audio independently while a local sound
   continues on the phone. Record the exact native executable path, PID,
   process start time and nonzero HWND with the command below. Remove only USB;
   after Wi-Fi recovery, run the same command again,
   check all three channels independently and compare PID/start time/HWND at
   the two checkpoints. Stop in Desktop; confirm native exit, responsive UI,
   saved profile and continued shared ADB operation.
4. After stopping that mirror, reconnect USB and disable only cross-transport
   fallback in the profile. Use Save to draft, then Apply. Retain its saved
   Wi-Fi endpoint and separate reconnect preference. Explicitly launch on USB,
   remove USB and verify the session
   does not silently transfer to Wi-Fi. Inspect the prepared request/child
   environment evidence rather than interpreting another ADB connection as
   failover. A normal native disconnect/exit may be the expected legacy
   outcome. Restore the original saved fallback policy with Save to draft and
   Apply afterward.
5. In a short real-window check, try same-root activation and normal close
   while a mirror runs. Do not create a deliberately unkillable real child or
   corrupt live data; deterministic synthetic tests cover failed cleanup.

Use this same PowerShell snapshot before USB removal and after recovery; the
executable path is fixed to this package, so an unrelated scrcpy process is
not counted. Compare PID, `StartedUtc`, nonzero HWND and title, and report
video, PC control and *audible* PC sound separately. Matching samples prove
only the observed checkpoints.

```powershell
$nativeExecutable = "$package\runtime\scrcpy.exe"
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -ieq $nativeExecutable } | ForEach-Object {
    $nativeProcess = Get-Process -Id $_.ProcessId
    [pscustomobject]@{
        PID = $_.ProcessId
        StartedUtc = $nativeProcess.StartTime.ToUniversalTime().ToString('o')
        HWND = $nativeProcess.MainWindowHandle
        Title = $nativeProcess.MainWindowTitle
    }
} | Format-List
```

Record the actual source SHA, package/ZIP hashes, Android version, selected
route, expected/actual result and sanitized logs for each run. The previous
`g2b902065` smoke is historical evidence only. A timer, process existence or
decoded packet count does not establish audible output. Multi-monitor DPI and
screen-reader listening remain not run unless those environments are actually
available. A fresh failure should retain sanitized evidence before any retry
or product change.
