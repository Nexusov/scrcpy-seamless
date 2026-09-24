# Seamless 2.0 execution plan

Status: Phase 0 and Phase 1 accepted and merged through PRs #1 and #2.
Phase 2 is active only on local `2.0/p02-build`, based exactly on Phase 1 merge
`0c869722ad7a0e802011766c88d788e8ffd08177`. Phase 3 has not started.
Final Phase 0 artifact evidence remains in the local handoff report.

## Authority and scope

The owner accepted the complete `SCRCPY_SEAMLESS_2_MASTER_PROMPT.md` charter on
2026-09-23. This plan implements its ordered checkpoints, with repository
[Git](../../development/git-workflow.md),
[debugging](../../development/debugging.md), and
[release](../../development/release-process.md) policies remaining canonical.

The owner authorized PR #2 merge and local Phase 2 clean-build foundations.
Phase 2 may not push its branch, open/merge its PR, create tags or releases,
change remote repository settings, force-push, modify `main`, or enter Phase 3
without separate authorization. Keep the current launcher and imported runtime
fallback functional; native lifecycle and application features belong later.

## Source baseline

- Integration branch: `seamless-2.0`.
- Phase 0 work branch: `2.0/p00-baseline-freeze` (merged, then deleted locally
  and remotely).
- Base: `98c613019049ef3e97ae6644f6f14a2cef7ed627`.
- Stable-line ancestor: `00863b508ffc0c9bf6ea464b6a07591fa998749c`.
- Initial worktree: clean. Read-only remote fetch completed; no remote state
  was mutated.
- Existing project-local toolchain and prior artifacts are retained. The
  owner's personal installed copy is read-only and is not an input runtime.

## Phase 0 checkpoints

| ID | Checkpoint | Evidence / exit | Status |
| --- | --- | --- | --- |
| P0.1 | Read complete charter and applicable policies; inspect Git/environment | Base and tools recorded; original suites 23/23 PASS | Complete |
| P0.2 | Inventory behavior, architecture, package and licenses | Source-anchored baseline and explicit coverage gaps | Complete |
| P0.3 | Add missing behavioral characterization | Imported production functions; synthetic fixtures; full suites 24/24 PASS | Complete |
| P0.4 | Document target, risks, hardware procedure and AGENTS impact | Current/target/unverified claims separated; independent agent review | Complete |
| P0.5 | Validate, commit and package exact source | Clean native build and 14/14 C tests; exact DEV ZIP/source suites 24/24; checksum/privacy/provenance and UI/discovery recorded in handoff report | Complete |

Changes should form reviewable local Conventional Commits: characterization
coverage separately from the cohesive baseline/target/risk documentation.
No automatic integration or remote publication follows completion.

## Phase 1 — governance, knowledge and release safety

Phase 1 started from merge commit
`4d71e1ecc29d91b5696fb0e8f81a2dd6bbf9150c` on `seamless-2.0`. PR #2
merged as `0c869722ad7a0e802011766c88d788e8ffd08177`; its nine commits
remain in history and its short-lived work branch was deleted.
The existing 1.x publisher is in scope for release-integrity and privacy
corrections; product launch, native, server and future Desktop architecture are
not.

| ID | Checkpoint | Evidence / exit | Status |
| --- | --- | --- | --- |
| P1.1 | Reconcile Phase 0 integration and inventory; review bootstrap AGENTS/development policies | Plan, exact branch base and minimal policy corrections; clean starting tree | Complete |
| P1.2 | Establish navigable governance knowledge | Documentation indexes, ADR/research structure, third-party reuse rules, competitive audit/matrix scaffolds and repository-setting recommendations; claims marked planned or observed | Complete |
| P1.3 | Add initial DocsCheck | Deterministic tracked-Markdown local link/anchor checks, focused fixtures, and the applicable local/CI invocation; built-package/config/spec checks remain later extensions | Complete |
| P1.4 | Repair 1.x publication safety | Demonstrate existing mutable-tag and private-file failures against disposable local remotes/fixtures; then reject existing tags, publish new tags without force, correct privacy filtering and align release docs; positive and negative tests pass | Complete |
| P1.5 | Validate and close Phase 1 | Full applicable PowerShell suites, DocsCheck, publication fixtures and relevant package/build checks; AGENTS/docs impact, traceable artifact where applicable, logical commits and clean local branch | Complete |

Phase 1 validation on 2026-09-24: the mutable-tag and force-staged-private-file
regressions failed against the old publisher and passed after correction.
Independent review also identified outgoing private-history and Windows path-case
gaps; both received fixtures and fixes. DocsCheck passed 364 links across 59
tracked Markdown files. The full Windows PowerShell suite passed 26/26 with a
locally built archive, including layout, provenance and disposable-remote
publication tests. Phase 1 changed release tooling, tests and documentation;
the native product source and Phase 0 hardware baseline were not changed.

Prefer separate green commits for the plan/policy, governance structure,
DocsCheck, and publication fix with its regression tests and documentation.
For the release bug, first run the new regression against the old publisher and
record the expected failure; commit the regression with the fix so the commit
remains green. Publication tests must use disposable local remotes. Do not
publish a real tag or release while validating Phase 1.

## Phase 2 — clean build foundations

Phase 2 starts at `0c869722ad7a0e802011766c88d788e8ffd08177` on a
clean local `2.0/p02-build` branch. Phase 0 tool versions are evidence to
investigate, not permanent pins. Prefer additive scripts and scaffolds that
keep 1.x development and the reviewed imported package fallback working.

| ID | Checkpoint | Evidence / exit | Status |
| --- | --- | --- | --- |
| P2.1 | Inventory and reproduce current build boundaries | Exact native/server commands, local tools, dependency origins/licenses/hashes, hidden state and clean-checkout gaps recorded | In progress |
| P2.2 | Pin native and Android build inputs | Reviewed versions, source/download URLs, hashes and licenses; local restore/bootstrap instructions without tracked binaries | Pending |
| P2.3 | Establish source-build paths | Android server and native client build from this tree; tests and package consumption demonstrated without weakening provenance | Pending |
| P2.4 | Add .NET 10 foundations | Exact stable SDK in global.json; minimal Core/Infrastructure/Desktop and test projects, central package versions, analyzers, nullable and deterministic policy | Pending |
| P2.5 | Add stable Avalonia foundation | Exact stable 12.x packages; minimal startup, compiled-binding policy, restore/build and later self-contained publishing path demonstrated; no product UI | Pending |
| P2.6 | Reconcile canonical metadata | Audit existing manifest/version duplication; introduce only minimum shared development/upstream/build metadata and mechanical checks | Pending |
| P2.7 | Extend CI build coverage | Legacy tests and DocsCheck stay green; source native/server and .NET scaffold build/test in read-only, pinned/reviewed jobs where feasible | Pending |
| P2.8 | Audit latest stable upstream | Determine latest stable scrcpy at execution time; record urgent security/crash/race/correctness/protocol applicability and isolated ports only if justified | Pending |
| P2.9 | Reproduce from clean checkout and close | Disposable clean clone/bootstrap, all applicable builds/tests, package/privacy/provenance, traceable local DEV artifact, AGENTS/docs review and clean branch | Pending |

Keep toolchain bootstrap, Android server, native, .NET, Avalonia, CI, metadata,
upstream audit and any urgent upstream port in separate logical commits where
practical. Never replace the current launcher or import fallback in Phase 2.
No Phase 2 branch push, PR, tag, release or repository-settings change is
authorized. Stop before Phase 3.

## Validation boundaries

Use the current Windows PowerShell 5.1 runner and canonical build/package
scripts. Use repository-local TEMP/TMP. Source-build the native client; retain
reviewed imports where the current 1.x packager requires exact runtime hashes.
Do not weaken provenance to manufacture a fully source-built release claim.

A traceable DEV artifact contains a documented staging-only isolation overlay.
The owner explicitly approved shared ADB trust/server on 2026-09-23, replacing
the earlier offline-only constraint. DEV files/settings/shortcuts/mutex remain
separate. The overlay is not the production baseline. The owner has manually
observed visual USB-to-Wi-Fi recovery, matching native PID/HWND at two
checkpoints, and audible return with occasional volume jumps. Intermediate
transport state, stable audio quality/timing, post-failover control and
repeated-cycle recovery were not established. Current 1.x does not
automatically fail back from Wi-Fi to USB. The owner accepted these limits for
the Phase 0 hardware baseline; this is not full 2.0 hardware acceptance.

## Ordered roadmap after Phase 0

Every row requires a separate owner acceptance before entering the next Phase.
Exact package/tool versions are rechecked only when their owning Phase adopts
and pins them; planning-date observations are not installation instructions.

| Phase | Dependency | Intended result / acceptance boundary |
| --- | --- | --- |
| 1 | Accepted Phase 0 | Refine existing AGENTS/docs, docs/research/ADR indexes, documentation checks, immutable-tag tooling; no C# rewrite |
| 2 | 1 | Clean server/native source build, pinned toolchain/CI, .NET 10 and stable Avalonia 12.x foundations, central metadata, urgent upstream fix audit |
| 3 | 2 | Headless Core/Infrastructure: identities, endpoints, plans, config v2/migration, persistence, ADB, activation and native-host boundaries |
| 4 | 3 | Canonical option spec/schema/generator, metadata/parity and named complex rules |
| 5 | 3, 4 | Avalonia 1.x workflow parity, localization/accessibility/design system, fake host tests; isolated legacy adapter permitted; alpha eligibility only |
| 6 | 5 | Bounded versioned stdio IPC, handshake, Stop/Focus/events, EOF/parent-death behavior; structured ordered lifecycle events; no HWND lifecycle authority |
| 7 | 6 | Separate native app/session/presentation/input/dispatcher lifetimes; generation-aware lifecycle diagnostics and deterministic reconnect harness |
| 8 | 7 | ConnectionManager, resolver, retry/failure policy, hysteresis, degradation, recording/headless/deadline semantics; timed transport decisions and readiness; next alpha eligibility |
| 9 | 8 | Recheck latest stable upstream; selective documented ports with attribution and tests |
| 10 | 9 | Evidence-based competitive completion with licensing decisions; no generic Android-management expansion |
| 11 | 10 | Remove legacy production paths/adapters/imported native baseline only after parity; beta eligibility |
| 12 | 11 | Fuzz/sanitizers/fault/soak/performance/UI/accessibility/hardware/security/package hardening; diagnostic bundle, metrics, privacy and rotation validation; RC eligibility |
| 13 | Accepted 12 RC | Minimal blocker fixes and repeated RC acceptance; exact approved stable source, immutable v2.0.0 only after remote authorization |

## Observability developed with the 2.0 architecture

The source-tree `scripts/observe-dev.ps1` remains a useful Phase 0/legacy
diagnostic instrument. It is not the 2.0 observability architecture. Build the
new contract with the owning subsystems rather than adding it wholesale after
the new runtime is complete:

- **Phase 6 — Desktop/native IPC:** structured lifecycle machine events carry a
  sequence number, UTC timestamp, monotonic process timestamp, SessionId,
  ConnectionAttemptId, subsystem, event type and typed reason/error. Define
  deterministic order in the machine event stream and flush critical lifecycle
  events immediately. Cross-process correlation must not pretend that UTC
  timestamps alone establish a total order.
- **Phase 7 — native lifetime:** logs carry session-generation identity and
  cover app/session and thread/worker lifecycle, stop/join/destroy boundaries,
  and rejected stale callbacks.
- **Phase 8 — ConnectionManager:** record transport candidates, resolution and
  connection attempts, retry/backoff, failover/failback decisions and capability
  degradation. Measure TransportLost -> ConnectStart -> ServerReady ->
  FirstVideoFrame -> FirstAudioPacket -> ControlReady -> StreamResumed with
  explicit attempt/session correlation and channel-specific outcomes.
- **Phase 12 — hardening:** export a local diagnostic bundle with structured
  JSONL logs, resource/performance counters, aggregated audio/video/control
  metrics, redaction/privacy validation, log-size/rotation policy and soak-test
  correlation. Audio diagnostics cover packets received/decoded, samples
  submitted/dropped, underflow/overflow, queue depth where applicable,
  decoder/sink restarts and first audio packet after reconnect.

Do not log every audio sample or video frame. Aggregate high-frequency counters
and flush them periodically; critical lifecycle events flush immediately.
Diagnostic bundles must exclude pairing codes, ADB private keys and other
secrets. This section specifies later Phase acceptance, not Phase 0
implementation.

Future 3.0 directions remain plans: runtime language switching, embedded
mirroring, Linux/macOS, Windows ARM64, automation API, extensions/transports,
secure updates, a measured Meson-vs-CMake review and Native AOT benchmarking.
No speculative production scaffolding is authorized by this list.

## Decisions and progress log

- 2026-09-23: continued the existing workspace instead of following the stale
  request statement that no clone/tools exist. Read the entire supplied
  charter and root/scoped policies. Created the local work branch from the
  clean integration branch. Phase 0 baseline tests started.
- 2026-09-23: clean native rebuild completed (73 steps), separate debug C tests
  passed 14/14, and full PowerShell suites passed 24/24 after characterization.
  Added portable state independence, legacy reconnect compatibility and literal
  persisted-option contracts without production changes.
- 2026-09-23: source/license/build and native ownership audits produced the
  baseline and 14 open risks. Independent review corrected Phase 8 versus
  Phase 12 ownership in the hardware procedure. New repository links checked.
- 2026-09-23: owner approved shared ADB trust/server. A staging-only DEV overlay
  retains separate config, temporary data, shortcuts and activation. Settings
  created a real form and completed ADB discovery; no authorized USB devices were
  present. No mirroring/pairing/reconnect acceptance is claimed. Personal package
  metadata remained unchanged (62 files).
- Final handoff records exact committed SHA, archive checksum, final archive
  suite result and smoke evidence in ignored work/phase0/PHASE0-REPORT.md and
  dist/test-results/phase0-final-archive/. Build artifacts are never committed.
- 2026-09-24: the owner reported DEV Start/video/control success, USB video and
  control continuity with Wi-Fi disabled, and visual USB-to-Wi-Fi failover with
  mirroring recovered in a still-visible window. The native executable was
  identified from package/source and current process path as `app/scrcpy.exe`.
  No paired PID/HWND samples or audio recovery were reported. An earlier
  `Get-Process scrcpy` no-match is inconclusive, not a failed failover test.
  The hardware procedure then provided a path-based query for a later cycle.
- 2026-09-24: during USB operation with Wi-Fi disabled, the owner measured DEV
  `app/scrcpy.exe` PID `28600`, HWND `594730` and title `Phone-Seamless`.
  Following owner-reported USB-to-Wi-Fi recovery and subsequent transport
  changes, an independent read-only exact-path query found the same PID/HWND.
  The owner's attempted after-command pasted PowerShell prompts and Markdown
  syntax, so its errors are unrelated to native behavior. Audio was audible
  after USB removal and Wi-Fi restoration, with occasional perceived volume
  jumps; timing and stable quality were not measured. Screen/audio loss after
  disabling Wi-Fi with USB reattached is consistent with the current lack of
  automatic Wi-Fi-to-USB failback, not evidence of fresh USB failure.
  Post-failover PC control and repeated measured cycles remain open.
- 2026-09-24: a separate fresh DEV startup with Wi-Fi disabled and USB attached
  produced video and audio. This confirms USB startup in that run while leaving
  automatic Wi-Fi-to-USB failback unimplemented and unverified.
- 2026-09-24: added an opt-in, local observer for the existing DEV package. It
  samples exact-package process/window state, sanitized native event categories,
  Wi-Fi adapter availability and ADB endpoint counts without changing product
  sources or the verified artifact. Collection time is not native emission time;
  full hardware transition traces still require a controlled owner run. The
  current source suite passed 25/25 including the observer fixture; the
  original DEV artifact remains verified against its 24/24 source-suite result.
- 2026-09-24: the owner ran the local observer for three minutes. All 103
  exact-package process samples retained one PID/HWND and reported responsive.
  ADB availability changed across network-like and USB/other categories;
  native-log order included USB selection, two TCP/IP selections and two
  stream resumes. Native output arrived in five buffered batches, and the
  process predated collection by 22 minutes; event times and video/audio/control
  outcomes for this capture are not established. The owner could not reliably
  reconstruct the action timeline afterward. Optional predefined keyboard
  markers were added to the source-tree observer for future live annotation;
  they were checked in an interactive console with a synthetic package. Keep
  full hardware acceptance open pending owner-observed outcomes and
  synchronized evidence.
- 2026-09-24: the owner accepted Phase 0 and its hardware baseline, including
  the stable DEV process/window observations, visual USB-to-Wi-Fi failover,
  observed audio return and the limits of buffered native timestamps. The 62
  audio sample-skip messages remain observations, not a proven audio defect.
  No further audio investigation or production change is in scope. The owner
  assigned observability requirements to Phases 6/7/8/12; no new logging
  architecture was implemented. Git integration awaits separate authorization.
- 2026-09-24: PR #1 passed its `test` check and merged with a two-parent merge
  commit `4d71e1ecc29d91b5696fb0e8f81a2dd6bbf9150c`. Local
  `seamless-2.0` fast-forwarded to the merge; the merged Phase 0 branch was
  deleted locally and remotely. The owner authorized Phase 1 on the local
  `2.0/p01-governance` branch with no Phase 1 push or remote settings change.
- 2026-09-24: PR #2 passed its `test` check and merged with a two-parent merge
  commit `0c869722ad7a0e802011766c88d788e8ffd08177`. Local
  `seamless-2.0` fast-forwarded; the nine Phase 1 commits were preserved and
  the merged work branch was deleted locally and remotely. The owner authorized
  local Phase 2 foundations from this exact merge commit.

## Handoff gate

The owner accepted Phases 0 and 1, and PRs #1 and #2 merged into `seamless-2.0`.
Complete and report Phase 2 locally, then await owner review before Phase 3 or
any Phase 2 remote publication.
