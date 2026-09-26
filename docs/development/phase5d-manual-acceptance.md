# Phase 5D manual acceptance (prepared; not executed)

Use only the frozen Phase 5D source `9a02818b35aa300a4625edcd97a8794f69149613`
and DEV package `D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-g9a02818b`.
The ZIP at the same path plus `.zip` has SHA-256
`95ac4a39dc8d99609f652642c29ae2b35e0cd4c067b0f3195414c1bb1d7de058`.
The isolated data root is `D:\My Projects\scrcpy-seamless\.dev-data\p05d`;
it was created empty and must not be reseeded from earlier DEV or personal
installations. Keep one active mirror at a time. Do not share pairing codes,
device addresses, serials or private traces.

From PowerShell, these are the exact package launches. Preview is isolated;
settings-only has no device adapter; only the last command enables explicit
device actions. Close one window before launching a different mode on the same
data root.

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05d-g9a02818b'
$devData = 'D:\My Projects\scrcpy-seamless\.dev-data\p05d'
& "$package\ScrcpySeamless.Desktop.exe" --preview --scenario=fallback
& "$package\ScrcpySeamless.Desktop.exe" "--dev-data-dir=$devData" --page=settings
& "$package\ScrcpySeamless.Desktop.exe" "--dev-data-dir=$devData" "--device-runtime=$package\runtime" --page=devices
```

The directory and ZIP were validated from a clean extraction path containing
spaces. Local preview/settings/profile window inspection did not contact ADB,
did not write to the empty p05d data root, and did not test a phone.

## Cold-start discovery precondition and separate permission

A read-only preparation check found an existing `adb.exe` PID 34800 listening
on `127.0.0.1:5037`. The absent-server precondition is therefore **not met**. This
procedure must wait until the shared server is naturally absent or the owner
separately approves a safe moment for one controlled stop. Stopping that server
temporarily interrupts ADB connections held by Android Studio and other apps;
this task does not authorize the stop. Do not delete ADB keys, unpair the phone,
change global environment variables or manually prestart a replacement server.

At an agreed safe moment, close other ADB-using tools. Inspect the process,
executable/start time and port-5037 listener with these read-only commands:

```powershell
Get-CimInstance Win32_Process -Filter "Name='adb.exe'" | Select-Object ProcessId,ExecutablePath,CreationDate
Get-NetTCPConnection -LocalPort 5037 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess
```

**Do not run the next command without separate authorization.** It is the
proposed single shared-server stop and may temporarily disconnect Android
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
   150% application scale. Record whether inspection used Windows UI
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
