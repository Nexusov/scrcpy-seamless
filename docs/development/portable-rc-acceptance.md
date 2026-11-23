# Portable 2.0 candidate acceptance

Select the exact Windows x64 ZIP from the engineering handoff. Check its SHA-256
and extract it to a fresh directory with spaces. Record the source SHA, package
hash and native/server hashes beside the results. Use the extracted copy's
portable data directory and a synthetic or intentionally selected profile.

| Check | Action and expected result | Record |
| --- | --- | --- |
| Portable startup and drafts | Open `ScrcpySeamless.Desktop.exe` with no arguments. Navigate by keyboard through Devices, Profiles, Settings, Sessions and Diagnostics. Cancel an edit, then apply a harmless separate edit and reopen. | The expected portable root, keyboard focus, restored draft and persisted choice. If using a representative 1.x copy, exercise the explicit migration and confirm the original remains usable. |
| Device setup | Use the existing trusted USB or Wi-Fi route. Refresh and associate the selected device with the saved profile. If pairing needs validation, use a disposable profile and the Android pairing prompt. | Discovery state, association, and any refusal. Omit endpoints, pairing codes and serials. A warm ADB run and a truly absent ADB server are different tests; coordinate separately before stopping a shared server. |
| Buffered USB and recovery | Start one USB mirror with an allowed Wi-Fi fallback, a brief temporary video buffer and a moving test source. Confirm video, PC input and audible PC audio independently. Unplug USB once while leaving Wi-Fi enabled. | Old/new attempt, continuity of the exact session and native window, independent media/control results and observed delay. A retained image and audible output are separate observations. |
| Commands and settlement | With the same owned session, test an ordinary Focus request with the mirror behind Desktop. Exercise one configured mirror shortcut and then Desktop Stop. Save Diagnostics to a new local JSONL file before closing Desktop. | Focus command result and actual foreground separately. Record the shortcut's effect, terminal reason, Stop intent, cleanup and exact-child exit without replacement. Check available retired-generation counters against the [diagnostic field guide](desktop-native-runtime.md#explicit-local-jsonl-diagnostics). Record audible output independently. |
| Second session and closure | If a second eligible device is available, run two profiles and check that Focus and Stop target only the selected session. In a short session, close Desktop normally while mirroring. | Both owner identities and settlement after close. Record unavailable hardware explicitly. |
| Recording and accessibility | When the configured use case includes recording or headless use, exercise one short permitted recovery with a destination in the isolated data root and inspect the resulting segments. Check keyboard traversal, screen reader announcements and DPI across available monitors. | Segment names, actual playback/termination, and observed accessibility results. Keep source/headless coverage separate from monitor and assistive technology observations. |

Restore temporary buffer, shortcut, appearance and fallback changes through the
UI after stopping. Close the isolated application normally. Preserve the package
and export only redacted observations. At a material failure, retain the selected
artifact, precise action and observed result before deciding whether to repeat.
Prior hardware reports remain tied to their own packages.
