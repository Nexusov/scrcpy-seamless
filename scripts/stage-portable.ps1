[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('(?-i:^[a-f0-9]{40}$)')][string]$SourceSha,
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [string]$DotnetPath,
    [string]$NativeExecutablePath,
    [string]$ServerArtifactDirectory,
    [string]$RuntimeArchiveDirectory,
    [ValidatePattern('^2[.]0[.]0(-[a-zA-Z0-9]+([.-][a-zA-Z0-9]+)*)?$')][string]$Version,
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-common.ps1')
. (Join-Path $PSScriptRoot 'portable-sbom.ps1')
. (Join-Path $PSScriptRoot 'provenance.ps1')
. (Join-Path $PSScriptRoot 'server-provenance.ps1')

Assert-PortableSource $SourceSha

if (-not $NativeExecutablePath) {
    $NativeExecutablePath = Join-Path $portableRepository 'dist/scrcpy.exe'
}

if (-not $ServerArtifactDirectory) {
    $ServerArtifactDirectory = Join-Path $portableRepository 'work/phase2/server/artifacts'
}

if (-not $RuntimeArchiveDirectory) {
    $RuntimeArchiveDirectory = Join-Path $portableRepository 'work/native/downloads'
}

$nativePath = [IO.Path]::GetFullPath($NativeExecutablePath)
$serverPath = [IO.Path]::GetFullPath((Join-Path $ServerArtifactDirectory 'scrcpy-server'))
$serverManifestPath = Join-Path ([IO.Path]::GetDirectoryName($serverPath)) 'server-build.json'
$nativeManifestPath = "$nativePath.manifest.json"

foreach ($path in @($nativePath, $serverPath, $serverManifestPath, $nativeManifestPath)) {
    Assert-PlainPortablePath $path
}

$nativeEvidence = Get-Content -LiteralPath $nativeManifestPath -Raw | ConvertFrom-Json
$serverEvidence = Get-Content -LiteralPath $serverManifestPath -Raw | ConvertFrom-Json
$nativeFingerprint = Get-NativeSourceFingerprint -RepositoryDirectory $portableRepository
$serverFingerprint = Get-ServerBuildFingerprint -RepositoryDirectory $portableRepository

if ($nativeEvidence.SchemaVersion -ne 1 -or $nativeEvidence.Origin -cne 'local-build' -or
    $nativeEvidence.SourceHead -cne $SourceSha -or $nativeEvidence.SourceFingerprint -cne $nativeFingerprint -or
    (Get-PortableHash $nativePath) -cne $nativeEvidence.ExecutableSha256) {
    throw 'Source-built native evidence is stale or mismatched.'
}

if ($serverEvidence.SchemaVersion -ne 1 -or $serverEvidence.SourceHead -cne $SourceSha -or
    $serverEvidence.SourceFingerprintSha256 -cne $serverFingerprint -or $serverEvidence.Artifact -cne 'scrcpy-server' -or
    $serverEvidence.BuildCommand -cnotmatch '^gradlew[.]bat :server:assembleRelease :server:check --no-daemon --dependency-verification=strict( --offline)?$' -or
    (Get-PortableHash $serverPath) -cne $serverEvidence.ArtifactSha256) {
    throw 'Source-built Android server evidence is stale or mismatched.'
}

function Copy-ReviewedRuntime {
    param([string]$RuntimeDirectory)

    $pins = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'native-toolchain.json') -Raw |
        ConvertFrom-Json).packages
    $groups = @($portableCatalog.Imports.PSObject.Properties | Group-Object { $_.Value.Archive })

    foreach ($group in $groups) {
        $pin = $pins.PSObject.Properties[$group.Name].Value
        $cachePath = [IO.Path]::GetFullPath((Join-Path $RuntimeArchiveDirectory $pin.filename))
        Assert-PlainPortablePath $cachePath

        if (-not [IO.File]::Exists($cachePath)) {
            throw "Missing pinned runtime archive: $($pin.filename). Restore it with bootstrap-native.py."
        }

        $stream = [IO.File]::OpenRead($cachePath)

        try {
            if ((Get-PortableHash $cachePath) -cne $pin.sha256) {
                throw "Pinned runtime archive SHA-256 mismatch: $($pin.filename)"
            }

            $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)

            try {
                foreach ($import in $group.Group) {
                    $entry = $zip.GetEntry($import.Value.Entry)
                    $destination = Join-Path $RuntimeDirectory $import.Name

                    if ($null -eq $entry) {
                        throw "Missing reviewed runtime archive entry: $($import.Value.Entry)"
                    }

                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)

                    if ((Get-PortableHash $destination) -cne $import.Value.Sha256) {
                        throw "Reviewed runtime file SHA-256 mismatch: $($import.Name)"
                    }
                }
            }
            finally {
                $zip.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        [pscustomobject]@{ Filename = $pin.filename; Url = $pin.url; Sha256 = $pin.sha256 }
    }
}

$sdk = (Get-Content -LiteralPath (Join-Path $portableRepository 'global.json') -Raw | ConvertFrom-Json).sdk
$desktopDirectory = Join-Path $portableRepository "src/desktop/$portableProject"
$project = Join-Path $desktopDirectory "$portableProject.csproj"
$packagePath = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$parent = [IO.Path]::GetDirectoryName($packagePath)
Assert-PlainPortablePath $packagePath

if (Test-Path -LiteralPath $packagePath) {
    throw 'The requested portable package directory already exists.'
}

if (-not $DotnetPath) {
    $DotnetPath = Join-Path $portableRepository "work/dotnet-sdk-$($sdk.version)/dotnet.exe"

    if (-not $Offline) {
        $DotnetPath = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
    }
}

$DotnetPath = [IO.Path]::GetFullPath($DotnetPath)
Assert-PlainPortablePath $DotnetPath
$scratch = Join-Path $parent ('.portable-stage-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $scratch 'publish'
$stage = Join-Path $scratch 'package'
$runtime = Join-Path $stage 'runtime'
[IO.Directory]::CreateDirectory($runtime) | Out-Null

Push-Location -LiteralPath $portableRepository
$originalTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

try {
    $imports = @(Copy-ReviewedRuntime -RuntimeDirectory $runtime)
    $built = [ordered]@{ 'scrcpy.exe' = $nativePath; 'scrcpy-server' = $serverPath }
    $expectedBuildHashes = [ordered]@{ 'scrcpy.exe' = $nativeEvidence.ExecutableSha256; 'scrcpy-server' = $serverEvidence.ArtifactSha256 }

    foreach ($name in $built.Keys) {
        $destination = Join-Path $runtime $name
        Copy-Item -LiteralPath $built[$name] -Destination $destination

        if ((Get-PortableHash $destination) -cne $expectedBuildHashes[$name]) {
            throw "Source-built runtime changed during staging: $name"
        }
    }

    $installedSdk = & $DotnetPath --version

    if ($LASTEXITCODE -or $installedSdk -cne $sdk.version -or $sdk.rollForward -cne 'disable' -or $sdk.allowPrerelease) {
        throw "Portable staging requires the pinned SDK $($sdk.version)."
    }

    if (-not $Version) {
        $Version = & $DotnetPath msbuild $project -nologo -getProperty:Version -p:Configuration=Release

        if ($LASTEXITCODE) {
            throw 'Could not evaluate the Desktop version.'
        }
    }

    $restoreArguments = @('restore', $project, '--locked-mode', '-p:Configuration=Release')

    if ($Offline) {
        $emptyFeed = Join-Path $scratch 'empty-feed'
        [IO.Directory]::CreateDirectory($emptyFeed) | Out-Null
        $restoreArguments += @('--source', $emptyFeed, '-p:NuGetAudit=false')
    }

    & $DotnetPath @restoreArguments | Out-Host

    if ($LASTEXITCODE) {
        throw 'Locked Desktop restore failed.'
    }

    & $DotnetPath publish $project --configuration Release --runtime win-x64 --self-contained true --no-restore --output $publish "-p:Version=$Version" "-p:SourceRevisionId=$SourceSha" '-p:ContinuousIntegrationBuild=true' | Out-Host

    if ($LASTEXITCODE) {
        throw 'Self-contained Desktop publish failed.'
    }

    $assets = @(Get-PortablePublishAssets $publish)
    $payload = $portableApplication + @($assets | Select-Object -ExpandProperty Name -Unique)

    foreach ($name in $payload) {
        $from = Join-Path $publish $name
        Assert-PlainPortablePath $from
        Copy-Item -LiteralPath $from -Destination (Join-Path $stage $name)
    }

    $runtimeFiles = [ordered]@{}
    $runtimeOrigins = [ordered]@{}

    foreach ($name in $portableRuntimeNames) {
        $runtimeFiles[$name] = Get-PortableHash (Join-Path $runtime $name)
        $runtimeOrigins[$name] = if ($name -cin $portableSourceBinaries) { 'source-built' } else { 'reviewed-import' }
    }

    $contractPath = 'spec/desktop-native/runtime-contract.json'
    $contract = Get-Content -LiteralPath (Join-Path $portableRepository $contractPath) -Raw | ConvertFrom-Json
    Write-PortableJson -Path (Join-Path $stage $portableRuntimeManifest) -Value ([ordered]@{
        SchemaVersion = 1
        SourceSha = $SourceSha
        NativeSourceFingerprint = $nativeFingerprint
        ServerSourceFingerprint = $serverFingerprint
        Files = $runtimeFiles
        Origins = $runtimeOrigins
        MachineContract = $contract
    })

    $licenses = Join-Path $stage 'licenses'
    [IO.Directory]::CreateDirectory($licenses) | Out-Null
    Copy-Item -LiteralPath (Join-Path $portableRepository 'LICENSE') -Destination (Join-Path $stage 'LICENSE')

    foreach ($notice in $portableCatalog.Notices.PSObject.Properties) {
        Copy-Item -LiteralPath (Join-Path $portableRepository $notice.Value) -Destination (Join-Path $licenses $notice.Name)
    }

    $readme = @"
# scrcpy Seamless $Version

Extract the complete ZIP to a folder on Windows x64. Open ScrcpySeamless.Desktop.exe
from the extracted directory. Keep runtime and the Desktop libraries beside it.
The app includes its .NET runtime. Device discovery and mirroring require a user action.

Source commit $SourceSha. Runtime hashes and ownership are in
runtime/runtime-manifest.json. The ZIP has a separate SHA-256 checksum.
"@
    [IO.File]::WriteAllText((Join-Path $stage 'README.md'), $readme + [char]10)
    $notice = @"
# Notices

scrcpy Seamless derives from Genymobile scrcpy under Apache-2.0. See LICENSE.
The bundled Android Platform Tools, SDL, FFmpeg, dav1d, zlib, GCC and MinGW runtime
components retain the notices under licenses. The native client also embeds yyjson.
Desktop dependency license information is recorded in licenses/dotnet and sbom.json.

FFmpeg shared libraries are LGPL-2.1-or-later. Compatible modified DLLs may
replace them. The corresponding dependency versions, source locations and
upstream build configuration are documented in the source repository's
THIRD_PARTY.md. Distribute the corresponding-source companion with public releases.
"@
    [IO.File]::WriteAllText((Join-Path $stage 'NOTICE.md'), $notice + [char]10)

    $inputHashes = [ordered]@{}
    $inputPaths = @('global.json', 'Directory.Build.props', 'Directory.Packages.props', $contractPath,
        'scripts/portable-runtime.json', 'scripts/native-toolchain.json', 'scripts/server-toolchain.json',
        'src/desktop/ScrcpySeamless.Core/packages.lock.json', 'src/desktop/ScrcpySeamless.Infrastructure/packages.lock.json',
        'src/desktop/ScrcpySeamless.Desktop/packages.lock.json')

    foreach ($path in $inputPaths) {
        $inputHashes[$path] = Get-PortableHash (Join-Path $portableRepository $path)
    }

    Write-PortableJson -Path (Join-Path $stage 'build-provenance.json') -Value ([ordered]@{
        SchemaVersion = 1
        SourceSha = $SourceSha
        Version = $Version
        RuntimeIdentifier = 'win-x64'
        DesktopSdk = $sdk.version
        NativeFingerprint = $nativeFingerprint
        ServerFingerprint = $serverFingerprint
        BuildInputSha256 = $inputHashes
        ReviewedArchives = $imports
    })

    Write-PortableSbom -Directory $stage -Assets $assets -RestoreAssetsPath (Join-Path $desktopDirectory 'obj/project.assets.json') -Version $Version -RuntimeFiles $runtimeFiles
    $files = [ordered]@{}

    foreach ($name in Get-PortableFiles $stage) {
        $files[$name] = Get-PortableHash (Join-Path $stage $name)
    }

    Write-PortableJson -Path (Join-Path $stage $portableInventory) -Value ([ordered]@{
        SchemaVersion = 1
        SourceSha = $SourceSha
        Files = $files
    })
    $null = Assert-PortableContents -Directory $stage -SourceSha $SourceSha
    Assert-PortableSource $SourceSha
    [IO.Directory]::Move($stage, $packagePath)
    Write-Host "Staged Windows x64 portable package: $packagePath"
}
finally {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $originalTelemetry
    Pop-Location
    Assert-PlainPortablePath $scratch
    [IO.Directory]::Delete($scratch, $true)
}
