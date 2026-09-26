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

| Requirement / scenario | Layer and expected result | Actual result and status | Source/artifact | Remaining limit |
| --- | --- | --- | --- | --- |
| Preview opens without persistence, ADB, native launch or activation | Headless composition and extracted EXE; no external adapters/effects | **Passed**: integrated headless suite 399/399; extracted-package check pending | Phase 5D source through local fixes; final DEV pending | Process smoke alone cannot prove all absence of side effects |
| Missing/malformed runtime leaves Settings usable | Headless composition and extracted EXE; structured unavailable state, unchanged files | **Passed**: integrated headless fixtures; extracted-package check pending | Phase 5D source through local fixes; final DEV pending | No real ADB required |
| Settings/profile Apply, Cancel, restart and two save groups | Headless fixture; committed values persist and detached edits do not | **Passed**: integrated .NET suite 399/399 | Phase 5D source through local fixes | Manual Windows editing remains separate |
| Unknown options and explicit false/zero/empty values | Core/Desktop fixtures; values remain recoverable and unsupported execution is explicit | **Passed**: integrated .NET suite 399/399 | Phase 5D source through local fixes | Native remains final runtime authority |
| Profile association uses the selected transport, not model text | Headless + UI; explicit typed route fills only its corresponding draft field | **Passed**: USB/network/unknown/dirty-buffer fixtures; real UI check pending | Phase 5D source through local fixes; final DEV pending | Opaque mDNS selector needs manual endpoint entry |
| Pending profile edit survives association, migration and navigation | Headless fixture; no unstaged overwrite or implicit save | **Passed**: association and prior migration/navigation tests in integrated suite | Phase 5D source through local fixes | Apply must still pass revision check |
| Mirror uses committed snapshot and explicit selected target | Headless fixture; dirty/stale input blocks launch | **Passed**: integrated .NET suite 399/399 | Phase 5D source through local fixes | Process start does not prove channel readiness |
| Disabled USB fallback and same-network retry | Core/host fixture; forbidden network target absent even when inherited | **Passed**: PR #8 focused regression cases rerun in integrated suite | Phase 5D source through local fixes | Physical disconnect behavior not yet tested on final build |
| Pair/Connect cancellation and replacement lifetime | Controlled gateway tasks; shutdown waits for every unsettled operation | **Passed**: PR #8 regression cases rerun in integrated suite | Phase 5D source through local fixes | No shared ADB daemon is touched by fixture |
| Same-scope activation and one mirror owner | Synthetic Windows processes; second launch routes to first scope | Earlier synthetic process test passed; final package check pending | PR #8 merge; final DEV pending | Real running-mirror activation pending |
| Stop, spontaneous exit and failed Stop/close | Fake child/coordinator; no replacement launch, retained ownership and retry | **Passed**: PR #8 focused cases rerun in integrated suite | Phase 5D source through local fixes | Abnormal parent death remains Phase 6 |
| Dirty close with Keep editing, partial save and live cleanup | Headless coordinator; drafts and child ownership remain truthful | **Passed**: existing coordinator cases plus enlarged dialog Escape/X checks | Phase 5D source through local fixes | Real-window running-mirror close pending |
| Keyboard, focus, AutomationIds, reasons and dialogs | Headless and Windows UI Automation; controls reachable with visible focus | **Passed at headless layer**: validation ID/name and close-dialog Escape/X; Windows UI Automation pending | Phase 5D source through local fixes; final DEV pending | Screen-reader listening is a separate check |
| 660×460 minimum and 150% application scale | Headless/Windows layout; errors/footer actions remain reachable | **Passed at headless layer**: Settings viewport/actions and wrapped dialog bounds | Phase 5D source through local fixes; final DEV pending | Display DPI and multi-monitor movement are separate |
| Light/dark and stored font fallback | Headless/Windows presentation; no compounded scale | Existing headless tests passed; real-window matrix pending | PR #8 merge; final DEV pending | OS theme change not authorized here |
| DEV ZIP provenance, privacy and clean extraction with spaces | Artifact audit; exact source/runtime hashes, no private data | Not run; final staging pending | Final DEV pending | Local DEV artifact is not a signed release |
| Cold-start mDNS from absent shared ADB server | Physical Windows/phone; product starts server under reviewed process-local policy | **Blocked**: shared ADB server and port 5037 listener present during preparation | Final DEV pending | Requires separately authorized controlled stop or naturally absent server |
| USB Mirror, Wi-Fi recovery, audio/video/control and Stop | Physical Android; independent channels and exact PID/start/HWND checkpoints | **Not run** on final Phase 5D artifact | Final DEV pending | Old `g2b902065` success is historical only |
| Disabled cross-transport fallback after USB removal | Physical Android; session does not silently switch to saved Wi-Fi route | **Not run** on final Phase 5D artifact | Final DEV pending | Retain saved endpoint; inspect actual request evidence |
| Multi-monitor DPI and screen-reader listening | Windows hardware/assistive technology | **Not run** | Final DEV pending | Do not infer from headless resize or accessibility tree |

The prepared [manual procedure](phase5d-manual-acceptance.md) will identify
the exact final artifact and actions before any phone or shared-server test.
No Phase 5D or overall Phase 5 hardware acceptance is claimed here.
