$ErrorActionPreference = 'Stop'
$storePath = Join-Path $PSScriptRoot '../launcher/configuration-store.ps1'
. $storePath
$directory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-store-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $directory
$phonePath = Join-Path $directory 'phone.json'
$worker = $null
$pending = $null
$workerTimeout = [TimeSpan]::FromSeconds(15)
$acquiredSignal = [Threading.ManualResetEvent]::new($false)
$releaseSignal = [Threading.ManualResetEvent]::new($false)

# Refuse regressions in atomic settings concurrency and reset scope.
function Assert-Store { param($Condition, $Message)
    if (-not $Condition) { throw $Message }
}

# Require an operation to reject stale or malformed writes.
function Assert-StoreRejected { param($Action, $Message)
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-Store $rejected $Message
}

# Avoid inspecting unrelated desktop sessions during a persistence unit test.
function Get-Process { param($Name, $ErrorAction) return @() }
try {
    $configuration = [pscustomobject]@{ UsbSerial = 'phone-A'; WirelessService = ''; ConnectionMode = 'usb' }
    $firstSnapshot = Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $null -PassThruSnapshot
    Assert-Store ($firstSnapshot -ceq (Get-DeviceConfigurationSnapshot -RootDirectory $directory)) 'First-run null snapshot save failed.'
    [IO.File]::WriteAllText($phonePath, '')
    Assert-StoreRejected { Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $null } 'Empty file was confused with absent configuration.'
    Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot ''
    $oldSnapshot = Get-DeviceConfigurationSnapshot -RootDirectory $directory
    $configuration.UsbSerial = 'phone-B'
    Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $oldSnapshot
    Assert-StoreRejected { Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $oldSnapshot } 'Competing write was overwritten.'
    Assert-Store ((Get-PhoneConfiguration -RootDirectory $directory).UsbSerial -eq 'phone-B') 'Rejected stale save changed device.'
    $snapshotBeforeReset = Get-DeviceConfigurationSnapshot -RootDirectory $directory
    $null = Reset-DeviceConfiguration -RootDirectory $directory -Confirmed $true
    Assert-StoreRejected { Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $snapshotBeforeReset } 'Stale window restored reset settings.'
    Assert-Store (-not (Test-Path $phonePath)) 'Reset was undone by stale save.'
    [IO.File]::WriteAllText($phonePath, '{invalid json')
    Assert-Store ($null -eq (Get-PhoneConfiguration -RootDirectory $directory)) 'Malformed settings accepted.'
    Assert-Store ((Get-DeviceConfigurationSnapshot -RootDirectory $directory) -ceq '{invalid json') 'Reading malformed settings modified file.'
    Remove-Item -LiteralPath $phonePath
    $worker = [PowerShell]::Create()
    $null = $worker.AddScript({
        param($StorePath, $Directory, $AcquiredSignal, $ReleaseSignal)
        . $StorePath
        Invoke-DeviceConfigurationLock -RootDirectory $Directory -Action {
            $null = $AcquiredSignal.Set()
            $null = $ReleaseSignal.WaitOne()
        }
    })
    $null = $worker.AddArgument([IO.Path]::GetFullPath($storePath))
    $null = $worker.AddArgument($directory)
    $null = $worker.AddArgument($acquiredSignal)
    $null = $worker.AddArgument($releaseSignal)
    $pending = $worker.BeginInvoke()
    $firstSignal = [Threading.WaitHandle]::WaitAny(
        [Threading.WaitHandle[]]@($acquiredSignal, $pending.AsyncWaitHandle), $workerTimeout)
    if ($firstSignal -eq 1) {
        $null = $worker.EndInvoke($pending)
        if ($worker.Streams.Error.Count) {
            throw $worker.Streams.Error[0]
        }
        throw 'Competing worker exited before acquiring the store lock.'
    }
    Assert-Store ($firstSignal -eq 0) 'Competing worker did not acquire store lock before timeout.'
    $lockError = $null
    try {
        Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration
    }
    catch {
        $lockError = $_.Exception.Message
    }
    Assert-Store ($lockError -eq 'Device settings are in use. Wait a moment and try again.') 'Concurrent store write did not reject the held lock.'
    $null = $releaseSignal.Set()
    $null = $worker.EndInvoke($pending)
    if ($worker.Streams.Error.Count) {
        throw $worker.Streams.Error[0]
    }
    $pending = $null
    Save-PhoneConfiguration -RootDirectory $directory -Configuration $configuration -ExpectedSnapshot $null
    Assert-Store ((Get-PhoneConfiguration -RootDirectory $directory).UsbSerial -eq 'phone-B') 'Released lock was not reusable.'
    Write-Output 'PASS: absent/empty snapshots, competing writes, reset, malformed reads and cross-worker locking.'
} finally {
    $null = $releaseSignal.Set()
    if ($null -ne $worker) {
        if ($null -ne $pending -and -not $pending.AsyncWaitHandle.WaitOne($workerTimeout)) {
            $worker.Stop()
        }
        $worker.Dispose()
    }
    $acquiredSignal.Dispose()
    $releaseSignal.Dispose()
    $resolvedDirectory = (Resolve-Path $directory).ProviderPath
    if ((Split-Path -Parent $resolvedDirectory) -ne [IO.Path]::GetTempPath().TrimEnd('\')) { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
