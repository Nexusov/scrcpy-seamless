# Seamless 2.0 execution plan

Status at the 2026-09-25 Phase 5B PR preparation gate: Phase 5A merged
through PR #6 at `190bce459895c25e5d1b2ac6708acf0b0d426a70`, preserving
the approved `75cf922981042accdd45126099d2ebf4b983ac07` head and its ten
commits. Phase 5B is being prepared for independent PR review; overall Phase 5
remains incomplete.
Phase 5C–5D and Phases 6–13 have not started.
Final Phase 0 artifact evidence remains in the local handoff report.

## Authority and scope

The maintainer accepted the complete `SCRCPY_SEAMLESS_2_MASTER_PROMPT.md`
charter on 2026-09-23. This plan implements its ordered checkpoints, with repository
[Git](../../development/git-workflow.md),
[debugging](../../development/debugging.md), and
[release](../../development/release-process.md) policies remaining canonical.

The accepted Phase 5A merge is the base for branch `2.0/p05b-settings`.
Only publication of that branch as a PR into `seamless-2.0` is authorized at
this gate; merge, tag, release and Phase 5C remain unapproved. Keep the current
launcher and imported runtime fallback functional. Phase 6 machine IPC,
Phase 7 native lifetime and Phase 8 ConnectionManager remain separate work.

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
| P4.4 | Generate typed Core option descriptors | Immutable catalogue, typed IDs/enums/resource keys; no YAML dictionaries at runtime | Complete; generated C# descriptors in Core and English resource embedded only in Desktop |
| P4.5 | Generate legacy catalogue | Existing JSON contract/order and 1.x temporary compatibility preserved; generated ownership documented and drift verified | Complete; 106/106 entries and all 13 fields unchanged in value, exact generated bytes verified |
| P4.6 | Generate native static CLI declarations | Commit generated include/table; preserve parser, handlers, argument modes, option order and conditional help text; compiled parity coverage without `cli.c` text parsing | Complete; compiled table 109 entries, 16/16 native tests and help parity except executable path in Usage |
| P4.7 | Add pure Core option selection/value validation | Editable versus managed/action enforcement; semantic diagnostics for unknown stored options without data loss; simple typed checks and named typed complex RuleIds | Complete; six named typed rules, discrete arguments only for valid selections, 92/92 Core tests after PR review follow-up |
| P4.8 | Prove parity and determinism | Negative spec fixtures, generate-twice byte identity, verify drift/no mutation, compiled native CLI coverage, legacy/migration fixtures | Complete; frozen Phase 3 native/help and 106 × 13 legacy fixtures, compiled-table and generated-projection comparisons; clean checkout verify/generate/verify leaves Git clean |
| P4.9 | Wire verification into docs/build/CI | Generated schema/native/Core/legacy/English-resource/reference-doc outputs checked; semantic IDs checked without prose regex; clean-checkout generation requires no hidden state | Complete; Desktop CI runs SpecGen verify after locked restore, native build consumes committed include, DocsCheck checks reference links |
| P4.10 | Document and close Phase 4 locally | AGENTS/docs/risk review, all applicable .NET/native/legacy/docs/package checks, logical green commits and clean review handoff; no hardware claim unless runtime changes unexpectedly | Complete locally; evidence below and final closure commit |

The three short-only aliases must be explicit in the spec even though the
legacy catalogue intentionally contains only long names. Existing
`SeamlessCompatible` values are a legacy projection, not a permanent 2.0
capability verdict. The canonical data must distinguish static build features
from device/runtime support. Complex recording, camera, codec and transport
semantics remain named typed rules; YAML must not become an executable rule
language. The v2 persisted bool/string shape and loss-averse unknown-option
behavior remain intact. `options.yaml` is canonical option data; SpecGen's typed
model/validator is the structural interpretation, and `options.schema.json` is
a generated artifact. Stable option identity is the semantic string `id`, not
the internal numeric `OPT_*` C dispatch value; future IPC must use the string.
Localized text is owned by Desktop while Core contains resource keys only.
No Phase 5 UI, Phase 6 IPC, Phase 7 lifetime or Phase
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

Phase 4 acceptance-gate follow-up on 2026-09-25: generated English option text
was moved from Core to a Desktop-owned embedded resource. A Desktop assembly
test checks every generated descriptor's keys against that resource and confirms
Core embeds no option text. Frozen Phase 3 migration fixtures compare all 109
native CLI rows, help semantics and compiled declarations, and all 106 × 13
legacy catalogue fields against the exact accepted pre-Phase-4 state. The intermittent
ADB cancellation test's original failure was a sharing violation while reading
its unique temporary `ready.txt` (`AdbTests.cs:484`). Review found a GUID-scoped
fixture, disposed Process and joined stream readers; no owner of the transient
lock was proven. Stress passed: exact method 40/40 independent runs, ADB class
10/10 runs of 18 tests, and Infrastructure suite 5/5 runs of 39 tests. The
dev-observer's original `tests/dev-observer.Tests.ps1:29` failure was missing
selected-transport classification while reading a GUID-scoped existing-log
trace from a fake native executable, without real ADB; its failing trace was
deleted by fixture cleanup. That exact test passed 20/20 independent runs and
the full legacy suite passed 28/28 twice. No test or product fix was justified
by the available evidence, and neither observation is claimed resolved; both
remain tracked in [R16](../../architecture/SEAMLESS_2_RISK_REGISTER.md).
The gate's locked .NET restore, SpecGen verify, zero-warning Release build and
all 124 .NET tests passed (Core 67, Infrastructure 39, Desktop 2, SpecGen 16).
The native client rebuilt from source, and 16/16 native tests passed with the
compiled pre-generator table and rendered-help checks. The package passed
provenance/privacy validation and the exact-archive legacy suite passed 28/28.
DocsCheck passed 468 links in 73 tracked Markdown files; build metadata and
`git diff --check` agreed. A detached clean checkout at the parity commit passed
locked restore and read-only `verify` → `generate` → `verify`, then remained Git
clean. The Android server and reviewed runtime dependencies
were unchanged. No physical-device claim is added by this gate.

PR #5 static-semantics follow-up on 2026-09-25: focused C# regressions first
failed 7/90 cases as expected: explicit zero window dimensions were falsely
rejected with `flex-display`, while the upper bounds for audio output buffer,
maximum size and minimum size alignment, and the alignment power-of-two domain,
were missing. Core now checks effective nonzero window dimensions, canonical
data carries the three native numeric upper bounds, and one named typed rule
checks alignment. Independent native parser tests exercise the same listed
cases without ADB or a mirror window. The native parser, generated native
table, legacy catalogue and frozen Phase 3 fixtures are unchanged. The
separate leading-zero/base-zero lexical mismatch was documented in the
[option guide](../../development/options.md) for a later scoped decision.
Final local
validation passed locked restore, zero-warning Release build, 149/149 .NET
tests (Core 92, Infrastructure 39, Desktop 2, SpecGen 16), 16/16 native tests,
28/28 exact-archive legacy suites, SpecGen verify, DocsCheck, build metadata
and diff checks. The reviewed Android server sources were unchanged.

PR #5 numeric-syntax follow-up on 2026-09-25: focused Core regression tests
failed 14/24 cases before the fix, including native-incompatible `08` and
misclassified octal/hexadecimal spellings. A pure Windows C-long-width
base-zero parser now supplies scalar range, alignment and conditional nonzero
checks; bitrate K/M multiplication is checked separately. Core still emits
the original validated raw value. Native CLI tests assert parsed numeric
fields, while Core tests assert valid argument emission and rule outcomes.
Canonical spec limits cover native scalar ranges without changing legacy
regex fields. The native parser, legacy catalogue, frozen fixtures and v2 JSON
shape remain unchanged. Local validation passed locked restore, zero-warning
Release build, 196/196 .NET tests, 16/16 native tests, 28/28 legacy suites,
SpecGen verify and repeat-generation byte identity, DocsCheck, metadata and
diff checks. Structured `port` component syntax remains a separate parity
limitation; no Phase 5 work or hardware claim is included. Before Phase 5
presents a `port` editor as fully validated, define its `N[:N]` component
grammar and test it independently against the native parser.

## Phase 5 — Desktop application, in bounded slices

Phase 5 keeps the existing Desktop project and Core/Infrastructure dependency
direction. A focused three-project UX reference review informs the initial
interface here; broad competitive feature completion remains Phase 10.

| Slice | Dependency | Acceptance boundary | Status |
| --- | --- | --- | --- |
| 5A — UX and UI foundation | Accepted Phase 4 integration | Pinned UX reference audit; reusable Avalonia shell, Devices workspace and Settings preview; deterministic side-effect-free scenarios, tests and self-contained DEV preview | Accepted and integrated through PR #6 |
| 5B — configuration integration | Accepted 5A | Canonical v2 draft, validation, Apply/Save revision conflicts and Cancel; profiles/settings integration; native-parity composite `port` grammar before enabling its editor | Accepted and integrated through PR #7 |
| 5C — device/native integration | Accepted 5B | Real discovery and pairing; narrowly isolated legacy native-host compatibility adapter and honest channel readiness | In progress locally; hardware pending |
| 5D — integration and acceptance | Accepted 5C | Navigation, accessibility, package and real hardware acceptance for Phase 5; only then assess alpha eligibility | Not started |

### Phase 5C dependency-ordered checkpoints

| Checkpoint | Reviewable result | Status |
| --- | --- | --- |
| 5C.1 | Explicit device-enabled composition, validated runtime paths and committed launch snapshot | Implemented locally; synthetic tests passed |
| 5C.2 | Explicit live discovery and truthful saved-profile/device association | Implemented locally; hardware pending |
| 5C.3 | Guided pairing with separate explicit profile persistence | Implemented locally; hardware pending |
| 5C.4 | Execution preflight and immutable native request translated from committed settings | Implemented locally; synthetic tests passed |
| 5C.5 | Owned legacy native start/stop/completion adapter with bounded diagnostics | Implemented locally; hardware pending |
| 5C.6 | Device/session actions, cancellation and coordinated application close | Implemented locally; synthetic tests passed |
| 5C.7 | Same-scope activation and duplicate-launch protection | Implemented locally; Windows multi-instance smoke pending |
| 5C.8 | Deterministic integration checks, isolated DEV runtime and manual hardware procedure | 324 .NET and 16 native tests passed; DEV staging pending; hardware explicitly pending |

### Phase 5A dependency-ordered checkpoints

The UI review correction pass proceeds in this order: reproduce and test
scroll/resize behavior; repair layout and scroll ownership; add System theme
behavior and shared appearance tokens; improve Devices/Settings presentation;
validate the UI and record future contracts; then build a new runnable preview
and capture real Windows screenshots. This is still Phase 5A, not Phase 5B.

| Checkpoint | Reviewable result | Status |
| --- | --- | --- |
| 5A.1 | Pin bounded reference revisions and record interaction decisions; update current Phase 5 guidance and 5B/5C contracts | Complete locally |
| 5A.2 | Replace placeholder with a typed shell, resource boundary and reusable design tokens/components; normal startup remains truthful | Complete locally |
| 5A.3 | Add Devices and Settings ViewModels/Views with deterministic, visibly simulated scenarios and isolated in-memory option drafts | Complete locally |
| 5A.4 | Add headless behavior/layout tests, validate locked build and legacy checks, publish and smoke a self-contained DEV preview, review visual evidence | Complete for PR review; hardware/accessibility follow-up remains 5D |

The Phase 5A UI correction pass was prepared for PR review before its accepted
integration; it did not complete overall Phase 5. At small heights the category
ListBox previously inherited unbounded StackPanel measurement, and at 150%
preview metrics the Settings header and status block could leave the option
viewport at zero height. A bounded category Grid, compact selector, reflowing
option editor and short-window presentation now keep the content scrollable.
Category/search result changes reset only the actual option ScrollViewer;
editing, theme changes and resizing retain its offset and detached draft.
The in-memory theme choice defaults to System through Avalonia's inherited
theme variant; explicit Light/Dark, neutral charcoal surfaces, semantic
palette roles, compact device summaries with expandable evidence, specific
metadata-derived range feedback and one implemented Ctrl+F search shortcut
remain preview-only behavior. No configuration, ADB, native, download or
update path was added.

The code source at `95d9ca5e` produced
`dist/dev/scrcpy-seamless-desktop-p05a-g95d9ca5e/`. Actual running-window
captures are under ignored `work/phase5a/screenshots/g95d9ca5e/`:
`settings-small-light.png` has an 800×500 logical client and 1222×806
physical window capture; `settings-enlarged-dark.png` and
`devices-details-dark.png` have 900×620 logical clients and 1372×986
captures; `devices-standard-light.png` has a 1080×720 logical client and
1642×1136 capture. All used the current display's 144 DPI / 150% scale;
the enlarged case also applied preview UI metrics at 150%. These are own-window
captures, not phone or system-theme-switch evidence. Headless tests covered
the actual scroll control, last-item reachability, 800×500 through 1440×900
reference layouts, enlarged metrics, focus/shortcut, theme inheritance,
unknown device evidence and long text. Locked restore, zero-warning Release
build and 210 .NET tests passed; 28/28 legacy suites, SpecGen verify,
DocsCheck and build metadata checks passed. Native/server and hardware tests
were not rerun for this presentation-only correction.

The final visible correction at code source `40634406fd5ba566cc6775753acbeb418e54bdcf`
wraps the complete product name within the constrained sidebar and gives its
accessible name the full title. The Devices summary labels the selected route
as `Selected transport`; Wi-Fi recovery describes its target instead of
presenting that same route as an additional fallback. USB selection still
reports prepared Wi-Fi fallback. No preview effects or Core state changed.
The self-contained `win-x64` preview at
`dist/dev/scrcpy-seamless-desktop-p05a-g40634406/` was launched only with
explicit `--preview` scenarios. Its new own-window captures under ignored
`work/phase5a/screenshots/g40634406/` include 800×500 logical / 1222×806
physical Settings, 900×620 / 1372×986 enlarged Settings and expanded Devices,
and 1080×720 / 1642×1136 standard Devices. Windows reported 144 DPI / 150%
display scaling; the enlarged Settings capture also used `--ui-scale=1.5`
application metrics, without multiplying the two scales. The captured
expanded details continue below the initial viewport and remain reachable by
normal scrolling. Focused headless coverage checks brand wrapping, option
editor/validation/reset/help reachability and expanded Connection details.
Locked restore, warning-free Release build, 214/214 .NET tests, SpecGen verify,
DocsCheck, build metadata and 28/28 legacy PowerShell suites passed for this
source. The new captures do not establish real Windows theme switching,
other monitor/DPI combinations, screen-reader behavior or hardware outcomes.

The intended local commit sequence is: (1) research/plan and boundary decisions,
(2) UI shell and resource foundation, (3) Devices and Settings preview behavior,
and (4) scenario/test and DEV documentation. Keep each checkpoint buildable where
practical; a test stays with the behavior it protects. Preview composition may
not create or call ADB, native-host, migration or production configuration
adapters. At the Phase 5A gate, normal startup showed an empty/not-yet-connected
state before the explicit Phase 5B storage mode was added.

The implementation kept the coupled shell, Views/ViewModels, preview sources
and their tests in one buildable UI commit. Later local commits added the
deterministic Settings filter, corrected empty-state alignment and completed
the token set. The source build at
`7d6002ca9aca20ea847bacb8892e61d5b05d18d4` produced the self-contained
`dist/dev/scrcpy-seamless-desktop-p05a-g7d6002c/` preview; it opens a real
Avalonia window with `--preview` and no phone or mirror. The local visual
record under ignored `work/phase5a/screenshots/g7d6002c/` contains light and
dark Devices, a filtered Settings validation state and an empty state. These
are Windows window captures from the running application at a configured
1080×720 logical window, 150% display scaling and 1642×1136 captured pixels
including window chrome; they are not concept art or hardware observations.

At this gate locked restore and Release build passed with no warnings; 204
.NET tests passed, including 10 Desktop tests; 28/28 legacy PowerShell suites,
SpecGen verify (109 native entries, six current outputs), DocsCheck and build
metadata checks passed. The Desktop assembly has no Infrastructure reference;
the preview tests verify normal startup stays empty, scenarios are deterministic,
Settings search leaves the draft unchanged, Core validation appears in the
rendered view, and Reset removes the in-memory override. Headless layout checks
cover a 900×620 window and light/dark variants. These checks do not establish
real Windows DPI/multi-monitor behavior, screen-reader usability, ADB/native
integration or hardware recovery. No new native/server/toolchain input was
changed or rebuilt for this UI slice.

Phase 5B opens a detached draft of canonical v2 state. Opening or searching
Settings cannot mutate stored values. Omitted, false, zero and an empty optional
argument remain distinct; raw numeric spelling such as `010` survives a no-op
round trip. Ordinary numeric-editor formatting does not silently replace native
base-zero parsing. Unknown stored options remain recoverable. Apply/Save
validates and commits against a revision; Cancel discards the draft. Unsupported
options cannot become launch arguments. The current schema has global mirroring
preferences; per-profile overrides are not implemented. Before enabling a
validated `port` editor, test `N[:N]` components, ranges and effective values
against the actual native parser.

### Phase 5B edit-session and storage contract

Normal startup selects exactly one explicit storage mode: portable
`<application>/data/`, installed per-user data, or a named development data
directory. Preview composition is selected first and uses only in-memory
sources; it cannot read, create, migrate or write configuration. A missing v2
document creates an empty edit session in memory; malformed, unsupported or
inaccessible data is an explicit error and never silently becomes an empty
session. Legacy migration is prepared from an explicitly selected DEV source,
reviewed, and committed separately; an existing valid v2 remains authoritative.
Migration preparation and confirmation require the separate profile editor to
be clean, so their reload cannot replace unstaged valid or invalid input.

The device/profile/mirroring document and Desktop preferences are independent
save groups. Each group tracks its last loaded or applied snapshot, exact byte
revision, and detached draft. Apply validates and compares against that revision,
then advances the baseline only after a successful atomic commit; an unchanged
draft performs no write. Cancel restores the current baseline without I/O.
Reset removes only a selected draft override, or resets an explicitly scoped
preference group, and remains unsaved until Apply. Conflicts retain the draft
and require a deliberate reload/discard; write failures retain the draft and
expose a diagnostic. Navigation retains drafts. Settings Apply waits for an
unstaged profile editor, and ordinary Settings Reload waits for pending
configuration or profile edits to be applied or cancelled. Failed-authority
recovery offers an explicitly labelled discard-and-reload retry. Closing with
dirty groups asks to Save and close, Discard and close, or Keep editing, and
reports any partial two-document save outcome. Save and close reserves both
edit groups through the sequential writes, releases that ownership on success
or failure, and rechecks for pending edits before shutdown. Escape and the
confirmation dialog's title-bar close retain drafts. A successful first write
remains committed if the second fails; no cross-file transaction is implied.

Desktop preferences live in versioned `desktop-preferences.json` alongside but
separate from `configuration.v2.json`. Appearance edits may preview live and
Cancel restores committed rendering; command shortcuts take effect only after
Apply. No transaction is implied across the two files. Profiles are saved
identities, not discovered or online devices, and mirroring preferences remain
global. No Phase 5B path launches ADB or native mirroring.

Phase 5B also owns persistent application appearance preferences, separate from
phone profiles and scrcpy option metadata: System/Light/Dark selection, a
selectable accent, system/default or installed UI font with safe fallback, and
an interface-scale preference with reset to defaults. Start with a small
documented scale set (for example 100%, 110%, 125%, 150%); scale typography and
layout metrics without multiplying display DPI or applying a post-layout
transform. This choice does not change Android text or the separate native
mirror. No font downloads or bundled copies of installed fonts are required.
Phase 5B also owns remapping of supported control-center commands with
overlapping-scope collision detection, reset and persistence outside mirroring
options. Shortcut help and tooltips must use the effective bindings. Native
mirror/input remapping belongs to the Phase 7 input architecture and its
Phase 10 product UI, where Desktop shortcuts and keys forwarded to Android
remain distinct; Desktop key events or global hooks are not a substitute.

### Phase 5B local validation

Code source `190b37c2953c1822869c4a1c5f8971274c2d9389` produced the
self-contained `dist/dev/scrcpy-seamless-desktop-p05b-g190b37c2/` Windows x64
DEV application. An earlier source build at `17116ca07c623b2175f52015e80fc0d92738e51a`
performed the initial write/restart UI exercise; the final source adds only a
safe close-time discard for stale drafts and was run against those saved files.
Normal-mode writes were exercised only under the ignored
`.dev-data/p05b/` directory, which was absent at first launch and remained
absent until Apply. A real first process saved a synthetic profile, raw
`max-size=1024`, accent `#3B82F6` and 110% interface scale. A second process
loaded those values; editing `max-size` to `2048` and selecting Cancel returned
the visible value to `1024`; the final build repeated that Cancel check. A
separate synthetic invalid-v2 root remained unchanged and disabled editing.
Actual running-window captures of final preview, dirty/saved settings, saved
profile and invalid-v2 state are under ignored
`work/phase5b/screenshots/g190b37c2/`. These are Desktop-only observations; no
phone, discovery, native mirroring or hardware outcome is claimed.
The final Windows build also displayed the group-specific Apply/Discard/Stay
close prompt for an unsaved option; selecting Stay kept the draft open, and
normal Cancel restored the committed value before closing.

The PR preparation gate added focused headless checks of the actual window
Closing decision path, sequential partial-save behavior and pending profile
buffers. A previously unguarded Settings Reload could replace unstaged profile
input; Settings Apply could also report success while omitting that input.
Both commands now wait for the separate editor to be staged or cancelled.
The invalid-v2 diagnostic retains its complete path in the status tooltip and
accessibility help text; its visual ellipsis does not truncate those values.

Locked restore, zero-warning Release build, 272/272 .NET tests, 28/28 legacy
PowerShell suites, native Meson 16/16 tests including port-parser endpoints,
SpecGen verify (109 native entries and six outputs), DocsCheck and build
metadata checks passed. After the close-time correction, the warning-free
Release build and all 39 Desktop tests passed again. Headless tests cover detached drafts, raw values,
profile identity, migration authority, preference conflict/fallback,
shortcuts, preview isolation and enlarged-metrics reachability. Real Windows
screen-reader and multi-monitor behavior remain unverified for the later
Phase 5D acceptance boundary. The Android server is unchanged in this slice;
the accepted Phase 5A/earlier build evidence is reused rather than described
as a new server build.

After one-way migration, v2 is canonical; legacy files are not a second writer
and are not automatically reimported over v2. Phase 5C may introduce a
temporary Infrastructure compatibility adapter consuming an isolated v2 view.
Legacy environment variables, process/window inspection and log-derived
signals remain inside that adapter. Process existence is not proof of video,
audio or control readiness; unknown readiness must stay visible as unknown.
The native runtime retains reconnect execution until Phases 7–8; Desktop does
not add another reconnect state machine. Phase 6 owns versioned machine IPC.

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

Each Phase requires separate maintainer acceptance before work begins.
Exact package/tool versions are rechecked only when their owning Phase adopts
and pins them; planning-date observations are not installation instructions.

| Phase | Dependency | Intended result / acceptance boundary |
| --- | --- | --- |
| 1 | Accepted Phase 0 | Refine existing AGENTS/docs, docs/research/ADR indexes, documentation checks, immutable-tag tooling; no C# rewrite |
| 2 | 1 | Clean server/native source build, pinned toolchain/CI, .NET 10 and stable Avalonia 12.x foundations, central metadata, urgent upstream fix audit |
| 3 | 2 | Headless Core/Infrastructure: identities, endpoints, plans, config v2/migration, persistence, ADB, activation and native-host boundaries |
| 4 | 3 | Canonical option spec/schema/generator, metadata/parity and named complex rules |
| 5 | 3, 4 | Avalonia 1.x workflow parity, localization/accessibility/design system, fake host tests; define and test composite `port` grammar against native before calling its editor fully validated; isolated legacy adapter permitted; alpha eligibility only |
| 6 | 5 | Bounded versioned stdio IPC, handshake, Stop/Focus/events, EOF/parent-death behavior; structured ordered lifecycle events; no HWND lifecycle authority |
| 7 | 6 | Separate native app/session/presentation/input/dispatcher lifetimes; generation-aware lifecycle diagnostics and deterministic reconnect harness |
| 8 | 7 | ConnectionManager, resolver, retry/failure policy, hysteresis, degradation, recording/headless/deadline semantics; timed transport decisions and readiness; next alpha eligibility |
| 9 | 8 | Recheck latest stable upstream; selective documented ports with attribution and tests |
| 10 | 9 | Evidence-based competitive completion with licensing decisions; native-input shortcut remapping UI after Phase 7; manual release check/notification; no generic Android-management expansion |
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

Phase 10's manual release check/notification should default to user initiation;
any later background checks require explicit opt-in. Compare semantic versions
and stable/prerelease channels. Checking, downloading and automatically
installing are separate capabilities; a notice must not imply that an update
was downloaded or installed.

Future 3.0 directions remain plans: advanced public semantic-palette overrides
with separate light/dark values, live preview, contrast feedback, reset and
optional data-only theme import/export; runtime language switching; embedded
mirroring; Linux/macOS; Windows ARM64; automation API; extensions/transports;
secure updates; a measured Meson-vs-CMake review; and Native AOT benchmarking.
Theme imports cannot execute XAML, scripts or assemblies, and invalid or
missing roles fall back to readable built-in values. Always bundle complete
English localization as an offline fallback. An optional selected-language
download/cache policy needs measured compressed resource sizes before adoption:
do not infer memory use from the number of bundled strings. Language packs
need a versioned data-only schema, locale and compatible resource-key/app
versions, bounded input and authenticated release metadata/packs. Failure must
keep the previous usable locale, and missing keys fall back safely. Cached
languages work offline. Automatic update installation likewise waits for a
release/update security design: authenticated metadata and artifacts, compatible
Desktop/native/server/dependency packages, safe staging, rollback/recovery,
data preservation and no replacement of active binaries during mirroring or
recording. Do not add networking or speculative loaders to Phase 5A.

The request for an “ico from adb” is ambiguous between the Windows Seamless
executable icon and Android application/device icons. Reuse only an existing
approved Seamless asset with established provenance if one is available; do not
extract ADB or other third-party branding. Clarify the intended target before
planning device-side icon enumeration or APK extraction.

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

## Phase 3 integration record

Phases 0–2 were accepted. PR #3 merged at
`ee373709ecdda8e323d93a856a346772b2c46485`; Phase 2's 20 commits are
ancestors of `seamless-2.0` and the short-lived branch was deleted. The
intermittent audio watch item remains tracked and is not Phase 3 audio work.
Phase 3 integrated separate Core/domain, application-contract,
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
These baseline checks preceded the focused gate and PR #4 merge-commit
integration.

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
DocsCheck, metadata and diff validation completed this local gate before PR #4
integration. No new hardware claim follows from these checks.

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
their timing assertions. The hosted `test` job and other PR #4 checks passed
before integration. The local timing limitation remains part of the handoff
evidence.
