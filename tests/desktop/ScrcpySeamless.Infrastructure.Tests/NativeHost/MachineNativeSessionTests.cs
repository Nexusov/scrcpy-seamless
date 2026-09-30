using System.Diagnostics;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

/// <summary>Exercises the production machine session over real redirected process pipes.</summary>
public sealed class MachineNativeSessionTests
{
    /// <summary>Retains actual accepted values and omits arbitrary capabilities without consuming events.</summary>
    [Fact]
    public async Task AcceptedHandshakeRetainsAllowlistedValuesAfterDisposal()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "extra-capability"),
                TestContext.Current.CancellationToken);
            INativeHandshakeEvidence evidence = session;
            NativeHandshakeObservation handshake = Assert.IsType<NativeHandshakeObservation>(
                evidence.AcceptedHandshake);
            Assert.Equal("scrcpy-seamless", handshake.Product);
            Assert.Equal(1, handshake.ProtocolMajor);
            Assert.Equal(0, handshake.ProtocolMinor);
            Assert.Equal(["focus-window", "lifecycle-v1", "stop"], handshake.Capabilities);
            Assert.Equal(1, handshake.OmittedCapabilityCount);
            Assert.Same(handshake, evidence.AcceptedHandshake);
            Assert.Equal(session.ProcessId, evidence.ProcessId);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)handshake.Capabilities)[0] = "synthetic-private-value");

            await session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken);
            await session.DisposeAsync();
            Assert.Same(handshake, evidence.AcceptedHandshake);
            Assert.False(IsAlive(session.ProcessId));
            List<NativeLifecycleObservation> observations = [];

            await foreach (NativeLifecycleObservation observation in
                session.ObserveLifecycleAsync(TestContext.Current.CancellationToken))
            {
                observations.Add(observation);
            }

            Assert.Collection(observations,
                ready => Assert.Equal(NativeLifecycleEventType.NativeReady, ready.EventType),
                stopped => Assert.Equal(NativeLifecycleEventType.SessionStopped, stopped.EventType));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The immutable handshake detaches its known capabilities from a caller-owned collection.</summary>
    [Fact]
    public void HandshakeObservationCopiesCapabilityValues()
    {
        List<string> capabilities = ["focus-window", "lifecycle-v1", "stop", "synthetic-private-value"];
        NativeHandshakeObservation observation = new("scrcpy-seamless", 1, 0, capabilities);
        capabilities[0] = "replacement-private-value";
        capabilities.Clear();
        Assert.Equal(["focus-window", "lifecycle-v1", "stop"], observation.Capabilities);
        Assert.Equal(1, observation.OmittedCapabilityCount);
    }

    /// <summary>Proves negotiation, main-thread Focus result correlation and separate Stop completion.</summary>
    [Fact]
    public async Task RealProcessNegotiatesFocusesAndStops()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId), TestContext.Current.CancellationToken);
            Assert.Equal(NativeFocusOutcome.Applied,
                await session.FocusWindowAsync(TestContext.Current.CancellationToken));
            await session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken);
            NativeExit exit = await session.Completion;
            Assert.Equal(sessionId, exit.SessionId);
            Assert.Equal(NativeTerminationReason.UserStop, exit.Reason);
            Assert.False(IsAlive(session.ProcessId));
            List<NativeLifecycleObservation> observations = [];

            await foreach (NativeLifecycleObservation observation in
                session.ObserveLifecycleAsync(TestContext.Current.CancellationToken))
            {
                observations.Add(observation);
            }

            Assert.Collection(observations,
                ready => Assert.Equal(NativeLifecycleEventType.NativeReady, ready.EventType),
                stopped => Assert.Equal(NativeLifecycleEventType.SessionStopped, stopped.EventType));
            Assert.Equal(observations[0].Sequence + 1, observations[1].Sequence);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A wrong negotiated product fails before ownership transfer and settles the exact child.</summary>
    [Theory]
    [InlineData("wrong-product")]
    [InlineData("bad-minor")]
    [InlineData("missing-ready")]
    public async Task RejectedHandshakeDoesNotLeakChild(string mode)
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        string markerPath = Path.Combine(directory, "pid.txt");

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, mode, markerPath),
                TestContext.Current.CancellationToken));
            Assert.True(File.Exists(markerPath));
            int processId = int.Parse(File.ReadAllText(markerPath));
            Assert.False(IsAlive(processId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Drains a large unterminated stderr message independently of protocol stdout.</summary>
    [Fact]
    public async Task UnterminatedStderrCannotBlockHandshakeOrStop()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "verbose-stderr"),
                TestContext.Current.CancellationToken);
            await session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken);
            Assert.Equal(NativeTerminationReason.UserStop, (await session.Completion).Reason);
            Assert.True(session.StandardErrorTruncated);
            Assert.True(session.StandardError.Length <= 65_536);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A command whose result never arrives closes the entire channel and exact child.</summary>
    [Fact]
    public async Task MissingCommandResultFailsWithinTheFiniteBudget()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "no-result"),
                TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                session.FocusWindowAsync(TestContext.Current.CancellationToken));
            NativeExit exit = await session.Completion;
            Assert.Equal(NativeTerminationReason.NativeFailure, exit.Reason);
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A forced exact-child termination cannot be reported as a graceful Stop.</summary>
    [Fact]
    public async Task StopWithoutAcknowledgementReportsEscalation()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "no-stop-result"),
                TestContext.Current.CancellationToken);
            MachineNativeSession.StopException failure = await Assert.ThrowsAsync<MachineNativeSession.StopException>(() =>
                session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken));
            Assert.Equal(MachineNativeSession.StopFailure.Escalated, failure.Failure);
            Assert.Equal(NativeTerminationReason.NativeFailure, failure.Exit?.Reason);
            Assert.NotNull(failure.InnerException);
            Assert.Equal(NativeTerminationReason.NativeFailure, (await session.Completion).Reason);
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A Stop acknowledgement is not a replacement for SessionStopped and process exit.</summary>
    [Fact]
    public async Task StopAcknowledgedWithoutTerminalLifecycleReportsFailure()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "no-terminal"),
                TestContext.Current.CancellationToken);
            MachineNativeSession.StopException failure = await Assert.ThrowsAsync<MachineNativeSession.StopException>(() =>
                session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken));
            Assert.Equal(MachineNativeSession.StopFailure.TerminalFailure, failure.Failure);
            Assert.Equal(NativeTerminationReason.NativeFailure, failure.Exit?.Reason);
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A wrong-session result cannot complete the waiting command or preserve the channel.</summary>
    [Fact]
    public async Task WrongSessionResultFailsTheExactChannel()
    {
        await AssertMalformedResultFailsAsync("wrong-session");
    }

    /// <summary>Truncated frame bytes cannot be reused as a shifted later message.</summary>
    [Fact]
    public async Task PartialNativeFrameFailsTheExactChannel()
    {
        await AssertMalformedResultFailsAsync("partial-frame");
    }

    /// <summary>A syntactically valid but incompatible Focus status terminates the channel.</summary>
    [Fact]
    public async Task UnsupportedFocusResultFailsTheExactChannel()
    {
        await AssertMalformedResultFailsAsync("invalid-focus-result");
    }

    /// <summary>Cancelling one sent wait does not free its request ID or shift the next result.</summary>
    [Fact]
    public async Task PostSendCancellationKeepsLateResultCorrelated()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        string readyName = "Local\\scrcpy-machine-ready-" + Guid.NewGuid().ToString("N");
        string releaseName = "Local\\scrcpy-machine-release-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle ready = new(false, EventResetMode.ManualReset, readyName);
        using EventWaitHandle release = new(false, EventResetMode.ManualReset, releaseName);
        using CancellationTokenSource canceledWait = new();
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "delayed-focus", "", readyName, releaseName),
                TestContext.Current.CancellationToken);
            Task<NativeFocusOutcome> focus = session.FocusWindowAsync(canceledWait.Token);
            Assert.True(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(5))));
            canceledWait.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => focus);
            release.Set();
            await session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken);
            Assert.Equal(NativeTerminationReason.UserStop, (await session.Completion).Reason);
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

    /// <summary>Earlier reserved Focus IDs must reach the native pipe before later Focus IDs.</summary>
    [Fact]
    public async Task ConcurrentFocusRequestsPublishInRequestIdOrder()
    {
        await AssertReservedRequestPublishesFirstAsync(stopSecond: false);
    }

    /// <summary>A concurrent Stop cannot overtake an earlier Focus reservation.</summary>
    [Fact]
    public async Task ConcurrentFocusAndStopPublishInRequestIdOrder()
    {
        await AssertReservedRequestPublishesFirstAsync(stopSecond: true);
    }

    /// <summary>A caller cancelled before admission cannot occupy a request slot or consume an ID.</summary>
    [Fact]
    public async Task CancellationBeforeAdmissionLeavesNoPendingRequest()
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        string orderPath = Path.Combine(directory, "request-order.txt");
        File.WriteAllText(scriptPath, FixtureScript);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "record-order", orderPath),
                TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                session.FocusWindowAsync(cancelled.Token));
            Assert.Equal(NativeFocusOutcome.Applied,
                await session.FocusWindowAsync(TestContext.Current.CancellationToken));
            await session.StopAsync(NativeTerminationReason.UserStop,
                TestContext.Current.CancellationToken);
            Assert.Equal(["1", "2"], File.ReadAllLines(orderPath));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertReservedRequestPublishesFirstAsync(bool stopSecond)
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        string orderPath = Path.Combine(directory, "request-order.txt");
        File.WriteAllText(scriptPath, FixtureScript);
        using ManualResetEventSlim firstReserved = new(false);
        using ManualResetEventSlim releaseFirst = new(false);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, "record-order", orderPath),
                TestContext.Current.CancellationToken,
                (requestId, _) =>
                {
                    if (requestId == 1)
                    {
                        firstReserved.Set();

                        if (!releaseFirst.Wait(TimeSpan.FromSeconds(8)))
                        {
                            throw new TimeoutException("The first request publication was not released.");
                        }
                    }
                });
            MachineNativeSession activeSession = session;
            Task<NativeFocusOutcome> first = Task.Run(() =>
                activeSession.FocusWindowAsync(TestContext.Current.CancellationToken));
            Assert.True(await Task.Run(() => firstReserved.Wait(TimeSpan.FromSeconds(5))));
            Task later = stopSecond
                ? Task.Run(() => activeSession.StopAsync(NativeTerminationReason.UserStop,
                    TestContext.Current.CancellationToken))
                : Task.Run(async () =>
                {
                    Assert.Equal(NativeFocusOutcome.Applied,
                        await activeSession.FocusWindowAsync(TestContext.Current.CancellationToken));
                });

            // A later request must not reach the pipe while the earlier ID is reserved.
            await Assert.ThrowsAsync<TimeoutException>(() => later.WaitAsync(TimeSpan.FromSeconds(2)));
            releaseFirst.Set();
            Assert.Equal(NativeFocusOutcome.Applied, await first.WaitAsync(TimeSpan.FromSeconds(5)));
            await later.WaitAsync(TimeSpan.FromSeconds(5));

            if (!stopSecond)
            {
                await session.StopAsync(NativeTerminationReason.UserStop,
                    TestContext.Current.CancellationToken);
            }

            Assert.Equal(NativeTerminationReason.UserStop, (await session.Completion).Reason);
            Assert.Equal(stopSecond ? ["1", "2"] : ["1", "2", "3"],
                File.ReadAllLines(orderPath));
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            releaseFirst.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertMalformedResultFailsAsync(string mode)
    {
        string directory = CreateScratchDirectory();
        SessionId sessionId = SessionId.New();
        string scriptPath = Path.Combine(directory, "machine-fixture.ps1");
        File.WriteAllText(scriptPath, FixtureScript);
        MachineNativeSession? session = null;

        try
        {
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, mode), TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                session.FocusWindowAsync(TestContext.Current.CancellationToken));
            Assert.Equal(NativeTerminationReason.NativeFailure, (await session.Completion).Reason);
            Assert.False(IsAlive(session.ProcessId));
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates an isolated PowerShell fixture with no ADB or device access.</summary>
    private static ProcessStartInfo CreateStartInfo(string scriptPath, SessionId sessionId,
        string mode = "normal", string markerPath = "", string focusReadyName = "",
        string focusReleaseName = "")
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
        startInfo.ArgumentList.Add(markerPath);
        startInfo.ArgumentList.Add(focusReadyName);
        startInfo.ArgumentList.Add(focusReleaseName);
        return startInfo;
    }

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

    private static string CreateScratchDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private const string FixtureScript = """
        param([string]$sessionId, [string]$mode, [string]$markerPath, [string]$focusReadyName, [string]$focusReleaseName)
        $inputPipe = [Console]::OpenStandardInput()
        $outputPipe = [Console]::OpenStandardOutput()
        if ($markerPath -and $mode -ne 'record-order') { [IO.File]::WriteAllText($markerPath, $PID.ToString()) }
        if ($mode -eq 'verbose-stderr') {
            $stderrPipe = [Console]::OpenStandardError()
            $noise = New-Object byte[] 131072
            $stderrPipe.Write($noise, 0, $noise.Length)
            $stderrPipe.Flush()
        }
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
        $product = if ($mode -eq 'wrong-product') { 'different-product' } else { 'scrcpy-seamless' }
        $minor = if ($mode -eq 'bad-minor') { 1 } else { 0 }
        $capabilities = if ($mode -eq 'extra-capability') { '["focus-window","lifecycle-v1","pairing-code-synthetic-secret","stop"]' } else { '["focus-window","lifecycle-v1","stop"]' }
        SendFrame ('{"messageType":"helloResult","product":"' + $product + '","protocolMajor":1,"protocolMinor":' + $minor + ',"status":"accepted","capabilities":' + $capabilities + '}')
        if ($mode -eq 'wrong-product' -or $mode -eq 'bad-minor') { [Threading.ManualResetEventSlim]::new($false).Wait(); exit 13 }
        if ($mode -eq 'missing-ready') { exit 0 }
        SendFrame ('{"messageType":"lifecycle","sequence":"1","utc":"2026-01-01T00:00:00.000Z","monotonicMicroseconds":"0","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"NativeReady","reason":"none","error":"none"}')
        while ($true) {
            $command = ReadFrame
            if ($mode -eq 'record-order') { [IO.File]::AppendAllText($markerPath, $command.requestId + [Environment]::NewLine) }
            if ($mode -eq 'no-result') { [Threading.ManualResetEventSlim]::new($false).Wait(); exit 14 }
            if ($mode -eq 'no-stop-result' -and $command.command -eq 'Stop') { [Threading.ManualResetEventSlim]::new($false).Wait(); exit 16 }
            if ($mode -eq 'delayed-focus' -and $command.command -eq 'FocusWindow') {
                $focusReady = [Threading.EventWaitHandle]::OpenExisting($focusReadyName)
                $focusRelease = [Threading.EventWaitHandle]::OpenExisting($focusReleaseName)
                $focusReady.Set() | Out-Null
                $focusRelease.WaitOne() | Out-Null
            }
            if ($mode -eq 'partial-frame') {
                $header = [BitConverter]::GetBytes([uint32]100)
                $outputPipe.Write($header, 0, $header.Length)
                $outputPipe.WriteByte(65)
                $outputPipe.Flush()
                exit 0
            }
            $status = if ($mode -eq 'invalid-focus-result') { 'unsupportedCommand' } elseif ($command.command -eq 'FocusWindow') { 'applied' } else { 'accepted' }
            $resultSession = if ($mode -eq 'wrong-session') { '00000000-0000-4000-8000-000000000001' } else { $sessionId }
            SendFrame ('{"messageType":"commandResult","requestId":"' + $command.requestId + '","sessionId":"' + $resultSession + '","command":"' + $command.command + '","status":"' + $status + '"}')
            if ($mode -eq 'wrong-session' -or $mode -eq 'invalid-focus-result') { [Threading.ManualResetEventSlim]::new($false).Wait(); exit 15 }
            if ($mode -eq 'no-terminal' -and $command.command -eq 'Stop') { exit 0 }
            if ($command.command -eq 'Stop') {
                SendFrame ('{"messageType":"lifecycle","sequence":"2","utc":"2026-01-01T00:00:01.000Z","monotonicMicroseconds":"1000000","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"SessionStopped","reason":"userStop","error":"none"}')
                exit 0
            }
        }
        """;
}
