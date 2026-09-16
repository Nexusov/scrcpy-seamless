# Build, package, and release tooling agent guide

Scope: `scripts/`.

Build/release code is security- and provenance-sensitive. Read:

- `/AGENTS.md`
- `/docs/BUILD.md`
- `/docs/PACKAGING.md`
- `/docs/development/git-workflow.md`
- `/docs/development/release-process.md`
- `/docs/development/debugging.md`

## Safety

Never push, merge, create/push a release tag, publish a GitHub Release, or
force-push without explicit user authorization.

The current 1.x publisher is known to move an existing release tag. Seamless
2.0 must remove that behavior. Do not use the mutable-tag workflow for a new
2.0 release.

Published tags are immutable.

## Package privacy

A package must never include:

- `phone.json` or migrated personal device profiles;
- pairing codes;
- ADB private keys;
- local build configuration;
- logs containing personal/secrets data;
- developer-only temporary files.

Keep package-content tests explicit.

## Provenance

Do not bypass a provenance/hash failure by merely updating expected hashes.

A dependency/runtime hash change requires understanding why the bytes changed,
updating licensing/provenance, and validating the resulting package.

Official 2.0 releases must be built from the exact tagged source through the
release pipeline. Imported baseline binaries are a 1.x compatibility mechanism,
not the final 2.0 release model.

## Build changes

Keep dependency/compiler/build-system upgrades separate from unrelated native
lifecycle refactors whenever possible. This keeps regressions diagnosable.

Meson/Ninja remains the canonical native build system for 2.0.

## Tests

Run the applicable build/package/publication tests. For a 1.x package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 -ArchivePath .\dist\scrcpy-seamless-win64.zip
```

Publication tests must use disposable/local remotes unless the user explicitly
authorizes real remote operations.
