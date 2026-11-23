# Shared portable package layout and validation. Legacy DEV archives have their own verifier.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$portableRepository = Split-Path -Parent $PSScriptRoot
$portableCatalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'portable-runtime.json') -Raw | ConvertFrom-Json
$portableRuntimeManifest = 'runtime/runtime-manifest.json'
$portableInventory = 'package-manifest.json'
$portableProject = 'ScrcpySeamless.Desktop'
$portableSourceBinaries = @('scrcpy.exe', 'scrcpy-server')
$portableRuntimeNames = $portableSourceBinaries + @($portableCatalog.Imports.PSObject.Properties.Name)
$portableMetadata = @('LICENSE', 'README.md', 'NOTICE.md', 'build-provenance.json', 'sbom.json')
$portableApplication = @("$portableProject.exe", "$portableProject.deps.json", "$portableProject.runtimeconfig.json")

function Get-PortableHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-PortableJson {
    param([string]$Path, $Value)
    $json = ConvertTo-Json -InputObject $Value -Depth 40
    [IO.File]::WriteAllText($Path, ($json.Replace([string][char]13, '') + [char]10), [Text.UTF8Encoding]::new($false))
}

function Assert-PortableSource {
    param([string]$SourceSha)

    $head = & git -C $portableRepository rev-parse --verify HEAD

    if ($LASTEXITCODE -or $head -cne $SourceSha) {
        throw 'Portable staging requires the exact source HEAD.'
    }

    $dirty = @(& git -C $portableRepository status --porcelain=v1 --untracked-files=all --ignore-submodules=none)

    if ($LASTEXITCODE -or $dirty.Count) {
        throw 'Portable staging requires a clean committed source tree.'
    }
}

# Limit paths to ASCII Windows-safe segments. This excludes ADS, traversal, devices and ambiguous aliases.
function Assert-PortableName {
    param([string]$Name)

    foreach ($segment in $Name.Split('/')) {
        if ($segment -cnotmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*$' -or
            $segment.EndsWith('.') -or
            $segment -match '^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)') {
            throw "Unsafe portable package path: $Name"
        }
    }
}

function Assert-PlainPortablePath {
    param([string]$Path)

    for ($item = $Path; $item; $item = [IO.Path]::GetDirectoryName($item)) {
        if ((Test-Path -LiteralPath $item) -and
            ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Reparse point in portable package path: $item"
        }
    }
}

function Get-PortableFiles {
    param([string]$Directory)

    Assert-PlainPortablePath $Directory
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push($Directory)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = [Collections.Generic.List[string]]::new()

    while ($directories.Count) {
        foreach ($item in Get-ChildItem -LiteralPath ($directories.Pop()) -Force) {
            $name = $item.FullName.Substring($Directory.Length + 1).Replace('\', '/')
            Assert-PortableName $name

            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point in portable package: $name"
            }

            if (-not $names.Add($name)) {
                throw "Duplicate portable package path: $name"
            }

            if ($item.PSIsContainer) {
                if ($name -cnotin @('runtime', 'licenses', 'licenses/dotnet')) {
                    throw "Unexpected portable package directory: $name"
                }
                $directories.Push($item.FullName)
                continue
            }

            $files.Add($name)
        }
    }

    $files.Sort([StringComparer]::Ordinal)
    return $files.ToArray()
}

function Get-PortablePublishAssets {
    param([string]$Directory)

    $deps = Get-Content -LiteralPath (Join-Path $Directory "$portableProject.deps.json") -Raw | ConvertFrom-Json
    $target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name]

    if ($null -eq $target -or $target.Name -cne '.NETCoreApp,Version=v10.0/win-x64') {
        throw 'Expected a Windows x64 self-contained Desktop dependency graph.'
    }

    foreach ($library in $target.Value.PSObject.Properties) {
        $libraryMetadata = $deps.libraries.PSObject.Properties[$library.Name].Value

        foreach ($group in $library.Value.PSObject.Properties | Where-Object Name -In @('runtime', 'native')) {
            foreach ($asset in $group.Value.PSObject.Properties) {
                $name = [IO.Path]::GetFileName($asset.Name)
                Assert-PortableName $name

                if ($name.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                if ($name -cnotmatch '\.dll$' -and $name -cne 'createdump.exe') {
                    throw "Unexpected Desktop runtime asset: $name"
                }

                [pscustomobject]@{ Name = $name; Library = $library.Name; Type = $libraryMetadata.type }
            }
        }
    }
}

function Assert-PortableContents {
    param([string]$Directory, [string]$SourceSha)

    $files = @(Get-PortableFiles $Directory)
    $assets = @(Get-PortablePublishAssets $Directory)
    $required = $portableApplication + $portableMetadata + @(
        "$portableProject.dll", 'ScrcpySeamless.Core.dll', 'ScrcpySeamless.Infrastructure.dll',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', $portableRuntimeManifest, $portableInventory
    ) + @($portableRuntimeNames | ForEach-Object { "runtime/$_" }) +
        @($portableCatalog.Notices.PSObject.Properties.Name | ForEach-Object { "licenses/$_" })

    $sbom = Get-Content -LiteralPath (Join-Path $Directory 'sbom.json') -Raw | ConvertFrom-Json
    $desktopNotices = @($sbom.components | ForEach-Object {
        $properties = $_.PSObject.Properties['properties']

        if ($null -ne $properties) {
            $properties.Value | Where-Object name -CEQ 'seamless:notice' | Select-Object -ExpandProperty value
        }
    })

    foreach ($notice in $desktopNotices) {
        if ($notice -cnotmatch '^licenses/dotnet/[A-Za-z0-9_.-]+[.](txt|md|license)$') {
            throw "Unexpected Desktop license path: $notice"
        }
    }

    $required += $desktopNotices + @($assets.Name)

    foreach ($name in $required) {
        if ($name -cnotin $files) {
            throw "Required portable package file is missing: $name"
        }
    }

    $allowed = $required + $assets.Name

    foreach ($name in $files) {
        if ($name -cnotin $required) {
            throw "Unexpected file in portable package: $name"
        }
    }

    $manifest = Get-Content -LiteralPath (Join-Path $Directory $portableRuntimeManifest) -Raw | ConvertFrom-Json
    $expectedContract = Get-Content -LiteralPath (Join-Path $portableRepository 'spec/desktop-native/runtime-contract.json') -Raw |
        ConvertFrom-Json
    $actualContract = ConvertTo-Json -InputObject $manifest.MachineContract -Depth 10 -Compress
    $canonicalContract = ConvertTo-Json -InputObject $expectedContract -Depth 10 -Compress

    if ($manifest.SchemaVersion -ne 1 -or $manifest.SourceSha -cne $SourceSha -or
        $manifest.NativeSourceFingerprint -cnotmatch '^[a-f0-9]{64}$' -or
        $manifest.ServerSourceFingerprint -cnotmatch '^[a-f0-9]{64}$' -or
        @($manifest.Files.PSObject.Properties).Count -ne $portableRuntimeNames.Count -or
        @($manifest.Origins.PSObject.Properties).Count -ne $portableRuntimeNames.Count -or
        $actualContract -cne $canonicalContract) {
        throw 'Portable runtime manifest differs from the expected source or machine contract.'
    }

    foreach ($name in $portableRuntimeNames) {
        $origin = 'source-built'
        $hash = Get-PortableHash (Join-Path $Directory "runtime/$name")
        $import = $portableCatalog.Imports.PSObject.Properties[$name]

        if ($null -ne $import) {
            $origin = 'reviewed-import'

            if ($hash -cne $import.Value.Sha256) {
                throw "Portable runtime import differs from its reviewed hash: $name"
            }
        }

        if ($manifest.Origins.PSObject.Properties[$name].Value -cne $origin -or
            $manifest.Files.PSObject.Properties[$name].Value -cne $hash) {
            throw "Portable runtime manifest hash or origin mismatch: $name"
        }
    }

    $provenance = Get-Content -LiteralPath (Join-Path $Directory 'build-provenance.json') -Raw | ConvertFrom-Json
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory "$portableProject.dll")).ProductVersion

    if ($provenance.SourceSha -cne $SourceSha -or $provenance.RuntimeIdentifier -cne 'win-x64' -or
        $provenance.NativeFingerprint -cne $manifest.NativeSourceFingerprint -or
        $provenance.ServerFingerprint -cne $manifest.ServerSourceFingerprint -or
        $productVersion -cne "$($provenance.Version)+$SourceSha" -or
        $sbom.bomFormat -cne 'CycloneDX' -or $sbom.specVersion -cne '1.6' -or
        $sbom.metadata.component.version -cne $provenance.Version) {
        throw 'Portable build provenance, Desktop metadata or SBOM mismatch.'
    }

    $binaryNames = @("$portableProject.exe") + @($assets.Name | Select-Object -Unique) +
        @($portableRuntimeNames | ForEach-Object { "runtime/$_" })
    $binaryEntries = @($sbom.components | Where-Object type -CEQ 'file')

    if ($binaryEntries.Count -ne $binaryNames.Count) {
        throw 'Portable SBOM file set differs from the shipped runtime binaries.'
    }

    foreach ($name in $binaryNames) {
        $component = @($binaryEntries | Where-Object name -CEQ $name)

        if ($component.Count -ne 1 -or $component[0].hashes[0].alg -cne 'SHA-256' -or
            $component[0].hashes[0].content -cne (Get-PortableHash (Join-Path $Directory $name))) {
            throw "Portable SBOM hash mismatch: $name"
        }
    }

    $inventory = Get-Content -LiteralPath (Join-Path $Directory $portableInventory) -Raw | ConvertFrom-Json
    $listed = [string[]]@($inventory.Files.PSObject.Properties.Name)
    $payload = @($files | Where-Object { $_ -cne $portableInventory })
    [Array]::Sort($listed, [StringComparer]::Ordinal)

    if ($inventory.SchemaVersion -ne 1 -or $inventory.SourceSha -cne $SourceSha -or
        ($listed -join [char]10) -cne ($payload -join [char]10)) {
        throw 'Portable package inventory differs from the actual file set.'
    }

    foreach ($name in $payload) {
        if ($inventory.Files.PSObject.Properties[$name].Value -cne (Get-PortableHash (Join-Path $Directory $name))) {
            throw "Portable package inventory hash mismatch: $name"
        }
    }

    return $files
}
