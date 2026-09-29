[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$NativeFixturePath,
    [Parameter(Mandatory = $true)][string]$ProductionNativePath,
    [Parameter(Mandatory = $true)][string]$RuntimeDirectory,
    [Parameter(Mandatory = $true)][string]$DotnetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryDirectory = Split-Path -Parent $PSScriptRoot
$nativeFixture = (Resolve-Path -LiteralPath $NativeFixturePath -ErrorAction Stop).ProviderPath
$productionNative = (Resolve-Path -LiteralPath $ProductionNativePath -ErrorAction Stop).ProviderPath
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory -ErrorAction Stop).ProviderPath
$dotnet = (Resolve-Path -LiteralPath $DotnetPath -ErrorAction Stop).ProviderPath

if ((Split-Path -Leaf $nativeFixture) -ne 'test_machine_child.exe') {
    throw 'The process check must use the isolated native machine fixture, never a device-enabled client.'
}

if ((Split-Path -Leaf $productionNative) -ne 'scrcpy.exe' -or
    $productionNative -ieq $nativeFixture) {
    throw 'The bootstrap check requires the separately built production scrcpy.exe.'
}

$testProject = Join-Path $repositoryDirectory 'tests\desktop\ScrcpySeamless.IpcProcess.Tests\ScrcpySeamless.IpcProcess.Tests.csproj'
$supervisor = Join-Path $repositoryDirectory 'tests\desktop\ScrcpySeamless.IpcSupervisorFixture\bin\Release\net10.0\ScrcpySeamless.IpcSupervisorFixture.exe'
$scratchDirectory = Join-Path $repositoryDirectory 'work\phase6b\ipc-process-tests'
$temporaryDirectory = Join-Path $scratchDirectory 'temp'
New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

$environmentNames = @('PATH', 'TEMP', 'TMP', 'SCRCPY_IPC_NATIVE_TEST_CHILD',
    'SCRCPY_IPC_PRODUCTION_NATIVE', 'SCRCPY_IPC_SUPERVISOR_FIXTURE',
    'SCRCPY_IPC_TEST_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT')
$originalEnvironment = @{}

foreach ($environmentName in $environmentNames) {
    $originalEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
}

try {
    $env:PATH = $runtime + [IO.Path]::PathSeparator + $env:PATH
    $env:TEMP = $temporaryDirectory
    $env:TMP = $temporaryDirectory
    $env:SCRCPY_IPC_NATIVE_TEST_CHILD = $nativeFixture
    $env:SCRCPY_IPC_PRODUCTION_NATIVE = $productionNative
    $env:SCRCPY_IPC_SUPERVISOR_FIXTURE = $supervisor
    $env:SCRCPY_IPC_TEST_ROOT = $scratchDirectory
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    & $dotnet restore $testProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Locked IPC process-test restore failed.'
    }

    & $dotnet build $testProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'IPC process-test build failed.'
    }

    if (!(Test-Path -LiteralPath $supervisor -PathType Leaf)) {
        throw "The test supervisor was not built: $supervisor"
    }

    & $dotnet test $testProject --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'IPC real-process tests failed.'
    }
}
finally {
    foreach ($environmentName in $environmentNames) {
        [Environment]::SetEnvironmentVariable($environmentName, $originalEnvironment[$environmentName], 'Process')
    }
}
