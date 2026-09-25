# scrcpy Seamless agent guide

This file is the short entry point for coding agents and contributors. It is
not the project encyclopedia. Detailed rules live under `docs/`.

## Project

scrcpy Seamless is an independent Apache-2.0 open-source project derived from
Genymobile/scrcpy. The current 1.x tree is a Windows x64 PowerShell/WinForms
launcher around a modified scrcpy 4.0 client. Seamless 2.0 is a planned
architectural rewrite that keeps the product identity and behavior while
replacing the launcher/application layer and restructuring the native
lifecycle.

The product name is **scrcpy Seamless**. Do not rename it without an explicit
product decision.

## Canonical repository language

Use English for code, comments, commit messages, AGENTS files, architecture
documents, ADRs, specifications, contribution docs, and release docs.

When the repository owner speaks Russian in chat, answer the owner in Russian
unless asked otherwise. Do not translate established identifiers.
For README, CONTRIBUTING and repository documents, follow the
[documentation voice guidance](docs/AGENTS.md#documentation-voice).

## Read before changing code

Always read the most specific `AGENTS.md` that applies to every file you touch.

For normal development, also read:

- `CONTRIBUTING.md`
- `docs/ARCHITECTURE.md`
- `docs/development/git-workflow.md`
- `docs/development/debugging.md`
- `docs/development/release-process.md`
- `docs/development/repository-settings.md`

For native/upstream work also read:

- `docs/BUILD.md`
- `docs/CHANGES.md`
- `docs/PACKAGING.md`

For Seamless 2.0 work, read the active execution plan under
`docs/exec-plans/active/` once it exists.

## Current repository map

- `launcher/` — current 1.x PowerShell/WinForms application and VBS wrappers.
- `src/scrcpy/` — current native scrcpy-derived client and Android server tree.
- `src/desktop/` — .NET/Avalonia Desktop with headless Core/Infrastructure
  contracts, the accepted Phase 5A UI foundation, and local Phase 5B settings
  integration; device/native integration and IPC remain later Phases.
- `spec/options/` — canonical static option data and its generated schema;
  read `spec/AGENTS.md` before changing the specification.
- `tools/ScrcpySeamless.SpecGen/` — deterministic option metadata generator
  and read-only drift verification.
- `scripts/` — tests, build, package, provenance, and publication tooling.
- `tests/` — PowerShell regression/integration and .NET headless tests.
- `docs/` — architecture, build, packaging, and development knowledge.
- `licenses/` / `THIRD_PARTY.md` — third-party licensing and provenance.
- `release-manifest.json` — current 1.x reviewed release/runtime metadata.

The 2.0 target structure may evolve. When files move, move or replace scoped
AGENTS files in the same change so instructions remain aligned with the tree.

## Current validation

Until the 2.0 build system replaces these commands, the canonical 1.x test
command is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

For an exact 1.x package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 -ArchivePath .\dist\scrcpy-seamless-win64.zip
```

Run every additional subsystem-specific check required by nested AGENTS files.
The current [build guide](docs/BUILD.md) links the Phase 2 native, server and
Desktop source-build paths and their separate validation commands.

## Non-negotiable engineering rules

- Understand the current behavior before replacing it.
- Prefer explicit ownership, explicit lifetimes, and typed state.
- Fix root causes, not symptoms. Follow `docs/development/debugging.md`.
- Add regression coverage for non-trivial bugs where reasonably possible.
- Do not hide races with sleeps, retries, swallowed errors, or weakened tests.
- Keep commits logically atomic and, where practical, green.
- Do not mix unrelated cleanup into behavioral fixes.
- Never discard, reset, overwrite, or stash user-created work without explicit
  permission.
- Do not push, merge, tag, publish, force-push, or rewrite shared history
  without explicit authorization.
- Published release tags are immutable.
- Never commit pairing codes, ADB private keys, personal device configuration,
  logs containing secrets, or local build configuration.
- Treat ADB output, device names, endpoints, configuration, IPC, and user input
  as untrusted input.
- Do not add telemetry or remote content by default.
- Preserve required upstream and third-party copyright/license notices.
- Do not copy GPL/AGPL code or assets into the Apache-2.0 project without an
  explicit licensing decision by the owner.

## Seamless 2.0 direction

The accepted high-level direction is:

- C# / .NET 10 LTS for the desktop application.
- Avalonia UI 12.x stable for the desktop presentation layer.
- C remains the native mirroring/runtime language.
- Java remains the Android server language.
- Meson/Ninja remains the native build system for 2.0.
- Desktop and native runtime remain separate processes.
- Application lifetime and connection-session lifetime must be distinct.
- Reconnection/transport switching becomes a first-class subsystem.
- Canonical option metadata replaces duplicated option catalogues.
- The project remains a monorepo.
- The 2.0 release remains Windows x64, while Core/Application code should not
  acquire unnecessary Win32 dependencies.
- The native mirror remains a separate window in 2.0; embedded rendering is a
  future direction, not a 2.0 requirement.

Do not perform speculative migrations outside the current approved Phase.

## Documentation impact rule

Every task ends with an AGENTS/docs impact review.

Ask whether the change modified:

- architecture or ownership;
- lifecycle/threading invariants;
- paths or commands;
- build/test/release workflow;
- protocol/config/spec behavior;
- generated-code rules;
- public behavior.

If yes, update the applicable AGENTS file and canonical documentation in the
same change. If no update is needed, state that explicitly in the task report.

## Generated code and sources of truth

Never edit generated output when a canonical spec/generator owns it. Change the
source of truth and regenerate. Phase 4 option metadata is owned by
`spec/options/options.yaml`; run SpecGen verification after regeneration.

## Bug fixes

Follow `docs/development/debugging.md`. In particular:

- reproduce or collect evidence before guessing;
- identify the first incorrect state and violated invariant;
- prefer a failing regression test before the fix;
- validate neighboring lifecycle/failure states;
- do not claim certainty when the root cause is still uncertain.

## Git and releases

Follow `docs/development/git-workflow.md` and
`docs/development/release-process.md`.

Important summary:

- `main` is the stable line.
- Seamless 2.0 integrates through `seamless-2.0` when that branch exists.
- Use short-lived work branches/PRs when the execution environment allows it.
- Development artifacts are not GitHub Releases.
- 2.0 uses SemVer prereleases (`alpha`, `beta`, `rc`), not fake 1.x milestones.
- Never move an already published tag.

## Stop instead of guessing

Stop and report uncertainty when:

- repository state conflicts with the expected workflow;
- required user work would be destroyed;
- a license is unclear;
- a release operation would mutate remote/shared state without authorization;
- a security-sensitive assumption is unverified;
- a hardware-only claim cannot actually be validated in the current
  environment.
