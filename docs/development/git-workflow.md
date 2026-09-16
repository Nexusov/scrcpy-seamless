# Git workflow

This document defines the intended source-control workflow for scrcpy Seamless.
Execution-environment or higher-priority agent instructions may prohibit some
Git operations. Never bypass those restrictions; report the remaining manual
operation instead.

## Goals

History should be:

- understandable;
- reviewable;
- bisectable;
- revertable;
- traceable to architectural decisions.

Avoid both giant "everything" commits and artificial micro-commits.

## Before editing

Inspect:

```bash
git status
git branch --show-current
git log --oneline --decorate -n 20
```

Determine the current branch, HEAD, worktree state, and whether user-created
uncommitted changes exist.

Never reset, discard, overwrite, or stash user work without explicit
permission.

## Branch model

- `main` — current stable production line.
- `seamless-2.0` — long-lived 2.0 integration branch once created.
- `2.0/pXX-*` — short-lived Phase/work branches.

A large Phase may use several narrower branches when that improves review.

Do not add a generic `develop` branch.

If `seamless-2.0` does not exist at project bootstrap, create it from the
accepted stable `main` commit only when the environment allows branch creation
and the owner authorizes the operation. Otherwise document the intended target
and continue only with operations permitted by the harness.

Normal implementation work should not be pushed directly to `main` or
`seamless-2.0`.

## 1.x maintenance

While 2.0 is in development, `main` may receive real 1.x maintenance releases.

Forward-port relevant fixes into 2.0 deliberately. Do not merge a heavily
diverged stable branch merely to obtain one fix.

## Commits

Use Conventional Commits 1.0.0 style:

```text
feat(core): add typed device profile model
fix(ipc): reject oversized frames
refactor(native): separate app and session lifetimes
test(reconnect): cover stale session callbacks
build(ci): add native sanitizer job
docs(architecture): document transport ownership
```

A commit should represent one coherent change that can be explained, reviewed,
and reverted independently.

Use a commit body when the reason/tradeoff is non-obvious.

Avoid generic subjects such as `update`, `fix stuff`, `refactor`, or
`Seamless 2.0 changes`.

## Green commits

Prefer additive migrations that keep history buildable:

1. add new implementation and tests;
2. migrate consumers;
3. validate;
4. remove obsolete implementation.

Do not intentionally create broken `part 1/4` commits when a compatibility seam
can keep history bisectable.

## Keep together

Normally keep these in the same logical commit:

- a behavior fix and its regression test;
- a canonical spec change and generated outputs;
- a migration and its migration tests;
- an invariant change and the documentation needed to explain it.

## Keep separate

When practical, separate unrelated:

- file moves/renames;
- formatting;
- dependency upgrades;
- refactors;
- behavior changes;
- unrelated documentation cleanup.

A mechanical rename/move immediately before a semantic change is often clearer
as its own commit.

## Commit size

There is no line-count limit. Ask:

> Can this commit be independently explained, reviewed, and reverted?

If not, split it.

## Validation before commit

Run checks for every touched subsystem.

Examples:

- C#: build + affected `dotnet test`.
- Native C: Meson compile + affected native tests.
- Android server: affected Gradle build/tests.
- Specs: generator verify + parity tests.
- Docs/AGENTS: DocsCheck once available.
- UI: relevant headless/visual/E2E tests.

Do not commit a known failing relevant test without an explicitly documented
approved reason.

## Checkpoints and Phases

Before a major Phase, record explicit checkpoints in
`docs/exec-plans/active/seamless-2.md`.

After a meaningful checkpoint:

- relevant validation is green;
- logical commits exist;
- plan progress is current.

Before a Phase is complete, run the full automated validation available without
physical hardware and build a traceable development package artifact.

## Development artifacts

Development artifacts are not releases and do not need tags.

Recommended naming:

```text
scrcpy-seamless-2.0-dev-p05-g1a2b3c4.zip
```

Where practical, diagnostics/About should expose the Git SHA and development
status.

Do not commit ordinary build outputs to Git.

## Push policy

Do not push automatically just because a checkpoint is complete.

Never force-push shared/protected branches.

Push a work branch only with explicit owner authorization or an already
approved workflow that grants that permission.

Do not merge a PR without explicit authorization.

## Pull requests

Usually one Phase maps to one PR, but split a Phase into multiple PRs when the
architecture changes are independently reviewable.

PR descriptions should include:

- Summary
- Motivation
- Architecture impact
- Behavior changes
- Migration impact
- Tests
- Build/package validation
- AGENTS/docs impact
- Known limitations
- Follow-up work

Preserve useful granular commits instead of automatically squashing a large
architecture Phase into one commit.

## End-of-Phase report

Report:

- Branch
- Base SHA
- Head SHA
- Commit list
- Diff summary
- Builds
- Tests
- Artifact
- Architecture impact
- AGENTS/docs impact
- Known risks
- Next-Phase prerequisites

Then stop and wait for owner approval before starting the next Phase.
