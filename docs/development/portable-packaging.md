# Windows x64 portable package

The 2.0 portable package starts at ScrcpySeamless.Desktop.exe with its
self-contained .NET runtime and bundled machine-capable native runtime. The
archive includes the source-built Android server. Build and stage from the
same clean Git commit. Device and interactive acceptance are separate checks.

## Build the inputs

Use the [native source-build procedure](native-build.md) and the
[Android server source-build procedure](server-build.md). Keep the native
executable and its build sidecar together. Keep the Android server and
server-build.json together. Both build sidecars must identify the commit
being staged. Staging recomputes both source fingerprints and artifact hashes.
Build the native component with `scripts/build.ps1 -PortableRelease`. This
selects the Release configuration and strips symbols from the linked client.
The stage requires that build profile from the exact committed source.

The native bootstrap downloads two preapproved archives. Staging uses
[scripts/portable-runtime.json](../../scripts/portable-runtime.json) to select
only the enumerated ADB libraries, SDL, FFmpeg and image assets. It verifies
their individual hashes and the original archive pins in
[scripts/native-toolchain.json](../../scripts/native-toolchain.json).
The upstream client and server remain outside the 2.0 package.

## Stage and verify

Run these commands from the clean source checkout using Windows PowerShell 5.1.

~~~powershell
$source = (git rev-parse HEAD).Trim()
$stage = Join-Path $PWD "dist/portable/scrcpy-seamless-win-x64-g$($source.Substring(0, 8))"
$archive = "$stage.zip"

.\scripts\stage-portable.ps1 -SourceSha $source -PackageDirectory $stage
.\scripts\package-portable.ps1 -SourceSha $source -PackageDirectory $stage -ArchivePath $archive
.\scripts\package-portable.ps1 -SourceSha $source -ArchivePath $archive -VerifyOnly
~~~

Staging uses the exact SDK in global.json, locked restore and a Release
self-contained win-x64 publish. It uses the Desktop project version unless
an explicit -Version is supplied. It checks the actual Desktop informational
version against the selected version and Git SHA. The optional
-DotnetPath selects a trusted local installation of the exact SDK.
-Offline requires a prepopulated SDK and NuGet cache, and restores exclusively
from local inputs. -NativeExecutablePath, -ServerArtifactDirectory and
-RuntimeArchiveDirectory can select existing build outputs and downloaded
ZIPs outside the checkout. The selected artifacts still pass the same gates.

The package stage contains a README, license notices, a CycloneDX 1.6 SBOM,
build provenance, the runtime manifest and the complete SHA-256 inventory.
The SBOM covers the RID-specific Desktop graph, its published runtime
assets and the bundled native files. Managed package notices come from the
restored NuGet archives. Static dependencies have separate upstream notices
and corresponding-source obligations. See [Third-party components](../../THIRD_PARTY.md)
before public distribution.

The ZIP writer fixes the entry order and timestamps, writes a SHA-256 sidecar
and verifies a new extraction in a directory with spaces. Staging and
verification admit the exact runtime and published dependency paths. The
check rejects reparse points, unsafe Windows path names, case-insensitive
duplicates, extra files and hash drift. Independent extraction checks local
integrity. Compiler output reproducibility and public provenance attestation
are separate claims.

The [legacy 1.x and Desktop DEV procedures](../PACKAGING.md) remain available
for earlier packages. Run the offline fixture for the new archive path with:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\portable-package.Tests.ps1
~~~
