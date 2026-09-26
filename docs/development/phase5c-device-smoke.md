# Phase 5C DEV device smoke (manual, pending)

This procedure tests the local Phase 5C compatibility adapter. Automated
tests use synthetic ADB and native children; they do not establish hardware
success. Use one active DEV mirror at a time. Close earlier DEV mirrors through
their normal Stop/close controls before starting. Do not use the personal
portable installation as a test build.

From `D:\My Projects\scrcpy-seamless`, resolve the staged package without
switching runtime or configuration during the test:

```powershell
$sourceSha = (git rev-parse --short=8 HEAD).Trim()
$package = (Resolve-Path ".\dist\dev\scrcpy-seamless-desktop-p05c-g$sourceSha").Path
$runtime = Join-Path $package 'runtime'
$data = 'D:\My Projects\scrcpy-seamless\.dev-data\p05c'
& (Join-Path $package 'ScrcpySeamless.Desktop.exe') "--dev-data-dir=$data" "--device-runtime=$runtime" --page=devices
```

1. Connect the intended phone by USB and leave Wi-Fi on. In Devices, press
   **Refresh** and explicitly select its eligible USB ADB transport. Open
   **Wireless setup** and then open **Pair device with pairing code** on the
   phone. The setup entry runs one discovery snapshot; press **Refresh** after
   opening the phone dialog if the snapshot was too early. Confirm that a
   single intended pairing service is visibly selected without copying an IP
   address. Multiple candidates require an explicit choice. If no pairing
   service is found, record whether the result says empty, failed or cancelled;
   **Enter address manually** remains available. Discovery can be checked
   without submitting another pairing code when the device is already paired.
   If a repeat pairing is intentionally needed, use a fresh code and the
   pairing endpoint shown in that phone dialog. Pair does not require the
   separate Wireless debugging connection endpoint. Do not send a pairing
   code or private device address to chat.
2. Check connected ADB transports after Pair or discovery. If the shared ADB
   server has already connected the phone, select that observed transport;
   otherwise use **Connect selected endpoint** with the distinct connection
   service or its manual address. Neither action saves a profile or starts
   mirroring.
3. In Profiles, create or select the intended DEV profile. Save the USB serial
   and Wi-Fi connection endpoint explicitly with **Apply**. Return to Devices,
   choose the saved profile in **Mirror session**, and press **Mirror**. Pair
   and Mirror contact the device; pairing does not save the profile.
4. Confirm USB video, PC control and audible PC output separately. Record the
   owned PID and observed HWND shown in the session panel. For an independent
   exact-path check, run:

   ```powershell
   $nativeExecutable = Join-Path $runtime 'scrcpy.exe'
   Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -ieq $nativeExecutable } |
     ForEach-Object {
       $owned = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
       [pscustomobject]@{ PID = $_.ProcessId; HWND = $owned.MainWindowHandle; Title = $owned.MainWindowTitle; ExecutablePath = $_.ExecutablePath }
     }
   ```

5. Keep the same local sound source playing, physically disconnect USB and
   wait for native Wi-Fi recovery. Run the same exact-path check. Compare PID
   and HWND, then independently report video, PC control and PC audio. A
   running process alone is not evidence that those channels recovered.
6. Press **Stop** in Desktop and confirm the owned process exits. Reopen the
   same DEV data root in settings-only mode and confirm saved settings remain.
   The app must not intentionally stop the shared ADB daemon.

Sanitized lifecycle JSONL is under `$data\native-sessions`; it contains local
receive order/time and owned process identity, not raw native output. It does
not prove precise reconnect timings or audible output. No pairing code, key,
private identifier or personal configuration needs to be pasted into chat.

If discovery remains empty or fails while the phone pairing dialog is open,
the maintainer may run the following optional check with this package's
bundled ADB. These client commands can start the shared ADB server if it is
not already running; do not run them during another active mirror or treat
`adb version` as proof of the server's binary or mDNS backend. Sanitize all
addresses, serials and instance names before sharing output.

```powershell
$adb = Join-Path $runtime 'adb.exe'
& $adb version
& $adb mdns check
& $adb mdns services
```

Record the result of `mdns check`, the count and service types (`_adb-tls-pairing`
versus `_adb-tls-connect`) from `mdns services`, and whether Desktop's visible
pairing list matches. The existing shared server's startup environment/backend
is otherwise unknown; do not restart it as a diagnostic shortcut. ADB 34.0.5
may return `ERROR: mdns daemon unavailable` from `mdns check` with exit code
zero. That means discovery is unavailable on the current server and must not
be reported as "no phone found" merely because `mdns services` is empty.
