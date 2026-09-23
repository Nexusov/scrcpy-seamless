$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-docs-check-' + [guid]::NewGuid().ToString('N'))
$fixtureDirectory = Join-Path $temporaryDirectory 'fixture'
$fixtureScript = Join-Path $repositoryDirectory 'scripts/docs-check.ps1'

# Run DocsCheck against a disposable Git repository.
function Invoke-FixtureDocsCheck {
    $ErrorActionPreference = 'Continue'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixtureScript -RepositoryDirectory $fixtureDirectory 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

# Fail with the complete DocsCheck output when an expectation is violated.
function Assert-DocsCheck {
    param([bool]$Condition, [string]$Message, [hashtable]$Result)

    if (-not $Condition) {
        throw "$Message`n$($Result.Output)"
    }
}

try {
    $null = New-Item -ItemType Directory -Path (Join-Path $fixtureDirectory 'docs') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixtureDirectory 'assets') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fixtureDirectory 'src/scrcpy/doc') -Force
    & git -C $fixtureDirectory init -b main *> $null
    & git -C $fixtureDirectory config user.name 'DocsCheck test'
    & git -C $fixtureDirectory config user.email 'test@example.invalid'
    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'README.md'), "# Intro`n## Repeated`n## Repeated`n")
    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'assets/icon.svg'), '<svg/>')
    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'src/scrcpy/README.md'), "# Prerequisites`n")
    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'src/scrcpy/doc/upstream.md'), '[upstream](/README.md#prerequisites)')
    $guide = Join-Path $fixtureDirectory 'docs/guide.md'
    [IO.File]::WriteAllText($guide, @'
# Guide

[root](../README.md#intro) [duplicate](../README.md#repeated-1) [self](#guide)
![icon](../assets/icon.svg)
<img src="../assets/icon.svg" alt="fixture" />
[reference][intro]
[intro]: ../README.md#intro
[external](https://example.invalid/missing)
`[ignored](missing.md)`

```markdown
[example](missing.md)
```
'@)
    & git -C $fixtureDirectory add -- README.md docs/guide.md assets/icon.svg src/scrcpy/README.md src/scrcpy/doc/upstream.md
    & git -C $fixtureDirectory commit -m 'DocsCheck fixture' *> $null

    $result = Invoke-FixtureDocsCheck
    Assert-DocsCheck ($result.ExitCode -eq 0) 'Valid local links failed.' $result

    [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'untracked.md'), '[ignored](missing.md)')
    $result = Invoke-FixtureDocsCheck
    Assert-DocsCheck ($result.ExitCode -eq 0) 'Untracked Markdown was scanned.' $result

    Add-Content -LiteralPath $guide -Value "`n[missing](../missing.md)"
    $result = Invoke-FixtureDocsCheck
    Assert-DocsCheck ($result.ExitCode -ne 0 -and $result.Output.Contains('missing target')) 'Missing target was accepted.' $result

    & git -C $fixtureDirectory checkout -- docs/guide.md
    Add-Content -LiteralPath $guide -Value "`n[missing heading](../README.md#absent)"
    $result = Invoke-FixtureDocsCheck
    Assert-DocsCheck ($result.ExitCode -ne 0 -and $result.Output.Contains('missing Markdown anchor')) 'Missing anchor was accepted.' $result

    & git -C $fixtureDirectory checkout -- docs/guide.md
    Add-Content -LiteralPath $guide -Value "`n[generated](../untracked.md)"
    $result = Invoke-FixtureDocsCheck
    Assert-DocsCheck ($result.ExitCode -ne 0 -and $result.Output.Contains('target is not tracked')) 'Untracked target was accepted.' $result

    Write-Output 'PASS: tracked files, local paths, anchors, references, fenced examples and external URLs.'
} finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        $resolvedDirectory = (Resolve-Path -LiteralPath $temporaryDirectory).ProviderPath

        if ((Split-Path -Parent $resolvedDirectory) -ne [IO.Path]::GetTempPath().TrimEnd('\')) {
            throw 'Unexpected DocsCheck fixture cleanup path.'
        }

        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
}
