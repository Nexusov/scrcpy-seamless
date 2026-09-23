$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-publish-' + [guid]::NewGuid().ToString('N'))
$checkout = Join-Path $temporaryDirectory 'checkout'
$remote = Join-Path $temporaryDirectory 'remote.git'
$null = New-Item -ItemType Directory -Path $checkout -Force

# Execute fixture Git commands with explicit failures.
function Invoke-TestGit { param([string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    $result = & git -C $checkout @Arguments 2>&1
    if ($LASTEXITCODE) { throw ($result -join "`n") }
    return $result
}

# Assert publication effects against a disposable local bare repository.
function Assert-Publish { param($Condition, $Message)
    if (-not $Condition) { throw $Message }
}

# Capture expected script failure without stopping the negative test.
function Invoke-TestPublish {
    $ErrorActionPreference = 'Continue'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $checkout 'scripts/publish.ps1') -CommitMessage 'Fixture update' 2>&1
    return @{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Select a new synthetic release label without changing the real repository manifest.
function Set-TestRelease {
    param([string]$Version)
    $manifestPath = Join-Path $checkout 'release-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.Release = $Version
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
try {
    & git init --bare $remote *> $null
    $null = Invoke-TestGit @('init', '-b', 'main')
    $null = Invoke-TestGit @('config', 'user.name', 'Publication test')
    $null = Invoke-TestGit @('config', 'user.email', 'test@example.invalid')
    foreach ($directory in @('scripts', 'src', 'licenses', '.github')) { $null = New-Item -ItemType Directory -Path (Join-Path $checkout $directory) }
    Copy-Item (Join-Path $repositoryDirectory 'scripts/publish.ps1') (Join-Path $checkout 'scripts')
    Copy-Item (Join-Path $repositoryDirectory 'release-manifest.json') $checkout
    Set-Content (Join-Path $checkout 'README.md') 'fixture'
    $null = Invoke-TestGit @('add', '.')
    $null = Invoke-TestGit @('commit', '-m', 'Initial fixture')
    $null = Invoke-TestGit @('remote', 'add', 'origin', $remote)
    $null = Invoke-TestGit @('push', '-u', 'origin', 'main')
    # Phase 1 regression: force-staged mirroring preferences are never public source.
    $initialRemoteMain = Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')
    Set-Content (Join-Path $checkout 'src/scrcpy-settings.json') '{"private":"fixture"}'
    $null = Invoke-TestGit @('add', '-f', 'src/scrcpy-settings.json')
    $privacyResult = Invoke-TestPublish
    Assert-Publish ($privacyResult.ExitCode -ne 0) 'Force-staged mirroring preferences were published.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')) -eq $initialRemoteMain) 'Remote main changed despite private settings.'
    $null = Invoke-TestGit @('reset', 'HEAD', '--', 'src/scrcpy-settings.json')
    Remove-Item -LiteralPath (Join-Path $checkout 'src/scrcpy-settings.json')
    $null = Invoke-TestGit @('tag', 'v1.0.0')
    $null = Invoke-TestGit @('push', 'origin', 'v1.0.0')
    foreach ($path in @('src/client.c', 'licenses/test.txt', '.github/ci.yml', 'CONTRIBUTING.md')) { Set-Content (Join-Path $checkout $path) 'public change' }
    # Phase 1 regression: an already published tag must reject the entire update.
    $originalHead = Invoke-TestGit @('rev-parse', 'HEAD')
    $originalRemoteMain = Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')
    $originalRemoteTag = Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.0')
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -ne 0) 'Publisher reused an existing release tag.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')) -eq $originalRemoteMain) 'Remote main changed despite existing tag.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.0')) -eq $originalRemoteTag) 'Published tag changed.'
    Assert-Publish ((Invoke-TestGit @('rev-parse', 'HEAD')) -eq $originalHead) 'Publisher committed before checking tag availability.'

    # A fresh version publishes the branch and new tag together without force.
    Set-TestRelease '1.0.1'
    Set-Content (Join-Path $checkout 'AGENTS.md') 'public agent guide'
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -eq 0) ($result.Output -join "`n")
    $head = Invoke-TestGit @('rev-parse', 'HEAD')
    $tag = Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.1')
    Assert-Publish ($tag -match $head) 'New release tag did not point to the published commit.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')) -match $head) 'Remote main did not reach published commit.'
    Assert-Publish ((Invoke-TestGit @('ls-files', 'AGENTS.md')) -eq 'AGENTS.md') 'Root agent guide was not included in public source.'
    Assert-Publish ((Invoke-TestGit @('log', '-1', '--format=%s')) -eq 'Fixture update') 'Commit message parameter ignored.'
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -ne 0 -and (Invoke-TestGit @('rev-parse', 'HEAD')) -eq $head) 'Repeated release label was accepted.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.1')) -eq $tag) 'Repeated publish changed an existing tag.'

    Set-TestRelease '1.0.2'
    Set-Content (Join-Path $checkout 'personal.txt') 'private'
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -ne 0) 'Unaccounted file was silently omitted.'
    Remove-Item -LiteralPath (Join-Path $checkout 'personal.txt')
    Set-Content (Join-Path $checkout 'src/phone.json') 'private'
    $null = Invoke-TestGit @('add', '-f', 'src/phone.json')
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -ne 0) 'Forced-staged private settings were published.'
    $null = Invoke-TestGit @('reset', 'HEAD', '--', 'src/phone.json')
    Remove-Item -LiteralPath (Join-Path $checkout 'src/phone.json')

    # A tag-only remote rejection must roll back the branch update atomically.
    $hook = "#!/bin/sh`nwhile read old new ref; do`n  case `$ref in refs/tags/v1.0.2) exit 1;; esac`ndone`nexit 0`n"
    [IO.File]::WriteAllText((Join-Path $remote 'hooks/pre-receive'), $hook, [Text.UTF8Encoding]::new($false))
    Add-Content (Join-Path $checkout 'README.md') 'another change'
    $result = Invoke-TestPublish
    Assert-Publish ($result.ExitCode -ne 0) 'Rejected remote push was reported successful.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/heads/main')) -match $head) 'Remote main advanced after tag rejection.'
    Assert-Publish (-not (Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.2'))) 'Rejected new tag appeared remotely.'
    Assert-Publish ((Invoke-TestGit @('ls-remote', '--refs', 'origin', 'refs/tags/v1.0.1')) -eq $tag) 'Existing tag changed after rejected push.'
    Write-Output 'PASS: existing tags immutable, new atomic publication, public paths, privacy and rejected-tag rollback.'
} finally {
    $resolvedDirectory = (Resolve-Path -LiteralPath $temporaryDirectory).ProviderPath
    if ((Split-Path -Parent $resolvedDirectory) -ne [IO.Path]::GetTempPath().TrimEnd('\')) { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
