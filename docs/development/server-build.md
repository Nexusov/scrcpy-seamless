# Android server source build

Phase 2 keeps the Java server build separate from the current 1.x portable
package. The source is [under `src/scrcpy/server`](../../src/scrcpy/server/),
derived from Genymobile scrcpy 4.0 and covered by the root Apache-2.0
[license](../../LICENSE). The existing [release manifest](../../release-manifest.json)
still pins the reviewed upstream server binary. A locally rebuilt server is
build evidence, not a silent replacement for that runtime.

## Pinned inputs

The exact JDK and command-line-tool archive URLs and SHA-256 values, along
with Android SDK package revisions and repository SHA-1 values, live in
[`scripts/server-toolchain.json`](../../scripts/server-toolchain.json). The
bootstrap verifies the downloaded standalone archives before extraction and
checks installed SDK revisions. The source build uses:

| Input | Pin | Origin / terms |
| --- | --- | --- |
| Eclipse Temurin Windows x64 JDK | `17.0.20.1+1`, archive SHA-256 `e53a79c3c3d86865bd7e787903884331068e71321714ffd44f145785affc7cb0` | [Adoptium release](https://github.com/adoptium/temurin17-binaries/releases/tag/jdk-17.0.20.1%2B1), OpenJDK GPL-2.0 with Classpath Exception; build tool only |
| Android command-line tools for Windows | build `15859902`, archive SHA-256 `90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a` | [Google download and SDK terms](https://developer.android.com/studio#command-line-tools-only); build tool only |
| Android SDK packages | `platforms;android-36` revision 2, `build-tools;36.0.0`, `platform-tools` revision `37.0.1` | Installed by Google's `sdkmanager` after the user accepts the [Android SDK License Agreement](https://developer.android.com/studio#terms-and-conditions); [Google repository metadata](https://dl.google.com/android/repository/repository2-3.xml) names `platform-36_r02.zip` (SHA-1 `2c1a80dd4d9f7d0e6dd336ec603d9b5c55a6f576`), `build-tools_r36_windows.zip` (SHA-1 `f16ccffd34de8790dede813a6c7d8e2c11a27b50`) and `platform-tools_r37.0.1-win.zip` (SHA-1 `e03e78b1d80b396f1c3358e31251cb31740e1110`) |
| Gradle wrapper | `9.3.1` and distribution checksum in [`gradle-wrapper.properties`](../../src/scrcpy/gradle/wrapper/gradle-wrapper.properties) | [Gradle distribution](https://gradle.org/releases/), Apache-2.0 build tool |
| Android Gradle plugin | `9.1.0` in the [root Gradle build](../../src/scrcpy/build.gradle) | Google Maven build plugin from the [AOSP tools source](https://android.googlesource.com/platform/tools/base/); [AGP 9.1 compatibility](https://developer.android.com/build/releases/agp-9-1-0-release-notes) requires JDK 17, Gradle 9.3.1 and Build Tools 36.0.0 |
| JUnit | `4.13.2` in the [server Gradle build](../../src/scrcpy/server/build.gradle) | [JUnit 4](https://github.com/junit-team/junit4), EPL-1.0; test dependency only |
| Checkstyle | `10.12.5` in the [style configuration](../../src/scrcpy/config/android-checkstyle.gradle) | [Checkstyle](https://github.com/checkstyle/checkstyle), LGPL-2.1; build-time style tool only |

The server currently declares no third-party Java runtime library dependency.
Gradle resolves transitive build/test dependencies from Google's Maven repository
and Maven Central. Exact top-level versions and distribution archives are pinned,
and the bootstrap rejects SDK package revisions other than those above. Google
publishes SHA-1 values for those SDK archives in its repository metadata; the
bootstrap does not yet have independent SHA-256 values for them. Gradle's
[buildscript lock](../../src/scrcpy/buildscript-gradle.lockfile),
[server lock](../../src/scrcpy/server/gradle.lockfile), and
[SHA-256 verification metadata](../../src/scrcpy/gradle/verification-metadata.xml)
now approve the resolved Maven graph and artifact bytes. The root build locks
the Android Gradle plugin classpath separately; strict locking applies to every
resolvable project configuration. Verification covers dependency artifacts and
Maven metadata, with signatures disabled. These controls do not prove that
upstream repositories were uncompromised when the hashes were reviewed, nor do
they make the server APK byte-for-byte reproducible. Review licenses and source
obligations before redistributing any new build input or switching the shipped
server binary.

## Maintaining Gradle dependency state

The lock files and verification metadata are generated Gradle inputs, not
ordinary build outputs. A routine build and CI run must not use `--write-locks`
or `--write-verification-metadata`. `scripts/build-server.ps1` requires these
files and includes them in its source fingerprint. The hosted Android job checks
that all three are tracked, runs strict verification, exercises rejected version
and checksum fixtures, and rejects changed dependency-state files.

For an intentional dependency upgrade, first review the new version, artifact
origins and license. Update the direct pin and `scripts/server-toolchain.json`
where applicable. Then, with the pinned JDK/SDK and a clean or refreshed Gradle
dependency cache, run from `src/scrcpy`:

```powershell
.\gradlew.bat :server:dependencies :server:assembleRelease :server:check `
  --write-locks --write-verification-metadata sha256 `
  --refresh-dependencies --no-daemon
```

Both write flags are deliberate maintenance operations. The `dependencies`
task includes the server's resolvable configurations, while Gradle's
verification bootstrap also reaches Android Gradle Plugin internal
configurations. Review the complete lock/XML diff; do not trust a new
Gradle-generated checksum solely because it was generated. Check the coordinates
against the intended Google Maven/Maven Central inputs, independently compare
critical artifacts with their official repository bytes, and rerun the build
with `--dependency-verification=strict` from a fresh dependency cache. Commit
only the reviewed generated state and related version changes. If a clean
cache reveals an additional required POM/module artifact, regenerate with
`--refresh-dependencies`, review that addition, and repeat the clean-cache
check. Do not replace mismatching hashes without establishing why bytes differ.

For the Phase 2 baseline, the direct AGP 9.1.0, JUnit 4.13.2 and Checkstyle
10.12.5 JAR hashes were separately checked against Google Maven/Maven Central.
The complete recorded graph then passed a second empty-cache strict build.
No PGP trust decision or full supply-chain attestation is claimed.

## Windows source-build commands

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap-server.ps1
```

The bootstrap uses the ignored `work/phase2/server/` directory. It downloads
only the fixed JDK and Android command-line-tool archives, verifies their
checksums, and extracts them locally. It does not change the machine-wide `PATH`
or install Android Studio. If SDK licenses have not been accepted, it stops and
prints the required manual step:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap-server.ps1 -ReviewSdkLicenses
```

Review the terms and answer the interactive `sdkmanager --licenses` prompts
yourself. The scripts never answer them for you. Then rerun the first command to
install the pinned SDK platform and Build Tools, followed by:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-server.ps1
```

The build script bootstraps any missing pinned tools, sets Java/SDK/Gradle paths
only for its process, and runs `gradlew.bat :server:assembleRelease
:server:check --no-daemon --dependency-verification=strict`. The unsigned APK appears at
`src/scrcpy/server/build/outputs/apk/release/server-release-unsigned.apk` and a
copy plus SHA-256 evidence appears under `work/phase2/server/artifacts/`.
`server-build.json` records the source HEAD, SHA-256 fingerprint of the relevant
non-ignored source/build inputs, and the executed task list. These
outputs are ignored by Git and are not included by the current 1.x packager.

For an existing verified local cache, append `-Offline` to the bootstrap or
build command. Offline mode refuses absent/corrupt pinned archives or missing SDK
packages and adds Gradle's `--offline` flag. Android server unit tests and Checkstyle are part of `:server:check`;
the inherited Checkstyle configuration currently sets `ignoreFailures = true`,
so a style violation alone does not fail the task. These checks do not establish
behavior on physical hardware. Keep any changed server
binary out of the portable package until runtime-hash provenance, compatibility,
licensing and device tests receive their own review.
