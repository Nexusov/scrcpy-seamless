[CmdletBinding(DefaultParameterSetName = 'Package')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Package')][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('(?-i:^[a-f0-9]{40}$)')][string]$SourceSha,
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')][switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'portable-common.ps1')

$zipPath = [IO.Path]::GetFullPath($ArchivePath)
$checksumPath = "$zipPath.sha256"
$zipTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

if ([IO.Path]::GetExtension($zipPath) -cne '.zip') {
    throw 'Choose a .zip archive destination.'
}

Assert-PlainPortablePath $zipPath
Assert-PlainPortablePath $checksumPath

function Assert-PortableZip {
    param([string]$Path, [string]$ExpectedHash)

    Assert-PlainPortablePath $Path
    $stream = [IO.File]::OpenRead($Path)
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy portable extraction ' + [guid]::NewGuid().ToString('N'))

    try {
        if ((Get-PortableHash $Path) -cne $ExpectedHash) {
            throw 'Portable ZIP SHA-256 mismatch.'
        }

        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)

        try {
            $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            $previous = ''

            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName
                Assert-PortableName $name

                if (-not $names.Add($name)) {
                    throw "Duplicate portable ZIP path: $name"
                }

                $fileType = ($entry.ExternalAttributes -shr 16) -band 0xf000

                if ($fileType -notin @(0, 0x8000) -or
                    ($entry.ExternalAttributes -band [int][IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Non-regular portable ZIP entry: $name"
                }

                if ([StringComparer]::Ordinal.Compare($previous, $name) -ge 0 -or
                    $entry.LastWriteTime.DateTime -ne $zipTimestamp.DateTime) {
                    throw 'Portable ZIP entries lack canonical ordering or timestamps.'
                }

                $previous = $name
            }
        }
        finally {
            $archive.Dispose()
        }

        Assert-PlainPortablePath $scratch
        [IO.Directory]::CreateDirectory($scratch) | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($Path, $scratch)
        $files = @(Assert-PortableContents -Directory $scratch -SourceSha $SourceSha)

        if (-not $names.SetEquals([string[]]$files)) {
            throw 'Portable ZIP entries differ from the independently extracted package.'
        }
    }
    finally {
        $stream.Dispose()

        if ([IO.Directory]::Exists($scratch)) {
            Assert-PlainPortablePath $scratch
            [IO.Directory]::Delete($scratch, $true)
        }
    }
}

if ($VerifyOnly) {
    $expected = [IO.File]::ReadAllText($checksumPath)
    $pattern = '\A([a-f0-9]{64})  ' + [regex]::Escape([IO.Path]::GetFileName($zipPath)) + '\r?\n\z'

    if ($expected -cnotmatch $pattern) {
        throw 'Invalid portable ZIP SHA-256 sidecar.'
    }

    Assert-PortableZip -Path $zipPath -ExpectedHash $Matches[1]
    Write-Host "Verified Windows x64 portable ZIP: $zipPath"
    return
}

$packagePath = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$outputDirectory = [IO.Path]::GetDirectoryName($zipPath)

if ($zipPath.StartsWith(($packagePath + '\'), [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw 'Choose a new ZIP/sidecar destination outside the staged portable package.'
}

$files = @(Assert-PortableContents -Directory $packagePath -SourceSha $SourceSha)
$scratch = Join-Path $outputDirectory ('.portable-archive-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$temporaryZip = Join-Path $scratch 'package.zip'
$temporaryChecksum = "$temporaryZip.sha256"
$publishedChecksum = $false
$publishedZip = $false

try {
    $stream = [IO.File]::Open($temporaryZip, [IO.FileMode]::CreateNew)

    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)

        try {
            foreach ($name in $files) {
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $zipTimestamp
                $entry.ExternalAttributes = 0
                $input = [IO.File]::OpenRead((Join-Path $packagePath $name))

                try {
                    $output = $entry.Open()

                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                    }
                }
                finally {
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

    $hash = Get-PortableHash $temporaryZip
    Assert-PortableZip -Path $temporaryZip -ExpectedHash $hash
    $line = "$hash  $([IO.Path]::GetFileName($zipPath))" + [char]10
    [IO.File]::WriteAllText($temporaryChecksum, $line, [Text.Encoding]::ASCII)
    [IO.File]::Move($temporaryChecksum, $checksumPath)
    $publishedChecksum = $true
    [IO.File]::Move($temporaryZip, $zipPath)
    $publishedZip = $true
    Write-Host "Verified Windows x64 portable ZIP: $zipPath ($hash)"
}
finally {
    if ($publishedChecksum -and -not $publishedZip) {
        [IO.File]::Delete($checksumPath)
    }

    Assert-PlainPortablePath $scratch
    [IO.Directory]::Delete($scratch, $true)
}
