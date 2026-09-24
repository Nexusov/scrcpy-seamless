[CmdletBinding()]
param(
    [string]$ToolDirectory,
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot

if (-not $ToolDirectory) {
    $ToolDirectory = Join-Path $repositoryDirectory 'work\phase2\server'
}

$sourceDirectory = Join-Path $repositoryDirectory 'src\scrcpy'
$toolDirectoryPath = [IO.Path]::GetFullPath($ToolDirectory)
$toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server-toolchain.json') -Raw | ConvertFrom-Json

# Keep the checked-in Gradle and Android pins aligned with the bootstrap manifest.
function Assert-ServerBuildPins {
    param($Toolchain, [string]$SourceDirectory)

    $wrapperProperties = Get-Content -LiteralPath (Join-Path $SourceDirectory 'gradle\wrapper\gradle-wrapper.properties') -Raw
    $rootBuild = Get-Content -LiteralPath (Join-Path $SourceDirectory 'build.gradle') -Raw
    $serverBuild = Get-Content -LiteralPath (Join-Path $SourceDirectory 'server\build.gradle') -Raw
    $checkstyleBuild = Get-Content -LiteralPath (Join-Path $SourceDirectory 'config\android-checkstyle.gradle') -Raw

    if ($wrapperProperties -notmatch [regex]::Escape("gradle-$($Toolchain.Gradle)-bin.zip")) {
        throw 'Gradle wrapper version differs from the pinned server toolchain.'
    }

    if ($rootBuild -notmatch [regex]::Escape("com.android.tools.build:gradle:$($Toolchain.AndroidGradlePlugin)")) {
        throw 'Android Gradle plugin version differs from the pinned server toolchain.'
    }

    if ($serverBuild -notmatch 'compileSdk\s*=\s*36\b' -or $serverBuild -notmatch 'targetSdkVersion\s+36\b') {
        throw 'Server Android API level differs from the pinned server toolchain.'
    }

    if ($serverBuild -notmatch [regex]::Escape("junit:junit:$($Toolchain.Junit)")) {
        throw 'JUnit version differs from the pinned server toolchain.'
    }

    if ($checkstyleBuild -notmatch [regex]::Escape("toolVersion = '$($Toolchain.Checkstyle)'")) {
        throw 'Checkstyle version differs from the pinned server toolchain.'
    }
}

# Fingerprint tracked and new non-ignored server/build inputs, excluding outputs.
function Get-ServerBuildFingerprint {
    param([string]$RepositoryDirectory)

    $inputPaths = @(
        'src/scrcpy/server', 'src/scrcpy/build.gradle', 'src/scrcpy/settings.gradle',
        'src/scrcpy/gradle.properties', 'src/scrcpy/gradlew.bat', 'src/scrcpy/gradle/wrapper',
        'src/scrcpy/config', 'scripts/server-toolchain.json',
        'scripts/bootstrap-server.ps1', 'scripts/build-server.ps1'
    )
    $sourcePaths = @(& git -C $RepositoryDirectory ls-files --cached --others --exclude-standard -- @inputPaths)

    if ($LASTEXITCODE -or -not $sourcePaths.Count) {
        throw 'Could not enumerate Android server source/build inputs.'
    }

    $sortedPaths = [string[]]$sourcePaths
    [Array]::Sort($sortedPaths, [StringComparer]::Ordinal)
    $entries = foreach ($sourcePath in $sortedPaths) {
        $sourceFile = Join-Path $RepositoryDirectory $sourcePath

        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "Android server build input disappeared: $sourcePath"
        }

        $hash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
        "$sourcePath $hash"
    }

    $hasher = [Security.Cryptography.SHA256]::Create()

    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
        return [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

Assert-ServerBuildPins -Toolchain $toolchain -SourceDirectory $sourceDirectory
& (Join-Path $PSScriptRoot 'bootstrap-server.ps1') -ToolDirectory $toolDirectoryPath -Offline:$Offline
$sourceFingerprint = Get-ServerBuildFingerprint -RepositoryDirectory $repositoryDirectory

$javaDirectory = Join-Path (Join-Path $toolDirectoryPath 'jdk') $toolchain.Jdk.ExtractedDirectory
$sdkDirectory = Join-Path $toolDirectoryPath 'sdk'
$gradleDirectory = Join-Path $toolDirectoryPath 'gradle-home'
$serverApk = Join-Path $sourceDirectory 'server\build\outputs\apk\release\server-release-unsigned.apk'
$artifactDirectory = Join-Path $toolDirectoryPath 'artifacts'
$originalJavaHome = $env:JAVA_HOME
$originalAndroidHome = $env:ANDROID_HOME
$originalAndroidSdkRoot = $env:ANDROID_SDK_ROOT
$originalGradleUserHome = $env:GRADLE_USER_HOME
$originalPath = $env:PATH

try {
    $env:JAVA_HOME = $javaDirectory
    $env:ANDROID_HOME = $sdkDirectory
    $env:ANDROID_SDK_ROOT = $sdkDirectory
    $env:GRADLE_USER_HOME = $gradleDirectory
    $env:PATH = (Join-Path $javaDirectory 'bin') + [IO.Path]::PathSeparator + $originalPath
    New-Item -ItemType Directory -Path $gradleDirectory -Force | Out-Null

    Push-Location -LiteralPath $sourceDirectory

    try {
        $gradleArguments = @(':server:assembleRelease', ':server:check', '--no-daemon')

        if ($Offline) {
            $gradleArguments += '--offline'
        }

        & (Join-Path $sourceDirectory 'gradlew.bat') @gradleArguments

        if ($LASTEXITCODE) {
            throw "Android server Gradle build/check failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $serverApk -PathType Leaf)) {
        throw "Android server output is missing: $serverApk"
    }

    $serverFile = Get-Item -LiteralPath $serverApk

    if (-not $serverFile.Length) {
        throw "Android server output is empty: $serverApk"
    }

    if ((Get-ServerBuildFingerprint -RepositoryDirectory $repositoryDirectory) -ne $sourceFingerprint) {
        throw 'Android server source/build inputs changed during compilation; rebuild before using the artifact.'
    }

    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $copiedServer = Join-Path $artifactDirectory 'scrcpy-server'
    Copy-Item -LiteralPath $serverApk -Destination $copiedServer -Force
    $serverHash = (Get-FileHash -LiteralPath $copiedServer -Algorithm SHA256).Hash.ToLowerInvariant()
    $headCommit = (& git -C $repositoryDirectory rev-parse HEAD).Trim()

    if ($LASTEXITCODE) {
        throw 'Could not identify the Git source commit for Android server evidence.'
    }

    $buildEvidence = [ordered]@{
        SchemaVersion = 1
        SourceHead = $headCommit
        SourceFingerprintSha256 = $sourceFingerprint
        ServerSourceDirectory = 'src/scrcpy/server'
        ToolchainManifest = 'scripts/server-toolchain.json'
        BuildCommand = ('gradlew.bat ' + ($gradleArguments -join ' '))
        Artifact = 'scrcpy-server'
        ArtifactSha256 = $serverHash
        BundledByCurrentPublisher = $false
    }
    $buildEvidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactDirectory 'server-build.json') -Encoding UTF8
    Write-Host "Built and checked Android server: $copiedServer"
    Write-Host "SHA-256: $serverHash"
    Write-Host 'This source-built server is not substituted for the reviewed imported runtime in the current 1.x package.'
}
finally {
    $env:JAVA_HOME = $originalJavaHome
    $env:ANDROID_HOME = $originalAndroidHome
    $env:ANDROID_SDK_ROOT = $originalAndroidSdkRoot
    $env:GRADLE_USER_HOME = $originalGradleUserHome
    $env:PATH = $originalPath
}
