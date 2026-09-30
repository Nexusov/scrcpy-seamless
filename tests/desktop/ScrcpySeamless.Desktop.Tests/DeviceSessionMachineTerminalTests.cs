using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Connects real redirected machine pipes to the actual automatic Desktop cleanup path.</summary>
public sealed partial class DeviceSessionViewModelTests
{
    private static readonly TimeSpan MachineScenarioWatchdog = TimeSpan.FromSeconds(25);
    private const int SyntheticFailedExitCode = 17;

    /// <summary>Completion-first disposal cannot lose the spontaneous result when terminal cleanup joins it.</summary>
    [AvaloniaFact]
    public async Task MachineCompletionBeforeTerminalObserverPreservesUnsuccessfulResult()
    {
        await RunMachineScenarioAsync("completion-first", async scenario =>
        {
            CompletionFirstSession controlled = Assert.IsType<CompletionFirstSession>(scenario.ForwardedSession);
            scenario.TerminalTrigger.Set();
            scenario.ExitRelease.Set();
            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog)).Reason);
            await controlled.DisposalEntered.Task.WaitAsync(MachineScenarioWatchdog);
            Assert.True(scenario.Actions.HasOwnedSession);
            controlled.ReleaseTerminal.TrySetResult();
            // Resuming after yield proves the real consumer already called BeginTerminalCleanup.
            await controlled.TerminalHandled.Task.WaitAsync(MachineScenarioWatchdog);
            controlled.ReleaseDisposal.TrySetResult();
            await AwaitMachineStatusAsync(scenario.Actions, () => !scenario.Actions.HasOwnedSession &&
                (scenario.Actions.Status.StartsWith("Native session ended:", StringComparison.Ordinal) ||
                    scenario.Actions.Status == "Native session stopped."));
            Assert.Equal("Native session ended: NativeFailure.", scenario.Actions.Status);
            Assert.Equal(0, controlled.StopCalls);
            Assert.Equal(1, controlled.DisposeCalls);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
            Assert.True(scenario.Child.HasExited);
            Assert.False(scenario.WireStopReceived.WaitOne(TimeSpan.Zero));
            Assert.Equal(1, scenario.Host.Starts);
        });
    }

    /// <summary>An unsuccessful spontaneous session preserves its result after successful cleanup.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticFailurePreservesSessionResultAfterSettledCleanup()
    {
        await RunMachineScenarioAsync("automatic-failure", async scenario =>
        {
            scenario.TerminalTrigger.Set();
            await scenario.AwaitWireStopAsync();
            Assert.True(scenario.Actions.HasOwnedSession);
            Assert.False(scenario.Native.Completion.IsCompleted);
            scenario.ExitRelease.Set();
            NativeExit result = await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog);
            Assert.Equal(NativeTerminationReason.NativeFailure, result.Reason);
            await AwaitReleasedMachineStatusAsync(scenario.Actions);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
            Assert.True(scenario.Child.HasExited);
            Assert.Null(scenario.Actions.ProcessId);
            Assert.Equal(new[] { NativeLifecycleEventType.NativeReady, NativeLifecycleEventType.FatalError,
                NativeLifecycleEventType.SessionStopped }, scenario.Actions.RecentLifecycleEvents.Select(item => item.EventType));
            MachineNativeSession.StopException terminalFailure = await Assert.ThrowsAsync<MachineNativeSession.StopException>(
                () => scenario.Native.StopAsync(NativeTerminationReason.NativeFailure, CancellationToken.None));
            Assert.Equal(MachineNativeSession.StopFailure.TerminalFailure, terminalFailure.Failure);
            Assert.Equal(result, terminalFailure.Exit);
            // Repeated real disposal proves settlement and resource release, not just process disappearance.
            await scenario.Native.DisposeAsync();
            Assert.Equal("Native session ended: NativeFailure.", scenario.Actions.Status);
            Assert.Equal(1, scenario.Host.Starts);

            scenario.Host.OnStart = null;
            await scenario.Actions.MirrorAsync();
            Assert.Equal(2, scenario.Host.Starts);
            Assert.True(scenario.Actions.HasOwnedSession);
            Assert.True(await scenario.Actions.StopAsync());
        });
    }

    /// <summary>Explicit Stop still reports an unsuccessful native terminal result.</summary>
    [AvaloniaFact]
    public async Task MachineExplicitStopKeepsTerminalFailureVisible()
    {
        await RunMachineScenarioAsync("explicit-failure", async scenario =>
        {
            Task<bool> stopping = scenario.Actions.StopAsync();
            await scenario.AwaitWireStopAsync();
            scenario.ExitRelease.Set();
            Assert.False(await stopping.WaitAsync(MachineScenarioWatchdog));
            Assert.Equal(NativeTerminationReason.NativeFailure, (await scenario.Native.Completion).Reason);
            Assert.False(scenario.Actions.HasOwnedSession);
            Assert.Contains("Stop failed", scenario.Actions.Status);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
        });
    }

    /// <summary>Normal native-window close retains its observed reason without relaunch.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticWindowClosePreservesObservedReason()
    {
        await RunMachineScenarioAsync("window-close", async scenario =>
        {
            scenario.TerminalTrigger.Set();
            await AwaitSequenceAsync(scenario.Actions, 2);
            scenario.ExitRelease.Set();
            Assert.Equal(NativeTerminationReason.WindowClosed,
                (await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog)).Reason);
            await AwaitReleasedMachineStatusAsync(scenario.Actions);
            Assert.Equal("Native session ended: WindowClosed.", scenario.Actions.Status);
            Assert.Equal(1, scenario.Host.Starts);
            Assert.False(scenario.WireStopReceived.WaitOne(TimeSpan.Zero));
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
        });
    }

    /// <summary>A genuine disposal failure stays visible even after the real child and pipes settle.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticFailureDoesNotHideDisposalFailureAfterExit()
    {
        await RunMachineScenarioAsync("automatic-failure", async scenario =>
        {
            scenario.TerminalTrigger.Set();
            await scenario.AwaitWireStopAsync();
            scenario.ExitRelease.Set();
            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog)).Reason);
            await AwaitMachineStatusAsync(scenario.Actions,
                () => scenario.Actions.Status.StartsWith("Native cleanup failed", StringComparison.Ordinal));
            Assert.True(scenario.Actions.HasOwnedSession);
            Assert.Contains(nameof(IOException), scenario.Actions.Status);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
        }, failDisposal: true);
    }

    /// <summary>Rejected internal Stop preserves escalation evidence after releasing the exact child.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticCleanupEscalationRemainsVisible()
    {
        await RunMachineScenarioAsync("reject-stop", async scenario =>
        {
            scenario.TerminalTrigger.Set();
            await scenario.AwaitWireStopAsync();
            await AwaitReleasedMachineStatusAsync(scenario.Actions);
            MachineNativeSession.StopException failure = await Assert.ThrowsAsync<MachineNativeSession.StopException>(
                () => scenario.Native.StopAsync(NativeTerminationReason.NativeFailure, CancellationToken.None));
            Assert.Equal(MachineNativeSession.StopFailure.Escalated, failure.Failure);
            Assert.Equal(NativeTerminationReason.NativeFailure, failure.Exit?.Reason);
            Assert.Equal("Native cleanup failed (Escalated); the exact child exited and was released.",
                scenario.Actions.Status);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
        });
    }

    /// <summary>Preserves the primary assertion if exact-fixture teardown also fails.</summary>
    private static async Task RunMachineScenarioAsync(string mode, Func<MachineScenario, Task> assertions,
        bool failDisposal = false)
    {
        using Fixture fixture = new();
        MachineScenario scenario = new(fixture, mode);
        Exception? primaryFailure = null;

        try
        {
            await scenario.StartAsync(failDisposal);
            await assertions(scenario);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        try
        {
            await scenario.DisposeAsync();
        }
        catch (Exception cleanupFailure)
        {
            if (primaryFailure is not null)
            {
                throw new AggregateException("Assertion and owned synthetic-fixture cleanup both failed.",
                    primaryFailure, cleanupFailure);
            }

            throw;
        }

        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
    }

    /// <summary>Ownership release and a final status must both be published before checking the outcome.</summary>
    private static Task AwaitReleasedMachineStatusAsync(DeviceSessionViewModel actions) =>
        AwaitMachineStatusAsync(actions, () => !actions.HasOwnedSession &&
            (actions.Status.StartsWith("Native session ended:", StringComparison.Ordinal) ||
                actions.Status.Contains("the exact child exited and was released", StringComparison.Ordinal)));

    /// <summary>Waits for final presentation with a subscribe-and-recheck semantic barrier.</summary>
    private static async Task AwaitMachineStatusAsync(DeviceSessionViewModel actions, Func<bool> predicate)
    {
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler changed = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.Status) && predicate())
            {
                observed.TrySetResult();
            }
        };
        actions.PropertyChanged += changed;

        try
        {
            if (predicate())
            {
                return;
            }

            await observed.Task.WaitAsync(MachineScenarioWatchdog, TestContext.Current.CancellationToken);
        }
        finally
        {
            actions.PropertyChanged -= changed;
        }
    }

    /// <summary>Owns only its synthetic child, private scripts and named synchronization handles.</summary>
    private sealed class MachineScenario : IAsyncDisposable
    {
        private readonly Fixture fixture;
        private readonly string mode;
        private readonly string eventPrefix = "Local\\scrcpy-desktop-terminal-" + Guid.NewGuid().ToString("N");
        private MachineNativeSession? native;
        public EventWaitHandle TerminalTrigger { get; }
        public EventWaitHandle WireStopReceived { get; }
        public EventWaitHandle ExitRelease { get; }
        public DeviceSessionViewModel Actions { get; private set; } = null!;
        public FakeHost Host { get; private set; } = null!;
        public MachineNativeSession Native => native!;
        public Process Child { get; private set; } = null!;
        public INativeInteractiveSession ForwardedSession { get; private set; } = null!;

        /// <summary>Creates private event gates before the redirected child can open them.</summary>
        public MachineScenario(Fixture fixture, string mode)
        {
            this.fixture = fixture;
            this.mode = mode;
            TerminalTrigger = new(false, EventResetMode.ManualReset, eventPrefix + "-trigger");
            WireStopReceived = new(false, EventResetMode.ManualReset, eventPrefix + "-received");
            ExitRelease = new(false, EventResetMode.ManualReset, eventPrefix + "-exit");
        }

        /// <summary>Hands the production machine session to the ordinary committed-launch ViewModel.</summary>
        public async Task StartAsync(bool failDisposal)
        {
            (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
            Actions = actions;
            Host = host;
            string scriptPath = Path.Combine(fixture.Directory, "desktop-machine-terminal.ps1");
            File.WriteAllText(scriptPath, DesktopMachineTerminalFixture);
            Host.OnStart = async (request, cancellationToken) =>
            {
                ProcessStartInfo startInfo = new("powershell.exe")
                {
                    WorkingDirectory = fixture.Directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                string[] arguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                    request.SessionId.Value.ToString("D"), mode, eventPrefix,
                    SyntheticFailedExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)];

                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                native = await MachineNativeSession.StartAsync(request.SessionId, startInfo, cancellationToken);
                // Capture a handle while the private child is gated alive; PID reuse cannot affect HasExited.
                Child = Process.GetProcessById(native.ProcessId);
                _ = Child.Handle;
                ForwardedSession = mode == "completion-first"
                    ? new CompletionFirstSession(native)
                    : failDisposal ? new FailedDisposalSession(native) : native;
                return ForwardedSession;
            };
            await Actions.MirrorAsync();
            Assert.NotNull(native);
            Assert.True(Actions.HasOwnedSession);
        }

        /// <summary>Proves the synthetic child actually read the internal wire command.</summary>
        public async Task AwaitWireStopAsync()
        {
            Assert.True(await Task.Run(() => WireStopReceived.WaitOne(MachineScenarioWatchdog),
                TestContext.Current.CancellationToken), "Synthetic child never received wire Stop.");
        }

        /// <summary>Opens all gates and settles the exact child without operating on process names.</summary>
        public async ValueTask DisposeAsync()
        {
            TerminalTrigger.Set();
            ExitRelease.Set();

            if (ForwardedSession is CompletionFirstSession controlled)
            {
                controlled.ReleaseTerminal.TrySetResult();
                controlled.ReleaseDisposal.TrySetResult();
            }

            try
            {
                if (native is not null)
                {
                    await native.DisposeAsync();
                }
            }
            finally
            {
                TerminalTrigger.Dispose();
                WireStopReceived.Dispose();
                ExitRelease.Dispose();
                Child?.Dispose();
            }
        }
    }

    /// <summary>Injects only a disposal fault while forwarding real process completion and lifecycle.</summary>
    private sealed class FailedDisposalSession(MachineNativeSession native) : INativeInteractiveSession
    {
        public SessionId SessionId => native.SessionId;
        public Task<NativeExit> Completion => native.Completion;
        public IAsyncEnumerable<NativeLifecycleObservation> ObserveLifecycleAsync(CancellationToken cancellationToken) =>
            native.ObserveLifecycleAsync(cancellationToken);
        public Task<NativeFocusOutcome> FocusWindowAsync(CancellationToken cancellationToken) =>
            native.FocusWindowAsync(cancellationToken);
        public Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken) =>
            native.StopAsync(reason, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.FromException(new IOException("Synthetic resource-disposal failure."));
    }

    /// <summary>Orders real completion, consumer delivery and disposal without changing machine results.</summary>
    private sealed class CompletionFirstSession(MachineNativeSession native) : INativeInteractiveSession
    {
        public SessionId SessionId => native.SessionId;
        public Task<NativeExit> Completion => native.Completion;
        public TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseTerminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TerminalHandled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        /// <summary>Withholds only terminal delivery until actual completion-owned disposal has begun.</summary>
        public async IAsyncEnumerable<NativeLifecycleObservation> ObserveLifecycleAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (NativeLifecycleObservation observation in native.ObserveLifecycleAsync(cancellationToken))
            {
                bool terminal = observation.EventType == NativeLifecycleEventType.SessionStopped;

                if (terminal)
                {
                    await ReleaseTerminal.Task.WaitAsync(cancellationToken);
                }

                yield return observation;

                if (terminal)
                {
                    TerminalHandled.TrySetResult();
                }
            }
        }

        /// <summary>Forwards explicit Stop while exposing duplicate-operation regressions.</summary>
        public Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken)
        {
            StopCalls++;
            return native.StopAsync(reason, cancellationToken);
        }

        /// <summary>Forwards native focus unchanged.</summary>
        public Task<NativeFocusOutcome> FocusWindowAsync(CancellationToken cancellationToken) =>
            native.FocusWindowAsync(cancellationToken);

        /// <summary>Holds resource-disposal return after real exact-child settlement.</summary>
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            await native.DisposeAsync();
            DisposalEntered.TrySetResult();
            await ReleaseDisposal.Task;
        }
    }

    private const string DesktopMachineTerminalFixture = """
        param([string]$sessionId, [string]$mode, [string]$eventPrefix, [int]$failureExitCode)
        $ErrorActionPreference = 'Stop'
        $inputPipe = [Console]::OpenStandardInput()
        $outputPipe = [Console]::OpenStandardOutput()
        $trigger = [Threading.EventWaitHandle]::OpenExisting($eventPrefix + '-trigger')
        $received = [Threading.EventWaitHandle]::OpenExisting($eventPrefix + '-received')
        $exitRelease = [Threading.EventWaitHandle]::OpenExisting($eventPrefix + '-exit')
        function ReadExactly([int]$count) {
            $bytes = New-Object byte[] $count
            $offset = 0
            while ($offset -lt $count) {
                $read = $inputPipe.Read($bytes, $offset, $count - $offset)

                if (!$read) {
                    throw 'truncated synthetic fixture input'
                }

                $offset += $read
            }
            return ,$bytes
        }
        function ReadFrame {
            $header = ReadExactly 4
            $body = ReadExactly ([BitConverter]::ToUInt32($header, 0))
            return [Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
        }
        function SendFrame([string]$json) {
            $body = [Text.UTF8Encoding]::new($false).GetBytes($json)
            $header = [BitConverter]::GetBytes([uint32]$body.Length)
            $outputPipe.Write($header, 0, $header.Length)
            $outputPipe.Write($body, 0, $body.Length)
            $outputPipe.Flush()
        }
        function SendLifecycle([int]$sequence, [string]$eventType, [string]$reason, [string]$errorCode) {
            SendFrame ('{"messageType":"lifecycle","sequence":"' + $sequence + '","utc":"2026-01-01T00:00:01.000Z","monotonicMicroseconds":"1000000","sessionId":"' + $sessionId + '","connectionAttemptId":null,"subsystem":"native","eventType":"' + $eventType + '","reason":"' + $reason + '","error":"' + $errorCode + '"}')
        }
        $hello = ReadFrame

        if ($hello.messageType -ne 'hello') {
            throw 'synthetic fixture expected hello'
        }

        SendFrame '{"messageType":"helloResult","product":"scrcpy-seamless","protocolMajor":1,"protocolMinor":0,"status":"accepted","capabilities":["focus-window","lifecycle-v1","stop"]}'
        SendLifecycle 1 'NativeReady' 'none' 'none'

        if ($mode -ne 'explicit-failure') {
            $trigger.WaitOne() | Out-Null
        }

        if ($mode -eq 'window-close') {
            SendLifecycle 2 'SessionStopped' 'windowClosed' 'none'
            $exitRelease.WaitOne() | Out-Null
            exit 0
        }

        if ($mode -eq 'completion-first') {
            SendLifecycle 2 'SessionStopped' 'nativeFailure' 'none'
            $exitRelease.WaitOne() | Out-Null
            exit $failureExitCode
        }

        if ($mode -ne 'explicit-failure') {
            SendLifecycle 2 'FatalError' 'nativeFailure' 'internalFailure'
        }

        $command = ReadFrame

        if ($command.command -ne 'Stop') {
            throw 'synthetic fixture expected Stop'
        }

        $received.Set() | Out-Null
        $status = 'accepted'

        if ($mode -eq 'reject-stop') {
            $status = 'failed'
        }

        SendFrame ('{"messageType":"commandResult","requestId":"' + $command.requestId + '","sessionId":"' + $sessionId + '","command":"Stop","status":"' + $status + '"}')

        if ($mode -eq 'reject-stop') {
            $exitRelease.WaitOne() | Out-Null
            exit $failureExitCode
        }

        if ($mode -eq 'explicit-failure') {
            SendLifecycle 2 'FatalError' 'nativeFailure' 'internalFailure'
        }

        SendLifecycle 3 'SessionStopped' 'nativeFailure' 'none'
        $exitRelease.WaitOne() | Out-Null
        exit $failureExitCode
        """;
}
