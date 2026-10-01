using System.Diagnostics;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

/// <summary>Exercises reachable machine-session admission and lifecycle pressure over real pipes.</summary>
public sealed class MachineNativePressureTests
{
    private const int MaximumPendingCommands = 16;
    private const int MaximumRetainedLifecycleEvents = 128;
    private static readonly TimeSpan FixtureWatchdog = TimeSpan.FromSeconds(8);

    /// <summary>A seventeenth valid Focus command cannot exceed the pending-command budget.</summary>
    [Fact]
    public async Task SeventeenthPendingFocusIsRejectedWithoutFailingAdmittedCommands()
    {
        string directory = CreateScratchDirectory();
        string scriptPath = Path.Combine(directory, "machine-pressure-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        SessionId sessionId = SessionId.New();
        string readyName = EventName("focus-ready");
        string releaseName = EventName("focus-release");
        using EventWaitHandle ready = CreateEvent(readyName);
        using EventWaitHandle release = CreateEvent(releaseName);
        MachineNativeSession? session = null;
        int grantCount = 0;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "pending-focus", readyName, releaseName),
                TestContext.Current.CancellationToken,
                grantForegroundPermission: _ => { grantCount++; return false; });
            Task<NativeFocusOutcome>[] admitted = Enumerable.Range(0, MaximumPendingCommands)
                .Select(_ => session.FocusWindowAsync(TestContext.Current.CancellationToken))
                .ToArray();
            await WaitForSignalAsync(ready);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.FocusWindowAsync(TestContext.Current.CancellationToken));
            Assert.Equal(MaximumPendingCommands, grantCount);
            release.Set();
            NativeFocusOutcome[] outcomes = await Task.WhenAll(admitted).WaitAsync(FixtureWatchdog,
                TestContext.Current.CancellationToken);
            Assert.All(outcomes, outcome => Assert.Equal(NativeFocusOutcome.Applied, outcome));

            await session.StopAsync(NativeTerminationReason.UserStop,
                TestContext.Current.CancellationToken).WaitAsync(FixtureWatchdog,
                    TestContext.Current.CancellationToken);
            Assert.Equal(NativeTerminationReason.UserStop, (await session.Completion).Reason);
            Assert.Equal(MaximumPendingCommands, grantCount);
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            release.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An unread, full lifecycle queue fails the exact channel instead of dropping events.</summary>
    [Fact]
    public async Task FullLifecycleQueueTerminatesTheOwnedChild()
    {
        string directory = CreateScratchDirectory();
        string scriptPath = Path.Combine(directory, "machine-pressure-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        SessionId sessionId = SessionId.New();
        string releaseName = EventName("lifecycle-release");
        using EventWaitHandle release = CreateEvent(releaseName);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "lifecycle-pressure", "", releaseName),
                TestContext.Current.CancellationToken);
            release.Set();

            NativeExit exit = await session.Completion.WaitAsync(FixtureWatchdog,
                TestContext.Current.CancellationToken);
            Assert.Equal(NativeTerminationReason.NativeFailure, exit.Reason);
            Assert.False(IsAlive(session.ProcessId));
            List<NativeLifecycleObservation> observations = [];

            await foreach (NativeLifecycleObservation observation in
                session.ObserveLifecycleAsync(TestContext.Current.CancellationToken))
            {
                observations.Add(observation);
            }

            Assert.Equal(MaximumRetainedLifecycleEvents, observations.Count);
            Assert.Equal(NativeLifecycleEventType.NativeReady, observations[0].EventType);
            Assert.Equal((ulong)MaximumRetainedLifecycleEvents, observations[^1].Sequence);
        }
        finally
        {
            release.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Overlapping and later Stop calls share one result and one exact-child cleanup.</summary>
    [Fact]
    public async Task OverlappingStopCallsShareTheSingleNativeCommand()
    {
        string directory = CreateScratchDirectory();
        string scriptPath = Path.Combine(directory, "machine-pressure-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        SessionId sessionId = SessionId.New();
        string readyName = EventName("stop-ready");
        string releaseName = EventName("stop-release");
        using EventWaitHandle ready = CreateEvent(readyName);
        using EventWaitHandle release = CreateEvent(releaseName);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "overlapping-stop", readyName, releaseName),
                TestContext.Current.CancellationToken);
            int nativeProcessId = session.ProcessId;
            Task first = session.StopAsync(NativeTerminationReason.UserStop,
                TestContext.Current.CancellationToken);
            await WaitForSignalAsync(ready);
            Task second = session.StopAsync(NativeTerminationReason.UserStop,
                TestContext.Current.CancellationToken);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            release.Set();

            await Task.WhenAll(first, second).WaitAsync(FixtureWatchdog,
                TestContext.Current.CancellationToken);
            await session.StopAsync(NativeTerminationReason.UserStop,
                TestContext.Current.CancellationToken).WaitAsync(FixtureWatchdog,
                    TestContext.Current.CancellationToken);
            Assert.Equal(NativeTerminationReason.UserStop, (await session.Completion).Reason);
            Assert.Equal(nativeProcessId, session.ProcessId);
            Assert.False(IsAlive(nativeProcessId));
        }
        finally
        {
            release.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates one isolated process double without ADB or device access.</summary>
    private static ProcessStartInfo CreateStartInfo(string scriptPath, SessionId sessionId,
        string mode, string readyName, string releaseName)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(sessionId.Value.ToString("D"));
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(readyName);
        startInfo.ArgumentList.Add(releaseName);
        return startInfo;
    }

    /// <summary>Waits for the fixture's semantic barrier with a finite test watchdog.</summary>
    private static async Task WaitForSignalAsync(EventWaitHandle signal)
    {
        bool signaled = await Task.Run(() => signal.WaitOne(FixtureWatchdog));
        Assert.True(signaled, "The fixture did not reach its expected protocol barrier.");
    }

    /// <summary>Creates a local named event for cross-process test synchronization.</summary>
    private static EventWaitHandle CreateEvent(string name) =>
        new(false, EventResetMode.ManualReset, name);

    /// <summary>Gives each fixture run a unique event name.</summary>
    private static string EventName(string purpose) =>
        "Local\\scrcpy-machine-" + purpose + "-" + Guid.NewGuid().ToString("N");

    /// <summary>Checks only the exact process owned by this test session.</summary>
    private static bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Allocates a GUID-scoped directory for one process-double run.</summary>
    private static string CreateScratchDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "scrcpy-machine-pressure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private const string FixtureScript = """
        param([string]$sessionId, [string]$mode, [string]$readyName, [string]$releaseName)
        $inputPipe = [Console]::OpenStandardInput()
        $outputPipe = [Console]::OpenStandardOutput()
        function ReadExactly([int]$count) {
            $bytes = New-Object byte[] $count
            $offset = 0
            while ($offset -lt $count) {
                $read = $inputPipe.Read($bytes, $offset, $count - $offset)
                if ($read -eq 0) { throw 'truncated fixture input' }
                $offset += $read
            }
            return ,$bytes
        }
        function ReadFrame {
            $header = ReadExactly 4
            $length = [BitConverter]::ToUInt32($header, 0)
            $body = ReadExactly $length
            return [Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
        }
        function SendFrame([string]$json) {
            $body = [Text.UTF8Encoding]::new($false).GetBytes($json)
            $header = [BitConverter]::GetBytes([uint32]$body.Length)
            $outputPipe.Write($header, 0, $header.Length)
            $outputPipe.Write($body, 0, $body.Length)
            $outputPipe.Flush()
        }
        $hello = ReadFrame
        if ($hello.messageType -ne 'hello') { exit 12 }
        SendFrame '{"messageType":"helloResult","product":"scrcpy-seamless","protocolMajor":1,"protocolMinor":0,"status":"accepted","capabilities":["focus-window","lifecycle-v1","stop"]}'
        SendFrame ('{"messageType":"lifecycle","sequence":"1","utc":"2026-01-01T00:00:00.000Z","monotonicMicroseconds":"0","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"NativeReady","reason":"none","error":"none"}')
        if ($mode -eq 'lifecycle-pressure') {
            $release = [Threading.EventWaitHandle]::OpenExisting($releaseName)
            $release.WaitOne() | Out-Null
            for ($sequence = 2; $sequence -le 129; $sequence++) {
                SendFrame ('{"messageType":"lifecycle","sequence":"' + $sequence + '","utc":"2026-01-01T00:00:01.000Z","monotonicMicroseconds":"' + $sequence + '","sessionId":"' + $sessionId + '","connectionAttemptId":"00000000-0000-4000-8000-000000000001","subsystem":"connection","eventType":"Connecting","reason":"none","error":"none"}')
            }
            [Threading.ManualResetEventSlim]::new($false).Wait()
            exit 13
        }
        $ready = [Threading.EventWaitHandle]::OpenExisting($readyName)
        $release = [Threading.EventWaitHandle]::OpenExisting($releaseName)
        if ($mode -eq 'pending-focus') {
            $commands = @()
            for ($commandNumber = 0; $commandNumber -lt 16; $commandNumber++) {
                $command = ReadFrame
                if ($command.command -ne 'FocusWindow') { exit 14 }
                $commands += $command
            }
            $ready.Set() | Out-Null
            $release.WaitOne() | Out-Null
            foreach ($command in $commands) {
                SendFrame ('{"messageType":"commandResult","requestId":"' + $command.requestId + '","sessionId":"' + $sessionId + '","command":"FocusWindow","status":"applied"}')
            }
            $command = ReadFrame
        } else {
            $command = ReadFrame
            $ready.Set() | Out-Null
            $release.WaitOne() | Out-Null
        }
        if ($command.command -ne 'Stop') { exit 15 }
        SendFrame ('{"messageType":"commandResult","requestId":"' + $command.requestId + '","sessionId":"' + $sessionId + '","command":"Stop","status":"accepted"}')
        SendFrame ('{"messageType":"lifecycle","sequence":"2","utc":"2026-01-01T00:00:02.000Z","monotonicMicroseconds":"2","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"SessionStopped","reason":"userStop","error":"none"}')
        exit 0
        """;
}
