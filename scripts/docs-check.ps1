[CmdletBinding()]
param([string]$RepositoryDirectory)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryDirectory) {
    $RepositoryDirectory = Split-Path -Parent $PSScriptRoot
}

$repository = (Resolve-Path -LiteralPath $RepositoryDirectory).ProviderPath

# Return a GitHub-style heading identifier for a Markdown heading.
function Get-HeadingSlug {
    param([string]$Heading)

    $text = $Heading -replace '<[^>]+>', ''
    $text = $text -replace '\[([^]]+)\]\([^)]*\)', '$1'
    $text = $text -replace '[`*_~]', ''
    $text = $text.ToLowerInvariant()
    $text = $text -replace '[^\p{L}\p{N}\p{M}_\-\s]', ''
    return ($text.Trim() -replace '\s+', '-')
}

# Remove fenced code so examples do not become documentation links or headings.
function Get-MarkdownLines {
    param([string]$Path)

    $insideFence = $false
    $fenceCharacter = ''
    $fenceLength = 0
    $result = New-Object 'System.Collections.Generic.List[string]'

    foreach ($line in [IO.File]::ReadAllLines($Path)) {
        $fence = [regex]::Match($line, '^ {0,3}(`{3,}|~{3,})')

        if ($fence.Success) {
            $marker = $fence.Groups[1].Value

            if (-not $insideFence) {
                $insideFence = $true
                $fenceCharacter = $marker[0]
                $fenceLength = $marker.Length
            } elseif ($marker[0] -eq $fenceCharacter -and $marker.Length -ge $fenceLength) {
                $insideFence = $false
            }

            $result.Add('')
            continue
        }

        if ($insideFence) {
            $result.Add('')
            continue
        }

        $result.Add($line)
    }

    return ,$result.ToArray()
}

# Collect heading identifiers, including GitHub's suffix for duplicate headings.
function Get-HeadingAnchors {
    param([string[]]$Lines)

    $anchors = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $occurrences = @{}

    foreach ($line in $Lines) {
        $heading = [regex]::Match($line, '^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$')

        if (-not $heading.Success) {
            continue
        }

        $slug = Get-HeadingSlug -Heading $heading.Groups[1].Value

        if (-not $slug) {
            continue
        }

        $count = 0

        if ($occurrences.ContainsKey($slug)) {
            $count = $occurrences[$slug]
        }

        $occurrences[$slug] = $count + 1
        $anchor = $slug

        if ($count) {
            $anchor = "$slug-$count"
        }

        $null = $anchors.Add($anchor)
    }

    return $anchors
}

# Validate a local Markdown destination without accessing the network.
function Test-LocalLink {
    param(
        [string]$Source,
        [int]$LineNumber,
        [string]$Destination,
        [System.Collections.Generic.HashSet[string]]$TrackedPaths,
        [hashtable]$MarkdownCache,
        [System.Collections.Generic.List[string]]$Errors
    )

    $destination = $Destination.Trim('<', '>')

    if (-not $destination -or $destination -match '^[a-z][a-z0-9+.-]*:' -or $destination.StartsWith('//')) {
        return
    }

    $parts = $destination -split '#', 2
    $pathPart = [Uri]::UnescapeDataString(($parts[0] -split '\?', 2)[0])
    $anchor = ''

    if ($parts.Count -gt 1) {
        $anchor = [Uri]::UnescapeDataString($parts[1])
    }

    $sourcePath = Join-Path $repository $Source
    $sourceDirectory = Split-Path -Parent $sourcePath
    $target = $sourcePath

    if ($pathPart) {
        $target = Join-Path $sourceDirectory $pathPart
    }

    if ($pathPart.StartsWith('/')) {
        $linkRoot = $repository

        # Imported upstream docs retain root-relative links to their own source tree.
        if ($Source -like 'src/scrcpy/*') {
            $linkRoot = Join-Path $repository 'src/scrcpy'
        }

        $target = Join-Path $linkRoot $pathPart.TrimStart('/')
    }

    $target = [IO.Path]::GetFullPath($target)

    if (-not ($target -eq $repository -or $target.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        $Errors.Add("${Source}:${LineNumber}: link leaves repository: $Destination")
        return
    }

    $relativeTarget = $target.Substring($repository.Length).TrimStart('\', '/') -replace '\\', '/'

    if (-not (Test-Path -LiteralPath $target)) {
        $Errors.Add("${Source}:${LineNumber}: missing target: $Destination")
        return
    }

    if ((Test-Path -LiteralPath $target -PathType Leaf) -and -not $TrackedPaths.Contains($relativeTarget)) {
        $Errors.Add("${Source}:${LineNumber}: target is not tracked: $Destination")
        return
    }

    if (-not $anchor -or -not $target.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if (-not $MarkdownCache.ContainsKey($target)) {
        $MarkdownCache[$target] = Get-HeadingAnchors -Lines (Get-MarkdownLines -Path $target)
    }

    if (-not $MarkdownCache[$target].Contains($anchor)) {
        $Errors.Add("${Source}:${LineNumber}: missing Markdown anchor: $Destination")
    }
}

$trackedFiles = @(& git -C $repository -c core.quotepath=false ls-files)

if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked repository files.'
}

$trackedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

foreach ($trackedFile in $trackedFiles) {
    $null = $trackedPaths.Add(($trackedFile -replace '\\', '/'))
}

$markdownFiles = @($trackedFiles | Where-Object { $_ -match '\.md$' } | Sort-Object)
$markdownCache = @{}
$errors = New-Object 'System.Collections.Generic.List[string]'
$linkCount = 0

foreach ($source in $markdownFiles) {
    $lineNumber = 0

    foreach ($line in (Get-MarkdownLines -Path (Join-Path $repository $source))) {
        $lineNumber++
        $withoutInlineCode = $line -replace '(`+)[^`]*\1', ''
        $destinations = New-Object 'System.Collections.Generic.List[string]'

        foreach ($match in [regex]::Matches($withoutInlineCode, '!?(?<!\\)\[[^]]+\]\((?<target><[^>]+>|[^)\s]+)(?:\s+["''][^"'']*["''])?\)')) {
            $destinations.Add($match.Groups['target'].Value)
        }

        foreach ($match in [regex]::Matches($withoutInlineCode, '<(?:a|img)\b[^>]*\b(?:href|src)\s*=\s*["''](?<target>[^"'']+)["'']', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $destinations.Add($match.Groups['target'].Value)
        }

        $definition = [regex]::Match($withoutInlineCode, '^ {0,3}\[[^]]+\]:\s*(?<target><[^>]+>|\S+)')

        if ($definition.Success) {
            $destinations.Add($definition.Groups['target'].Value)
        }

        foreach ($destination in $destinations) {
            $linkCount++
            Test-LocalLink -Source $source -LineNumber $lineNumber -Destination $destination -TrackedPaths $trackedPaths -MarkdownCache $markdownCache -Errors $errors
        }
    }
}

foreach ($errorMessage in $errors) {
    Write-Host $errorMessage
}

if ($errors.Count) {
    Write-Host "DocsCheck failed: $($errors.Count) invalid links in $($markdownFiles.Count) tracked Markdown files."
    exit 1
}

Write-Host "DocsCheck passed: $linkCount links in $($markdownFiles.Count) tracked Markdown files."
