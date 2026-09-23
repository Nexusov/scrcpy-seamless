param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [string]$OutputDirectory,
    [ValidateRange(1, 3600)][int]$DurationSeconds = 300,
    [ValidateRange(250, 10000)][int]$PollMilliseconds = 1000,
    [switch]$IncludeAdb,
    [switch]$IncludeExistingLogs,
    [switch]$InteractiveMarkers
)

$ErrorActionPreference = 'Stop'

# Write one privacy-limited observation as a JSON line.
function Write-DevObservation {
    param($Observation)

    $Observation.observedAtUtc = [DateTime]::UtcNow.ToString('o')
    $script:observationWriter.WriteLine(($Observation | ConvertTo-Json -Compress -Depth 5))
}

# Classify native messages without retaining serials, addresses or free-form text.
function Get-SanitizedNativeEvent {
    param([string]$Line)

    if ($Line -match '^\w+:\s*-->\s+\((usb|tcpip)\)') {
        return @{ event = 'selected_transport'; transport = $Matches[1] }
    }

    $patterns = @(
        @{ Pattern = 'Stream resumed in the existing window'; Event = 'video_resumed' },
        @{ Pattern = 'Reconnecting to '; Event = 'reconnect_attempt' },
        @{ Pattern = 'Server disconnected'; Event = 'server_disconnected' },
        @{ Pattern = 'Server connected'; Event = 'server_connected' },
        @{ Pattern = 'Device disconnected'; Event = 'device_disconnected' },
        @{ Pattern = 'Audio stream error'; Event = 'audio_stream_error' },
        @{ Pattern = 'Could not open audio device'; Event = 'audio_device_open_error' },
        @{ Pattern = 'No audio playback'; Event = 'audio_disabled' }
    )

    foreach ($entry in $patterns) {

        if ($Line.Contains($entry.Pattern)) {
            return @{ event = $entry.Event }
        }
    }

    $samplePattern = '\[Audio\] (Buffering threshold exceeded, skipping|Buffer underflow, inserting silence:) (\d+) samples'

    if ($Line -match $samplePattern) {
        $event = if ($Matches[1] -like 'Buffering*') { 'audio_buffer_drop' } else { 'audio_underflow' }
        return @{ event = $event; samples = [int]$Matches[2] }
    }

    if ($Line.StartsWith('ERROR:')) {
        return @{ event = 'native_error' }
    }

    if ($Line.StartsWith('WARN:')) {
        return @{ event = 'native_warning' }
    }

    return $null
}

# Remember the current file boundary so earlier messages are not given new timestamps.
function New-LogReadState {
    param($LogFile)

    $initialLength = 0
    $prefix = ''
    $hasExistingLog = $false

    try {
        $stream = [IO.File]::Open($LogFile, 'Open', 'Read', 'ReadWrite')
        $hasExistingLog = $true

        try {
            $initialLength = if ($IncludeExistingLogs) { 0 } else { $stream.Length }
            $prefix = Get-LogPrefix -Stream $stream
        } finally {
            $stream.Dispose()
        }
    } catch [IO.FileNotFoundException] {
        # The native launcher may not have created its log yet.
    } catch [IO.DirectoryNotFoundException] {
        # The native launcher may not have created its log directory yet.
    }

    return @{
        Path = $LogFile
        Position = $initialLength
        Prefix = $prefix
        Pending = ''
        CaptureLineNumber = 0
        Historical = [bool]$IncludeExistingLogs -and $hasExistingLog
    }
}

# Read a short stable prefix to detect a newly recreated or truncated log.
function Get-LogPrefix {
    param($Stream)

    $null = $Stream.Seek(0, [IO.SeekOrigin]::Begin)
    $prefixLength = [Math]::Min(128, [int]$Stream.Length)
    $prefixBytes = New-Object byte[] $prefixLength
    $null = $Stream.Read($prefixBytes, 0, $prefixLength)
    return [BitConverter]::ToString($prefixBytes)
}

# Read newly appended lines, including files recreated by a new DEV launch.
function Read-NewNativeLines {
    param($State)

    $file = Get-Item -LiteralPath $State.Path -ErrorAction SilentlyContinue

    if ($null -eq $file) {
        $State.Position = 0
        $State.Prefix = ''
        $State.Pending = ''
        $State.Historical = $false
        return
    }

    $stream = [IO.File]::Open($State.Path, 'Open', 'Read', 'ReadWrite')

    try {
        $prefix = Get-LogPrefix -Stream $stream
        $replaced = $State.Prefix -and -not $prefix.StartsWith($State.Prefix)

        if ($stream.Length -lt $State.Position -or $replaced) {
            $State.Position = 0
            $State.Pending = ''
            $State.Historical = $false
            Write-DevObservation @{ kind = 'log_reset'; source = [IO.Path]::GetFileName($State.Path) }
        }

        $State.Prefix = $prefix
        $null = $stream.Seek($State.Position, [IO.SeekOrigin]::Begin)
        $reader = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8, $true, 4096, $true)

        try {
            $chunk = $reader.ReadToEnd()
            $State.Position = $stream.Position
        } finally {
            $reader.Dispose()
        }
    } finally {
        $stream.Dispose()
    }

    $lines = ($State.Pending + $chunk) -split '\r?\n'
    $State.Pending = $lines[-1]

    if ($State.Pending.Length -gt 16384) {
        $State.Pending = ''
        Write-DevObservation @{ kind = 'log_line_discarded'; reason = 'too_long' }
    }

    for ($lineIndex = 0; $lineIndex -lt $lines.Count - 1; $lineIndex++) {
        $State.CaptureLineNumber++
        $nativeEvent = Get-SanitizedNativeEvent -Line $lines[$lineIndex]

        if ($null -ne $nativeEvent) {
            $observation = @{
                kind = 'native_event'
                source = [IO.Path]::GetFileName($State.Path)
                captureLineNumber = $State.CaptureLineNumber
                event = $nativeEvent.event
                timeSource = if ($State.Historical) { 'historical_at_start' } else { 'collector_poll' }
            }

            foreach ($field in @('samples', 'transport')) {

                if ($nativeEvent.ContainsKey($field)) {
                    $observation[$field] = $nativeEvent[$field]
                }
            }

            Write-DevObservation $observation
        }
    }

    $State.Historical = $false
}

# Sample only the native process belonging to this exact DEV package.
function Get-NativeProcessObservation {
    param([string]$ExecutablePath)

    $matches = @(Get-CimInstance Win32_Process -Filter "Name = 'scrcpy.exe'" |
        Where-Object { $_.ExecutablePath -ieq $ExecutablePath })
    $processes = @()

    foreach ($match in $matches) {
        $windowProcess = Get-Process -Id $match.ProcessId -ErrorAction SilentlyContinue

        if ($null -eq $windowProcess) {
            continue
        }

        $processes += @{
            pid = [int]$match.ProcessId
            startedAtUtc = $match.CreationDate.ToUniversalTime().ToString('o')
            windowHandle = [int64]$windowProcess.MainWindowHandle
            responding = [bool]$windowProcess.Responding
            workingSetBytes = [int64]$windowProcess.WorkingSet64
            handleCount = [int]$windowProcess.HandleCount
            threadCount = [int]$windowProcess.Threads.Count
        }
    }

    return @{ kind = 'native_process'; count = $processes.Count; processes = $processes }
}

# Count visible ADB endpoints without writing device identifiers or command output.
function Get-AdbAvailabilityObservation {
    param([string]$AdbPath)

    $process = New-Object Diagnostics.Process
    $started = $false
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $AdbPath
    $startInfo.Arguments = 'devices'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process.StartInfo = $startInfo

    try {
        $null = $process.Start()
        $started = $true
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit(3000)) {
            $process.Kill()
            $process.WaitForExit()
            return @{ kind = 'adb_availability'; status = 'timeout' }
        }

        $output = $outputTask.GetAwaiter().GetResult()
        $null = $errorTask.GetAwaiter().GetResult()

        if ($process.ExitCode -ne 0) {
            return @{ kind = 'adb_availability'; status = 'error'; exitCode = $process.ExitCode }
        }

        $counts = @{ networkLike = 0; usbOrOther = 0; unauthorized = 0; offline = 0 }

        foreach ($line in ($output -split '\r?\n')) {

            if ($line -notmatch '^\s*(\S+)\s+(device|unauthorized|offline)\s*$') {
                continue
            }

            $identifier = $Matches[1]
            $state = $Matches[2]

            if ($state -eq 'unauthorized' -or $state -eq 'offline') {
                $counts[$state]++
                continue
            }

            $kind = if ($identifier.Contains(':') -or $identifier.Contains('_adb-tls-')) { 'networkLike' } else { 'usbOrOther' }
            $counts[$kind]++
        }

        return @{ kind = 'adb_availability'; status = 'ok'; counts = $counts }
    } catch {
        return @{ kind = 'adb_availability'; status = 'error' }
    } finally {

        if ($started -and -not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }

        $process.Dispose()
    }
}

# Record Wi-Fi adapter availability without names, addresses or SSIDs.
function Get-WifiAvailabilityObservation {
    $adapters = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        Where-Object { $_.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Wireless80211 })
    $upCount = @($adapters | Where-Object {
        $_.OperationalStatus -eq [Net.NetworkInformation.OperationalStatus]::Up
    }).Count
    return @{ kind = 'wifi_availability'; adapterCount = $adapters.Count; upCount = $upCount }
}

# Record predefined owner observations without accepting private free-form input.
function Read-InteractiveMarkers {
    while ([Console]::KeyAvailable) {
        $key = [Console]::ReadKey($true).KeyChar.ToString().ToLowerInvariant()

        if (-not $script:markerActions.ContainsKey($key)) {
            continue
        }

        $action = $script:markerActions[$key]
        Write-DevObservation @{ kind = 'owner_marker'; action = $action }
        Write-Host "Marker: $action"
    }
}

$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).ProviderPath
$appDirectory = Join-Path $packageRoot 'app'
$nativeExecutable = Join-Path $appDirectory 'scrcpy.exe'
$adbExecutable = Join-Path $appDirectory 'adb.exe'

if (-not (Test-Path -LiteralPath $nativeExecutable -PathType Leaf)) {
    throw "DEV native executable was not found under the package app directory."
}

if ($IncludeAdb -and -not (Test-Path -LiteralPath $adbExecutable -PathType Leaf)) {
    throw 'Package-local ADB executable was not found.'
}

if ($InteractiveMarkers) {
    try {
        $null = [Console]::KeyAvailable
    } catch [InvalidOperationException] {
        throw '-InteractiveMarkers requires an interactive PowerShell console.'
    }
}

if (-not $OutputDirectory) {
    $captureName = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputDirectory = Join-Path (Join-Path $PSScriptRoot '..\work\phase0\observations') $captureName
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $outputRoot -Force
$tracePath = Join-Path $outputRoot 'trace.jsonl'

if (Test-Path -LiteralPath $tracePath) {
    throw 'Diagnostic trace already exists; choose a new output directory.'
}

$logStates = @(
    (New-LogReadState -LogFile (Join-Path $appDirectory 'last-run.log')),
    (New-LogReadState -LogFile (Join-Path $appDirectory 'last-run-errors.log'))
)
$script:observationWriter = New-Object IO.StreamWriter($tracePath, $false, (New-Object Text.UTF8Encoding($false)))
$script:observationWriter.AutoFlush = $true
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$nextAdbMilliseconds = 0
$script:markerActions = @{
    '1' = 'usb_removed'
    '2' = 'usb_attached'
    '3' = 'phone_wifi_off'
    '4' = 'phone_wifi_on'
    '5' = 'video_lost'
    '6' = 'video_restored'
    '7' = 'audio_lost'
    '8' = 'audio_restored'
    '9' = 'control_failed'
    '0' = 'control_works'
    'g' = 'audio_glitch'
}

if ($InteractiveMarkers) {
    Write-Host 'Press 1/2: USB removed/attached; 3/4: phone Wi-Fi off/on.'
    Write-Host 'Press 5/6: video lost/restored; 7/8: audio lost/restored.'
    Write-Host 'Press 9/0: control failed/works; g: audible glitch.'
    Write-Host 'Keep this terminal focused when pressing a marker key.'
}

Write-Host "Diagnostic trace: $tracePath"

try {
    Write-DevObservation @{
        kind = 'capture_start'
        schemaVersion = 1
        nativeSha256 = (Get-FileHash -LiteralPath $nativeExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        pollMilliseconds = $PollMilliseconds
        includeExistingLogs = [bool]$IncludeExistingLogs
        adbPolling = [bool]$IncludeAdb
        interactiveMarkers = [bool]$InteractiveMarkers
    }

    while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        try {
            Write-DevObservation (Get-NativeProcessObservation -ExecutablePath $nativeExecutable)
        } catch {
            Write-DevObservation @{ kind = 'native_process'; status = 'query_error' }
        }

        try {
            Write-DevObservation (Get-WifiAvailabilityObservation)
        } catch {
            Write-DevObservation @{ kind = 'wifi_availability'; status = 'query_error' }
        }

        foreach ($state in $logStates) {
            try {
                Read-NewNativeLines -State $state
            } catch {
                Write-DevObservation @{
                    kind = 'log_read_error'
                    source = [IO.Path]::GetFileName($state.Path)
                }
            }
        }

        if ($IncludeAdb -and $stopwatch.ElapsedMilliseconds -ge $nextAdbMilliseconds) {
            Write-DevObservation (Get-AdbAvailabilityObservation -AdbPath $adbExecutable)
            $nextAdbMilliseconds = $stopwatch.ElapsedMilliseconds + 3000
        }

        if ($InteractiveMarkers) {
            Read-InteractiveMarkers
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }

    Write-DevObservation @{ kind = 'capture_end'; status = 'completed' }
} finally {
    $script:observationWriter.Dispose()
}

Write-Output "Capture completed: $tracePath"
