# Seamless 2.0 execution plan

Status: Phase 0 source freeze complete. Final committed-artifact verification is
recorded in the local handoff report. Phase 1 is not authorized.

## Authority and scope

The owner accepted the complete `SCRCPY_SEAMLESS_2_MASTER_PROMPT.md` charter on
2026-09-23. This plan implements its ordered checkpoints, with repository
[Git](../../development/git-workflow.md),
[debugging](../../development/debugging.md), and
[release](../../development/release-process.md) policies remaining canonical.

The current task permits environment preparation, baseline verification,
characterization tests and architecture/risk/planning documentation only.
No production architecture rewrite, remote push/merge/tag/PR/release, repository
settings change, or Phase 1 implementation is authorized.

## Source baseline

- Integration branch: `seamless-2.0`.
- Local work branch: `2.0/p00-baseline-freeze`.
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
repeated-cycle recovery remain pending hardware cases. Current 1.x does not
automatically fail back from Wi-Fi to USB.
Do not block inventory/documentation work on hardware-only evidence; report the
coverage boundary and do not claim hardware acceptance.

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
| 6 | 5 | Bounded versioned stdio IPC, handshake, Stop/Focus/events, EOF/parent-death behavior; no HWND lifecycle authority |
| 7 | 6 | Separate native app/session/presentation/input/dispatcher lifetimes; generation safety and deterministic reconnect harness |
| 8 | 7 | ConnectionManager, resolver, retry/failure policy, hysteresis, degradation, recording/headless/deadline semantics; next alpha eligibility |
| 9 | 8 | Recheck latest stable upstream; selective documented ports with attribution and tests |
| 10 | 9 | Evidence-based competitive completion with licensing decisions; no generic Android-management expansion |
| 11 | 10 | Remove legacy production paths/adapters/imported native baseline only after parity; beta eligibility |
| 12 | 11 | Fuzz/sanitizers/fault/soak/performance/UI/accessibility/hardware/security/package hardening; RC eligibility |
| 13 | Accepted 12 RC | Minimal blocker fixes and repeated RC acceptance; exact approved stable source, immutable v2.0.0 only after remote authorization |

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

## Handoff gate

Do not begin Phase 1 until the owner has reviewed this Phase's report and
explicitly accepted its documented hardware/isolation limitations. Remote
settings and publication remain separately authorized actions.
