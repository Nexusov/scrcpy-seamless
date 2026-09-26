# Phase 5D manual acceptance (prepared; not executed)

This procedure will use one frozen Phase 5D DEV package and the isolated
`D:\My Projects\scrcpy-seamless\.dev-data\p05d` data root. The final package
path, source SHA and exact commands will be inserted after local validation.
Do not substitute the personal portable installation or an earlier Phase 5C
package. Keep one active mirror at a time. Do not share pairing codes, device
addresses, serials or private traces.

## Cold-start discovery precondition and separate permission

A read-only preparation check found an existing `adb.exe` process listening on
TCP port 5037. The absent-server precondition is therefore **not met**. This
procedure must wait until the shared server is naturally absent or the owner
separately approves a safe moment for one controlled stop. Stopping that server
temporarily interrupts ADB connections held by Android Studio and other apps;
this task does not authorize the stop. Do not delete ADB keys, unpair the phone,
change global environment variables or manually prestart a replacement server.

After separate authorization, close other ADB-using tools and inspect the
server process, executable path, start time and port-5037 listener before any
ADB command. If still present, perform only the specifically approved
one-time stop with the final package's bundled ADB, then verify absence using
process/listener inspection. If another tool restarts it first, record the
case as contaminated. Launch the final Desktop with its normal reviewed
process-local Openscreen policy and then request discovery from the UI; only
after that action inspect the new server identity/start time, `mdns check`
and `mdns services` with commands supported by bundled ADB 34.0.5. Confirm
whether the intended pairing service appears while the phone advertises it.
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
   process start time and nonzero HWND. Remove only USB; after Wi-Fi recovery,
   check all three channels independently and compare PID/start time/HWND at
   the two checkpoints. Stop in Desktop; confirm native exit, responsive UI,
   saved profile and continued shared ADB operation.
4. After stopping that mirror, disable only cross-transport fallback in the
   profile and Apply. Retain its saved Wi-Fi endpoint and separate reconnect
   preference. Explicitly launch on USB, remove USB and verify the session
   does not silently transfer to Wi-Fi. Inspect the prepared request/child
   environment evidence rather than interpreting another ADB connection as
   failover. A normal native disconnect/exit may be the expected legacy
   outcome. Restore the original saved fallback policy explicitly afterward.
5. In a short real-window check, try same-root activation and normal close
   while a mirror runs. Do not create a deliberately unkillable real child or
   corrupt live data; deterministic synthetic tests cover failed cleanup.

Record the actual source SHA, package/ZIP hashes, Android version, selected
route, expected/actual result and sanitized logs for each run. The previous
`g2b902065` smoke is historical evidence only. A timer, process existence or
decoded packet count does not establish audible output. Multi-monitor DPI and
screen-reader listening remain not run unless those environments are actually
available. A fresh failure should retain sanitized evidence before any retry
or product change.
