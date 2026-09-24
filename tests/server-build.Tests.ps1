$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$bootstrapScript = Join-Path $repositoryDirectory 'scripts\bootstrap-server.ps1'
$toolchain = Get-Content -LiteralPath (Join-Path $repositoryDirectory 'scripts\server-toolchain.json') -Raw | ConvertFrom-Json
$fixtureParent = Join-Path $repositoryDirectory 'work\phase2\server-test-fixtures'
$fixtureDirectory = Join-Path $fixtureParent ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixtureDirectory 'downloads') -Force | Out-Null

# Run an intentionally failing child process without PowerShell promoting stderr.
function Invoke-OfflineServerBootstrap {
    param([string]$ScriptPath, [string]$ToolDirectory)

    $originalErrorAction = $ErrorActionPreference

    try {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath -ToolDirectory $ToolDirectory -Offline 2>&1
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
    }
    finally {
        $ErrorActionPreference = $originalErrorAction
    }
}

try {
    $offlineResult = Invoke-OfflineServerBootstrap -ScriptPath $bootstrapScript -ToolDirectory $fixtureDirectory

    if ($offlineResult.ExitCode -eq 0 -or $offlineResult.Output -notmatch 'Required archive is absent from the offline cache') {
        throw 'Offline server bootstrap did not reject missing pinned archives.'
    }

    $corruptArchive = Join-Path (Join-Path $fixtureDirectory 'downloads') $toolchain.Jdk.ArchiveName
    [IO.File]::WriteAllText($corruptArchive, 'synthetic corrupt archive')
    $corruptResult = Invoke-OfflineServerBootstrap -ScriptPath $bootstrapScript -ToolDirectory $fixtureDirectory

    if ($corruptResult.ExitCode -eq 0 -or $corruptResult.Output -notmatch 'Cached archive has the wrong SHA-256') {
        throw 'Server bootstrap did not reject a corrupt cached JDK archive.'
    }

    if (-not (Test-Path -LiteralPath $corruptArchive -PathType Leaf)) {
        throw 'Server bootstrap unexpectedly deleted the corrupt evidence.'
    }

    Write-Output 'PASS: offline bootstrap rejects absent and corrupt pinned archives without fetching or deleting evidence.'
}
finally {
    $resolvedFixture = (Resolve-Path -LiteralPath $fixtureDirectory).ProviderPath
    $resolvedParent = (Resolve-Path -LiteralPath $fixtureParent).ProviderPath

    if ((Split-Path -Parent $resolvedFixture) -ne $resolvedParent) {
        throw 'Unexpected server bootstrap fixture cleanup path.'
    }

    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
