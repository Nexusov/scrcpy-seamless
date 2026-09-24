[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$releaseManifest = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'release-manifest.json') -Raw | ConvertFrom-Json
$nativeToolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'native-toolchain.json') -Raw | ConvertFrom-Json
$serverToolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server-toolchain.json') -Raw | ConvertFrom-Json
$sdkPolicy = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'global.json') -Raw | ConvertFrom-Json
$mesonBuild = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'src/scrcpy/meson.build') -Raw
$serverBuild = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'src/scrcpy/server/build.gradle') -Raw
$gradleBuild = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'src/scrcpy/build.gradle') -Raw
$gradleWrapper = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'src/scrcpy/gradle/wrapper/gradle-wrapper.properties') -Raw
$packageVersions = [xml](Get-Content -LiteralPath (Join-Path $repositoryDirectory 'Directory.Packages.props') -Raw)
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $repositoryDirectory 'Directory.Build.props') -Raw)

if ($releaseManifest.Upstream -notmatch '^scrcpy (?<Version>\d+\.\d+)$') {
    throw 'The reviewed runtime manifest has no parseable scrcpy baseline.'
}

$baselineVersion = $Matches.Version
$mesonVersion = [regex]::Match($mesonBuild, "version:\s*'(?<Version>\d+\.\d+)'" ).Groups['Version'].Value
$serverVersion = [regex]::Match($serverBuild, 'versionName\s+"(?<Version>\d+\.\d+)"').Groups['Version'].Value
$versionSources = @($mesonVersion, $serverVersion, $nativeToolchain.packages.scrcpyRuntime.version)

foreach ($sourceVersion in $versionSources) {

    if ($sourceVersion -ne $baselineVersion) {
        throw "scrcpy source/runtime baseline differs from release-manifest.json: $sourceVersion versus $baselineVersion"
    }
}

$androidPlugin = [regex]::Escape("com.android.tools.build:gradle:$($serverToolchain.AndroidGradlePlugin)")
$gradleDistribution = [regex]::Escape("gradle-$($serverToolchain.Gradle)-bin.zip")

if ($gradleBuild -notmatch $androidPlugin -or $gradleWrapper -notmatch $gradleDistribution) {
    throw 'Android plugin or Gradle wrapper differs from scripts/server-toolchain.json.'
}

if ($sdkPolicy.sdk.rollForward -ne 'disable' -or $sdkPolicy.sdk.allowPrerelease) {
    throw 'global.json no longer requires an exact stable .NET SDK.'
}

$sdkMajor = ([version]$sdkPolicy.sdk.version).Major
$targetFramework = $buildProperties.Project.PropertyGroup.TargetFramework

if ($targetFramework -ne "net$sdkMajor.0") {
    throw "The .NET target framework $targetFramework differs from SDK major version $sdkMajor."
}

$avaloniaVersions = @($packageVersions.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -like 'Avalonia*' } |
    ForEach-Object { $_.Version })
$uniqueAvaloniaVersions = @($avaloniaVersions | Select-Object -Unique)

if (-not $avaloniaVersions.Count -or $uniqueAvaloniaVersions.Count -ne 1 -or
    $uniqueAvaloniaVersions[0] -notmatch '^\[12\.\d+\.\d+\]$') {
    throw 'Avalonia packages must share one exact stable 12.x version.'
}

Write-Host "Build metadata agrees: scrcpy $baselineVersion; .NET SDK $($sdkPolicy.sdk.version); Avalonia $($uniqueAvaloniaVersions[0])."
