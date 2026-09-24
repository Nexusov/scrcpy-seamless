# Build and version sources of truth

Phase 2 adds build foundations without changing the shipped 1.x product version
or declaring a 2.0 release. Keep each value at its owning boundary:

| Boundary | Canonical input | Current meaning |
| --- | --- | --- |
| Shipped 1.x version, imported native baseline and reviewed runtime hashes | [`release-manifest.json`](../../release-manifest.json) | `1.0.0`, build `20260915.3`, scrcpy 4.0 runtime. The current packager enforces its hashes. |
| scrcpy-derived source protocol/version | Native [`meson.build`](../../src/scrcpy/meson.build) and Android [`build.gradle`](../../src/scrcpy/server/build.gradle) | Both remain 4.0 while the fork still uses the 4.0 protocol. The [metadata check](../../scripts/check-build-metadata.ps1) requires agreement with the reviewed runtime baseline. |
| Native development dependencies | [`native-toolchain.json`](../../scripts/native-toolchain.json) | Exact download URLs, archive SHA-256 values and versions; the bootstrap also verifies each reviewed runtime file against the release manifest. |
| Android build dependencies | [`server-toolchain.json`](../../scripts/server-toolchain.json), checked-in Gradle wrapper and Gradle files, [buildscript lock](../../src/scrcpy/buildscript-gradle.lockfile), [server lock](../../src/scrcpy/server/gradle.lockfile), and [verification metadata](../../src/scrcpy/gradle/verification-metadata.xml) | Fixed JDK and SDK-tool archive checksums, direct versions, strict resolved-version locks and SHA-256 checks for the approved Maven artifacts. The server build requires these inputs; [regeneration](server-build.md#maintaining-gradle-dependency-state) is a separate maintenance action. |
| Desktop SDK and direct packages | [`global.json`](../../global.json), [`Directory.Packages.props`](../../Directory.Packages.props) and project `packages.lock.json` files | Exact stable .NET 10 SDK, central NuGet package versions and locked transitive graphs. The local SDK bootstrap reads `global.json`. |
| Latest upstream audit | [Upstream baseline](../upstream/BASELINE.md) and [port decisions](../upstream/PORTS.md) | Records what was inspected; it does not silently change the 4.0 protocol or imported runtime. |
| Phase 2 development artifact identity | Git HEAD plus the `p02` identifier in the artifact name and SHA-256 sidecar | Traceability for a local artifact, not a product release version or Git tag. |

Run `scripts/check-build-metadata.ps1` when editing these boundaries. It checks
cross-file version agreement; it does not prove binary or dependency
reproducibility. The 2.0 release version will need an explicit canonical owner
when the release pipeline is designed. Creating one now would introduce a
second, unused product-version source alongside the 1.x release manifest.
