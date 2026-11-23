function Get-ServerToolProperty {
    param([string]$Path, [string]$Name)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Server toolchain metadata is missing: $Path"
    }

    $pattern = '^' + [regex]::Escape($Name) + '\s*='
    $lines = @(Get-Content -LiteralPath $Path | Where-Object { $_ -cmatch $pattern })

    if ($lines.Count -ne 1) {
        throw "Missing or ambiguous $Name in $Path"
    }

    return ($lines[0] -split '=', 2)[1].Trim().Trim('"')
}

function Assert-AndroidPackageRevision {
    param([string]$PropertiesPath, [string]$ExpectedRevision)

    $actual = Get-ServerToolProperty -Path $PropertiesPath -Name 'Pkg.Revision'

    if ($actual -cne $ExpectedRevision) {
        throw "Android SDK package revision $actual differs from pinned $ExpectedRevision in $PropertiesPath"
    }
}

function Get-ServerToolchainEnvironment {
    param($Toolchain, [string]$ToolDirectory, [switch]$UseProvisionedToolchain)

    $javaDirectory = Join-Path $ToolDirectory ('jdk/' + $Toolchain.Jdk.ExtractedDirectory)
    $sdkDirectory = Join-Path $ToolDirectory 'sdk'
    $origin = 'pinned-cache'

    if ($UseProvisionedToolchain) {
        if (-not $env:JAVA_HOME_17_X64 -or -not $env:ANDROID_HOME) {
            throw 'Provisioned server tools require JAVA_HOME_17_X64 and ANDROID_HOME.'
        }

        $origin = 'provisioned'
        $javaDirectory = (Resolve-Path -LiteralPath $env:JAVA_HOME_17_X64).ProviderPath
        $sdkDirectory = (Resolve-Path -LiteralPath $env:ANDROID_HOME).ProviderPath
    }

    $platformName = ($Toolchain.AndroidPlatform.Package -split ';', 2)[1]
    $buildToolsName = ($Toolchain.AndroidBuildTools.Package -split ';', 2)[1]
    $jdkRelease = Join-Path $javaDirectory 'release'
    $platformProperties = Join-Path $sdkDirectory "platforms/$platformName/source.properties"
    $buildProperties = Join-Path $sdkDirectory "build-tools/$buildToolsName/source.properties"
    $platformToolsProperties = Join-Path $sdkDirectory 'platform-tools/source.properties'

    foreach ($path in @(
        (Join-Path $javaDirectory 'bin/java.exe'), (Join-Path $javaDirectory 'bin/javac.exe'),
        (Join-Path $sdkDirectory "platforms/$platformName/android.jar"),
        (Join-Path $sdkDirectory "build-tools/$buildToolsName/aapt2.exe"),
        (Join-Path $sdkDirectory 'platform-tools/adb.exe')
    )) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Server toolchain file is missing: $path"
        }
    }

    $javaVersion = Get-ServerToolProperty -Path $jdkRelease -Name 'JAVA_RUNTIME_VERSION'
    $javaArchitecture = Get-ServerToolProperty -Path $jdkRelease -Name 'OS_ARCH'

    if ($javaVersion -cnotmatch '^17([.+-]|$)' -or $javaArchitecture -notin @('amd64', 'x86_64')) {
        throw 'Server builds require a Windows x64 JDK 17.'
    }

    if (-not $UseProvisionedToolchain -and $javaVersion -cne $Toolchain.Jdk.Version) {
        throw 'The server JDK runtime version differs from its pinned input.'
    }

    Assert-AndroidPackageRevision -PropertiesPath $platformProperties -ExpectedRevision $Toolchain.AndroidPlatform.Revision
    Assert-AndroidPackageRevision -PropertiesPath $buildProperties -ExpectedRevision $Toolchain.AndroidBuildTools.Revision
    Assert-AndroidPackageRevision -PropertiesPath $platformToolsProperties -ExpectedRevision $Toolchain.AndroidPlatformTools.Revision

    $api = Get-ServerToolProperty -Path $platformProperties -Name 'AndroidVersion.ApiLevel'

    if ("android-$api" -cne $platformName) {
        throw 'Android SDK platform metadata does not match the selected API level.'
    }

    $evidence = [ordered]@{
        Origin = $origin
        JavaRuntimeVersion = $javaVersion
        JdkReleaseSha256 = (Get-FileHash -LiteralPath $jdkRelease -Algorithm SHA256).Hash.ToLowerInvariant()
        AndroidPlatform = $platformName
        AndroidPlatformRevision = $Toolchain.AndroidPlatform.Revision
        AndroidBuildToolsRevision = $Toolchain.AndroidBuildTools.Revision
        AndroidPlatformToolsRevision = $Toolchain.AndroidPlatformTools.Revision
    }

    return @{ JavaDirectory = $javaDirectory; SdkDirectory = $sdkDirectory; Evidence = $evidence }
}
