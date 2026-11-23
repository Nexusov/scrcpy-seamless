# Fingerprint tracked and new non-ignored server/build inputs, excluding outputs.
function Get-ServerBuildFingerprint {
    param([string]$RepositoryDirectory)

    $inputPaths = @(
        'src/scrcpy/server', 'src/scrcpy/build.gradle', 'src/scrcpy/settings.gradle',
        'src/scrcpy/gradle.properties', 'src/scrcpy/gradlew.bat', 'src/scrcpy/gradle/wrapper',
        'src/scrcpy/buildscript-gradle.lockfile', 'src/scrcpy/gradle/verification-metadata.xml',
        'src/scrcpy/config', 'scripts/server-toolchain.json',
        'scripts/bootstrap-server.ps1', 'scripts/build-server.ps1',
        'scripts/server-provenance.ps1', 'scripts/server-toolchain.ps1'
    )
    $sourcePaths = @(& git -C $RepositoryDirectory ls-files --cached --others --exclude-standard -- @inputPaths)

    if ($LASTEXITCODE -or -not $sourcePaths.Count) {
        throw 'Could not enumerate Android server source/build inputs.'
    }

    $sortedPaths = [string[]]$sourcePaths
    [Array]::Sort($sortedPaths, [StringComparer]::Ordinal)
    $entries = foreach ($sourcePath in $sortedPaths) {
        $sourceFile = Join-Path $RepositoryDirectory $sourcePath

        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "Android server build input disappeared: $sourcePath"
        }

        $hash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
        "$sourcePath $hash"
    }

    $hasher = [Security.Cryptography.SHA256]::Create()

    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
        return [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}
