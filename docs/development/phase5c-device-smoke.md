# Phase 5C DEV device smoke (primary manual path passed)

This procedure tests the local Phase 5C compatibility adapter. Automated
tests use synthetic ADB and native children; they do not establish hardware
success. Use one active DEV mirror at a time. Close earlier DEV mirrors through
their normal Stop/close controls before starting. Do not use the personal
portable installation as a test build.

From `D:\My Projects\scrcpy-seamless`, use the verified package staged from
source commit `2b90206595932f78c02a3e4ea383a27d15644c2e`. Later docs-only
commits do not change its directory name or runtime:

```powershell
$package = 'D:\My Projects\scrcpy-seamless\dist\dev\scrcpy-seamless-desktop-p05c-g2b902065'
$runtime = Join-Path $package 'runtime'
$data = 'D:\My Projects\scrcpy-seamless\.dev-data\p05c'
& (Join-Path $package 'ScrcpySeamless.Desktop.exe') "--dev-data-dir=$data" "--device-runtime=$runtime" --page=devices
```

1. Connect the intended phone by USB and leave Wi-Fi on. In Devices, press
   **Refresh** and explicitly select its eligible USB ADB transport. If the
   phone is already paired and its `_adb-tls-connect` service or network ADB
   transport is visible, do not pair again. Open **Wireless setup** only to
   inspect the separate connection service if its endpoint is needed for the
   profile. For an unpaired device, open **Pair device with pairing code** on
   the phone; the setup entry runs one discovery snapshot, and **Refresh**
   can update an early snapshot. Choose the intended pairing service or
   **Enter address manually** when discovery is unavailable. Use only a fresh
   code and pairing endpoint from that phone dialog. The pairing endpoint is
   not the Wireless debugging connection endpoint. Do not send a pairing
   code or private device address to chat.
2. Check connected ADB transports after Pair or discovery. If the shared ADB
   server has already connected the phone, select that observed transport;
   otherwise use **Connect selected endpoint** with the distinct connection
   service or its manual address. Neither action saves a profile or starts
   mirroring.
3. In Profiles, create or select the intended DEV profile. The current ADB
   observation row displays the model before the USB transport serial; save
   the USB serial, not the model. This is guidance for the current display,
   not an ADB parsing rule. Save the distinct Wi-Fi connection endpoint from the
   `_adb-tls-connect` connection service, not the pairing-code endpoint.
   Select USB as preferred transport, allow fallback, then use **Save to
   draft** followed by **Apply**. Return to Devices,
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

The exact reviewed ADB 34.0.5 runtime now gives its child processes an
Openscreen startup setting, but an existing shared server retains its backend.
The controlled Openscreen startup check reported a healthy backend and the
owner observed the pairing service appear immediately while the phone's code
dialog was open. The owner also reported that the pairing device appeared in
the final `scrcpy-seamless-desktop-p05c-g2b902065` package. That run used the
already-started Openscreen server, so it does not independently verify the
new package's cold-start policy.

## Observed Mirror smoke — 2026-09-26

The owner ran the same `g2b902065` DEV package with the isolated
`.dev-data\p05c` root and one active mirror. The saved DEV profile had an
explicit USB serial, a distinct Wi-Fi connection endpoint, USB preference,
fallback and reconnect enabled. An initial Mirror attempt was blocked before
native launch because the model shown in the current ADB observation row was
entered as the USB serial. After the serial was corrected
and the profile was saved through **Save to draft** and **Apply**, the owner
explicitly selected the USB observation and launched Mirror. The adapter
passes that selected USB serial to native `-s`; the selected target and
successful launch are evidence of initial USB use, rather than cable presence
alone.

With USB connected, the owner confirmed changing video, PC control and
audible PC audio. The exact-path native process was
`runtime\scrcpy.exe`, PID `34212`, start time
`2026-09-26T13:22:12.9316230Z`, and nonzero HWND `1641858` titled
`Phone-Seamless`. The owner physically disconnected USB while Wi-Fi stayed
enabled while testing the same local audio source. After recovery, the owner
confirmed video, PC control and audible PC audio. A second exact-path process
sample had the same PID, start time and HWND. These two samples support
process/window continuity at the sampled points; they do not prove every
intermediate instant or automated channel readiness.

The owner then pressed **Stop**. The mirror window closed, Desktop reported
`stopped`, and an exact-path process check found zero remaining native
processes. The isolated profile remained saved, and the existing shared ADB
server was still running. The sanitized lifecycle log recorded `started`,
`stop_requested` and `exited` for this owned PID. No pairing was repeated, and
the shared ADB server was not restarted during this smoke.

This is one successful primary USB-to-Wi-Fi-and-Stop hardware cycle. Broader
device coverage remains for Phase 5D. In particular, the selected package's
first-run discovery from an absent shared ADB server remains unverified and
must be checked during integrated Phase 5D validation with separate approval
before any controlled shared-server restart.
