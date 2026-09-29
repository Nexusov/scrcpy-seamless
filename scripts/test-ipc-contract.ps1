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
$cConformanceFrames = Join-Path $output 'c-conformance-frames'
$managedConformanceFrames = Join-Path $output 'managed-conformance-frames'
$conformanceRoundTrip = Join-Path $output 'c-normalized-managed-conformance'
$binary = Join-Path $output 'test-ipc-contract.exe'
$golden = Join-Path $repository 'spec\desktop-native\golden'
$conformance = Join-Path $repository 'spec\desktop-native\conformance.tsv'
$originalPath = $env:PATH
$originalCFrames = $env:SC_IPC_C_FRAMES
$originalManagedFrames = $env:SC_IPC_MANAGED_FRAMES
$originalCConformanceFrames = $env:SC_IPC_C_CONFORMANCE_FRAMES
$originalManagedConformanceFrames = $env:SC_IPC_MANAGED_CONFORMANCE_FRAMES

New-Item -ItemType Directory -Force -Path $cFrames, $managedFrames, $roundTrip,
    $cConformanceFrames, $managedConformanceFrames, $conformanceRoundTrip |
    Out-Null
foreach ($frameDirectory in @($cFrames, $managedFrames, $roundTrip,
        $cConformanceFrames, $managedConformanceFrames, $conformanceRoundTrip)) {
    Get-ChildItem -LiteralPath $frameDirectory -Filter '*.frame' -File |
        Remove-Item -Force
}

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
        '-I', (Join-Path $repository 'src\scrcpy\app\src'),
        '-I', (Join-Path $repository 'src\scrcpy\app\vendor\yyjson')
    ) + $sources + @('-o', $binary)
    & $compiler @arguments
    if ($LASTEXITCODE) { throw "C contract harness compilation failed: $LASTEXITCODE" }

    & $binary '--golden' $golden $cFrames $cConformanceFrames
    if ($LASTEXITCODE) { throw "C golden contract tests failed: $LASTEXITCODE" }

    $env:SC_IPC_C_FRAMES = $cFrames
    $env:SC_IPC_MANAGED_FRAMES = $managedFrames
    $env:SC_IPC_C_CONFORMANCE_FRAMES = $cConformanceFrames
    $env:SC_IPC_MANAGED_CONFORMANCE_FRAMES = $managedConformanceFrames
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
    $expectedConformance = @(
        Get-Content -LiteralPath $conformance -Encoding UTF8 |
            Where-Object { $_.StartsWith("accept`t", [StringComparison]::Ordinal) }
    ).Count
    $conformanceFrames = @(Get-ChildItem -LiteralPath $managedConformanceFrames -Filter '*.frame')
    if ($expectedConformance -lt 1 -or $conformanceFrames.Count -ne $expectedConformance) {
        throw "Expected $expectedConformance managed conformance frames, found $($conformanceFrames.Count)."
    }
    if (@(Get-ChildItem -LiteralPath $cConformanceFrames -Filter '*.frame').Count -ne $expectedConformance) {
        throw 'Native conformance frame count differs from the shared accepted cases.'
    }
    foreach ($frame in $conformanceFrames) {
        $normalized = Join-Path $conformanceRoundTrip $frame.Name
        & $binary '--normalize' $frame.FullName $normalized
        if ($LASTEXITCODE) { throw "C could not decode managed conformance frame $($frame.Name)." }
        $expected = (Get-FileHash -LiteralPath (Join-Path $cConformanceFrames $frame.Name) -Algorithm SHA256).Hash
        $actual = (Get-FileHash -LiteralPath $normalized -Algorithm SHA256).Hash
        if ($actual -ne $expected) {
            throw "Cross-language semantic normalization differs for $($frame.Name)."
        }
    }
    Write-Host "IPC contract passed: 9 canonical vectors and $expectedConformance conformance frames, C -> C# and C# -> C."
}
finally {
    $env:PATH = $originalPath
    $env:SC_IPC_C_FRAMES = $originalCFrames
    $env:SC_IPC_MANAGED_FRAMES = $originalManagedFrames
    $env:SC_IPC_C_CONFORMANCE_FRAMES = $originalCConformanceFrames
    $env:SC_IPC_MANAGED_CONFORMANCE_FRAMES = $originalManagedConformanceFrames
}
