# Upstream baseline and early Phase 2 audit

Audit date: 2026-09-24. The native client and Android server in this repository
derive from [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy) v4.0,
whose tag resolves to commit
[`2322868e9e256eb5fce0b3d659ab2a409f29bae1`](https://github.com/Genymobile/scrcpy/commit/2322868e9e256eb5fce0b3d659ab2a409f29bae1).
The fork-specific reconnection changes are described in
[CHANGES.md](../CHANGES.md). The 1.x `release-manifest.json` records
`Upstream: scrcpy 4.0` and pins the reviewed imported server/runtime hashes;
that field describes current distribution, not the latest release audited.

At audit time the [latest stable GitHub release](https://github.com/Genymobile/scrcpy/releases/tag/v4.1)
was **v4.1**, published 2026-07-12, with annotated tag resolving to commit
[`2926c06c5dc3064ae6d8db706f1a98a37cfcf3f0`](https://github.com/Genymobile/scrcpy/commit/2926c06c5dc3064ae6d8db706f1a98a37cfcf3f0).
This was checked against the repository's latest non-prerelease GitHub Release
and the signed tag target. The [v4.0...v4.1 comparison](https://github.com/Genymobile/scrcpy/compare/v4.0...v4.1)
contains 42 upstream commits. A newer release must be rechecked before a
later broad upstream adoption or public 2.0 release.

This early audit covers urgent or relevant security, crash, race, correctness
and protocol fixes. It does not upgrade the fork wholesale, adopt new codecs,
change the reviewed runtime libraries, or claim source-built server output is
byte-identical to the imported server. See [PORTS.md](PORTS.md) for exact
decisions, affected files and tests. Phase 9 owns the broader selective
feature/architecture audit.
