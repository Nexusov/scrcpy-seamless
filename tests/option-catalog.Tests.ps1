param([string]$LauncherDirectory = (Join-Path $PSScriptRoot '..\launcher'))
$ErrorActionPreference = 'Stop'
. (Join-Path $LauncherDirectory 'option-catalog.ps1')

# Check the PowerShell catalogue contract; SpecGen verifies native/legacy parity.
function Assert-Catalog {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        throw $Message
    }
}

$catalog = @(Get-ScrcpyOptionCatalog)
Assert-Catalog ($catalog.Count -eq 106) 'The legacy catalogue changed size; review current native CLI coverage and update the characterization.'
Assert-Catalog (@($catalog.Name | Select-Object -Unique).Count -eq $catalog.Count) 'Duplicate CLI metadata.'

foreach ($option in $catalog) {
    Assert-Catalog (-not [string]::IsNullOrWhiteSpace($option.Label)) "Missing label: $($option.Name)"
    Assert-Catalog (-not [string]::IsNullOrWhiteSpace($option.Description)) "Missing help: $($option.Name)"
    Assert-Catalog ($option.Kind -in @('switch', 'value')) "Invalid kind: $($option.Name)"
    Assert-Catalog ($option.Availability -in @('editable', 'managed', 'unsupported', 'action')) "Invalid availability: $($option.Name)"
    Assert-Catalog ($option.Basic -is [bool]) "Basic must be Boolean: $($option.Name)"
    Assert-Catalog ($option.SeamlessCompatible -is [bool]) "Compatibility must be Boolean: $($option.Name)"

    if ($option.Availability -ne 'editable') {
        Assert-Catalog (-not [string]::IsNullOrWhiteSpace($option.Reason)) "Missing limitation explanation: $($option.Name)"
    }

    if ($option.Pattern) {
        $null = [regex]::new($option.Pattern)
    }
}

$byName = @{}
foreach ($option in $catalog) {
    $byName[$option.Name] = $option
}

foreach ($name in @('max-size', 'max-fps', 'video-bit-rate', 'no-audio')) {
    Assert-Catalog $byName[$name].Basic "Everyday option must remain visible: $name"
    Assert-Catalog ($byName[$name].Availability -eq 'editable') "Everyday option must be editable: $name"
}

foreach ($name in @('record', 'time-limit', 'no-window', 'no-video', 'no-video-playback', 'no-playback', 'otg')) {
    Assert-Catalog (-not $byName[$name].SeamlessCompatible) "Native reconnect precondition missing: $name"
}

foreach ($name in @('new-display', 'pause-on-exit', 'tcpip')) {
    Assert-Catalog $byName[$name].ArgumentOptional "Bare optional flag must be representable: $name"
}

Assert-Catalog ($byName['v4l2-sink'].Availability -eq 'unsupported') 'Linux-only output must not be offered on Windows.'
Assert-Catalog ($byName['serial'].Availability -eq 'managed') 'User options must not replace the selected phone.'
Assert-Catalog ($byName['list-encoders'].Availability -eq 'action') 'Device discovery must not be persisted as a streaming flag.'
foreach ($name in @('help', 'version', 'list-apps', 'list-cameras', 'list-camera-sizes', 'list-displays', 'list-encoders')) {
    Assert-Catalog ($byName[$name].Availability -eq 'action') "Diagnostic action became a saved option: $name"
}
Assert-Catalog ($byName['video-codec'].Values -contains 'av1') 'AV1 selection missing.'
Assert-Catalog ($byName['audio-source'].Values -contains 'mic') 'Microphone selection missing.'

$catalog[0].Label = 'Changed by a caller'
$freshCatalog = @(Get-ScrcpyOptionCatalog)
Assert-Catalog ($freshCatalog[0].Label -ne 'Changed by a caller') 'Catalogue objects must not leak state across settings sessions.'
Write-Output "PASS: $($catalog.Count) legacy catalogue entries preserve the PowerShell settings contract."
