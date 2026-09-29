using System.Diagnostics;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

/// <summary>Checks command ownership when a real redirected child ends normally.</summary>
public sealed class MachineNativeTerminalTests
{
    private static readonly TimeSpan WriteFailureWatchdog = TimeSpan.FromSeconds(7);

    /// <summary>A terminal observation settles an already received command without inventing success.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalLifecycleSettlesUnansweredFocusBeforeDisposal(bool cancelCallerAfterSend)
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "terminal-fixture.ps1");
        File.WriteAllText(scriptPath, TerminalFixture);
        string receivedName = "Local\\scrcpy-focus-received-" + Guid.NewGuid().ToString("N");
        string releaseName = "Local\\scrcpy-focus-release-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle received = new(false, EventResetMode.ManualReset, receivedName);
        using EventWaitHandle release = new(false, EventResetMode.ManualReset, releaseName);
        using CancellationTokenSource canceledWait = new();
        MachineNativeSession? session = null;

        try
        {
            SessionId sessionId = SessionId.New();
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, receivedName, releaseName),
                TestContext.Current.CancellationToken);
            CancellationToken callerToken = cancelCallerAfterSend
                ? canceledWait.Token
                : TestContext.Current.CancellationToken;
            Task<NativeFocusOutcome> focus = session.FocusWindowAsync(callerToken);
            Assert.True(await Task.Run(() => received.WaitOne(TimeSpan.FromSeconds(5))));

            if (cancelCallerAfterSend)
            {
                canceledWait.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => focus);
            }

            release.Set();
            Assert.Equal(NativeTerminationReason.WindowClosed,
                (await session.Completion.WaitAsync(TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken)).Reason);

            if (!cancelCallerAfterSend)
            {
                await Assert.ThrowsAnyAsync<IOException>(() => focus.WaitAsync(TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));
            }

            Assert.Equal((0, 0), session.OwnedCommandCounts);
            await session.DisposeAsync();
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

    /// <summary>Disposal after a terminal observation waits for the closing child without issuing Stop.</summary>
    [Fact]
    public async Task DisposeAfterTerminalBeforeExitPreservesWindowClose()
    {
        await AssertTerminalStopRaceAsync(stopBeforeTerminal: false);
    }

    /// <summary>A terminal observation winning an in-flight Stop preserves the observed exit.</summary>
    [Fact]
    public async Task TerminalBeforeStopResultPreservesWindowClose()
    {
        await AssertTerminalStopRaceAsync(stopBeforeTerminal: true);
    }

    /// <summary>A queued command write failing after native termination cannot rewrite the observed exit.</summary>
    [Fact]
    public async Task OutgoingWriteFailureAfterTerminalPreservesWindowClose()
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-late-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "terminal-fixture.ps1");
        File.WriteAllText(scriptPath, TerminalFixture);
        string triggerName = "Local\\scrcpy-late-write-" + Guid.NewGuid().ToString("N");
        string exitReleaseName = "Local\\scrcpy-late-exit-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle trigger = new(false, EventResetMode.ManualReset, triggerName);
        using EventWaitHandle exitRelease = new(false, EventResetMode.ManualReset, exitReleaseName);
        using CancellationTokenSource writeRelease = new();
        MachineNativeSession? session = null;

        try
        {
            SessionId sessionId = SessionId.New();
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, string.Empty, string.Empty,
                    exitReleaseName: exitReleaseName, terminalTriggerName: triggerName),
                TestContext.Current.CancellationToken,
                wrapStandardInput: stream => new LateFailWriteStream(stream, trigger, writeRelease.Token));
            Task<NativeFocusOutcome> focus = session.FocusWindowAsync(TestContext.Current.CancellationToken);
            using CancellationTokenSource terminalDeadline = new(TimeSpan.FromSeconds(5));
            await foreach (NativeLifecycleObservation observation in
                session.ObserveLifecycleAsync(terminalDeadline.Token))
            {
                if (observation.EventType == NativeLifecycleEventType.SessionStopped)
                {
                    break;
                }
            }

            writeRelease.Cancel();
            exitRelease.Set();
            Assert.Equal(NativeTerminationReason.WindowClosed,
                (await session.Completion.WaitAsync(TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken)).Reason);
            await Assert.ThrowsAnyAsync<IOException>(() => focus);
        }
        finally
        {
            writeRelease.Cancel();
            exitRelease.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Holds the synthetic child alive after its terminal event to expose Stop settlement races.</summary>
    private static async Task AssertTerminalStopRaceAsync(bool stopBeforeTerminal)
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-stop-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "terminal-fixture.ps1");
        File.WriteAllText(scriptPath, TerminalFixture);
        string receivedName = "Local\\scrcpy-race-received-" + Guid.NewGuid().ToString("N");
        string releaseName = "Local\\scrcpy-race-release-" + Guid.NewGuid().ToString("N");
        string exitReleaseName = "Local\\scrcpy-race-exit-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle received = new(false, EventResetMode.ManualReset, receivedName);
        using EventWaitHandle release = new(false, EventResetMode.ManualReset, releaseName);
        using EventWaitHandle exitRelease = new(false, EventResetMode.ManualReset, exitReleaseName);
        MachineNativeSession? session = null;

        try
        {
            SessionId sessionId = SessionId.New();
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, receivedName, releaseName,
                    expectedCommand: stopBeforeTerminal ? "Stop" : "FocusWindow",
                    exitReleaseName: exitReleaseName), TestContext.Current.CancellationToken);
            Task? stop = stopBeforeTerminal
                ? session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken)
                : null;
            Task<NativeFocusOutcome>? focus = stopBeforeTerminal
                ? null
                : session.FocusWindowAsync(TestContext.Current.CancellationToken);
            Assert.True(await Task.Run(() => received.WaitOne(TimeSpan.FromSeconds(5))));
            release.Set();

            using CancellationTokenSource terminalDeadline = new(TimeSpan.FromSeconds(5));
            await foreach (NativeLifecycleObservation observation in
                session.ObserveLifecycleAsync(terminalDeadline.Token))
            {
                if (observation.EventType == NativeLifecycleEventType.SessionStopped)
                {
                    break;
                }
            }

            Task? disposal = stopBeforeTerminal ? null : session.DisposeAsync().AsTask();
            exitRelease.Set();
            NativeExit exit = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(NativeTerminationReason.WindowClosed, exit.Reason);

            if (focus is not null)
            {
                await Assert.ThrowsAnyAsync<IOException>(() => focus);
            }

            if (stop is not null)
            {
                await stop;
            }

            if (disposal is not null)
            {
                await disposal;
            }
        }
        finally
        {
            release.Set();
            exitRelease.Set();

            if (session is not null)
            {
                await session.DisposeAsync();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An interrupted outgoing header ends the channel instead of retrying shifted bytes.</summary>
    [Fact]
    public async Task PartiallyPublishedOutgoingFrameFailsTheExactChannel()
    {
        await AssertInterruptedWriteFailsAsync(blockUntilDeadline: false);
    }

    /// <summary>A blocked outgoing write obeys the finite production deadline and exact-child cleanup.</summary>
    [Fact]
    public async Task BlockedOutgoingFrameFailsWithinTheWriteBudget()
    {
        await AssertInterruptedWriteFailsAsync(blockUntilDeadline: true);
    }

    /// <summary>Unknown and explicitly fatal native terminal reasons remain conservative.</summary>
    [Theory]
    [InlineData("unknown", 0)]
    [InlineData("nativeFailure", 17)]
    public async Task UnknownOrFatalTerminalDoesNotBecomeWindowClose(string nativeReason, int exitCode)
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-reason-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "terminal-fixture.ps1");
        File.WriteAllText(scriptPath, TerminalFixture);
        string receivedName = "Local\\scrcpy-reason-received-" + Guid.NewGuid().ToString("N");
        string releaseName = "Local\\scrcpy-reason-release-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle received = new(false, EventResetMode.ManualReset, receivedName);
        using EventWaitHandle release = new(false, EventResetMode.ManualReset, releaseName);
        MachineNativeSession? session = null;

        try
        {
            SessionId sessionId = SessionId.New();
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, receivedName, releaseName,
                    nativeReason, exitCode), TestContext.Current.CancellationToken);
            Task<NativeFocusOutcome> focus = session.FocusWindowAsync(TestContext.Current.CancellationToken);
            Assert.True(await Task.Run(() => received.WaitOne(TimeSpan.FromSeconds(5))));
            release.Set();
            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await session.Completion.WaitAsync(TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken)).Reason);
            await Assert.ThrowsAnyAsync<IOException>(() => focus);
            Assert.Equal((0, 0), session.OwnedCommandCounts);
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

    /// <summary>Runs one synthetic child through a controlled production write boundary.</summary>
    private static async Task AssertInterruptedWriteFailsAsync(bool blockUntilDeadline)
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-machine-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "terminal-fixture.ps1");
        File.WriteAllText(scriptPath, TerminalFixture);
        MachineNativeSession? session = null;
        InterruptingWriteStream? input = null;

        try
        {
            SessionId sessionId = SessionId.New();
            session = await MachineNativeSession.StartAsync(sessionId,
                CreateStartInfo(scriptPath, sessionId, string.Empty, string.Empty),
                TestContext.Current.CancellationToken,
                wrapStandardInput: stream => input = new InterruptingWriteStream(stream, blockUntilDeadline));
            Task<NativeFocusOutcome> focus = session.FocusWindowAsync(TestContext.Current.CancellationToken);

            if (blockUntilDeadline)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    focus.WaitAsync(WriteFailureWatchdog, TestContext.Current.CancellationToken));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() =>
                    focus.WaitAsync(WriteFailureWatchdog, TestContext.Current.CancellationToken));
            }

            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await session.Completion.WaitAsync(WriteFailureWatchdog,
                    TestContext.Current.CancellationToken)).Reason);
            Assert.Equal(3, input!.WriteCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.FocusWindowAsync(TestContext.Current.CancellationToken));
            Assert.Equal((0, 0), session.OwnedCommandCounts);
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

    /// <summary>Forwards hello, then interrupts only the first command header write.</summary>
    private sealed class InterruptingWriteStream(Stream inner, bool blockUntilDeadline) : Stream
    {
        private int writeCount;

        public int WriteCount => Volatile.Read(ref writeCount);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int currentWrite = Interlocked.Increment(ref writeCount);

            if (currentWrite <= 2)
            {
                await inner.WriteAsync(buffer, cancellationToken);
                return;
            }

            if (currentWrite == 3 && blockUntilDeadline)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return;
            }

            if (currentWrite == 3)
            {
                await inner.WriteAsync(buffer[..2], cancellationToken);
                throw new IOException("The test interrupted a partially published frame header.");
            }

            throw new InvalidOperationException("A terminal channel attempted another frame write.");
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Holds one Focus write until the synthetic child has emitted SessionStopped.</summary>
    private sealed class LateFailWriteStream(Stream inner, EventWaitHandle trigger,
        CancellationToken release) : Stream
    {
        private int writeCount;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int currentWrite = Interlocked.Increment(ref writeCount);

            if (currentWrite <= 2)
            {
                await inner.WriteAsync(buffer, cancellationToken);
                return;
            }

            trigger.Set();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, release);
            }
            catch (OperationCanceledException) when (release.IsCancellationRequested)
            {
                // The test explicitly released this previously queued write.
            }

            throw new IOException("The test released a queued write after native termination.");
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Starts only a synthetic PowerShell child with redirected protocol pipes.</summary>
    private static ProcessStartInfo CreateStartInfo(string scriptPath, SessionId sessionId,
        string receivedName, string releaseName, string terminalReason = "windowClosed",
        int exitCode = 0, string expectedCommand = "FocusWindow", string exitReleaseName = "",
        string terminalTriggerName = "")
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
        startInfo.ArgumentList.Add(receivedName);
        startInfo.ArgumentList.Add(releaseName);
        startInfo.ArgumentList.Add(terminalReason);
        startInfo.ArgumentList.Add(exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(expectedCommand);
        startInfo.ArgumentList.Add(exitReleaseName);
        startInfo.ArgumentList.Add(terminalTriggerName);
        return startInfo;
    }

    private const string TerminalFixture = """
        param([string]$sessionId, [string]$receivedName, [string]$releaseName, [string]$terminalReason, [int]$exitCode, [string]$expectedCommand, [string]$exitReleaseName, [string]$terminalTriggerName)
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
        if ($terminalTriggerName) {
            $terminalTrigger = [Threading.EventWaitHandle]::OpenExisting($terminalTriggerName)
            $terminalTrigger.WaitOne() | Out-Null
        } else {
            $command = ReadFrame
            if ($command.command -ne $expectedCommand) { exit 13 }
            $received = [Threading.EventWaitHandle]::OpenExisting($receivedName)
            $release = [Threading.EventWaitHandle]::OpenExisting($releaseName)
            $received.Set() | Out-Null
            $release.WaitOne() | Out-Null
        }
        SendFrame ('{"messageType":"lifecycle","sequence":"2","utc":"2026-01-01T00:00:01.000Z","monotonicMicroseconds":"1000000","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"SessionStopped","reason":"' + $terminalReason + '","error":"none"}')
        if ($exitReleaseName) {
            $exitRelease = [Threading.EventWaitHandle]::OpenExisting($exitReleaseName)
            $exitRelease.WaitOne() | Out-Null
        }
        exit $exitCode
        """;
}
