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

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repositoryDirectory ('dist\dev\scrcpy-seamless-desktop-p05c-g' + $sourceSha.Substring(0, 8))
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

if ($LASTEXITCODE -ne 0 -or $serverEvidence.SchemaVersion -ne 1 -or
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

$runtimeManifest = [ordered]@{
    SchemaVersion = 1
    SourceSha = $sourceSha.ToLowerInvariant()
    NativeSourceFingerprint = $nativeFingerprint.ToLowerInvariant()
    ServerSourceFingerprint = $serverEvidence.SourceFingerprintSha256.ToLowerInvariant()
    Files = $files
    Origins = $origins
}
$runtimeManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runtimeDirectory 'runtime-dev-manifest.json') -Encoding UTF8
Write-Host "Staged isolated Desktop DEV package: $packagePath"
