[CmdletBinding()]
param([string]$CommitMessage = 'Update scrcpy Seamless')
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $projectDirectory 'release-manifest.json') -Raw | ConvertFrom-Json
$releaseTag = 'v' + $manifest.Release

# Run Git against this checkout and stop before subsequent publication steps on failure.
function Invoke-ProjectGit {
    param([string[]]$Arguments)
    & git -C $projectDirectory @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Git failed: $($Arguments -join ' '). Resolve the error above and run this file again."
    }
}

# Admit public project paths while rejecting local settings even when force-staged.
function Test-PublicSourcePath {
    param([string]$Path)
    $privatePath = $Path -match '(^|/)(phone\.json|scrcpy-settings\.json|adbkey(\.pub)?|build\.local\.json)$' -or
        $Path -match '\.(log|pid|lnk)$' -or $Path -match '(^|/)(\.env($|\.)|credentials($|\.))'

    if ($privatePath) {
        return $false
    }
    $publicRootFiles = @('README.md', 'LICENSE', 'THIRD_PARTY.md', 'CONTRIBUTING.md', 'AGENTS.md', 'release-manifest.json', '.gitignore', '.gitattributes')
    return $Path -in $publicRootFiles -or $Path -match '^(launcher|scripts|tests|docs|src|licenses|\.github)/'
}

try {
    $branch = Invoke-ProjectGit -Arguments @('branch', '--show-current')

    if ($branch -ne 'main') {
        throw 'Switch the project to main before publishing.'
    }
    # Phase 1: reject a release label before staging or committing any local changes.
    $tagReference = 'refs/tags/' + $releaseTag
    $localTag = @(Invoke-ProjectGit -Arguments @('tag', '--list', $releaseTag))
    $remoteTag = @(Invoke-ProjectGit -Arguments @('ls-remote', '--refs', 'origin', $tagReference))

    if ($localTag -contains $releaseTag -or $remoteTag.Count) {
        throw "Release tag $releaseTag already exists. Choose a new release version; published tags are immutable."
    }
    $changedPaths = @(
        Invoke-ProjectGit -Arguments @('-c', 'core.quotepath=false', 'diff', '--name-only')
        Invoke-ProjectGit -Arguments @('-c', 'core.quotepath=false', 'diff', '--cached', '--name-only')
        Invoke-ProjectGit -Arguments @('-c', 'core.quotepath=false', 'ls-files', '--others', '--exclude-standard')
    ) | Sort-Object -Unique

    foreach ($path in $changedPaths) {
        if (-not (Test-PublicSourcePath -Path $path)) {
            throw "Unaccounted or private change: $path. Review it explicitly before publishing. No tag was moved."
        }
    }

    if ($changedPaths.Count) {
        Invoke-ProjectGit -Arguments (@('add', '--') + $changedPaths)
        Invoke-ProjectGit -Arguments @('commit', '-m', $CommitMessage)
    }
    $remainingChanges = @(Invoke-ProjectGit -Arguments @('status', '--porcelain', '--untracked-files=all'))

    if ($remainingChanges.Count) {
        throw 'The checkout is not clean after committing. Publication stopped before updating the release tag.'
    }

    # Phase 1: publish the branch and a new tag in one non-forced transaction.
    Invoke-ProjectGit -Arguments @('tag', $releaseTag, 'HEAD')
    Invoke-ProjectGit -Arguments @('push', '--atomic', 'origin', 'main', $tagReference)
    Write-Host "Source code and new tag $releaseTag published. Release ZIP assets must be uploaded separately." -ForegroundColor Green
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
