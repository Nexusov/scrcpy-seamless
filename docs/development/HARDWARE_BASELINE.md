# Manual 1.x hardware baseline

Status: procedure only; **not executed during Phase 0**. Automated coverage and
current restrictions are in the [baseline](../architecture/SEAMLESS_1_BASELINE.md).
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

Until these conditions are satisfied, report cases as NOT RUN, not PASS. A missing
phone or approved environment does not prevent the Phase 0 source inventory.

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
