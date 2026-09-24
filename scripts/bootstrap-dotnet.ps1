[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Keep SDK downloads and extraction outside the tracked source tree.
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$cacheDirectory = Join-Path $repositoryRoot 'work'
$sdkSettings = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk
$sdkVersion = [string]$sdkSettings.version

if ($sdkVersion -notmatch '^10\.0\.[0-9]+$' -or $sdkSettings.rollForward -ne 'disable' -or $sdkSettings.allowPrerelease -ne $false) {
    throw 'global.json must pin one exact stable .NET 10 SDK without roll-forward.'
}

$archivePath = Join-Path $cacheDirectory "dotnet-sdk-$sdkVersion-win-x64.zip"
$downloadPath = "$archivePath.download"
$installationDirectory = Join-Path $cacheDirectory "dotnet-sdk-$sdkVersion"
$dotnetExecutable = Join-Path $installationDirectory 'dotnet.exe'
$downloadUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
$expectedSha512 = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
    $downloadSha512 = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA512).Hash

    if (-not [string]::Equals($downloadSha512, $expectedSha512, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded .NET SDK SHA-512 mismatch: $downloadSha512"
    }

    Move-Item -LiteralPath $downloadPath -Destination $archivePath
}

$archiveSha512 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash

if (-not [string]::Equals($archiveSha512, $expectedSha512, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Cached .NET SDK SHA-512 mismatch: $archiveSha512"
}

if (-not (Test-Path -LiteralPath $dotnetExecutable)) {
    New-Item -ItemType Directory -Path $installationDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $installationDirectory -Force
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$installedVersion = & $dotnetExecutable --version

if ($LASTEXITCODE -ne 0 -or $installedVersion -ne $sdkVersion) {
    throw "Expected .NET SDK $sdkVersion at $dotnetExecutable; found '$installedVersion'."
}

Write-Output $dotnetExecutable
