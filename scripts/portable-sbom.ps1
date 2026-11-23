function Get-PortableManagedLicense {
    param([string]$Library, [string]$Type, [string[]]$PackageFolders, [string]$NoticeDirectory)

    $id, $version = $Library.Split('/')
    $packageId = $id -replace '^runtimepack[.]', ''
    $name = $packageId.ToLowerInvariant()
    $relative = "$name/$version"
    Assert-PortableName $relative
    $packagePath = $PackageFolders | ForEach-Object { Join-Path $_ "$relative/$name.$version.nupkg" } |
        Where-Object { [IO.File]::Exists($_) } | Select-Object -First 1

    if (-not $packagePath) {
        throw "Missing restored package for license inventory: $Library"
    }

    Assert-PlainPortablePath $packagePath
    $zip = [IO.Compression.ZipFile]::OpenRead($packagePath)

    try {
        $nuspec = $zip.Entries | Where-Object FullName -IEQ "$name.nuspec" | Select-Object -First 1
        $reader = [IO.StreamReader]::new($nuspec.Open())

        try {
            [xml]$document = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $document.package.metadata
        $declaration = $metadata.SelectSingleNode("*[local-name()='license']")
        $notices = @(foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '^(license([.](txt|md))?|third-party-notices[.](txt|md))$') {
                $filename = "$name.$version.$($entry.FullName.ToLowerInvariant())"

                if ($filename.EndsWith('.license')) {
                    $filename += '.txt'
                }

                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $NoticeDirectory $filename), $false)
                "licenses/dotnet/$filename"
            }
        })

        $component = [ordered]@{
            type = 'library'
            'bom-ref' = "package:$Library"
            name = $packageId
            version = $version
            purl = "pkg:nuget/$packageId@$version"
            hashes = @(@{ alg = 'SHA-512'; content = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA512).Hash.ToLowerInvariant() })
            properties = @()
        }
        $copyright = $metadata.SelectSingleNode("*[local-name()='copyright']")

        if ($null -ne $copyright) {
            $component.copyright = $copyright.InnerText
        }

        if ($null -ne $declaration -and $declaration.GetAttribute('type') -ceq 'expression') {
            $expression = $declaration.InnerText
            $component.licenses = @(@{ expression = $expression })

            if ($notices.Count -eq 0 -and $expression -ceq 'MIT') {
                $file = "$name.$version.license.txt"
                $text = [string]$component.copyright + [char]10 + (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'portable-mit-license.txt') -Raw)
                [IO.File]::WriteAllText((Join-Path $NoticeDirectory $file), $text)
                $notices += "licenses/dotnet/$file"
            }
        }

        if ($notices.Count -eq 0) {
            $component.properties += @{ name = 'seamless:notice-status'; value = 'No bundled license text identified' }
        }

        foreach ($notice in $notices) {
            $component.properties += @{ name = 'seamless:notice'; value = $notice }
        }

        if ($Type -ceq 'runtimepack') {
            $component.properties += @{ name = 'seamless:resolution'; value = 'Selected by the pinned .NET SDK' }
        }

        return $component
    }
    finally {
        $zip.Dispose()
    }
}

function Write-PortableSbom {
    param([string]$Directory, [object[]]$Assets, [string]$RestoreAssetsPath, [string]$Version, $RuntimeFiles)

    $restore = Get-Content -LiteralPath $RestoreAssetsPath -Raw | ConvertFrom-Json
    $noticeDirectory = Join-Path $Directory 'licenses/dotnet'
    [IO.Directory]::CreateDirectory($noticeDirectory) | Out-Null
    $components = [Collections.Generic.List[object]]::new()
    $desktopPackages = @($Assets | Where-Object Type -In @('package', 'runtimepack') | Group-Object Library)

    foreach ($package in $desktopPackages) {
        $arguments = @{
            Library = $package.Name
            Type = $package.Group[0].Type
            PackageFolders = @($restore.packageFolders.PSObject.Properties.Name)
            NoticeDirectory = $noticeDirectory
        }
        $components.Add((Get-PortableManagedLicense @arguments))
    }

    foreach ($file in @($Assets | Group-Object Name)) {
        $properties = @($file.Group | ForEach-Object { @{ name = 'seamless:owner'; value = $_.Library } })
        $components.Add([ordered]@{
            type = 'file'
            'bom-ref' = "file:$($file.Name)"
            name = $file.Name
            hashes = @(@{ alg = 'SHA-256'; content = (Get-PortableHash (Join-Path $Directory $file.Name)) })
            properties = $properties
        })
    }

    foreach ($file in $RuntimeFiles.GetEnumerator()) {
        $name = $file.Key
        $import = $portableCatalog.Imports.PSObject.Properties[$name]
        $properties = @()

        if ($null -ne $import) {
            $properties += @{ name = 'seamless:reviewed-archive'; value = $import.Value.Archive }
        }

        $components.Add([ordered]@{
            type = 'file'
            'bom-ref' = "file:runtime/$name"
            name = "runtime/$name"
            hashes = @(@{ alg = 'SHA-256'; content = $file.Value })
            properties = $properties
        })
    }

    Write-PortableJson -Path (Join-Path $Directory 'sbom.json') -Value ([ordered]@{
        '$schema' = 'http://cyclonedx.org/schema/bom-1.6.schema.json'
        bomFormat = 'CycloneDX'
        specVersion = '1.6'
        version = 1
        metadata = @{ component = @{ type = 'application'; name = 'scrcpy Seamless'; version = $Version } }
        components = $components.ToArray()
    })
}
