[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConfigPath,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][string]$NativeBuildDirectory,
    [string]$ToolDirectory,
    [string]$GradleUserHome,
    [string]$NativeSourceRoot,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$configuration = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server-toolchain.json') -Raw | ConvertFrom-Json
$release = Get-Content -LiteralPath (Join-Path $repository 'release-manifest.json') -Raw | ConvertFrom-Json

if (-not $NativeSourceRoot) {
    $NativeSourceRoot = $repository
}
$nativeRoot = (Resolve-Path -LiteralPath $NativeSourceRoot).ProviderPath
$nativeSources = Join-Path $nativeRoot 'src\scrcpy\app\src'
$ownNativeSources = Join-Path $repository 'src\scrcpy\app\src'
$nativeConfig = Join-Path (Resolve-Path -LiteralPath $NativeBuildDirectory).ProviderPath 'app\config.h'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).ProviderPath
$compiler = (Resolve-Path -LiteralPath $configuration.compiler).ProviderPath
$pkgConfig = (Resolve-Path -LiteralPath $configuration.pkgConfig).ProviderPath

if (-not $ToolDirectory) {
    $ToolDirectory = Join-Path $repository 'work\phase2\server'
}
$tools = (Resolve-Path -LiteralPath $ToolDirectory).ProviderPath
$javaHome = Join-Path $tools ('jdk\' + $toolchain.Jdk.ExtractedDirectory)
$androidHome = Join-Path $tools 'sdk'

if (-not $GradleUserHome) {
    $GradleUserHome = Join-Path $tools 'gradle-home'
}

if (-not (Test-Path -LiteralPath (Join-Path $javaHome 'bin\java.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $androidHome 'platforms\android-36\android.jar') -PathType Leaf)) {
    throw 'Run the canonical pinned server bootstrap before this check.'
}
$jdkRelease = Get-Content -LiteralPath (Join-Path $javaHome 'release') -Raw

if ($jdkRelease -notmatch ('JAVA_RUNTIME_VERSION="' + [regex]::Escape($toolchain.Jdk.Version) + '"')) {
    throw 'The server JDK runtime version differs from its pinned input.'
}
$sdl = Join-Path $runtime 'SDL3.dll'

if ((Get-FileHash -LiteralPath $sdl -Algorithm SHA256).Hash.ToLowerInvariant() -ne $release.RuntimeFiles.'SDL3.dll') {
    throw 'SDL runtime bytes do not match the reviewed manifest.'
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository ('work\device-protocol\' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path -LiteralPath $output) {
    throw 'Use a new evidence directory; existing observations are not overwritten.'
}
New-Item -ItemType Directory -Path $output | Out-Null
$binary = Join-Path $output 'test_device_protocol.exe'
$response = Join-Path $output 'compile.rsp'
$cFrames = Join-Path $output 'c-control.tsv'
$javaFrames = Join-Path $output 'java-device.tsv'
$goldens = Join-Path $repository 'spec\device-protocol\golden'
$manifestPath = Join-Path $output 'evidence.json'
$utf8 = New-Object Text.UTF8Encoding($false)

function Get-InputHashes {
    param([string[]]$Paths)
    $entries = foreach ($path in $Paths) {
        $actual = (Resolve-Path -LiteralPath $path).ProviderPath
        [ordered]@{ Path = $actual; Sha256 = (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    return @($entries)
}

function Invoke-CheckedTool {
    param([string]$Executable, [string[]]$Arguments, [string]$Log)
    $previousPreference = $ErrorActionPreference

    try {
        # PowerShell 5.1 represents redirected native stderr as ErrorRecord text.
        $ErrorActionPreference = 'Continue'
        $captured = @(& $Executable @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $lines = @($captured | ForEach-Object { $_.ToString() })
    [IO.File]::WriteAllLines($Log, [string[]]$lines, $utf8)
    $lines | ForEach-Object { Write-Host $_ }

    if ($code) {
        throw "Device-protocol tool failed with exit code $code; preserved log: $Log"
    }
}

function Quote-ResponsePath {
    param([string]$Path)
    return '"' + $Path.Replace('\', '/').Replace('"', '\"') + '"'
}

$sources = @(
    (Join-Path $repository 'src\scrcpy\app\tests\test_device_protocol.c'),
    (Join-Path $nativeSources 'control_msg.c'),
    (Join-Path $nativeSources 'device_msg.c'),
    (Join-Path $nativeSources 'util\str.c'),
    (Join-Path $nativeSources 'util\strbuf.c'),
    (Join-Path $ownNativeSources 'util\utf8.c')
)
$inputPaths = $sources + @(
    (Join-Path $nativeSources 'control_msg.h'), (Join-Path $nativeSources 'device_msg.h'),
    (Join-Path $nativeSources 'util\binary.h'), (Join-Path $nativeSources 'util\str.h'),
    (Join-Path $nativeSources 'util\strbuf.h'), (Join-Path $nativeSources 'common.h'),
    (Join-Path $nativeSources 'compat.h'), (Join-Path $ownNativeSources 'util\utf8.h'),
    $nativeConfig, $compiler, $pkgConfig, $sdl, $ConfigPath,
    (Join-Path $PSScriptRoot 'test-device-protocol.ps1'), (Join-Path $PSScriptRoot 'server-toolchain.json'),
    (Join-Path $repository 'src\scrcpy\server\build.gradle'),
    (Join-Path $repository 'src\scrcpy\buildscript-gradle.lockfile'),
    (Join-Path $repository 'src\scrcpy\server\gradle.lockfile'),
    (Join-Path $repository 'src\scrcpy\gradle\verification-metadata.xml')
) + @(Get-ChildItem -LiteralPath $goldens -File | ForEach-Object { $_.FullName })
$javaDirectory = Join-Path $repository 'src\scrcpy\server\src'

foreach ($relative in @('main\java\com\genymobile\scrcpy\control\ControlMessage.java',
    'main\java\com\genymobile\scrcpy\control\ControlMessageReader.java',
    'main\java\com\genymobile\scrcpy\control\ControlProtocolException.java',
    'main\java\com\genymobile\scrcpy\control\DeviceMessage.java',
    'main\java\com\genymobile\scrcpy\control\DeviceMessageWriter.java',
    'main\java\com\genymobile\scrcpy\control\DeviceProtocolText.java',
    'main\java\com\genymobile\scrcpy\model\Position.java', 'main\java\com\genymobile\scrcpy\model\Point.java',
    'main\java\com\genymobile\scrcpy\model\Size.java', 'main\java\com\genymobile\scrcpy\util\Binary.java',
    'test\java\com\genymobile\scrcpy\control\DeviceProtocolBoundsTest.java',
    'test\java\com\genymobile\scrcpy\control\DeviceProtocolContractTest.java',
    'test\java\com\genymobile\scrcpy\control\DeviceProtocolTextTest.java')) {
    $inputPaths += Join-Path $javaDirectory $relative
}
$inputs = Get-InputHashes -Paths $inputPaths
$mixed = -not [StringComparer]::OrdinalIgnoreCase.Equals($nativeRoot.TrimEnd('\'), $repository.TrimEnd('\'))
$manifest = [ordered]@{
    SchemaVersion = 1
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    HarnessRepository = $repository
    NativeSourceRoot = $nativeRoot
    MixedSourceValidation = $mixed
    Boundary = 'Production control/device codecs and shared text validator only; no device, socket, clipboard or media effects.'
    Inputs = $inputs
    Stage = 'prepared'
    Result = 'running'
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), $utf8)
$environmentNames = @('PATH', 'PKG_CONFIG_PATH', 'JAVA_HOME', 'ANDROID_HOME', 'ANDROID_SDK_ROOT', 'GRADLE_USER_HOME',
    'SC_DEVICE_PROTOCOL_SPEC', 'SC_DEVICE_PROTOCOL_C_CONTROL', 'SC_DEVICE_PROTOCOL_JAVA_DEVICE', 'SC_DEVICE_PROTOCOL_INTEROP_REQUIRED')
$previousEnvironment = @{}

foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    $env:PATH = $configuration.compilerBinDirectory + ';' + $runtime + ';' + (Join-Path $javaHome 'bin') + ';' + $previousEnvironment.PATH
    $env:PKG_CONFIG_PATH = @($configuration.pkgConfigDirectories) -join [IO.Path]::PathSeparator
    $env:JAVA_HOME = $javaHome
    $env:ANDROID_HOME = $androidHome
    $env:ANDROID_SDK_ROOT = $androidHome
    $env:GRADLE_USER_HOME = [IO.Path]::GetFullPath($GradleUserHome)
    $cflags = @(& $pkgConfig '--cflags' 'sdl3' 'libavcodec' 'libavformat' 'libavutil')

    if ($LASTEXITCODE) {
        throw 'Could not resolve the pinned codec/header include flags.'
    }
    $linkFlags = @(& $pkgConfig '--libs' 'sdl3')

    if ($LASTEXITCODE) {
        throw 'Could not resolve the pinned SDL link flags.'
    }
    # GCC response-file syntax preserves pkg-config's escaped spaces without a shell parser.
    $arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-UNDEBUG', '-B',
        (Quote-ResponsePath ($configuration.compilerBinDirectory + '\')), '-I', (Quote-ResponsePath $nativeSources),
        '-I', (Quote-ResponsePath $ownNativeSources), '-I', (Quote-ResponsePath (Split-Path -Parent $nativeConfig)))
    $arguments += $cflags
    $arguments += @($sources | ForEach-Object { Quote-ResponsePath $_ })
    $arguments += $linkFlags
    $arguments += @('-o', (Quote-ResponsePath $binary))
    [IO.File]::WriteAllLines($response, [string[]]$arguments, $utf8)
    $manifest.Stage = 'native-compile'
    Invoke-CheckedTool $compiler @('@' + $response) (Join-Path $output 'native-compile.log')
    $manifest['NativeHarnessSha256'] = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest.Stage = 'native-selftest'
    Invoke-CheckedTool $binary @('--selftest', $goldens) (Join-Path $output 'native-selftest.log')
    Invoke-CheckedTool $binary @('--emit-control', $goldens, $cFrames) (Join-Path $output 'native-emit.log')
    $env:SC_DEVICE_PROTOCOL_SPEC = $goldens
    $env:SC_DEVICE_PROTOCOL_C_CONTROL = $cFrames
    $env:SC_DEVICE_PROTOCOL_JAVA_DEVICE = $javaFrames
    $env:SC_DEVICE_PROTOCOL_INTEROP_REQUIRED = '1'
    $manifest.Stage = 'java-codec-contract'
    Push-Location -LiteralPath (Join-Path $repository 'src\scrcpy')

    try {
        Invoke-CheckedTool (Join-Path $repository 'src\scrcpy\gradlew.bat') @(':server:testDebugUnitTest', '--tests',
            'com.genymobile.scrcpy.control.DeviceProtocol*', '--no-daemon', '--dependency-verification=strict') (Join-Path $output 'java-contract.log')
    }
    finally {
        Pop-Location
    }
    $results = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src\scrcpy\server\build\test-results\testDebugUnitTest') `
        -Filter 'TEST-com.genymobile.scrcpy.control.DeviceProtocol*.xml' -File)

    if ($results.Count -ne 3 -or -not (Test-Path -LiteralPath $javaFrames -PathType Leaf)) {
        throw 'Required Java codec classes or actual emitted frames are missing.'
    }
    $testCount = 0

    foreach ($result in $results) {
        [xml]$xml = Get-Content -LiteralPath $result.FullName -Raw

        if ([int]$xml.testsuite.tests -lt 1 -or [int]$xml.testsuite.failures -or [int]$xml.testsuite.errors -or [int]$xml.testsuite.skipped) {
            throw "Java codec validation was missing, unsuccessful or skipped: $($result.Name)"
        }
        $testCount += [int]$xml.testsuite.tests
        Copy-Item -LiteralPath $result.FullName -Destination (Join-Path $output $result.Name)
    }
    $manifest.Stage = 'consume-actual-java-frames'
    Invoke-CheckedTool $binary @('--consume-device', $goldens, $javaFrames) (Join-Path $output 'native-consume.log')
    $after = Get-InputHashes -Paths $inputPaths

    if (($inputs | ConvertTo-Json -Depth 5 -Compress) -ne ($after | ConvertTo-Json -Depth 5 -Compress)) {
        throw 'A native or fixture input changed while validation was running.'
    }
    $manifest['JavaTests'] = $testCount
    $manifest['ControlFrames'] = 30
    $manifest['DeviceFrames'] = 10
    $manifest.Result = 'passed'
    Write-Host "Device protocol passed: 30 actual C -> Java and 10 actual Java -> C frames; $testCount Java tests; 30 shared UTF-8 cases."

    if ($mixed) {
        Write-Host 'This was an explicitly mixed-source codec check, not a complete isolated or canonical production build.'
    }
}
catch {
    $manifest.Result = 'failed'
    $manifest['Failure'] = $_.Exception.Message
    throw
}
finally {
    $manifest['FinishedUtc'] = [DateTime]::UtcNow.ToString('o')

    try {
        $manifest['InputsAfter'] = Get-InputHashes -Paths $inputPaths
        $manifest['InputsUnchanged'] = ($inputs | ConvertTo-Json -Depth 5 -Compress) -eq
            ($manifest.InputsAfter | ConvertTo-Json -Depth 5 -Compress)
    }
    catch {
        $manifest['InputObservationFailure'] = $_.Exception.Message
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), $utf8)

    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}
