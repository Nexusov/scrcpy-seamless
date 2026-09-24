# Packaging provenance

`release-manifest.json` owns the release/build labels and reviewed runtime hashes.
`launcher/version.ps1` reads this manifest from the repository or packaged `app/`.

The existing native client is an **imported baseline**. Its recorded source
fingerprint and executable hash document the approved pairing; they do not claim
that the binary was reproduced from those sources in this checkout. Launcher-only
packages may reuse that client only while its hash, the native source fingerprint,
and every bundled runtime-file hash still match the reviewed manifest.

`scripts/build.ps1` writes `dist/scrcpy.exe` and its `.manifest.json` sidecar.
It checks that native sources did not change during the build. Packaging selects
that output whenever either build file exists, verifies both files and the current
sources, and fails on incomplete or stale builds rather than silently falling back.
Remove both build files only when deliberately returning to the imported baseline.

Native source fingerprints normalize valid UTF-8 text line endings and hash other
files unchanged. Relative paths use ordinal ordering. Build provenance records
source identity, not a reproducible toolchain or dependency-build attestation.

`scripts/package.ps1 -RuntimeDirectory PATH` accepts a flat installed runtime or
a package containing `app/`. Runtime DLLs, server and resource images are validated
against the manifest even when selecting a freshly built client. The ZIP includes
`app/native-provenance.json` identifying which native client was selected.
Changes to dependencies require an intentional manifest review and matching
third-party notices/source archives.

Phase 2 also provides a source build for the Android server. Its output and
fingerprint are kept under ignored `work/phase2/server/artifacts/`; the current
packager still requires the reviewed `scrcpy-server` hash above. To try the
source-built server in an isolated development runtime, place its artifact at
`app/scrcpy-server` in that runtime and run the native client there. Do not
present that runtime as a validated 1.x package or bypass the packager's hash
gate. See the [server build guide](development/server-build.md).

`scripts/publish.ps1 -CommitMessage "Describe the change"` stages changed public
project paths and refuses unknown or private files, including force-staged device
settings. It rejects a release tag that already exists locally or remotely
before staging. It also rejects private paths in commits not yet on remote
`main`, including files removed again in a later local commit. It requires a
clean checkout after committing. A new tag and
`main` are pushed together with a non-forced atomic push; if the remote rejects
either ref, neither remote ref advances. A failed push may leave a local commit
and tag for manual review. ZIP assets remain a separate manual upload to the
same release.
