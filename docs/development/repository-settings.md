# Recommended GitHub repository settings

These are recommended remote safeguards for scrcpy Seamless. They are
documentation, not permission for an agent to modify repository settings.

Any remote ruleset/permission change requires explicit owner authorization.

## Protected branches

Once the 2.0 workflow is active, consider GitHub Rulesets/branch protection for:

- `main`
- `seamless-2.0`

Recommended properties once CI job names are stable:

- block force-pushes;
- block branch deletion;
- require successful required CI checks before merge;
- require pull requests for normal integration;
- prevent direct agent pushes to protected branches;
- allow an explicit maintainer bypass path for emergencies.

Do not enable a required status check before the corresponding workflow is
stable and consistently available; otherwise the repository can deadlock its
own merge process.

## Release tags

Protect release-tag patterns such as:

```text
v*
```

so existing published tags cannot be force-updated or deleted by normal
automation.

Tag creation should remain restricted to the documented release path.

## GitHub Actions permissions

Workflows should declare minimal explicit `permissions:` instead of relying on
broad defaults.

Examples:

- build/test jobs normally need `contents: read`;
- PR jobs should not receive write permissions unless the job genuinely needs
  them;
- artifact-attestation jobs may require `id-token: write` and
  `attestations: write` only where needed.

Never grant write permissions to untrusted fork code.

## Action pinning

For third-party GitHub Actions, prefer pinning to an immutable full commit SHA
after review. Keep the human-readable release version in a comment when useful.

Review updates deliberately rather than floating on an unbounded mutable
reference.

OpenAI/GitHub/Microsoft first-party actions should still be version-reviewed;
use the repository's chosen consistent pinning policy.

## Dependency/security automation

Where useful and available, enable:

- CodeQL for supported languages;
- dependency/dependency-review scanning;
- secret scanning for the public repository;
- Dependabot (or one chosen equivalent) for NuGet and GitHub Actions.

Do not run multiple competing dependency bots without a reason.

Native/Gradle dependency updates that are not fully represented in supported
manifests still require the explicit provenance/update process documented by
the project.

## Release environments

If GitHub Environments are used for release publication, consider requiring
maintainer approval for the production release environment.

The build/test pipeline should be able to run without production release
credentials.

## Apply deliberately

The owner should apply these settings after Phase 1/2 CI names and workflow
shape are known.

Agents must not change remote branch rules, repository permissions, secrets,
environments, or tag rules without explicit authorization.
