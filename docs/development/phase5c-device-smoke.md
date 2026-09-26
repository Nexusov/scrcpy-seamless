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
   **Refresh** and explicitly select its eligible USB ADB transport. Refresh
   contacts the shared ADB daemon. If already paired, use the explicit Connect
   action with the connection endpoint; otherwise Pair with separate pairing
   and connection endpoints. Use the IP and port shown inside the phone's
   **Pair device with pairing code** dialog for Pairing host:port; the ordinary
   Wireless debugging connection port serves a different purpose. Verify that
   the rebuilt Desktop reports pairing success after entering a fresh code.
   Do not send the pairing code to chat.
2. In Profiles, create or select the intended DEV profile. Save the USB serial
   and Wi-Fi connection endpoint explicitly with **Apply**. Return to Devices,
   choose the saved profile in **Mirror session**, and press **Mirror**. Pair
   and Mirror contact the device; pairing does not save the profile.
3. Confirm USB video, PC control and audible PC output separately. Record the
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

4. Keep the same local sound source playing, physically disconnect USB and
   wait for native Wi-Fi recovery. Run the same exact-path check. Compare PID
   and HWND, then independently report video, PC control and PC audio. A
   running process alone is not evidence that those channels recovered.
5. Press **Stop** in Desktop and confirm the owned process exits. Reopen the
   same DEV data root in settings-only mode and confirm saved settings remain.
   The app must not intentionally stop the shared ADB daemon.

Sanitized lifecycle JSONL is under `$data\native-sessions`; it contains local
receive order/time and owned process identity, not raw native output. It does
not prove precise reconnect timings or audible output. No pairing code, key,
private identifier or personal configuration needs to be pasted into chat.
