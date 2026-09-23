# Seamless 2.0 risk register

Phase 0 inventory, 2026-09-23. These are observed gaps or explicitly identified
future risks, not claims that an exploit or crash has been reproduced.
Evidence is mapped in the [1.x baseline](SEAMLESS_1_BASELINE.md); intended changes
are in the [target](SEAMLESS_2_TARGET.md) and ordered by the
[execution plan](../exec-plans/active/seamless-2.md).

Priority means consequence if left unresolved: High threatens privacy, lifetime
correctness or release integrity; Medium affects reliability, maintenance or
confidence. All entries are open. A local containment does not close a risk.

| ID / priority | Evidence and consequence | Present containment | Owning Phase / closure evidence |
| --- | --- | --- | --- |
| R01 High: mutable release tags | scripts/publish.ps1 uses tag -f and a forced tag push; publish.Tests.ps1 expects tag movement. PACKAGING/CONTRIBUTING retain conflicting prose. | Do not invoke the publisher on the real repository; canonical immutable-tag policy takes precedence. | 1: reject existing tags, test failure/order semantics with disposable remotes, align docs. Remote publication remains separately authorized. |
| R02 High: publication privacy filter | Test-PublicSourcePath omits scrcpy-settings.json; a force-staged copy under an allowed directory passes its predicate. No data publication was observed. | No real publication; explicit source-only staging and review. | 1: failing privacy regression then filter correction, including private fixtures and approved source exceptions. |
| R03 High: shared ADB trust | Reviewed ADB 34.0.5 uses the Windows profile .android key directory. A separate port/environment override does not establish isolated keys. | Owner explicitly approved shared ADB trust/server for the final DEV artifact; files/settings/shortcuts/mutex remain separate. No personal keys/config are copied. | 2/3: define supported DEV isolation and daemon ownership; verify any stronger isolation claim. Shared-device/daemon effects remain outside file isolation. |
| R04 Medium: installation boundaries | Default launcher mutex is shared across installs; shortcut description matching can update another installation's shortcut. | DEV staging uses a separate mutex and package-private shortcuts. | 3/5: define activation identity and shortcut ownership, test two installations and multiple sessions. |
| R05 High: native generation/lifetime safety | Static scrcpy aggregate, init flags, retained screen controller pointers and global callback gate rely on join/drain/flush ordering. No generation rejection exists. No new UAF was proven. | No production rewrite in Phase 0; record exact ownership and teardown ordering. | 7: explicit app/session owners, detached input, generation-aware dispatcher and deterministic late-work/teardown tests. |
| R06 High: teardown responsiveness | Main-thread synchronous joins can block despite responsive reconnect-wait loops. Current tests do not cover disconnect during every shutdown stage. | Do not claim move/resize/close responsiveness for all loss scenarios. | 7/8: bounded cancellation, controllable blocked workers, window/Stop tests during teardown and retries. |
| R07 Medium: reconnect product limits | Visible playback only; recording/positive deadline/headless conflicts, fixed retry delay, no general retry classification/budget or automatic USB failback. | New tests characterize current restrictions, explicitly replaceable in Phase 8. | 8: ConnectionManager policy, segmented recording, overall deadline, meaningful headless behavior, degradation and hysteresis tests. |
| R08 Medium: launcher/persistence coupling | Configuration lock spans final validation and process creation; mutable hashtable snapshots and HWND/environment contracts are legacy boundaries. | Existing snapshot, cancellation and process ownership tests remain authoritative for 1.x. | 3/6: typed identities/configuration, short CAS transactions, migration fixtures, owned IPC/process lifetime. |
| R09 Medium: metadata/protocol drift | Options are duplicated across native/catalog/UI; device protocol has local vectors but no complete cross-language contract/fuzz suite. Java tests were not run. | Existing parity tests and exact reviewed server retained. | 4/6/9: canonical basic option metadata, named complex rules, framed IPC and device-protocol golden/malformed vectors. |
| R10 High: incomplete source-build chain | Canonical Windows build imports Android server and reviewed libraries. No current source-built server validation, unified restoration lock, SBOM or full reproducibility evidence. | All 11 runtime hashes checked; native built locally with sidecar; notices preserved; origins reported explicitly. | 2: clean pinned server/native builds and CI. 12/13: dependency-source obligations, SBOM, provenance and measured reproducibility where claimed. |
| R11 Medium: fingerprint input scope | Native fingerprint includes all recursive files under src/scrcpy, including AGENTS and potentially ignored generated files. Older sidecar/imported fallback became stale. | Rebuilt native normally; no expected-hash bypass. | 2: explicit documented fingerprint input policy and generated-output placement; stale/incomplete-build rejection tests. |
| R12 High: validation coverage | CI runs PowerShell tests only. C unit tests do not form a reconnect harness. The owner manually observed DEV video/control, fresh USB-only video/audio, visual USB-to-Wi-Fi failover, and audible return with occasional volume jumps. Read-only exact-path samples matched native PID/HWND at two checkpoints. Intermediate transport state, audio quality/timing, post-failover control, repeated cycles and synchronized E2E evidence remain unverified. Loss of screen/audio after Wi-Fi was disabled with USB reattached is consistent with the known lack of automatic Wi-Fi-to-USB failback; fresh USB startup subsequently worked. | Preserve the original artifact's 24-suite/14-test evidence, the current 25-suite observer source run and separately attributed manual observations; the opt-in local observer cannot establish native event times or full acceptance alone. | 2/5/7/12: build CI, UI/IPC/native harnesses and sanitized hardware/fault/soak reports. See hardware procedure. |
| R13 Medium: documentation/distribution drift | BUILD says FFmpeg 8.1 vs pinned 8.1.1; old repository links remain; recursive package docs contain repository-only links; publisher excludes root AGENTS.md. | Preserve current policies and record discrepancies; locally check new source links. | 1/2: coherent docs/indexes/DocsCheck, publisher allowlist review and explicit package documentation policy. |
| R14 Medium: reuse/license evidence | Existing notices describe LGPL dependencies and a source companion; companion availability/transitive compliance were not established. Future competitors have not been audited. | No copied competitor code/assets, no public DEV release or compliance attestation. | 1/10/12: exact-source reuse records and licensing decisions; verify notices/source offer and distribution obligations before release. |

## Acceptance and review

Phase 0 can finish its inventory and characterization while hardware evidence is
pending. It cannot certify reconnect reliability, complete isolation of raw ADB
invocations, full license compliance, or release readiness. The owner reviews
these limitations before authorizing Phase 1.

Close a row only with linked implementation, tests and any required hardware or
release evidence. Reassess severity when the owning Phase discovers new facts.
Do not combine a narrow upstream correctness fix with a broad lifetime rewrite.
The current task makes no production change to remediate these entries.
