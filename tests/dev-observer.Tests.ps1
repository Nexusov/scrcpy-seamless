$ErrorActionPreference = 'Stop'
$observerPath = Join-Path $PSScriptRoot '../scripts/observe-dev.ps1'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('scrcpy-dev-observer-' + [guid]::NewGuid().ToString('N'))
$appDirectory = Join-Path $directory 'app'
$null = New-Item -ItemType Directory -Path $appDirectory -Force
$liveProcess = $null

try {
    Set-Content -LiteralPath (Join-Path $appDirectory 'scrcpy.exe') -Value 'fixture'
    @(
        'INFO:     --> (usb)  private-device-serial  device',
        'INFO: Reconnecting to 192.0.2.15:5555',
        'INFO: Stream resumed in the existing window',
        'DEBUG: [Audio] Buffering threshold exceeded, skipping 480 samples',
        'ERROR: pairing code 123456 and private-device-serial'
    ) | Set-Content -LiteralPath (Join-Path $appDirectory 'last-run.log')

    $existingOutput = Join-Path $directory 'existing'
    $null = & $observerPath -PackageDirectory $directory -OutputDirectory $existingOutput -DurationSeconds 1 -IncludeExistingLogs
    $existingTrace = Get-Content -LiteralPath (Join-Path $existingOutput 'trace.jsonl') -Raw
    $existingEvents = @(Get-Content -LiteralPath (Join-Path $existingOutput 'trace.jsonl') |
        ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.kind -eq 'native_event' })

    if ($existingTrace -match 'private-device-serial|192\.0\.2\.15|123456') {
        throw 'The diagnostic trace exposed a fixture identifier or pairing code.'
    }

    if (-not @($existingEvents | Where-Object { $_.event -eq 'selected_transport' -and $_.transport -eq 'usb' }).Count) {
        throw 'The selected transport was not classified.'
    }

    if (-not @($existingEvents | Where-Object { $_.event -eq 'audio_buffer_drop' -and $_.samples -eq 480 }).Count) {
        throw 'The audio sample count was not recorded.'
    }

    $freshOutput = Join-Path $directory 'fresh'
    $null = & $observerPath -PackageDirectory $directory -OutputDirectory $freshOutput -DurationSeconds 1
    $freshTrace = Get-Content -LiteralPath (Join-Path $freshOutput 'trace.jsonl') -Raw

    if ($freshTrace -match 'selected_transport|reconnect_attempt|audio_buffer_drop') {
        throw 'Historical native events were incorrectly timestamped as live observations.'
    }

    # A live append and a fresh launcher header must both be detected.
    $liveOutput = Join-Path $directory 'live'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $observerPath + '" -PackageDirectory "' + $directory + '" -OutputDirectory "' + $liveOutput + '" -DurationSeconds 3 -PollMilliseconds 250'
    $liveProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $liveTracePath = Join-Path $liveOutput 'trace.jsonl'
    $deadline = [DateTime]::UtcNow.AddSeconds(3)

    while (-not (Test-Path -LiteralPath $liveTracePath)) {

        if ([DateTime]::UtcNow -ge $deadline) {
            throw 'Live observer did not create a trace.'
        }

        Start-Sleep -Milliseconds 50
    }

    Start-Sleep -Milliseconds 500
    Add-Content -LiteralPath (Join-Path $appDirectory 'last-run.log') -Value 'DEBUG: Server disconnected'
    Start-Sleep -Milliseconds 500
    @('scrcpy Seamless DEV | Started: new fixture run', 'INFO: Stream resumed in the existing window') |
        Set-Content -LiteralPath (Join-Path $appDirectory 'last-run.log')

    if (-not $liveProcess.WaitForExit(7000) -or $liveProcess.ExitCode -ne 0) {
        throw 'Live observer did not finish successfully.'
    }

    $liveTrace = Get-Content -LiteralPath $liveTracePath -Raw

    foreach ($expectedEvent in @('server_disconnected', 'log_reset', 'video_resumed')) {

        if ($liveTrace -notmatch $expectedEvent) {
            throw "Live observer missed $expectedEvent."
        }
    }

    if ($liveTrace -match 'private-device-serial|192\.0\.2\.15|123456|selected_transport') {
        throw 'Live observer leaked or replayed prior native content.'
    }

    # A fake ADB executable proves endpoint totals without exposing identifiers.
    $fakeAdb = @'
using System;
public static class FakeAdbDevices {
    public static void Main() {
        Console.WriteLine("List of devices attached");
        Console.WriteLine("private-usb-serial\tdevice");
        Console.WriteLine("192.0.2.55:5555\tdevice");
        Console.WriteLine("private-offline-serial\toffline");
    }
}
'@
    Add-Type -TypeDefinition $fakeAdb -OutputAssembly (Join-Path $appDirectory 'adb.exe') -OutputType ConsoleApplication
    $adbOutput = Join-Path $directory 'adb'
    $null = & $observerPath -PackageDirectory $directory -OutputDirectory $adbOutput -DurationSeconds 1 -IncludeAdb
    $adbTrace = Get-Content -LiteralPath (Join-Path $adbOutput 'trace.jsonl') -Raw
    $adbObservation = @(Get-Content -LiteralPath (Join-Path $adbOutput 'trace.jsonl') |
        ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.kind -eq 'adb_availability' })

    if ($adbTrace -match 'private-usb-serial|192\.0\.2\.55|private-offline-serial') {
        throw 'ADB endpoint identifiers were written to the trace.'
    }

    if ($adbObservation.Count -ne 1 -or $adbObservation[0].counts.usbOrOther -ne 1 -or
        $adbObservation[0].counts.networkLike -ne 1 -or $adbObservation[0].counts.offline -ne 1) {
        throw 'ADB endpoint totals were not classified correctly.'
    }

    Write-Output 'PASS: DEV observer captures live/reset events and safe ADB totals without identifiers.'
} finally {

    if ($null -ne $liveProcess) {

        if (-not $liveProcess.HasExited) {
            $liveProcess.Kill()
            $liveProcess.WaitForExit()
        }

        $liveProcess.Dispose()
    }

    $resolvedDirectory = (Resolve-Path -LiteralPath $directory).ProviderPath

    if ((Split-Path -Parent $resolvedDirectory) -ne [IO.Path]::GetTempPath().TrimEnd('\')) {
        throw 'Unexpected observer fixture cleanup path.'
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}
