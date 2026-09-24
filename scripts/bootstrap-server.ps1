[CmdletBinding()]
param(
    [string]$ToolDirectory,
    [switch]$Offline,
    [switch]$ReviewSdkLicenses
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ToolDirectory) {
    $ToolDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'work\phase2\server'
}

$toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server-toolchain.json') -Raw | ConvertFrom-Json
$toolDirectoryPath = [IO.Path]::GetFullPath($ToolDirectory)
$downloadDirectory = Join-Path $toolDirectoryPath 'downloads'
$javaDirectory = Join-Path (Join-Path $toolDirectoryPath 'jdk') $toolchain.Jdk.ExtractedDirectory
$sdkDirectory = Join-Path $toolDirectoryPath 'sdk'
$commandLineToolsDirectory = Join-Path $sdkDirectory 'cmdline-tools\latest'

# Download an immutable archive and reject bytes outside its pinned SHA-256.
function Get-VerifiedServerArchive {
    param($Archive, [string]$DownloadDirectory, [bool]$UseOfflineCache)

    $archivePath = Join-Path $DownloadDirectory $Archive.ArchiveName

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {

        if ($UseOfflineCache) {
            throw "Required archive is absent from the offline cache: $archivePath"
        }

        $temporaryPath = $archivePath + '.' + [guid]::NewGuid().ToString('N') + '.download'

        try {
            & curl.exe '--fail' '--location' '--retry' '3' '--silent' '--show-error' '--output' $temporaryPath $Archive.Url

            if ($LASTEXITCODE) {
                throw "Archive download failed with exit code $LASTEXITCODE`: $($Archive.ArchiveName)"
            }

            $downloadedHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash

            if ($downloadedHash -ine $Archive.Sha256) {
                throw "Downloaded archive has the wrong SHA-256: $($Archive.ArchiveName)"
            }

            Move-Item -LiteralPath $temporaryPath -Destination $archivePath
        }
        finally {

            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }

    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash

    if ($actualHash -ine $Archive.Sha256) {
        throw "Cached archive has the wrong SHA-256: $archivePath"
    }

    return $archivePath
}

# Extract one checked archive without replacing an existing tool installation.
function Expand-VerifiedServerArchive {
    param([string]$ArchivePath, [string]$DestinationPath, [string]$ArchiveRoot)

    if (Test-Path -LiteralPath $DestinationPath) {
        throw "Incomplete tool installation exists; inspect it before retrying: $DestinationPath"
    }

    $destinationParent = Split-Path -Parent $DestinationPath
    $stagePath = Join-Path $destinationParent ('extract-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null

    try {
        New-Item -ItemType Directory -Path $stagePath | Out-Null
        & tar.exe '-xf' $ArchivePath '-C' $stagePath

        if ($LASTEXITCODE) {
            throw "Archive extraction failed with exit code $LASTEXITCODE."
        }

        $extractedPath = Join-Path $stagePath $ArchiveRoot

        if (-not (Test-Path -LiteralPath $extractedPath -PathType Container)) {
            throw "Pinned archive does not contain expected root: $ArchiveRoot"
        }

        Move-Item -LiteralPath $extractedPath -Destination $DestinationPath
        Remove-Item -LiteralPath $stagePath
    }
    catch {
        throw "Extraction failed; inspect $stagePath before retrying. $($_.Exception.Message)"
    }
}

# Reject SDK packages whose installed revision differs from the reviewed pin.
function Assert-AndroidPackageRevision {
    param([string]$PropertiesPath, [string]$ExpectedRevision)

    if (-not (Test-Path -LiteralPath $PropertiesPath -PathType Leaf)) {
        throw "Android SDK package metadata is missing: $PropertiesPath"
    }

    $revisionLine = @(Get-Content -LiteralPath $PropertiesPath | Where-Object { $_ -match '^Pkg\.Revision\s*=' })

    if ($revisionLine.Count -ne 1) {
        throw "Android SDK package revision is missing or ambiguous: $PropertiesPath"
    }

    $actualRevision = ($revisionLine[0] -split '=', 2)[1].Trim()

    if ($actualRevision -ne $ExpectedRevision) {
        throw "Android SDK package revision $actualRevision differs from pinned $ExpectedRevision`: $PropertiesPath"
    }
}

New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
$javaArchivePath = Get-VerifiedServerArchive -Archive $toolchain.Jdk -DownloadDirectory $downloadDirectory -UseOfflineCache $Offline.IsPresent
$sdkToolsArchivePath = Get-VerifiedServerArchive -Archive $toolchain.AndroidCommandLineTools -DownloadDirectory $downloadDirectory -UseOfflineCache $Offline.IsPresent

$javaExecutable = Join-Path $javaDirectory 'bin\java.exe'
$javaCompiler = Join-Path $javaDirectory 'bin\javac.exe'

if (-not (Test-Path -LiteralPath $javaExecutable -PathType Leaf)) {
    Expand-VerifiedServerArchive -ArchivePath $javaArchivePath -DestinationPath $javaDirectory -ArchiveRoot $toolchain.Jdk.ExtractedDirectory
}

if (-not (Test-Path -LiteralPath $javaCompiler -PathType Leaf)) {
    throw "Pinned JDK is incomplete: $javaDirectory"
}

$sdkManager = Join-Path $commandLineToolsDirectory 'bin\sdkmanager.bat'

if (-not (Test-Path -LiteralPath $sdkManager -PathType Leaf)) {
    Expand-VerifiedServerArchive -ArchivePath $sdkToolsArchivePath -DestinationPath $commandLineToolsDirectory -ArchiveRoot 'cmdline-tools'
}

if (-not (Test-Path -LiteralPath $sdkManager -PathType Leaf)) {
    throw "Pinned Android command-line tools are incomplete: $commandLineToolsDirectory"
}

$originalJavaHome = $env:JAVA_HOME
$originalAndroidHome = $env:ANDROID_HOME
$originalAndroidSdkRoot = $env:ANDROID_SDK_ROOT
$originalPath = $env:PATH

try {
    $env:JAVA_HOME = $javaDirectory
    $env:ANDROID_HOME = $sdkDirectory
    $env:ANDROID_SDK_ROOT = $sdkDirectory
    $env:PATH = (Join-Path $javaDirectory 'bin') + [IO.Path]::PathSeparator + $originalPath

    if ($ReviewSdkLicenses) {
        Write-Host 'Review the Android SDK license prompts yourself; this script does not accept them.'
        & $sdkManager "--sdk_root=$sdkDirectory" '--licenses'

        if ($LASTEXITCODE) {
            throw "Android SDK license review ended with exit code $LASTEXITCODE."
        }
    }

    $androidJar = Join-Path $sdkDirectory 'platforms\android-36\android.jar'
    $platformProperties = Join-Path $sdkDirectory 'platforms\android-36\source.properties'
    $buildToolsProperties = Join-Path $sdkDirectory 'build-tools\36.0.0\source.properties'
    $platformToolsProperties = Join-Path $sdkDirectory 'platform-tools\source.properties'
    $sdkPackagesPresent = (Test-Path -LiteralPath $androidJar -PathType Leaf) -and
        (Test-Path -LiteralPath $buildToolsProperties -PathType Leaf) -and
        (Test-Path -LiteralPath $platformToolsProperties -PathType Leaf)

    if (-not $sdkPackagesPresent) {

        if ($Offline) {
            throw 'A pinned Android SDK platform, Build Tools, or Platform-Tools package is absent from the offline cache.'
        }

        $sdkLicensePath = Join-Path $sdkDirectory 'licenses\android-sdk-license'

        if (-not (Test-Path -LiteralPath $sdkLicensePath -PathType Leaf)) {
            throw 'Android SDK terms require your review. Run scripts/bootstrap-server.ps1 -ReviewSdkLicenses interactively, then rerun this command.'
        }

        & $sdkManager "--sdk_root=$sdkDirectory" $toolchain.AndroidPlatform.Package $toolchain.AndroidBuildTools.Package $toolchain.AndroidPlatformTools.Package

        if ($LASTEXITCODE) {
            throw "Android SDK package installation failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $androidJar -PathType Leaf) -or
        -not (Test-Path -LiteralPath $buildToolsProperties -PathType Leaf) -or
        -not (Test-Path -LiteralPath $platformToolsProperties -PathType Leaf)) {
        throw 'Pinned Android SDK platform, Build Tools, or Platform-Tools were not installed completely.'
    }

    Assert-AndroidPackageRevision -PropertiesPath $platformProperties -ExpectedRevision $toolchain.AndroidPlatform.Revision
    Assert-AndroidPackageRevision -PropertiesPath $buildToolsProperties -ExpectedRevision $toolchain.AndroidBuildTools.Revision
    Assert-AndroidPackageRevision -PropertiesPath $platformToolsProperties -ExpectedRevision $toolchain.AndroidPlatformTools.Revision

    Write-Host "Pinned server build tools are ready under $toolDirectoryPath"
}
finally {
    $env:JAVA_HOME = $originalJavaHome
    $env:ANDROID_HOME = $originalAndroidHome
    $env:ANDROID_SDK_ROOT = $originalAndroidSdkRoot
    $env:PATH = $originalPath
}
