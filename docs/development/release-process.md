# Release process

This document defines the intended Seamless 2.0 release/versioning policy.
Publishing remote releases always requires explicit owner authorization.

## Separate these concepts

- **Build** — compilation/test of a source state.
- **Artifact** — CI/local package for testing.
- **Pull Request** — reviewable source change.
- **Prerelease** — public alpha/beta/RC for real testers.
- **Stable release** — officially supported version.

Do not create a GitHub Release for every commit or Phase.

## Versioning

Use Semantic Versioning 2.0.0.

1.x numbers are reserved for actual stable maintenance of Seamless 1.x.

Seamless 2.0 development uses:

```text
2.0.0-alpha.N
2.0.0-beta.N
2.0.0-rc.N
2.0.0
```

Git tags include the `v` prefix:

```text
v2.0.0-alpha.1
v2.0.0-beta.1
v2.0.0-rc.1
v2.0.0
```

Do not hard-code a prerelease ordinal to a Phase. If an extra alpha is needed,
use the next available alpha number.

## Recommended milestone eligibility

- After Phase 5: first usable 2.0 alpha candidate.
- After Phase 8: next architecture-complete alpha candidate.
- After Phase 11: beta candidate after the legacy production path is removed.
- After Phase 12: release-candidate eligibility after hardening.
- After Phase 13: stable 2.0.0 eligibility after RC acceptance/final release
  verification.

A milestone means "eligible to propose a prerelease", not "publish
automatically".

## Immutable tags

Published tags never move.

Never use `git tag -f` or force-push a published release tag.

If an alpha/beta/RC is bad, create the next prerelease.

If stable 2.0.0 needs a fix, publish 2.0.1.

Release tooling must fail rather than reuse an existing published tag.

The corrected 1.x publisher checks local and remote tag existence before
committing, then requests an atomic, non-forced push of `main` and a new tag.
If that push fails, inspect the local commit and tag before retrying with a new
release version; no remote ref should have advanced.

## Official release source

Official prereleases/releases are built by the release pipeline from the exact
immutable tagged commit.

Required release pipeline properties:

- clean source checkout;
- pinned/restored dependencies;
- source-built Android server;
- source-built native client;
- source-built Desktop;
- release-required automated tests;
- spec/docs verification;
- package smoke test;
- SHA-256 checksums;
- SBOM;
- provenance/artifact attestation where supported;
- third-party license/NOTICE validation.

Do not manually assemble a local binary and call it an official release once
the canonical release pipeline exists.

## Development artifacts

Development packages are traceable but untagged.

Example:

```text
scrcpy-seamless-2.0-dev-p07-g5d6e7f8.zip
```

They may be retained as CI artifacts for QA/regression comparison. They are not
GitHub Releases.


## CI/release workflow security

GitHub Actions workflows used for release integrity should declare minimal
explicit permissions.

Prefer immutable full-SHA pins for third-party Actions after review.

For official distributable artifacts, use GitHub Artifact Attestations where
available. Grant `id-token: write` / `attestations: write` only to the job that
needs them.

Do not expose production release credentials to ordinary PR builds.

See `repository-settings.md` for recommended branch/tag/ruleset protections.

## Release authorization

At a milestone:

1. report eligibility;
2. report exact commit SHA;
3. report tests/hardware status;
4. report known limitations;
5. stop.

Only after explicit owner authorization may tooling create/push the tag and
publish the prerelease/release.

## Release candidate discipline

The RC must be as close as possible to the intended stable code.

Do not perform legacy-architecture removal after the first RC.

If a code change is required after RC:

- make the minimal reviewed fix;
- rerun required validation;
- issue the next RC (for example `rc.2`).

Stable 2.0.0 should be the accepted RC source state except for release metadata
changes that do not alter product behavior.

## Supply-chain integrity

Where GitHub Artifact Attestations are available for the public repository,
use them for official distributable artifacts, not every transient test build.

Attestation proves provenance, not security; tests/review are still required.

Do not claim byte-for-byte reproducibility unless it is actually measured and
documented.

## Signing

If a trustworthy code-signing/tag-signing setup becomes available, integrate
it as a separate reviewed release-security improvement. Do not block ordinary
local development on the absence of a personal signing certificate.
