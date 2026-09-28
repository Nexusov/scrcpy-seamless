# Phase 5D integrated acceptance matrix

This matrix separates tests of the current Desktop code from historical device
observations. `a3b1aa2b983cafbaa524a86502b1424315521582` integrated Phase 5C;
PR #8 hosted CI tested synthetic merge `4b7c6a88547a55e0b938fd6dfc3f05b4ad77518b`
with approved head `10b7d80cba023ad823ec53384196d50ab281047f`.
The earlier successful physical-device run used only
`scrcpy-seamless-desktop-p05c-g2b902065`, source
`2b90206595932f78c02a3e4ea383a27d15644c2e`. It does not validate the
corrected Phase 5C control plane or the Phase 5D artifact.

Status terms: **passed** means observed at the named layer and source;
**failed** means a demonstrated current defect; **blocked** means the test
environment lacks a required condition; **not run** means execution is reserved
for manual acceptance. A headless test is never labelled a Windows UI Automation
or audible-output observation.

Earlier Phase 5D UI-review code/artifact: source `9a02818b35aa300a4625edcd97a8794f69149613`,
`dist/dev/scrcpy-seamless-desktop-p05d-g9a02818b.zip` SHA-256
`95ac4a39dc8d99609f652642c29ae2b35e0cd4c067b0f3195414c1bb1d7de058`.
Its reviewed runtime identities and hashes are in the package-relative
`runtime/runtime-dev-manifest.json`; the source-built native/server bytes are
reused from unchanged accepted source fingerprints. The ZIP was verified and
launched from `work/phase5d/Final acceptance extraction with spaces/` with the
process working directory outside the checkout. Local Windows screenshots and
sanitized UI Automation samples are under `work/phase5d/final-visual-review/`.
Final local validation passed locked restore, zero-warning Release build,
400/400 .NET tests, 29/29 legacy suites, SpecGen verify, DocsCheck, metadata,
the ZIP verifier and extracted Windows startup/activation checks. Native and
Android server sources did not change in 5D, so their accepted tests are reused
as earlier evidence, not reported as new runs. There are no failing executed
final synthetic checks. Physical-device checks were outstanding for this
earlier UI-review artifact; later artifacts have separate results below.

Earlier broad Phase 5D manual-acceptance artifact: source
`eb3de1c55b159b980643ee6235e000770134a1b3`,
`dist/dev/scrcpy-seamless-desktop-p05d-geb3de1c5.zip` SHA-256
`cad4bcba0b4139ec13bfc36d9010fc6a4209343a19d0ba5ad8d152dccc49888f`.
Its 12 native/server/ADB/SDL/FFmpeg/image runtime files are byte-identical
to the previous package. Locked restore, zero-warning Release build, 405/405
.NET tests, 29/29 legacy suites, SpecGen verify, DocsCheck, metadata and ZIP
verification passed locally. The extracted ZIP launched preview from a path
with spaces and a working directory outside the checkout. Actual Windows
preview screenshots show compact-category results using the full available
width and the read-only Shortcuts tab at light/150% and dark/100%; see
`work/phase5d/shortcuts-visual-review/geb3de1c5/`. These images and UI
Automation's named/focusable tab do not establish screen-reader behavior or
multi-monitor DPI. Subsequent device-enabled Windows/phone and authorized
cold-start checks used this same artifact; their results appear below.
The table below retains the earlier UI-review package's separate evidence;
the broad [manual procedure](phase5d-manual-acceptance.md#earlier-exact-artifact-acceptance-procedure-and-evidence)
and observations below identify `geb3de1c5` separately. The corrected
`gda35a1bd` artifact and its targeted result are summarized in the final
section of this matrix.

| Requirement / scenario | Layer and expected result | Actual result and status | Source/artifact | Remaining limit |
| --- | --- | --- | --- | --- |
| Preview opens without persistence, ADB, native launch or activation | Headless composition and extracted EXE; no external adapters/effects | **Passed**: 400/400 .NET tests; real extracted preview opened/closed; `.dev-data/p05d` remained empty and shared ADB PID stayed 34800 | `g9a02818b` | Process smoke alone cannot prove every absent side effect; headless composition supplies adapter-level evidence |
| Missing/malformed runtime leaves Settings usable | Headless composition and extracted EXE; structured unavailable state, unchanged files | **Passed**: missing-runtime Windows settings window and existing malformed-input fixtures | `g9a02818b` | No real ADB required |
| Settings/profile Apply, Cancel, restart and two save groups | Headless fixture; committed values persist and detached edits do not | **Passed**: integrated 400/400 .NET suite | `g9a02818b` | Manual Windows editing remains separate |
| Unknown options and explicit false/zero/empty values | Core/Desktop fixtures; values remain recoverable and unsupported execution is explicit | **Passed**: integrated .NET suite | `g9a02818b` | Native remains final runtime authority |
| Profile association uses the selected transport, not model text | Headless + UI; explicit typed route fills only its corresponding draft field | **Passed**: USB/network/unknown/dirty-buffer fixtures; real Windows UI Automation found named/focusable action and separate fields | `g9a02818b` | Actual transfer from a physical selected transport is pending; opaque mDNS selector needs manual endpoint entry |
| Pending profile edit survives association, migration and navigation | Headless fixture; no unstaged overwrite or implicit save | **Passed**: association and prior migration/navigation tests in integrated suite | `g9a02818b` | Apply must still pass revision check |
| Mirror uses committed snapshot and explicit selected target | Headless fixture; dirty/stale input blocks launch | **Passed**: integrated .NET suite | `g9a02818b` | Process start does not prove channel readiness |
| Disabled USB fallback and same-network retry | Core/host fixture; forbidden network target absent even when inherited | **Passed**: PR #8 focused regression cases rerun in integrated suite | `g9a02818b` | Physical disconnect behavior not yet tested on final build |
| Pair/Connect cancellation and replacement lifetime | Controlled gateway tasks; shutdown waits for every unsettled operation | **Passed**: PR #8 regression cases rerun in integrated suite | `g9a02818b` | No shared ADB daemon is touched by fixture |
| Same-scope activation and one mirror owner | Synthetic Windows processes; second launch routes to first scope | **Passed**: extracted same-root secondary exited 0 while primary PID 6764/window remained; single-mirror ownership remains fixture-tested | `g9a02818b` | Real running-mirror activation pending |
| Stop, spontaneous exit and failed Stop/close | Fake child/coordinator; no replacement launch, retained ownership and retry | **Passed**: PR #8 focused cases rerun in integrated suite | `g9a02818b` | Abnormal parent death remains Phase 6 |
| Dirty close with Keep editing, partial save and live cleanup | Headless coordinator; drafts and child ownership remain truthful | **Passed**: existing coordinator cases plus enlarged dialog Escape/X checks | `g9a02818b` | Real-window running-mirror close pending |
| Keyboard, focus, AutomationIds, reasons and dialogs | Headless and Windows UI Automation; controls reachable with visible focus | **Passed at inspected layers**: validation ID/name and close-dialog Escape/X in headless; real Windows tree showed named, focusable profile fields/action with action focused | `g9a02818b` | Full Tab/Shift+Tab and real screen-reader listening remain manual checks |
| 660×460 minimum and 150% application scale | Headless/Windows layout; errors/footer actions remain reachable | **Passed at inspected layers**: headless viewport/actions, wrapped dialog/title bounds; actual Windows light/150% screenshot shows distinct title and Reset | `g9a02818b` | The real window's reported minimum was 1012×746 pixels during this application-scale check; display DPI was not varied |
| Light/dark and stored font fallback | Headless/Windows presentation; no compounded scale | **Passed at inspected layers**: actual dark Devices and light Settings preview; stored font/scale fixture passed | `g9a02818b` | OS theme switching not performed; no second monitor available |
| DEV ZIP provenance, privacy and clean extraction with spaces | Artifact audit; exact source/runtime hashes, no private data | **Passed**: package fixture rejects private/tampered inputs; real 62 MB ZIP verified, extracted in a path with spaces and launched outside checkout | `g9a02818b` ZIP SHA above | Local DEV artifact is not signed or a public-release license attestation |
| Cold-start mDNS from absent shared ADB server | Physical Windows/phone; product starts server under reviewed process-local policy | **Blocked for this artifact**: existing shared ADB PID 34800 still listened on 127.0.0.1:5037 | `g9a02818b` not run | Later authorized check on `geb3de1c5` is recorded below |
| USB Mirror, Wi-Fi recovery, audio/video/control and Stop | Physical Android; independent channels and exact PID/start/HWND checkpoints | **Not run** on this earlier artifact | `g9a02818b` | Later `geb3de1c5` result is recorded below |
| Disabled cross-transport fallback after USB removal | Physical Android; session does not silently switch to saved Wi-Fi route | **Not run** on this earlier artifact | `g9a02818b` | Later `geb3de1c5` result is recorded below |
| Multi-monitor DPI and screen-reader listening | Windows hardware/assistive technology | **Blocked / not run**: only one display was available; no screen-reader listening was requested | `g9a02818b` | Do not infer from headless resize or accessibility tree |

The [manual procedure](phase5d-manual-acceptance.md) identifies each artifact
and its distinct observed or pending checks.

## Earlier geb3de1c5 manual observations

All observations below used `scrcpy-seamless-desktop-p05d-geb3de1c5` from
source `eb3de1c55b159b980643ee6235e000770134a1b3`, the isolated
`.dev-data/p05d` scope and one physical phone. The Mirror checks used an
already-running shared ADB server; the cold-start check began with no server.
The owner reported audible PC sound and performed physical USB cable
changes; the agent inspected the real Desktop/native windows, process identity,
and saved DEV configuration. Neither UI Automation nor visual inspection is a
screen-reader test. The Android version was not captured.

| Check | Observation | Limit |
| --- | --- | --- |
| Shortcuts and compact Settings | Owner saw the MOD, Home/Back, rotation and conditional native shortcut rows without reported clipping or errors. Compact-category results filled the content width; scrolling worked and an edited draft value persisted. Settings-only mode opened the control-center shortcut editor; Tab/Shift+Tab and visible focus worked. | No real screen reader or multi-monitor DPI test. At 150% application scale, the compact window leaves little vertical space for settings; reducing the chosen scale implicitly would violate the selected preference. |
| Physical route/profile association | USB and network ADB selections appeared; the physical USB selector was labelled `Route unknown`. The selected-route draft action refused to fill USB serial, leaving a new profile unable to launch until its exact serial was entered manually and saved. After manual entry, the profile launched successfully. | **Failed usability check** for automatic USB association on this observed ADB output. The UI exposes the correct selector but did not classify the route. Raw `adb devices -l` was checked separately after a later reconnect; it contained the device state and model without a `usb:` field. This does not prove the exact earlier output bytes. |
| USB Mirror and Wi-Fi recovery with fallback enabled | Live video and PC control were observed, and the owner confirmed audible PC audio before and after USB removal. Wi-Fi recovery kept the native PID `32916`, UTC process start `2026-09-28T12:08:00.8138513Z`, HWND `1905410` and title `Phone-Seamless` at the two checkpoints. Desktop Stop closed the native child; the shared ADB server remained running. | One physical recovery cycle on this artifact. Matching PID/HWND samples do not prove every intervening frame or uninterrupted audio. |
| USB removal with fallback disabled | With the saved Wi-Fi endpoint retained and only fallback disabled, USB removal stopped the native session; the Desktop reported `NativeFailure`, with no observed Wi-Fi transfer. Fallback was restored with Save to draft and Apply. | Native disconnect/exit is expected legacy behavior in this condition; this does not classify the status wording as a separate failure. Child environment was not captured. |
| Same-root activation while Mirror ran | A secondary Desktop launch for the same DEV data root exited with code 0. Primary PID `31004`/HWND `8327314` and native PID `45780`/HWND `4264984` remained unchanged at the before/after checkpoints. | One real-window activation check. |
| Normal close while Mirror ran | Closing the primary Desktop window left no processes from the exact DEV Desktop/native executable paths. The shared ADB server remained PID `34800` listening on port 5037. | Does not exercise abnormal parent death or deliberately failed child cleanup. |
| Cold-start discovery | **Passed in the controlled check**: after separately authorized `kill-server`, both ADB process and port-5037 listener were absent. Desktop PID `32500` requested discovery and started bundled ADB PID `49044`; `mdns check` reported Openscreen. With the phone's pairing-code screen open, DEV UI found the pairing service and `mdns services` counted one. Closing Desktop left ADB available. | One cold-start cycle. Pairing was deliberately not repeated; this does not test every network environment or subsequent daemon restart. |

## USB route correction and targeted hardware retest

The `geb3de1c5` physical association failure above remains the observed result
for that artifact. A later sanitized no-devpath `adb devices -l` shape reproduces
the failure through the real discovery and profile-association path in a
deterministic test. The parser correctly retained Unknown because that listing
did not identify a route; the discovery gateway had no other positive USB
evidence. The local correction queries the same configured ADB server for a
USB-scoped transport ID, matches it uniquely to the current listing and checks
that the listing has not changed before publishing USB. It does not change
profile storage, launch selection or fallback policy. Synthetic tests establish
the corrected data flow and failure boundaries independently of the physical
result recorded below.
The corrected PR-review code artifact is `da35a1bdffb5d90fe02d81e9e965a4195dc1b692`,
`dist/dev/scrcpy-seamless-desktop-p05d-gda35a1bd.zip`, SHA-256
`5a14f48b83b507aa19398a34c84fcc3063cdcd651495b4a62721e0a7ffea3cb5`.
The verified package reuses all 12 runtime files byte-for-byte from
`geb3de1c5`. Locked restore, zero-warning Release build, 435/435 .NET tests,
29/29 legacy suites, SpecGen verify, DocsCheck, build metadata and ZIP
verification passed locally. These checks did not use a phone or query ADB.
The [short targeted retest](phase5d-manual-acceptance.md#targeted-usb-route-retest)
then **passed on this artifact**. With a physical USB connection, Refresh
displayed distinct USB and network rows; the selected USB row said `USB route`
and `Device`. In a separate empty DEV data root, the selected-transport action
filled only USB serial in a new profile editor. No configuration file existed
after that action or after Save to draft; Apply saved the profile with pairing,
wireless service and connection endpoint still empty. An explicit USB/profile
Mirror started this package's native process (PID `42352`, UTC start
`2026-09-28T14:10:22.9714926Z`, HWND `5312272`). The agent observed live video
and a PC-issued Back navigation changing the phone screen; the owner confirmed
audible PC sound. Desktop Stop reported `Native session stopped`, and the exact
native executable had no remaining process. This is one USB launch/Stop cycle,
not a reconnect test. The old recovery, fallback and cold-start observations
remain assigned only to `geb3de1c5`.

Phase 5D is ready for independent PR review: the USB association defect passed
its targeted retest. Final Phase 5 acceptance remains open. No USB-to-Wi-Fi or
cold-start cycle was repeated on `gda35a1bd`; those results belong to
`geb3de1c5`. Limited vertical workspace at 150% remains a usability limitation
while content is reachable. Real screen-reader listening and multi-monitor DPI
checks have not been established; abnormal parent-death cleanup remains Phase 6.
