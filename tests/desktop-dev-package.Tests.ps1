Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$packager = Join-Path $repositoryDirectory 'scripts\package-desktop-device-dev.ps1'
$sourceSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$runtimeNames = @(
    'scrcpy.exe', 'scrcpy-server', 'adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll',
    'SDL3.dll', 'avcodec-62.dll', 'avformat-62.dll', 'avutil-60.dll',
    'swresample-6.dll', 'scrcpy.png', 'disconnected.png'
)
$noticeNames = @(
    'Android-Platform-Tools-NOTICE.txt', 'dav1d-COPYING.txt',
    'FFmpeg-LGPL-2.1.txt', 'FFmpeg-LICENSE.md', 'GCC-GPL-3.0.txt',
    'GCC-RUNTIME-EXCEPTION.txt', 'MinGW-w64-COPYING.txt',
    'MinGW-w64-runtime-COPYING.txt', 'SDL-LICENSE.txt', 'zlib-LICENSE.txt',
    'yyjson-LICENSE.txt'
)
$machineContract = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'spec\desktop-native\runtime-contract.json') -Raw |
    ConvertFrom-Json
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy desktop package test with spaces ' + [guid]::NewGuid().ToString('N'))

# Build only synthetic bytes; no test invokes ADB, native mirroring or a published Desktop executable.
function New-SyntheticPackage {
    param([string]$Directory)

    New-Item -ItemType Directory -Path (Join-Path $Directory 'runtime'), (Join-Path $Directory 'licenses') -Force | Out-Null
    foreach ($name in @('ScrcpySeamless.Desktop.exe', 'ScrcpySeamless.Desktop.dll',
            'ScrcpySeamless.Core.dll', 'ScrcpySeamless.Infrastructure.dll',
            'ScrcpySeamless.Desktop.deps.json', 'ScrcpySeamless.Desktop.runtimeconfig.json', 'LICENSE')) {
        Set-Content -LiteralPath (Join-Path $Directory $name) -Value ('synthetic:' + $name) -Encoding UTF8
    }
    Set-Content -LiteralPath (Join-Path $Directory 'DEV-BUNDLE-NOTICES.md') -Value $sourceSha -Encoding UTF8

    foreach ($name in $noticeNames) {
        Set-Content -LiteralPath (Join-Path $Directory ('licenses\' + $name)) -Value ('synthetic:' + $name) -Encoding UTF8
    }

    $hashes = [ordered]@{}
    $origins = [ordered]@{}

    foreach ($name in $runtimeNames) {
        $path = Join-Path $Directory ('runtime\' + $name)
        Set-Content -LiteralPath $path -Value ('synthetic:' + $name) -Encoding UTF8
        $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $origins[$name] = if ($name -in @('scrcpy.exe', 'scrcpy-server')) { 'source-built' } else { 'reviewed-import' }
    }

    [ordered]@{
        SchemaVersion = 1
        SourceSha = $sourceSha
        NativeSourceFingerprint = ('b' * 64)
        ServerSourceFingerprint = ('c' * 64)
        Files = $hashes
        Origins = $origins
        MachineContract = $machineContract
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Directory 'runtime\runtime-dev-manifest.json') -Encoding UTF8
}

# Require each negative fixture to fail before it can create an accepted archive.
function Assert-RejectedPackage {
    param([string]$Directory, [string]$Archive)

    $rejected = $false

    try {
        & $packager -PackageDirectory $Directory -SourceSha $sourceSha -ArchivePath $Archive | Out-Null
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected -or (Test-Path -LiteralPath $Archive)) {
        throw 'Unsafe synthetic Desktop DEV package was accepted.'
    }
}

New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

try {
    $first = Join-Path $fixtureRoot 'first staged package'
    $second = Join-Path $fixtureRoot 'second staged package'
    $firstZip = Join-Path $fixtureRoot 'first.zip'
    $secondZip = Join-Path $fixtureRoot 'second.zip'
    New-SyntheticPackage -Directory $first
    New-SyntheticPackage -Directory $second
    & $packager -PackageDirectory $first -SourceSha $sourceSha -ArchivePath $firstZip | Out-Null
    & $packager -PackageDirectory $second -SourceSha $sourceSha -ArchivePath $secondZip | Out-Null
    & $packager -SourceSha $sourceSha -ArchivePath $firstZip -VerifyOnly | Out-Null

    if ((Get-FileHash -LiteralPath $firstZip -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $secondZip -Algorithm SHA256).Hash) {
        throw 'Identical Desktop DEV inputs produced different ZIP bytes.'
    }

    $privatePackage = Join-Path $fixtureRoot 'private package'
    New-SyntheticPackage -Directory $privatePackage
    Set-Content -LiteralPath (Join-Path $privatePackage 'phone.json') -Value 'synthetic personal data'
    Assert-RejectedPackage -Directory $privatePackage -Archive (Join-Path $fixtureRoot 'private.zip')

    $debugSymbols = Join-Path $fixtureRoot 'debug symbols'
    New-SyntheticPackage -Directory $debugSymbols
    Set-Content -LiteralPath (Join-Path $debugSymbols 'ScrcpySeamless.Desktop.pdb') -Value 'synthetic local path'
    Assert-RejectedPackage -Directory $debugSymbols -Archive (Join-Path $fixtureRoot 'symbols.zip')

    $missingNotice = Join-Path $fixtureRoot 'missing notice'
    New-SyntheticPackage -Directory $missingNotice
    Remove-Item -LiteralPath (Join-Path $missingNotice 'licenses\yyjson-LICENSE.txt')
    Assert-RejectedPackage -Directory $missingNotice -Archive (Join-Path $fixtureRoot 'notice.zip')

    $legacyRuntime = Join-Path $fixtureRoot 'legacy runtime'
    New-SyntheticPackage -Directory $legacyRuntime
    $legacyManifestPath = Join-Path $legacyRuntime 'runtime\runtime-dev-manifest.json'
    $legacyManifest = Get-Content -LiteralPath $legacyManifestPath -Raw | ConvertFrom-Json
    $legacyManifest.PSObject.Properties.Remove('MachineContract')
    $legacyManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $legacyManifestPath -Encoding UTF8
    Assert-RejectedPackage -Directory $legacyRuntime -Archive (Join-Path $fixtureRoot 'legacy.zip')

    # The read-only verifier still recognizes an already archived old bundle without the new notice.
    Remove-Item -LiteralPath (Join-Path $legacyRuntime 'licenses\yyjson-LICENSE.txt')
    $legacyFiles = [string[]]@(Get-ChildItem -LiteralPath $legacyRuntime -File -Recurse |
        ForEach-Object { $_.FullName.Substring($legacyRuntime.Length).TrimStart('\', '/').Replace('\', '/') })
    [Array]::Sort($legacyFiles, [StringComparer]::Ordinal)
    $legacyHashes = [ordered]@{}

    foreach ($name in $legacyFiles) {
        $legacyHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $legacyRuntime $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    [ordered]@{ SchemaVersion = 1; SourceSha = $sourceSha; Files = $legacyHashes } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $legacyRuntime 'dev-package-manifest.json') -Encoding UTF8
    $legacyArchive = Join-Path $fixtureRoot 'archived legacy.zip'
    $legacyZip = [IO.Compression.ZipFile]::Open($legacyArchive, [IO.Compression.ZipArchiveMode]::Create)

    try {
        foreach ($name in @($legacyFiles) + 'dev-package-manifest.json') {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($legacyZip,
                (Join-Path $legacyRuntime $name), $name) | Out-Null
        }
    }
    finally {
        $legacyZip.Dispose()
    }
    $legacyArchiveHash = (Get-FileHash -LiteralPath $legacyArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$legacyArchiveHash  archived legacy.zip" | Set-Content -LiteralPath ($legacyArchive + '.sha256') -Encoding Ascii
    & $packager -SourceSha $sourceSha -ArchivePath $legacyArchive -VerifyOnly | Out-Null

    $falseClaim = Join-Path $fixtureRoot 'false machine claim'
    New-SyntheticPackage -Directory $falseClaim
    $falseClaimManifestPath = Join-Path $falseClaim 'runtime\runtime-dev-manifest.json'
    $falseClaimManifest = Get-Content -LiteralPath $falseClaimManifestPath -Raw | ConvertFrom-Json
    $falseClaimManifest.MachineContract.ProtocolMajor = 2
    $falseClaimManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $falseClaimManifestPath -Encoding UTF8
    Assert-RejectedPackage -Directory $falseClaim -Archive (Join-Path $fixtureRoot 'false-claim.zip')

    $changedRuntime = Join-Path $fixtureRoot 'changed runtime'
    New-SyntheticPackage -Directory $changedRuntime
    Set-Content -LiteralPath (Join-Path $changedRuntime 'runtime\scrcpy.exe') -Value 'tampered runtime'
    Assert-RejectedPackage -Directory $changedRuntime -Archive (Join-Path $fixtureRoot 'runtime.zip')

    $badShaRejected = $false

    try {
        & $packager -SourceSha ('d' * 40) -ArchivePath $firstZip -VerifyOnly | Out-Null
    }
    catch {
        $badShaRejected = $true
    }

    if (-not $badShaRejected) {
        throw 'An unrelated source SHA accepted the Desktop DEV ZIP.'
    }

    Add-Type -AssemblyName System.IO.Compression
    $extraEntryArchive = [IO.Compression.ZipFile]::Open($secondZip, [IO.Compression.ZipArchiveMode]::Update)

    try {
        $entry = $extraEntryArchive.CreateEntry('phone.json')
        $writer = New-Object IO.StreamWriter($entry.Open())

        try {
            $writer.Write('synthetic personal data')
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $extraEntryArchive.Dispose()
    }

    $extraEntryHash = (Get-FileHash -LiteralPath $secondZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$extraEntryHash  second.zip" | Set-Content -LiteralPath ($secondZip + '.sha256') -Encoding Ascii
    $extraEntryRejected = $false

    try {
        & $packager -SourceSha $sourceSha -ArchivePath $secondZip -VerifyOnly | Out-Null
    }
    catch {
        $extraEntryRejected = $true
    }

    if (-not $extraEntryRejected) {
        throw 'A ZIP with an added personal file passed the independent archive verification.'
    }

    [IO.File]::AppendAllText($firstZip, 'tampered')
    $tamperedRejected = $false

    try {
        & $packager -SourceSha $sourceSha -ArchivePath $firstZip -VerifyOnly | Out-Null
    }
    catch {
        $tamperedRejected = $true
    }

    if (-not $tamperedRejected) {
        throw 'A changed Desktop DEV ZIP passed its SHA-256 sidecar.'
    }

    Write-Host 'PASS: deterministic Desktop DEV ZIP, clean extraction, source/hash checks, privacy and notice rejection.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + '\'

    if (-not $resolvedRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Synthetic Desktop package cleanup escaped the temporary directory.'
    }

    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
