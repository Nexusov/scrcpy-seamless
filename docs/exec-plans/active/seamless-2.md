# Seamless 2.0 execution plan

Status: Phases 0–3 accepted and merged through PRs #1–#4. Phase 3 merged
into `seamless-2.0` as `3207892ddd37cdd6b20bafc7c770a8470b6cb81f`,
preserving all 11 Phase 3 commits. Phase 4 is locally complete on
`2.0/p04-options` from that exact merge commit and awaits owner review; it has
not been published. Final Phase 0 artifact evidence remains in the local
handoff report.

## Authority and scope

The owner accepted the complete `SCRCPY_SEAMLESS_2_MASTER_PROMPT.md` charter on
2026-09-23. This plan implements its ordered checkpoints, with repository
[Git](../../development/git-workflow.md),
[debugging](../../development/debugging.md), and
[release](../../development/release-process.md) policies remaining canonical.

The owner accepted Phase 3 and authorized PR #4 merge-commit integration after
its final hosted checks, followed by local Phase 4 only. PR #4 passed all four
hosted jobs and was merged; its ancestry and short-lived branch cleanup were
verified. Do not modify `main`, push Phase 4, create tags/releases, change
repository settings, force-push, or enter Phase 5. Keep the current launcher
and imported runtime fallback functional; native lifecycle and product UI
belong later.

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
| P2.1 | Inventory and reproduce current build boundaries | Exact native/server commands, local tools, dependency origins/licenses/hashes, hidden state and clean-checkout gaps recorded | Complete |
| P2.2 | Pin native and Android build inputs | Reviewed versions, source/download URLs, hashes and licenses; local restore/bootstrap instructions without tracked binaries | Complete |
| P2.3 | Establish source-build paths | Android server and native client build from this tree; tests and package consumption demonstrated without weakening provenance | Complete |
| P2.4 | Add .NET 10 foundations | Exact stable SDK in global.json; minimal Core/Infrastructure/Desktop and test projects, central package versions, analyzers, nullable and deterministic policy | Complete |
| P2.5 | Add stable Avalonia foundation | Exact stable 12.x packages; minimal startup, compiled-binding policy, restore/build and later self-contained publishing path demonstrated; no product UI | Complete |
| P2.6 | Reconcile canonical metadata | Audit existing manifest/version duplication; introduce only minimum shared development/upstream/build metadata and mechanical checks | Complete |
| P2.7 | Extend CI build coverage | Legacy tests and DocsCheck stay green; source native/server and .NET scaffold build/test in read-only, pinned/reviewed jobs where feasible | Complete; four hosted jobs passed on initial PR #3 review and after Gradle hardening |
| P2.8 | Audit latest stable upstream | Determine latest stable scrcpy at execution time; record urgent security/crash/race/correctness/protocol applicability and isolated ports only if justified | Complete |
| P2.9 | Reproduce from clean checkout and close | Disposable clean clone/bootstrap, all applicable builds/tests, package/privacy/provenance, traceable local DEV artifact, AGENTS/docs review and clean branch | Complete locally. Hardware smoke was performed; controlled A/B restored PC audio 3/3 on Phase 0 and 3/3 on Phase 2. The initial intermittent audio failure remains unexplained and is an owner-accepted non-blocking tracked risk, not a proven Phase 2 regression or a fixed bug. |
| P2.10 | Lock and verify Android Gradle dependencies | Buildscript and project locks, reviewed SHA-256 metadata, strict CI build and negative fixtures; clean-cache confirmation | Complete; local strict/clean-cache and negative checks passed; hosted Android job passed after push |
| P2.11 | Characterize source-built server runtime | Separate labelled DEV-only source-native/source-server copy and exact owner hardware procedure; preserve canonical legacy package | Complete for one owner-run USB-to-Wi-Fi smoke; [evidence and limits](../../development/phase2-source-server-smoke.md) |

Keep toolchain bootstrap, Android server, native, .NET, Avalonia, CI, metadata,
upstream audit and any urgent upstream port in separate logical commits where
practical. Never replace the current launcher or import fallback in Phase 2.
PR #3 merged with a two-parent merge commit and its work branch was deleted
locally and remotely after ancestry verification. Phase 3 publication is now
limited to its reviewed work branch and PR after the targeted gate passes.

## Phase 3 — headless C# Core and Infrastructure

Base: `ee373709ecdda8e323d93a856a346772b2c46485` on local
`2.0/p03-core`. The Core project owns domain values and application policy;
Infrastructure implements only real process, filesystem and platform effects.
Desktop remains a buildable Avalonia scaffold without product behavior. Use
sanitized fixtures and isolated test data, never the owner's portable install.

| ID | Dependency-ordered checkpoint | Evidence / exit | Status |
| --- | --- | --- | --- |
| P3.1 | Establish value objects and semantic status/error codes | Profile/session/attempt/device/transport identity invariants; no presentation text or platform dependencies; focused headless tests | Complete |
| P3.2 | Define versioned DeviceProfile/configuration v2 | Stable ProfileId independent of USB/network identity; separate mDNS, pairing, connection, alias, policy and mirroring concepts; typed validation | Complete |
| P3.3 | Parse host/IPv4/IPv6 endpoints safely | Port range, bracketed IPv6, malformed input, ambiguous colon syntax and invariant formatting covered without naive splitting | Complete |
| P3.4 | Define pure legacy migration and v2 revision semantics | Snapshot both independent v1 files, report unknown/unmappable data, validate before commit, deterministic/idempotent migration plan | Complete |
| P3.5 | Preserve actual sanitized v1 shapes as fixtures | `phone.json`, `scrcpy-settings.json`, absent/optional fields, malformed/unsupported values, repeat/already-v2 and interruption cases | Complete |
| P3.6 | Resolve application-data paths | Portable `<application>/data/` and installed `%LOCALAPPDATA%\scrcpy-seamless\` are explicit, isolated and testable | Complete |
| P3.7 | Implement typed atomic configuration persistence | Short revision/CAS critical section, validated atomic replacement, recoverable migration backup and interruption tests; no unrelated process/network I/O while locked | Complete |
| P3.8 | Implement owned ADB process boundary | `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, bounded stdout/stderr, exit status, cancellation/timeout and owned termination | Complete |
| P3.9 | Parse untrusted ADB responses | Sanitized `devices -l`, `mdns services`, `pair`, `connect` fixtures including daemon noise, multiple/offline/unauthorized and malformed/IPv6 cases | Complete |
| P3.10 | Add headless discovery and pairing use cases | Injected ADB boundary, manual endpoint input, cancellation, semantic failures and pairing-secret non-persistence/redaction | Complete |
| P3.11 | Model ConnectionPlan and ConnectionPolicy | Candidate ordering, preferred/fallback transport, capability and retry configuration; no Phase 8 native orchestration | Complete |
| P3.12 | Establish activation and native-host boundaries | One-control-center/multiple-session semantics, same-user activation boundary and owned native start/stop/lifetime contract; no Phase 6 IPC or product UI | Complete as semantic Core contracts; Windows activation and native adapter remain later integration |
| P3.13 | Validate and close locally | Locked restore, Release build/tests, legacy suites, DocsCheck/metadata, applicable package/provenance checks, AGENTS/docs review, logical commits and clean tree | Complete locally; validation evidence below |
| P3.14 | Focused acceptance gate before PR | One-way v2 authority with migration/error tests; drain-safe bounded ADB output; direct-command cancellation preserving shared daemon; precise identity semantics and regressions | Complete locally after targeted regression and full validation; evidence below |
| P3.15 | Final pre-merge infrastructure follow-up | Generic ADB runner inherits mDNS backend environment; migration lock order documented; later ADB discovery review recorded; local validation and hosted PR checks | Locally complete; hosted CI pending. Local legacy timing caveat below. |

Keep each checkpoint reviewable; combine adjacent work only when the invariants
and their tests form one coherent commit. Do not introduce Phase 4 option
generation, Phase 5 UI, Phase 6 machine IPC, Phase 7 native lifetime work, or
Phase 8 reconnect execution into this Phase.

## Phase 4 — canonical option specification

Base: `3207892ddd37cdd6b20bafc7c770a8470b6cb81f` on local
`2.0/p04-options`. The YAML specification is the one data authority for the
current native CLI, the existing PowerShell option catalogue and new typed
Core metadata. Preserve native CLI behavior and 1.x launcher behavior. Keep
native parsing/semantics, runtime device capabilities and complex validation
rules in typed code. Commit generated outputs so a native-only source build
does not need .NET generation first.

| ID | Dependency-ordered checkpoint | Evidence / exit | Status |
| --- | --- | --- | --- |
| P4.1 | Inventory native CLI, legacy catalogue and consumers | Account for every long entry and short-only alias, argument mode, help special case, legacy field and current tests; record upstream provenance | Complete: 106 long entries, three short-only aliases and 106 legacy entries in native order; two conditional-help cases recorded |
| P4.2 | Define canonical data and schema | `spec/options/options.yaml` with independent spec version, stable IDs, classification, argument/value metadata, resource keys, static capabilities and simple relationships; schema and semantic validation boundaries explicit | Complete; spec v1, generated strict schema and semantic validator |
| P4.3 | Implement deterministic SpecGen | Maintained permissively licensed pinned YAML parser; `generate` and non-mutating `verify`; stable UTF-8/LF output and strict structural/reference checks | Complete; YamlDotNet 18.1.0 MIT, exact lock and license; 14/14 generator tests |
| P4.4 | Generate typed Core option descriptors | Immutable catalogue, typed IDs/enums/resource keys; no YAML dictionaries at runtime | Complete; generated C# descriptors and embedded English resources |
| P4.5 | Generate legacy catalogue | Existing JSON contract/order and 1.x temporary compatibility preserved; generated ownership documented and drift verified | Complete; 106/106 entries and all 13 fields unchanged in value, exact generated bytes verified |
| P4.6 | Generate native static CLI declarations | Commit generated include/table; preserve parser, handlers, argument modes, option order and conditional help text; compiled parity coverage without `cli.c` text parsing | Complete; compiled table 109 entries, 16/16 native tests and help parity except executable path in Usage |
| P4.7 | Add pure Core option selection/value validation | Editable versus managed/action enforcement; semantic diagnostics for unknown stored options without data loss; simple typed checks and named typed complex RuleIds | Complete; five named typed rules, discrete arguments only for valid selections, 67/67 Core tests |
| P4.8 | Prove parity and determinism | Negative spec fixtures, generate-twice byte identity, verify drift/no mutation, compiled native CLI coverage, legacy/migration fixtures | Complete; clean checkout verify/generate/verify leaves Git clean |
| P4.9 | Wire verification into docs/build/CI | Generated schema/native/Core/legacy/English-resource/reference-doc outputs checked; semantic IDs checked without prose regex; clean-checkout generation requires no hidden state | Complete; Desktop CI runs SpecGen verify after locked restore, native build consumes committed include, DocsCheck checks reference links |
| P4.10 | Document and close Phase 4 locally | AGENTS/docs/risk review, all applicable .NET/native/legacy/docs/package checks, logical green commits, clean branch and owner handoff; no hardware claim unless runtime changes unexpectedly | Complete locally; evidence below and final closure commit |

The three short-only aliases must be explicit in the spec even though the
legacy catalogue intentionally contains only long names. Existing
`SeamlessCompatible` values are a legacy projection, not a permanent 2.0
capability verdict. The canonical data must distinguish static build features
from device/runtime support. Complex recording, camera, codec and transport
semantics remain named typed rules; YAML must not become an executable rule
language. The v2 persisted bool/string shape and loss-averse unknown-option
behavior remain intact. No Phase 5 UI, Phase 6 IPC, Phase 7 lifetime or Phase
8 reconnect work is included.

Phase 4 local validation on 2026-09-25: locked .NET restore and zero-warning
Release build succeeded; 121/121 solution tests passed (Core 67,
Infrastructure 39, Desktop 1, SpecGen 14). The native release client rebuilt
from the final source, 16/16 native tests passed, and the generated CLI help
matched the prior native help except for the executable path in `Usage`.
`SpecGen verify` checks six committed outputs. A detached clean checkout passed
locked restore, verify, generate, verify, then had a clean Git status. That
test first exposed Windows CRLF output from JSON serialization; the generator
now normalizes all outputs to LF and has a regression assertion. The exact
local ZIP passed provenance/privacy packaging and the complete legacy suite
28/28. An earlier full run passed 27/28 after `dev-observer` missed an
existing-log event; that unchanged suite passed in isolation and in the
repeat full run. One later .NET solution run hit a transient Windows file lock
in an unchanged Infrastructure ADB fixture; its isolated suite and the repeat
full 121/121 run passed. These observations do not establish product defects
or hardware behavior. DocsCheck passed 466 links in 72 tracked Markdown
files; build metadata agreed and `git diff --check` passed. The Android server
and its build metadata were unchanged, so the accepted Phase 3 server evidence
is reused rather than repeating an unrelated server build. No Phase 5 work,
hardware test or remote Phase 4 operation was performed.

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

The non-blocking [audio recovery watch item](../../development/phase2-audio-ab.md)
requires channel-specific evidence in those existing Phases. Phase 6 correlates
structured IPC/lifecycle events. Phase 7 identifies audio capture, demux,
decoder, regulator/buffering and SDL sink ownership across session generations.
Phase 8 defines reconnect audio-recovery semantics and timestamps transport,
first packet/decoded frame and audible-readiness transitions. Phase 12 uses
aggregated packet/sample/drop, underflow/overflow and Windows output-state
diagnostics in repeated hardware/soak tests. This does not add Phase 2 product
code or broaden those later Phases.

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
- 2026-09-24: Phase 2 pinned reviewed native, Android and .NET/Avalonia inputs
  without replacing the imported 1.x runtime. The owner reviewed Android SDK
  terms interactively. Local Android Gradle assemble/check passed with 47 unit
  tests; native Meson/Ninja release build passed and 16/16 C tests passed.
  Desktop locked restore, warning-free Release build, headless startup/binding
  test and self-contained Windows scaffold publish passed. The upstream v4.1
  audit ported the initial-window race/zero-size fix and audio allocation guard
  in separate attributed commits. These automated checks do not prove device
  reconnect or audio behavior.
- 2026-09-24: a disposable Git clone at `37bef9539d7c9e54098a09ee9b59eb66d5076303`
  started without copied build outputs. It restored native archives into its
  own cache, built the client in 73 Ninja steps, and passed 16/16 C tests. It
  built the Android server from clone sources using an explicit, verified
  shared SDK cache whose license the owner had already reviewed. It downloaded
  and verified the exact .NET SDK into the clone, then passed locked restore,
  Release build (zero warnings/errors), one headless test and self-contained
  Windows publish. The clone's native-backed package passed 28/28 PowerShell
  suites; DocsCheck passed 419 links in 66 tracked files. This establishes a
  repeatable documented workflow, not byte-identical output. The local CI
  workflow passed actionlint v1.7.12; its new hosted jobs cannot be claimed
  green before separate Phase 2 publication authorization.
- 2026-09-24: the `p02-g7b140f3` local DEV ZIP passed 28/28 PowerShell suites
  and kept private settings out of the archive. The owner used an extracted
  copy with the earlier DEV-only phone configuration. Video, PC control and
  audible output worked initially over USB, but the owner heard speed/skip
  artifacts. After unplugging USB, video and PC control recovered over Wi-Fi
  in the existing window while audible output did not return on either PC or
  phone. During that first run the observer retained PID `36980` and nonzero
  HWND `2691502` through the transition, with responsive process samples. A
  later separate launch used PID `19792` and HWND `5311488`; do not combine
  the two runs into one lifetime claim. The trace recorded USB selection,
  disconnection, a TCP/IP attempt and video resume in order; its buffered log
  collection does not establish exact event times or an audio root cause.
  This is a failed changed-client audio smoke, not proof that the Phase 2
  allocation guard caused it. The owner subsequently authorized a narrow A/B
  investigation without accepting Phase 2.
- 2026-09-24: the [Phase 2 audio A/B investigation](../../development/phase2-audio-ab.md)
  compared the exact accepted Phase 0 DEV ZIP with the Phase 2 DEV ZIP. Their
  packaged server, ADB, SDL and FFmpeg components are byte-identical; native
  `scrcpy.exe` is the substantive runtime difference. Three clean USB-to-Wi-Fi
  cycles on each build restored video, control and PC audio with stable native
  PID/HWND. One additional old-build run overlapped another active native
  client and was excluded. The original B audio failure did not reproduce,
  and its playback-source state was not recorded. Classification is
  insufficient evidence for either a Phase 2 regression or a pre-existing
  1.x bug. No production fix was made. The owner accepted classification C,
  retained the unexplained failure as a non-blocking intermittent observation,
  and authorized P2.9 closure without claiming perfect audio stability.
- 2026-09-24: final automated validation on source HEAD
  `567f689061f559ea508dc5fcaeceded85c06efa1` passed: legacy PowerShell
  28/28, DocsCheck 422 links in 67 tracked files, metadata agreement,
  actionlint v1.7.12, fresh native release 73/73 and C tests 16/16, Android
  `:server:assembleRelease :server:check` with forced 76/76 tasks and 47/47
  tests, .NET locked restore/Release build (zero warnings/errors)/headless
  test 1/1/self-contained `win-x64` publish, and `git diff --check`. A clean
  checkout of the same HEAD packaged the freshly built native client with
  reviewed imported runtime files; archive verification passed 28/28 suites,
  metadata/DocsCheck and privacy checks. That local ZIP has SHA-256
  `06f14afca70c8d43ff1f51d54cd41cf7b87616bbe09d3de15e0b3235e6dbe867`.
  The earlier full clean-clone bootstrap/build at
  `37bef9539d7c9e54098a09ee9b59eb66d5076303` was not
  repeated because `git diff` proves subsequent commits changed only three
  documentation files, no source/build/test inputs. No byte-identical output
  claim follows; the new hosted CI jobs were still unrun at this checkpoint.
- 2026-09-24: Gradle buildscript and server lock state plus SHA-256 verification
  metadata were committed in `d13b6385dd53b93a9aea2accc4f392dbdd7cc06b`.
  A strict build/check passed from an empty Gradle dependency cache; disposable
  changed-checksum and changed-version fixtures failed as intended. All four
  hosted PR #3 jobs passed after push, including the Android strict build and
  negative safeguards. The owner then completed the
  [source-built-server DEV smoke](../../development/phase2-source-server-smoke.md):
  one USB-to-Wi-Fi recovery retained native PID/HWND and restored video,
  control and PC audio. No canonical release binary was substituted.

## Handoff gate

The owner accepted Phases 0–2. PR #3 merged at
`ee373709ecdda8e323d93a856a346772b2c46485`; Phase 2's 20 commits are
ancestors of `seamless-2.0` and the short-lived branch was deleted. The
intermittent audio watch item remains tracked and is not Phase 3 audio work.
Phase 3 is implemented locally as separate Core/domain, application-contract,
configuration/migration, ADB and documentation commits. The pure v1 migration
and v2 atomic store are headless APIs, not yet wired into the 1.x launcher or
the placeholder Avalonia Desktop. A same-user activation channel and concrete
native-host adapter are deliberately left for the owning Desktop/IPC phases;
their semantic contracts and tests are in Phase 3. See the
[configuration boundary](../../development/desktop-configuration.md).

Phase 3 validation on 2026-09-24: .NET 10.0.401 locked restore, Release build
(zero warnings) and 81/81 solution tests; existing PowerShell archive suite
28/28; DocsCheck 444 links across 69 tracked Markdown files; build metadata
check; fresh native Meson test build 16/16; Android offline strict
`assembleRelease`/`check` successful (75/76 tasks up-to-date, unchanged
server/build inputs); self-contained `win-x64` placeholder Desktop publish;
existing archive checksum and package test passed. Phase 3 did not change
`src/scrcpy`, the legacy launcher, reviewed package inputs or canonical 1.x
runtime behavior. No new hardware claim follows from these headless tests.
These baseline checks preceded the focused gate; the later authorization above
permits merge-commit integration and then local Phase 4 after final validation.

Focused Phase 3 acceptance gate on 2026-09-24: tests now enforce the one-way
v1-to-v2 cutover, changed-v1 authority, corrupt-v2 recovery and stale-preview
rejection. The ADB runner continues draining both pipes after a 65,536-character
capture cap per stream; synthetic 131,072-character stdout/stderr completes
with explicit truncation. A failing regression demonstrated that process-tree
termination killed a synthetic descendant; the runner now kills only the
invoked command, bounds termination, and awaits both readers before disposal.
Early stdin closure, cancellation and timeout have focused process tests.
Network `ro.serialno` is an observed property rather than `UsbSerial`; an
optional explicit property comparison does not claim physical-device identity.
Locked restore, zero-warning Release build and all 89/89 .NET tests passed
(Core 50, Infrastructure 38, Desktop 1); the 1.x archive suite passed 28/28.
DocsCheck, metadata and diff validation complete this local gate. Phase 3 is
locally complete for PR review, with no Phase 4 work or new hardware claim.

Final Phase 3 follow-up on 2026-09-24: the generic ADB process runner no longer
sets `ADB_MDNS_OPENSCREEN`; a regression failed before the removal when an
inherited value of `0` was changed to `1`, then passed. Migration documents the
legacy → v2 mutex order, and R03 tracks later ADB backend/discovery review.
Locked restore, zero-warning Release build, all 90/90 .NET tests (Core 50,
Infrastructure 39, Desktop 1), DocsCheck and build metadata passed. Three
local full 1.x suite runs yielded 27/28, 26/28 and 27/28: `options-view` and
`dev-observer` exceeded existing wall-clock/UI deadlines under load, while
both passed together under the same test runner when isolated (2/2). The
launcher, observer and their tests are unchanged by Phase 3; do not weaken
their timing assertions for this follow-up. Require the clean hosted `test`
job and other PR checks to pass before integration, and retain the local
timing limitation in the handoff report.
