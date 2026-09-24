$ErrorActionPreference = 'Stop'
$repositoryDirectory = Split-Path -Parent $PSScriptRoot
. (Join-Path $repositoryDirectory 'scripts/provenance.ps1')
$fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-native-provenance-' + [guid]::NewGuid().ToString('N'))
$sourceDirectory = Join-Path $fixtureDirectory 'src/scrcpy'

try {
    New-Item -ItemType Directory -Path (Join-Path $sourceDirectory 'server') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $sourceDirectory '.gitignore') -Value ".gradle/`nlocal.properties" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'server/.gitignore') -Value "/build`n.gradle/" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'client.c') -Value 'int client(void) { return 1; }' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'server/Server.java') -Value 'class Server {}' -Encoding UTF8
    & git -C $fixtureDirectory init --quiet

    if ($LASTEXITCODE) {
        throw 'Could not initialize the provenance Git fixture.'
    }

    & git -C $fixtureDirectory -c core.autocrlf=false add -- src/scrcpy

    if ($LASTEXITCODE) {
        throw 'Could not stage the provenance fixture sources.'
    }

    $originalFingerprint = Get-NativeSourceFingerprint -RepositoryDirectory $fixtureDirectory
    New-Item -ItemType Directory -Path (Join-Path $sourceDirectory 'server/build/generated') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sourceDirectory '.gradle/cache') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'server/build/generated/Server.class') -Value 'generated' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory '.gradle/cache/state.bin') -Value 'generated' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'local.properties') -Value 'sdk.dir=fixture' -Encoding UTF8

    if ((Get-NativeSourceFingerprint -RepositoryDirectory $fixtureDirectory) -ne $originalFingerprint) {
        throw 'Ignored Gradle outputs changed the native source fingerprint.'
    }

    Set-Content -LiteralPath (Join-Path $sourceDirectory 'server/Server.java') -Value 'class Server { int version = 2; }' -Encoding UTF8

    if ((Get-NativeSourceFingerprint -RepositoryDirectory $fixtureDirectory) -eq $originalFingerprint) {
        throw 'Tracked server source change did not affect the native source fingerprint.'
    }

    Set-Content -LiteralPath (Join-Path $sourceDirectory 'server/Server.java') -Value 'class Server {}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $sourceDirectory 'new-client.c') -Value 'int added(void) { return 1; }' -Encoding UTF8

    if ((Get-NativeSourceFingerprint -RepositoryDirectory $fixtureDirectory) -eq $originalFingerprint) {
        throw 'Nonignored untracked source did not affect the native source fingerprint.'
    }

    Write-Output 'PASS: ignored Gradle outputs are excluded and source edits remain visible.'
} finally {
    $resolvedDirectory = (Resolve-Path -LiteralPath $fixtureDirectory).ProviderPath
    $expectedParent = [IO.Path]::GetTempPath().TrimEnd('\')

    if ((Split-Path -Parent $resolvedDirectory) -ne $expectedParent) {
        throw 'Unexpected provenance fixture path; cleanup cancelled.'
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
