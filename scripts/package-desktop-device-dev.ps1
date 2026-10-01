[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$SourceSha,
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

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
$expectedMachineContract = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'spec\desktop-native\runtime-contract.json') -Raw |
    ConvertFrom-Json
$inventoryName = 'dev-package-manifest.json'
$runtimeManifestName = 'runtime/runtime-dev-manifest.json'
$archiveFullPath = [IO.Path]::GetFullPath($ArchivePath)
$archiveHashPath = $archiveFullPath + '.sha256'

if ($SourceSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'An exact lowercase 40-character source SHA is required.'
}

# Keep the archive's file namespace narrow, even when a publish directory gains an unexpected file.
function Assert-AllowedPath {
    param([string]$RelativePath)

    $rootBinary = $RelativePath -cmatch '^[^/]+\.dll$' -or
        $RelativePath -cin @('ScrcpySeamless.Desktop.exe', 'createdump.exe',
            'ScrcpySeamless.Desktop.deps.json', 'ScrcpySeamless.Desktop.runtimeconfig.json')
    $rootNotice = $RelativePath -cin @('LICENSE', 'DEV-BUNDLE-NOTICES.md')
    $runtimeFile = $RelativePath -cin @($runtimeNames | ForEach-Object { 'runtime/' + $_ }) -or
        $RelativePath -ceq $runtimeManifestName
    $licenseFile = $RelativePath -cin @($noticeNames | ForEach-Object { 'licenses/' + $_ })

    if (-not ($rootBinary -or $rootNotice -or $runtimeFile -or $licenseFile)) {
        throw "Unexpected file in Desktop DEV package: $RelativePath"
    }
}

# Enumerate without following symlinks; only the two reviewed subdirectories may exist.
function Get-PackageFiles {
    param([string]$Directory)

    $entries = @(Get-ChildItem -LiteralPath $Directory -Force -Recurse)

    foreach ($entry in $entries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Links are not allowed in Desktop DEV packages: $($entry.FullName)"
        }

        $relative = $entry.FullName.Substring($Directory.Length).TrimStart('\', '/').Replace('\', '/')

        if ($entry.PSIsContainer) {
            if ($relative -cnotin @('runtime', 'licenses')) {
                throw "Unexpected directory in Desktop DEV package: $relative"
            }
            continue
        }

        if ($relative -cne $inventoryName) {
            Assert-AllowedPath -RelativePath $relative
        }
    }

    $files = [string[]]@($entries | Where-Object { -not $_.PSIsContainer } |
        ForEach-Object { $_.FullName.Substring($Directory.Length).TrimStart('\', '/').Replace('\', '/') })
    [Array]::Sort($files, [StringComparer]::Ordinal)
    return $files
}

# Verify the source identity and all twelve runtime hashes before trusting package metadata.
function Assert-RuntimeManifest {
    param([string]$Directory, [bool]$RequireMachineContract)

    $path = Join-Path $Directory $runtimeManifestName

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Desktop DEV runtime manifest is missing.'
    }

    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json

    if ($manifest.SchemaVersion -ne 1 -or $manifest.SourceSha -cne $SourceSha -or
        $manifest.NativeSourceFingerprint -cnotmatch '^[0-9a-f]{64}$' -or
        $manifest.ServerSourceFingerprint -cnotmatch '^[0-9a-f]{64}$' -or
        $null -eq $manifest.Files -or $null -eq $manifest.Origins -or
        @($manifest.Files.PSObject.Properties).Count -ne $runtimeNames.Count -or
        @($manifest.Origins.PSObject.Properties).Count -ne $runtimeNames.Count) {
        throw 'Desktop DEV runtime manifest is invalid or has a different source SHA.'
    }

    $machineProperty = $manifest.PSObject.Properties['MachineContract']

    if ($null -eq $machineProperty) {
        if ($RequireMachineContract) {
            throw 'Desktop DEV runtime is legacy-only and lacks the required machine contract.'
        }
    }
    else {
        $machine = $machineProperty.Value
        $expectedCapabilities = @($expectedMachineContract.Capabilities)
        $actualCapabilities = @($machine.Capabilities)
        $invalidMachine = $machine -isnot [pscustomobject] -or
            @($machine.PSObject.Properties).Count -ne 4 -or
            $machine.Product -isnot [string] -or
            $machine.Product -cne $expectedMachineContract.Product -or
            ($machine.ProtocolMajor -isnot [int] -and $machine.ProtocolMajor -isnot [long]) -or
            $machine.ProtocolMajor -ne $expectedMachineContract.ProtocolMajor -or
            ($machine.ProtocolMinor -isnot [int] -and $machine.ProtocolMinor -isnot [long]) -or
            $machine.ProtocolMinor -ne $expectedMachineContract.ProtocolMinor -or
            $machine.Capabilities -isnot [array] -or
            $actualCapabilities.Count -ne $expectedCapabilities.Count

        if (-not $invalidMachine) {
            for ($index = 0; $index -lt $expectedCapabilities.Count; $index++) {
                if ($actualCapabilities[$index] -isnot [string] -or
                    $actualCapabilities[$index] -cne $expectedCapabilities[$index]) {
                    $invalidMachine = $true
                    break
                }
            }
        }

        if ($invalidMachine) {
            throw 'Desktop DEV runtime machine-contract claim differs from the canonical specification.'
        }
    }

    foreach ($name in $runtimeNames) {
        $file = Join-Path $Directory ('runtime/' + $name)
        $expectedOrigin = if ($name -in @('scrcpy.exe', 'scrcpy-server')) { 'source-built' } else { 'reviewed-import' }
        $hash = $manifest.Files.PSObject.Properties[$name].Value
        $origin = $manifest.Origins.PSObject.Properties[$name].Value

        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
            $hash -cnotmatch '^[0-9a-f]{64}$' -or $origin -cne $expectedOrigin -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
            throw "Desktop DEV runtime component is missing or mismatched: $name"
        }
    }

    return $null -ne $machineProperty
}

# Check the required public material and the exact sorted per-file inventory.
function Assert-PackageContents {
    param([string]$Directory, [bool]$RequireInventory, [bool]$RequireMachineContract)

    $isMachineBundle = Assert-RuntimeManifest -Directory $Directory -RequireMachineContract $RequireMachineContract
    $requiredNotices = if ($isMachineBundle) {
        $noticeNames
    }
    else {
        @($noticeNames | Where-Object { $_ -cne 'yyjson-LICENSE.txt' })
    }
    $required = @('ScrcpySeamless.Desktop.exe', 'ScrcpySeamless.Desktop.dll',
        'ScrcpySeamless.Core.dll', 'ScrcpySeamless.Infrastructure.dll',
        'ScrcpySeamless.Desktop.deps.json', 'ScrcpySeamless.Desktop.runtimeconfig.json',
        'LICENSE', 'DEV-BUNDLE-NOTICES.md', $runtimeManifestName) +
        @($runtimeNames | ForEach-Object { 'runtime/' + $_ }) +
        @($requiredNotices | ForEach-Object { 'licenses/' + $_ })
    $files = @(Get-PackageFiles -Directory $Directory)

    foreach ($name in $required) {
        if ($name -cnotin $files) {
            throw "Required Desktop DEV package file is missing: $name"
        }
    }

    $notice = Get-Content -LiteralPath (Join-Path $Directory 'DEV-BUNDLE-NOTICES.md') -Raw

    if ($notice -cnotmatch [regex]::Escape($SourceSha)) {
        throw 'DEV bundle notice does not identify the source SHA.'
    }

    if ($RequireInventory) {
        if ($inventoryName -cnotin $files) {
            throw 'Desktop DEV package inventory is missing.'
        }

        $inventory = Get-Content -LiteralPath (Join-Path $Directory $inventoryName) -Raw | ConvertFrom-Json
        $listedFiles = [string[]]@($inventory.Files.PSObject.Properties.Name)
        [Array]::Sort($listedFiles, [StringComparer]::Ordinal)
        $actualFiles = @($files | Where-Object { $_ -cne $inventoryName })

        if ($inventory.SchemaVersion -ne 1 -or $inventory.SourceSha -cne $SourceSha -or
            ($listedFiles -join "`n") -cne ($actualFiles -join "`n")) {
            throw 'Desktop DEV package inventory does not match the extracted file set.'
        }

        foreach ($name in $actualFiles) {
            $expectedHash = $inventory.Files.PSObject.Properties[$name].Value
            $actualHash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash.ToLowerInvariant()

            if ($expectedHash -cnotmatch '^[0-9a-f]{64}$' -or $expectedHash -cne $actualHash) {
                throw "Desktop DEV package inventory hash mismatch: $name"
            }
        }
    }

    return $files
}

# Inspect archive names before extracting to a disposable path containing spaces.
function Assert-Archive {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        -not (Test-Path -LiteralPath ($Path + '.sha256') -PathType Leaf)) {
        throw 'Desktop DEV ZIP or its SHA-256 sidecar is missing.'
    }

    $expectedHash = ((Get-Content -LiteralPath ($Path + '.sha256') -Raw).Trim() -split '\s+')[0]
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($expectedHash -cne $actualHash) {
        throw 'Desktop DEV ZIP SHA-256 mismatch.'
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)

    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })

        if ($names.Count -ne @($names | Select-Object -Unique).Count -or
            @($names | Where-Object { $_ -match '(^/|\\|(^|/)\.\.?(/|$)|:)' }).Count -ne 0) {
            throw 'Desktop DEV ZIP contains duplicate or unsafe entry names.'
        }
    }
    finally {
        $archive.Dispose()
    }

    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy desktop package with spaces ' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null

    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($Path, $scratch)
        $files = @(Assert-PackageContents -Directory $scratch -RequireInventory $true -RequireMachineContract $false)

        $sortedNames = [string[]]@($names)
        [Array]::Sort($sortedNames, [StringComparer]::Ordinal)

        if (($sortedNames -join "`n") -cne ($files -join "`n")) {
            throw 'Desktop DEV ZIP entries do not match the extracted package.'
        }
    }
    finally {
        $resolvedScratch = [IO.Path]::GetFullPath($scratch)
        $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + '\'

        if (-not $resolvedScratch.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Desktop DEV extraction cleanup escaped the temporary directory.'
        }

        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}

if ($VerifyOnly) {
    Assert-Archive -Path $archiveFullPath
    Write-Host "Verified Desktop DEV ZIP: $archiveFullPath"
    return
}

if (-not $PackageDirectory) {
    throw 'A staged package directory is required when creating a ZIP.'
}

$packagePath = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$packagePrefix = $packagePath + '\'

if ($archiveFullPath.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Desktop DEV ZIP must be created outside its staged package directory.'
}

if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) {
    throw 'The staged Desktop DEV package directory is missing.'
}

if ((Test-Path -LiteralPath $archiveFullPath) -or (Test-Path -LiteralPath $archiveHashPath) -or
    (Test-Path -LiteralPath (Join-Path $packagePath $inventoryName))) {
    throw 'The DEV ZIP or inventory already exists; never overwrite an accepted artifact.'
}

$files = @(Assert-PackageContents -Directory $packagePath -RequireInventory $false -RequireMachineContract $true)
$inventoryFiles = [ordered]@{}

foreach ($name in $files) {
    $inventoryFiles[$name] = (Get-FileHash -LiteralPath (Join-Path $packagePath $name) -Algorithm SHA256).Hash.ToLowerInvariant()
}

[ordered]@{ SchemaVersion = 1; SourceSha = $SourceSha; Files = $inventoryFiles } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packagePath $inventoryName) -Encoding UTF8
$allFiles = @(Assert-PackageContents -Directory $packagePath -RequireInventory $true -RequireMachineContract $true)
$archiveDirectory = Split-Path -Parent $archiveFullPath

if (-not (Test-Path -LiteralPath $archiveDirectory -PathType Container)) {
    throw 'The ZIP destination directory is missing.'
}

$stream = [IO.File]::Open($archiveFullPath, [IO.FileMode]::CreateNew)

try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)

    try {
        foreach ($name in $allFiles) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [IO.File]::OpenRead((Join-Path $packagePath $name))
            $output = $entry.Open()

            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $archiveFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $(Split-Path -Leaf $archiveFullPath)" | Set-Content -LiteralPath $archiveHashPath -Encoding Ascii
Assert-Archive -Path $archiveFullPath
Write-Host "Verified Desktop DEV ZIP: $archiveFullPath ($archiveHash)"
