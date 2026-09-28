param(
    [Parameter(Mandatory)]
    [string] $CompilerPath,
    [Parameter(Mandatory)]
    [string] $DotnetPath,
    [string] $OutputDirectory = '.\work\ipc-contract'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$dotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
$cFrames = Join-Path $output 'c-frames'
$managedFrames = Join-Path $output 'managed-frames'
$roundTrip = Join-Path $output 'c-normalized-managed'
$binary = Join-Path $output 'test-ipc-contract.exe'
$golden = Join-Path $repository 'spec\desktop-native\golden'
$originalPath = $env:PATH
$originalCFrames = $env:SC_IPC_C_FRAMES
$originalManagedFrames = $env:SC_IPC_MANAGED_FRAMES

New-Item -ItemType Directory -Force -Path $cFrames, $managedFrames, $roundTrip |
    Out-Null

try {
    $env:PATH = (Split-Path -Parent $compiler) + ';' + $originalPath
    $sources = @(
        (Join-Path $repository 'src\scrcpy\app\tests\test_ipc_contract.c'),
        (Join-Path $repository 'src\scrcpy\app\src\ipc\ipc_frame.c'),
        (Join-Path $repository 'src\scrcpy\app\src\ipc\ipc_json.c'),
        (Join-Path $repository 'src\scrcpy\app\vendor\yyjson\yyjson.c')
    )
    $arguments = @(
        '-std=c11', '-Wall', '-Wextra', '-Werror',
        '-DYYJSON_READER_DEPTH_LIMIT=2',
        '-I', (Join-Path $repository 'src\scrcpy\app\src'),
        '-I', (Join-Path $repository 'src\scrcpy\app\vendor\yyjson')
    ) + $sources + @('-o', $binary)
    & $compiler @arguments
    if ($LASTEXITCODE) { throw "C contract harness compilation failed: $LASTEXITCODE" }

    & $binary '--golden' $golden $cFrames
    if ($LASTEXITCODE) { throw "C golden contract tests failed: $LASTEXITCODE" }

    $env:SC_IPC_C_FRAMES = $cFrames
    $env:SC_IPC_MANAGED_FRAMES = $managedFrames
    & $dotnet restore (Join-Path $repository 'ScrcpySeamless.slnx') '--locked-mode'
    if ($LASTEXITCODE) { throw "Locked .NET restore failed: $LASTEXITCODE" }
    $testProject = Join-Path $repository 'tests\desktop\ScrcpySeamless.Infrastructure.Tests\ScrcpySeamless.Infrastructure.Tests.csproj'
    & $dotnet test $testProject '--no-restore' '--configuration' 'Release' '--filter-class' '*ProtocolContractTests'
    if ($LASTEXITCODE) { throw "Managed contract tests failed: $LASTEXITCODE" }

    $frames = @(Get-ChildItem -LiteralPath $managedFrames -Filter '*.frame')
    if ($frames.Count -ne 9) { throw "Expected nine managed frames, found $($frames.Count)." }
    foreach ($frame in $frames) {
        $normalized = Join-Path $roundTrip $frame.Name
        & $binary '--normalize' $frame.FullName $normalized
        if ($LASTEXITCODE) { throw "C could not decode managed frame $($frame.Name)." }
        $expected = (Get-FileHash -LiteralPath (Join-Path $cFrames $frame.Name) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath $normalized -Algorithm SHA256).Hash
        if ($actual -ne $expected) {
            throw "Cross-language canonical bytes differ for $($frame.Name)."
        }
    }
    Write-Host 'IPC contract passed: 9 shared vectors, C -> C# and C# -> C.'
}
finally {
    $env:PATH = $originalPath
    $env:SC_IPC_C_FRAMES = $originalCFrames
    $env:SC_IPC_MANAGED_FRAMES = $originalManagedFrames
}
