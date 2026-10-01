[CmdletBinding()]
param(
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$sourceSha = (& git -C $repositoryDirectory rev-parse HEAD).Trim()

if ($LASTEXITCODE -ne 0 -or $sourceSha -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'A committed source SHA is required for DEV staging.'
}

$uncommittedPaths = @(& git -C $repositoryDirectory status --porcelain=v1 --untracked-files=all)

if ($LASTEXITCODE -ne 0 -or $uncommittedPaths.Count -ne 0) {
    throw 'DEV staging requires a clean committed source tree.'
}

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repositoryDirectory ('dist\dev\scrcpy-seamless-desktop-p06c-g' + $sourceSha.Substring(0, 8))
}

$packagePath = [IO.Path]::GetFullPath($PackageDirectory)
$devRoot = [IO.Path]::GetFullPath((Join-Path $repositoryDirectory 'dist\dev'))
$devPrefix = $devRoot.TrimEnd('\') + '\'

if (-not $packagePath.StartsWith($devPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'DEV staging must remain under the repository dist/dev directory.'
}

if (Test-Path -LiteralPath $packagePath) {
    throw 'The requested DEV package already exists; never overwrite an accepted artifact.'
}

. (Join-Path $PSScriptRoot 'provenance.ps1')
$nativeExecutable = Join-Path $repositoryDirectory 'dist\scrcpy.exe'
$nativeEvidence = Get-Content -LiteralPath ($nativeExecutable + '.manifest.json') -Raw | ConvertFrom-Json
$nativeFingerprint = Get-NativeSourceFingerprint -RepositoryDirectory $repositoryDirectory
$nativeHash = (Get-FileHash -LiteralPath $nativeExecutable -Algorithm SHA256).Hash.ToLowerInvariant()

if ($nativeEvidence.SchemaVersion -ne 1 -or $nativeEvidence.Origin -ne 'local-build' -or
    $nativeEvidence.SourceFingerprint -ne $nativeFingerprint -or
    $nativeEvidence.ExecutableSha256 -ne $nativeHash) {
    throw 'Source-built native provenance is stale or mismatched.'
}

$serverDirectory = Join-Path $repositoryDirectory 'work\phase2\server\artifacts'
$serverEvidence = Get-Content -LiteralPath (Join-Path $serverDirectory 'server-build.json') -Raw | ConvertFrom-Json
$serverFile = Join-Path $serverDirectory 'scrcpy-server'
$serverHash = (Get-FileHash -LiteralPath $serverFile -Algorithm SHA256).Hash.ToLowerInvariant()
$serverInputs = @(
    'src/scrcpy/server', 'src/scrcpy/build.gradle', 'src/scrcpy/settings.gradle',
    'src/scrcpy/gradle.properties', 'src/scrcpy/gradlew.bat', 'src/scrcpy/gradle/wrapper',
    'src/scrcpy/buildscript-gradle.lockfile', 'src/scrcpy/gradle/verification-metadata.xml',
    'src/scrcpy/config', 'scripts/server-toolchain.json', 'scripts/bootstrap-server.ps1',
    'scripts/build-server.ps1'
)
& git -C $repositoryDirectory diff --quiet $serverEvidence.SourceHead HEAD -- $serverInputs
$serverHistoryUnchanged = $LASTEXITCODE -eq 0
$serverWorkingChanges = @(& git -C $repositoryDirectory status --porcelain=v1 --untracked-files=all -- $serverInputs)

if (-not $serverHistoryUnchanged -or $LASTEXITCODE -ne 0 -or $serverWorkingChanges.Count -ne 0 -or
    $serverEvidence.SchemaVersion -ne 1 -or
    $serverEvidence.ArtifactSha256 -ne $serverHash -or
    $serverEvidence.SourceFingerprintSha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'Source-built Android server evidence is stale or mismatched.'
}

$reviewedDirectory = Join-Path $repositoryDirectory 'dist\dev\scrcpy-seamless-win64-dev-source-server-p02-gd13b638\app'
$reviewedManifest = Get-Content -LiteralPath (Join-Path $reviewedDirectory 'release-manifest.json') -Raw | ConvertFrom-Json
$importedNames = @(
    'adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'SDL3.dll',
    'avcodec-62.dll', 'avformat-62.dll', 'avutil-60.dll', 'swresample-6.dll',
    'scrcpy.png', 'disconnected.png'
)

foreach ($name in $importedNames) {
    $expectedHash = $reviewedManifest.RuntimeFiles.$name
    $actualHash = (Get-FileHash -LiteralPath (Join-Path $reviewedDirectory $name) -Algorithm SHA256).Hash

    if (-not $expectedHash -or $expectedHash -ne $actualHash) {
        throw "Reviewed runtime component failed its existing release-manifest check: $name"
    }
}

$desktopProject = Join-Path $repositoryDirectory 'src\desktop\ScrcpySeamless.Desktop\ScrcpySeamless.Desktop.csproj'
& dotnet publish $desktopProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $packagePath

if ($LASTEXITCODE -ne 0) {
    throw 'Self-contained Desktop publish failed; inspect the partial DEV directory.'
}

# Symbol files are not needed for the manual DEV acceptance bundle and can contain build-machine paths.
foreach ($symbol in @(Get-ChildItem -LiteralPath $packagePath -File -Filter '*.pdb')) {
    Remove-Item -LiteralPath $symbol.FullName
}

$runtimeDirectory = Join-Path $packagePath 'runtime'
New-Item -ItemType Directory -Path $runtimeDirectory | Out-Null
$files = [ordered]@{}
$origins = [ordered]@{}
$sources = [ordered]@{ 'scrcpy.exe' = $nativeExecutable; 'scrcpy-server' = $serverFile }

foreach ($name in $importedNames) {
    $sources[$name] = Join-Path $reviewedDirectory $name
}

foreach ($name in $sources.Keys) {
    $destination = Join-Path $runtimeDirectory $name
    Copy-Item -LiteralPath $sources[$name] -Destination $destination
    $files[$name] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $origins[$name] = if ($name -in @('scrcpy.exe', 'scrcpy-server')) { 'source-built' } else { 'reviewed-import' }
}

$machineContract = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'spec\desktop-native\runtime-contract.json') -Raw |
    ConvertFrom-Json
$runtimeManifest = [ordered]@{
    SchemaVersion = 1
    SourceSha = $sourceSha.ToLowerInvariant()
    NativeSourceFingerprint = $nativeFingerprint.ToLowerInvariant()
    ServerSourceFingerprint = $serverEvidence.SourceFingerprintSha256.ToLowerInvariant()
    Files = $files
    Origins = $origins
    MachineContract = $machineContract
}
$runtimeManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runtimeDirectory 'runtime-dev-manifest.json') -Encoding UTF8

$noticeNames = @(
    'Android-Platform-Tools-NOTICE.txt', 'dav1d-COPYING.txt',
    'FFmpeg-LGPL-2.1.txt', 'FFmpeg-LICENSE.md', 'GCC-GPL-3.0.txt',
    'GCC-RUNTIME-EXCEPTION.txt', 'MinGW-w64-COPYING.txt',
    'MinGW-w64-runtime-COPYING.txt', 'SDL-LICENSE.txt', 'zlib-LICENSE.txt',
    'yyjson-LICENSE.txt'
)
$noticeDirectory = Join-Path $packagePath 'licenses'
New-Item -ItemType Directory -Path $noticeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryDirectory 'LICENSE') -Destination (Join-Path $packagePath 'LICENSE')

foreach ($name in $noticeNames) {
    $source = if ($name -eq 'yyjson-LICENSE.txt') {
        Join-Path $repositoryDirectory 'src\scrcpy\app\vendor\yyjson\LICENSE'
    }
    else {
        Join-Path $repositoryDirectory ('licenses\' + $name)
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $noticeDirectory $name)
}

@"
# Isolated Desktop DEV bundle

Source commit: $sourceSha

This is a development artifact, not an official release. Runtime hashes and
source/build origin labels are in runtime/runtime-dev-manifest.json. The project
license is in LICENSE; retained ADB, SDL, FFmpeg and compiler-runtime notices
are in licenses/, including the yyjson MIT notice. Do not copy personal
profiles, ADB keys or logs into this directory. A public 2.0 distribution
requires a separate review of Desktop dependency notices and the complete
release/license package.
"@ | Set-Content -LiteralPath (Join-Path $packagePath 'DEV-BUNDLE-NOTICES.md') -Encoding UTF8

Write-Host "Staged isolated Desktop DEV package: $packagePath"
