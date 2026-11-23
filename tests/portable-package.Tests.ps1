Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repository = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy portable test ' + [guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $scratch 'scripts'
$fixtureSpec = Join-Path $scratch 'spec/desktop-native'
$stage = Join-Path $scratch 'stage'
$source = '0123456789abcdef0123456789abcdef01234567'
$version = '2.0.0-rc.1'
$testZip = Join-Path $scratch 'portable test.zip'

function Set-Json {
    param([string]$Path, $Value)
    ConvertTo-Json -InputObject $Value -Depth 15 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Get-Hash {
    param([string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $Message) {
            throw "Expected rejection '$Message', got $($_.Exception.Message)"
        }
        return
    }

    throw "Operation unexpectedly accepted. Expected '$Message'."
}

function Update-Sidecar {
    param([string]$Path)
    "$(Get-Hash $Path)  $([IO.Path]::GetFileName($Path))" |
        Set-Content -LiteralPath "$Path.sha256" -Encoding ASCII
}

function New-ZipWithNames {
    param([string]$Path, [string[]]$Names, [int]$Attributes = 0)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)

    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)

        try {
            foreach ($name in $Names) {
                $entry = $zip.CreateEntry($name)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $entry.ExternalAttributes = $Attributes
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    Update-Sidecar $Path
}

[IO.Directory]::CreateDirectory($fixtureScripts) | Out-Null
[IO.Directory]::CreateDirectory($fixtureSpec) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $stage 'runtime')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $stage 'licenses')) | Out-Null

try {
    foreach ($name in @('package-portable.ps1', 'portable-common.ps1', 'portable-runtime.json')) {
        Copy-Item -LiteralPath (Join-Path $repository "scripts/$name") -Destination (Join-Path $fixtureScripts $name)
    }

    $packager = Join-Path $fixtureScripts 'package-portable.ps1'
    $stager = Join-Path $repository 'scripts/stage-portable.ps1'
    Assert-Rejected -Message 'exact source HEAD' -Action {
        & $stager -SourceSha $source -PackageDirectory $stage -Offline
    }

    $catalogPath = Join-Path $fixtureScripts 'portable-runtime.json'
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $contractPath = Join-Path $repository 'spec/desktop-native/runtime-contract.json'
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $contractPath -Destination (Join-Path $fixtureSpec 'runtime-contract.json')
    $files = [ordered]@{}
    $origins = [ordered]@{}
    $assets = [ordered]@{}

    foreach ($name in @('scrcpy.exe', 'scrcpy-server') + @($catalog.Imports.PSObject.Properties.Name)) {
        $path = Join-Path $stage "runtime/$name"
        Set-Content -LiteralPath $path -Value "Fixture $name"
        $files[$name] = Get-Hash $path
        $origins[$name] = 'source-built'
        $import = $catalog.Imports.PSObject.Properties[$name]

        if ($null -ne $import) {
            $origins[$name] = 'reviewed-import'
            $import.Value.Sha256 = $files[$name]
        }
    }

    Set-Json $catalogPath $catalog
    Set-Json (Join-Path $stage 'runtime/runtime-manifest.json') @{
        SchemaVersion = 1; SourceSha = $source; NativeSourceFingerprint = 'a' * 64
        ServerSourceFingerprint = 'b' * 64; Files = $files; Origins = $origins; MachineContract = $contract
    }

    foreach ($name in $catalog.Notices.PSObject.Properties.Name) {
        Set-Content -LiteralPath (Join-Path $stage "licenses/$name") -Value "Fixture notice $name"
    }

    $desktopDll = Join-Path $stage 'ScrcpySeamless.Desktop.dll'
    $assembly = @"
using System.Reflection;
[assembly: AssemblyInformationalVersion("$version+$source")]
public sealed class PortableArchiveFixture {}
"@
    Add-Type -TypeDefinition $assembly -OutputAssembly $desktopDll
    $assets['ScrcpySeamless.Desktop.dll'] = @{}

    foreach ($name in @('ScrcpySeamless.Core.dll', 'ScrcpySeamless.Infrastructure.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        Set-Content -LiteralPath (Join-Path $stage $name) -Value "Fixture $name"
        $assets[$name] = @{}
    }

    foreach ($name in @('LICENSE', 'README.md', 'NOTICE.md', 'ScrcpySeamless.Desktop.exe', 'ScrcpySeamless.Desktop.runtimeconfig.json')) {
        Set-Content -LiteralPath (Join-Path $stage $name) -Value "Fixture $name"
    }

    $rid = '.NETCoreApp,Version=v10.0/win-x64'
    Set-Json (Join-Path $stage 'ScrcpySeamless.Desktop.deps.json') @{
        runtimeTarget = @{ name = $rid }
        targets = @{ $rid = @{ "Synthetic/$version" = @{ runtime = $assets; native = @{ 'libFixture.pdb' = @{} } } } }
        libraries = @{ "Synthetic/$version" = @{ type = 'project' } }
    }
    Set-Json (Join-Path $stage 'build-provenance.json') @{
        SourceSha = $source; Version = $version; RuntimeIdentifier = 'win-x64'
        NativeFingerprint = 'a' * 64; ServerFingerprint = 'b' * 64
    }

    $binaryComponents = foreach ($name in @('ScrcpySeamless.Desktop.exe') + @($assets.Keys) + @($files.Keys | ForEach-Object { "runtime/$_" })) {
        @{ type = 'file'; name = $name; hashes = @(@{ alg = 'SHA-256'; content = Get-Hash (Join-Path $stage $name) }) }
    }

    Set-Json (Join-Path $stage 'sbom.json') @{
        bomFormat = 'CycloneDX'; specVersion = '1.6'; metadata = @{ component = @{ version = $version } }
        components = @($binaryComponents)
    }
    $inventory = [ordered]@{}

    foreach ($item in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $inventory[$item.FullName.Substring($stage.Length + 1).Replace('\', '/')] = Get-Hash $item.FullName
    }

    Set-Json (Join-Path $stage 'package-manifest.json') @{ SchemaVersion = 1; SourceSha = $source; Files = $inventory }
    & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $testZip
    & $packager -SourceSha $source -ArchivePath $testZip -VerifyOnly
    $second = Join-Path $scratch 'second.zip'
    & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $second

    if ((Get-Hash $testZip) -cne (Get-Hash $second)) {
        throw 'The same portable stage produced different ZIP bytes.'
    }

    Assert-Rejected -Message 'new ZIP/sidecar' -Action {
        & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $testZip
    }

    Assert-Rejected -Message 'source or machine contract' -Action {
        & $packager -SourceSha ('d' * 40) -ArchivePath $testZip -VerifyOnly
    }

    $private = Join-Path $stage 'phone.json'
    Set-Content -LiteralPath $private -Value '{}'
    $invalid = Join-Path $scratch 'invalid.zip'

    Assert-Rejected -Message 'Unexpected file.*phone[.]json' -Action {
        & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $invalid
    }

    [IO.File]::Delete($private)
    Set-Content -LiteralPath (Join-Path $stage 'README.md') -Value 'Changed fixture'
    Assert-Rejected -Message 'inventory hash mismatch: README[.]md' -Action {
        & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $invalid
    }

    $junction = Join-Path $stage 'logs'
    New-Item -Path $junction -ItemType Junction -Target $fixtureSpec | Out-Null

    try {
        Assert-Rejected -Message 'Reparse point' -Action {
            & $packager -PackageDirectory $stage -SourceSha $source -ArchivePath $invalid
        }
    }
    finally {
        [IO.Directory]::Delete($junction)
    }

    foreach ($badName in @('../outside.dll', 'runtime/CON.dll', 'runtime/adb.exe:stream', 'runtime./adb.exe', 'runtime\adb.exe')) {
        New-ZipWithNames -Path $invalid -Names @($badName)
        Assert-Rejected -Message 'Unsafe portable package path' -Action {
            & $packager -SourceSha $source -ArchivePath $invalid -VerifyOnly
        }

        [IO.File]::Delete($invalid)
    }

    New-ZipWithNames -Path $invalid -Names @('one.dll', 'ONE.dll')
    Assert-Rejected -Message 'Duplicate portable ZIP path' -Action {
        & $packager -SourceSha $source -ArchivePath $invalid -VerifyOnly
    }

    [IO.File]::Delete($invalid)
    New-ZipWithNames -Path $invalid -Names @('runtime/link.dll') -Attributes (0xa000 -shl 16)
    Assert-Rejected -Message 'Non-regular portable ZIP entry' -Action {
        & $packager -SourceSha $source -ArchivePath $invalid -VerifyOnly
    }

    [IO.File]::Delete($invalid)
    New-ZipWithNames -Path $invalid -Names @(1..1025 | ForEach-Object { 'fixture-{0:D4}.txt' -f $_ })
    Assert-Rejected -Message 'entry count limit' -Action {
        & $packager -SourceSha $source -ArchivePath $invalid -VerifyOnly
    }

    Set-Content -LiteralPath "$testZip.sha256" -Value ('a' * 513)
    Assert-Rejected -Message 'sidecar exceeds the size limit' -Action {
        & $packager -SourceSha $source -ArchivePath $testZip -VerifyOnly
    }

    Set-Content -LiteralPath "$testZip.sha256" -Value ("$('a' * 64)  " + [IO.Path]::GetFileName($testZip))
    Assert-Rejected -Message 'ZIP SHA-256 mismatch' -Action {
        & $packager -SourceSha $source -ArchivePath $testZip -VerifyOnly
    }

    Write-Host 'Portable packaging synthetic assertions passed.'
}
finally {
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + '\'

    if (-not [IO.Path]::GetFullPath($scratch).StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture directory escaped the temporary root.'
    }

    [IO.Directory]::Delete($scratch, $true)
}
