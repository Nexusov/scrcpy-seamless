Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts/server-toolchain.ps1')
$pins = Get-Content -LiteralPath (Join-Path $root 'scripts/server-toolchain.json') -Raw | ConvertFrom-Json
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy server tools ' + [guid]::NewGuid().ToString('N'))
$originalJava = $env:JAVA_HOME_17_X64
$originalAndroid = $env:ANDROID_HOME

function Assert-ToolchainFailure {
    param([string]$Message)

    try {
        Get-ServerToolchainEnvironment -Toolchain $pins -ToolDirectory $scratch -UseProvisionedToolchain
    }
    catch {
        if ($_.Exception.Message -notmatch $Message) {
            throw
        }
        return
    }

    throw "Invalid provisioned server toolchain accepted. Expected '$Message'."
}

try {
    $env:JAVA_HOME_17_X64 = Join-Path $scratch 'image JDK'
    $env:ANDROID_HOME = Join-Path $scratch 'image SDK'
    $jdk = $env:JAVA_HOME_17_X64
    $sdk = $env:ANDROID_HOME
    $platform = ($pins.AndroidPlatform.Package -split ';', 2)[1]
    $build = ($pins.AndroidBuildTools.Package -split ';', 2)[1]
    $jdkMetadata = Join-Path $jdk 'release'
    $platformMetadata = Join-Path $sdk "platforms/$platform/source.properties"
    $buildMetadata = Join-Path $sdk "build-tools/$build/source.properties"
    $platformToolsMetadata = Join-Path $sdk 'platform-tools/source.properties'

    foreach ($file in @((Join-Path $jdk 'bin/java.exe'), (Join-Path $jdk 'bin/javac.exe'),
        (Join-Path $sdk "platforms/$platform/android.jar"), (Join-Path $sdk "build-tools/$build/aapt2.exe"),
        (Join-Path $sdk 'platform-tools/adb.exe'), $jdkMetadata, $platformMetadata, $buildMetadata, $platformToolsMetadata)) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
        [IO.File]::WriteAllText($file, 'synthetic fixture')
    }

    $majorCompatible = @('JAVA_RUNTIME_VERSION="17.0.99+1"', 'OS_ARCH="x86_64"')
    Set-Content -LiteralPath $jdkMetadata -Value $majorCompatible
    Set-Content -LiteralPath $platformMetadata -Value @("Pkg.Revision=$($pins.AndroidPlatform.Revision)",
        ('AndroidVersion.ApiLevel=' + $platform.Substring('android-'.Length)))
    Set-Content -LiteralPath $buildMetadata -Value "Pkg.Revision=$($pins.AndroidBuildTools.Revision)"
    Set-Content -LiteralPath $platformToolsMetadata -Value "Pkg.Revision=$($pins.AndroidPlatformTools.Revision)"
    $environment = Get-ServerToolchainEnvironment -Toolchain $pins -ToolDirectory $scratch -UseProvisionedToolchain

    if ($environment.JavaDirectory -cne $jdk -or $environment.SdkDirectory -cne $sdk -or
        $environment.Evidence.Origin -cne 'provisioned' -or $environment.Evidence.JavaRuntimeVersion -cne '17.0.99+1') {
        throw 'Provisioned toolchain did not retain its validated paths and actual JDK version.'
    }

    Set-Content -LiteralPath $jdkMetadata -Value @('JAVA_RUNTIME_VERSION="21.0.99+1"', 'OS_ARCH="x86_64"')
    Assert-ToolchainFailure 'Windows x64 JDK 17'
    Set-Content -LiteralPath $jdkMetadata -Value $majorCompatible

    Set-Content -LiteralPath $buildMetadata -Value 'Pkg.Revision=99.0.0'
    Assert-ToolchainFailure 'differs from pinned'
    Set-Content -LiteralPath $buildMetadata -Value "Pkg.Revision=$($pins.AndroidBuildTools.Revision)"

    [IO.File]::Delete((Join-Path $sdk "platforms/$platform/android.jar"))
    Assert-ToolchainFailure 'Server toolchain file is missing'

    Remove-Item Env:ANDROID_HOME
    Assert-ToolchainFailure 'require JAVA_HOME_17_X64 and ANDROID_HOME'
    Write-Host 'Provisioned Android toolchain selection and rejection checks passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('JAVA_HOME_17_X64', $originalJava, 'Process')
    [Environment]::SetEnvironmentVariable('ANDROID_HOME', $originalAndroid, 'Process')
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + '\'

    if (-not [IO.Path]::GetFullPath($scratch).StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Toolchain fixture escaped the temporary directory.'
    }

    [IO.Directory]::Delete($scratch, $true)
}
