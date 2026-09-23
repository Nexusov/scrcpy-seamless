$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../launcher/options-store.ps1')

# Report the observable legacy contract that changed.
function Assert-Baseline {
    param([hashtable]$Assertion)

    if (-not $Assertion.Condition) {
        throw $Assertion.Message
    }
}

# Require the intended validation failure, not an unrelated exception.
function Assert-BaselineRejection {
    param([hashtable]$Expectation)
    $failure = $null

    try {
        & $Expectation.Action
    } catch {
        $failure = $_.Exception.Message
    }

    Assert-Baseline @{ Condition = $failure -ceq $Expectation.Message; Message = "Expected '$($Expectation.Message)', received '$failure'." }
}

$directory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-baseline-' + [guid]::NewGuid().ToString('N'))
$firstRoot = Join-Path $directory 'package-one/app'
$secondRoot = Join-Path $directory 'package-two/app'
$null = New-Item -ItemType Directory -Path $firstRoot, $secondRoot -Force

try {
    # Added Phase 0 coverage: portable roots own independent device and mirroring files.
    $firstPhone = [pscustomobject]@{ UsbSerial = 'fixture-one'; WirelessService = ''; ConnectionMode = 'usb' }
    $secondPhone = [pscustomobject]@{ UsbSerial = 'fixture-two'; WirelessService = '192.0.2.2:5555'; ConnectionMode = 'wifi' }
    $firstSettings = @{ Reconnect = $true; Options = @{ 'max-fps' = '30' } }
    $secondSettings = @{ Reconnect = $false; Options = @{ 'max-fps' = '60' } }
    Save-PhoneConfiguration -RootDirectory $firstRoot -Configuration $firstPhone -ExpectedSnapshot $null
    Save-PhoneConfiguration -RootDirectory $secondRoot -Configuration $secondPhone -ExpectedSnapshot $null
    Save-ScrcpySettings -RootDirectory $firstRoot -Settings $firstSettings -ExpectedSnapshot $null
    Save-ScrcpySettings -RootDirectory $secondRoot -Settings $secondSettings -ExpectedSnapshot $null
    $firstPhoneSnapshot = Get-DeviceConfigurationSnapshot -RootDirectory $firstRoot
    $firstSettingsSnapshot = Get-ScrcpySettingsSnapshot -RootDirectory $firstRoot
    $secondPhoneSnapshot = Get-DeviceConfigurationSnapshot -RootDirectory $secondRoot
    $secondSettingsSnapshot = Get-ScrcpySettingsSnapshot -RootDirectory $secondRoot

    Assert-Baseline @{ Condition = (Get-PhoneConfiguration -RootDirectory $firstRoot).UsbSerial -ceq 'fixture-one'; Message = 'First package loaded another package phone.' }
    Assert-Baseline @{ Condition = (Get-PhoneConfiguration -RootDirectory $secondRoot).UsbSerial -ceq 'fixture-two'; Message = 'Second package loaded another package phone.' }
    Assert-Baseline @{ Condition = (Get-ScrcpySettings -RootDirectory $firstRoot).Options['max-fps'] -ceq '30'; Message = 'First package loaded another package mirroring preferences.' }
    Assert-Baseline @{ Condition = (Get-ScrcpySettings -RootDirectory $secondRoot).Options['max-fps'] -ceq '60'; Message = 'Second package loaded another package mirroring preferences.' }

    $firstPhone.UsbSerial = 'fixture-replacement'
    Save-PhoneConfiguration -RootDirectory $firstRoot -Configuration $firstPhone -ExpectedSnapshot $firstPhoneSnapshot
    Assert-Baseline @{ Condition = (Get-ScrcpySettingsSnapshot -RootDirectory $firstRoot) -ceq $firstSettingsSnapshot; Message = 'Changing the saved phone reset its package mirroring preferences.' }
    $firstSettings.Options['max-fps'] = '24'
    Save-ScrcpySettings -RootDirectory $firstRoot -Settings $firstSettings -ExpectedSnapshot $firstSettingsSnapshot
    Assert-Baseline @{ Condition = (Get-PhoneConfiguration -RootDirectory $firstRoot).UsbSerial -ceq 'fixture-replacement'; Message = 'Changing mirroring preferences replaced the saved phone.' }
    Assert-Baseline @{ Condition = (Get-DeviceConfigurationSnapshot -RootDirectory $secondRoot) -ceq $secondPhoneSnapshot; Message = 'Saving in the first package changed the second package phone file.' }
    Assert-Baseline @{ Condition = (Get-ScrcpySettingsSnapshot -RootDirectory $secondRoot) -ceq $secondSettingsSnapshot; Message = 'Saving in the first package changed the second package mirroring file.' }

    # Added Phase 0 coverage: these are 1.x restrictions, deliberately replaced in Phase 8.
    $legacyReconnectCases = @(
        @{ Name = 'record'; Value = 'fixture-recording.mkv' }
        @{ Name = 'time-limit'; Value = '10' }
        @{ Name = 'no-window'; Value = $true }
        @{ Name = 'no-video'; Value = $true }
        @{ Name = 'no-video-playback'; Value = $true }
        @{ Name = 'no-playback'; Value = $true }
    )

    foreach ($case in $legacyReconnectCases) {
        $settings = @{ Reconnect = $true; Options = @{ $case.Name = $case.Value } }
        Assert-BaselineRejection @{
            Action = { Assert-ScrcpySettings -Settings $settings }
            Message = "--$($case.Name) requires turning off Seamless reconnection in Mirroring settings."
        }
        $settings.Reconnect = $false
        Assert-ScrcpySettings -Settings $settings
    }

    foreach ($zeroTimeLimit in @('0', '000')) {
        $settings = @{ Reconnect = $true; Options = @{ 'time-limit' = $zeroTimeLimit } }
        Assert-ScrcpySettings -Settings $settings
        $arguments = @(Get-ScrcpyArguments -Settings $settings)
        Assert-Baseline @{ Condition = $arguments -ccontains ('--time-limit=' + $zeroTimeLimit); Message = 'An explicitly disabled time limit was rejected or changed.' }
    }

    $disabledPlaybackFlags = @{ 'no-window' = $false; 'no-video' = $false; 'no-video-playback' = $false; 'no-playback' = $false }
    $settings = @{ Reconnect = $true; Options = $disabledPlaybackFlags }
    Assert-ScrcpySettings -Settings $settings
    Assert-Baseline @{ Condition = -not @(Get-ScrcpyArguments -Settings $settings).Count; Message = 'Disabled playback flags changed native arguments or prevented reconnect.' }

    # Added Phase 0 coverage: persisted intent reaches argument boundaries without reinterpretation.
    $literalTitle = 'Fixture "quoted" title & $(literal)'
    $literalRecording = 'C:\Fixture recordings\clip & $(literal).mkv'
    $settings = @{
        Reconnect = $false
        Options = @{
            'window-title' = $literalTitle
            'record' = $literalRecording
            'new-display' = ''
            'no-audio' = $false
            'time-limit' = '0'
        }
    }
    Save-ScrcpySettings -RootDirectory $firstRoot -Settings $settings -ExpectedSnapshot (Get-ScrcpySettingsSnapshot -RootDirectory $firstRoot)
    $savedSnapshot = Get-ScrcpySettingsSnapshot -RootDirectory $firstRoot
    $loadedSettings = Get-ScrcpySettings -RootDirectory $firstRoot
    Assert-Baseline @{ Condition = -not $loadedSettings.Reconnect; Message = 'Saving and loading re-enabled reconnect.' }
    Assert-Baseline @{ Condition = -not $loadedSettings.Options.ContainsKey('no-audio'); Message = 'Explicit false switch was persisted as an override.' }
    $expectedArguments = @('--new-display', ('--record=' + $literalRecording), '--time-limit=0', ('--window-title=' + $literalTitle))
    $actualArguments = @(Get-ScrcpyArguments -Settings $loadedSettings)
    Assert-Baseline @{ Condition = $actualArguments.Count -eq $expectedArguments.Count; Message = 'Persisted options added or lost native arguments.' }

    foreach ($expectedArgument in $expectedArguments) {
        Assert-Baseline @{ Condition = $actualArguments -ccontains $expectedArgument; Message = "Persisted option value changed: $expectedArgument" }
    }

    $invalidSettings = @{ Reconnect = $true; Options = @{ 'record' = $literalRecording } }
    Assert-BaselineRejection @{
        Action = { Save-ScrcpySettings -RootDirectory $firstRoot -Settings $invalidSettings -ExpectedSnapshot $savedSnapshot }
        Message = '--record requires turning off Seamless reconnection in Mirroring settings.'
    }
    Assert-Baseline @{ Condition = (Get-ScrcpySettingsSnapshot -RootDirectory $firstRoot) -ceq $savedSnapshot; Message = 'Rejected reconnect conflict changed saved mirroring preferences.' }
    Assert-Baseline @{ Condition = (Get-PhoneConfiguration -RootDirectory $firstRoot).UsbSerial -ceq 'fixture-replacement'; Message = 'Rejected mirroring save changed the saved phone.' }
    Write-Output 'PASS: legacy package-local persistence, reconnect compatibility boundaries, and persisted native option intent.'
} finally {
    $resolvedDirectory = (Resolve-Path -LiteralPath $directory).ProviderPath
    $temporaryRoot = [IO.Path]::GetTempPath().TrimEnd('\')

    if ((Split-Path -Parent $resolvedDirectory) -ne $temporaryRoot) {
        throw 'Unexpected baseline fixture cleanup path.'
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
