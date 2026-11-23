[CmdletBinding()]
param(
    [string]$ToolDirectory,
    [string]$ArtifactDirectory,
    [switch]$UseProvisionedToolchain,
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
    $androidApi = ($Toolchain.AndroidPlatform.Package -split 'android-', 2)[1]
    $androidApiPattern = [regex]::Escape($androidApi)

    if ($wrapperProperties -notmatch [regex]::Escape("gradle-$($Toolchain.Gradle)-bin.zip")) {
        throw 'Gradle wrapper version differs from the pinned server toolchain.'
    }

    if ($rootBuild -notmatch [regex]::Escape("com.android.tools.build:gradle:$($Toolchain.AndroidGradlePlugin)")) {
        throw 'Android Gradle plugin version differs from the pinned server toolchain.'
    }

    if ($serverBuild -notmatch "compileSdk\s*=\s*$androidApiPattern\b" -or
        $serverBuild -notmatch "targetSdkVersion\s+$androidApiPattern\b") {
        throw 'Server Android API level differs from the pinned server toolchain.'
    }

    if ($serverBuild -notmatch [regex]::Escape("junit:junit:$($Toolchain.Junit)")) {
        throw 'JUnit version differs from the pinned server toolchain.'
    }

    if ($checkstyleBuild -notmatch [regex]::Escape("toolVersion = '$($Toolchain.Checkstyle)'")) {
        throw 'Checkstyle version differs from the pinned server toolchain.'
    }

    # Gradle does not enable verification when its metadata file is absent.
    foreach ($relativePath in @('buildscript-gradle.lockfile', 'server\gradle.lockfile', 'gradle\verification-metadata.xml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory $relativePath) -PathType Leaf)) {
            throw "Required Gradle dependency state is missing: $relativePath"
        }
    }
}

. (Join-Path $PSScriptRoot 'server-provenance.ps1')

. (Join-Path $PSScriptRoot 'server-toolchain.ps1')
Assert-ServerBuildPins -Toolchain $toolchain -SourceDirectory $sourceDirectory

if (-not $UseProvisionedToolchain) {
    & (Join-Path $PSScriptRoot 'bootstrap-server.ps1') -ToolDirectory $toolDirectoryPath -Offline:$Offline
}

$environment = Get-ServerToolchainEnvironment -Toolchain $toolchain -ToolDirectory $toolDirectoryPath -UseProvisionedToolchain:$UseProvisionedToolchain
$sourceFingerprint = Get-ServerBuildFingerprint -RepositoryDirectory $repositoryDirectory
$javaDirectory = $environment.JavaDirectory
$sdkDirectory = $environment.SdkDirectory
$gradleDirectory = Join-Path $toolDirectoryPath 'gradle-home'
$serverApk = Join-Path $sourceDirectory 'server\build\outputs\apk\release\server-release-unsigned.apk'
$artifactPath = Join-Path $toolDirectoryPath 'artifacts'

if ($ArtifactDirectory) {
    $artifactPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactDirectory)

    if (Test-Path -LiteralPath $artifactPath) {
        throw 'Choose a new explicit Android server artifact directory.'
    }
}

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
        $gradleArguments = @(':server:assembleRelease', ':server:check', '--no-daemon', '--dependency-verification=strict')

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

    $serverHash = (Get-FileHash -LiteralPath $serverApk -Algorithm SHA256).Hash.ToLowerInvariant()
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
        Toolchain = $environment.Evidence
        BuildCommand = ('gradlew.bat ' + ($gradleArguments -join ' '))
        Artifact = 'scrcpy-server'
        ArtifactSha256 = $serverHash
        BundledByCurrentPublisher = $false
    }
    $outputPath = $artifactPath

    if ($ArtifactDirectory) {
        $outputPath = Join-Path ([IO.Path]::GetDirectoryName($artifactPath)) ('.server-output-' + [guid]::NewGuid().ToString('N'))
    }

    [IO.Directory]::CreateDirectory($outputPath) | Out-Null

    try {
        [IO.File]::Copy($serverApk, (Join-Path $outputPath 'scrcpy-server'), -not $ArtifactDirectory)
        $buildEvidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputPath 'server-build.json') -Encoding UTF8

        if ($ArtifactDirectory) {
            [IO.Directory]::Move($outputPath, $artifactPath)
        }
    }
    finally {
        if ($ArtifactDirectory -and [IO.Directory]::Exists($outputPath)) {
            # The unique staging directory has a fixed two-file layout.
            [IO.File]::Delete((Join-Path $outputPath 'scrcpy-server'))
            [IO.File]::Delete((Join-Path $outputPath 'server-build.json'))
            [IO.Directory]::Delete($outputPath)
        }
    }

    Write-Host "Built and checked Android server: $(Join-Path $artifactPath 'scrcpy-server')"
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
