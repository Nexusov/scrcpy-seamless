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
            SessionEvidenceSnapshot initial = await CaptureStartedMachineEvidenceAsync(scenario);
            scenario.TerminalTrigger.Set();
            scenario.ExitRelease.Set();
            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog)).Reason);
            await controlled.DisposalEntered.Task.WaitAsync(MachineScenarioWatchdog);
            Assert.True(scenario.Actions.HasOwnedSession);
            SessionEvidenceSnapshot disposing = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCompletionState.Completed, disposing.CompletionState);
            Assert.Equal(NativeTerminationReason.NativeFailure, disposing.TerminalResult?.Reason);
            Assert.Equal(SessionCleanupIntent.CompletionCleanup, disposing.Cleanup.Intent);
            Assert.Equal(SessionCleanupState.InProgress, disposing.Cleanup.State);
            Assert.False(disposing.Cleanup.OwnershipReleased);
            controlled.ReleaseTerminal.TrySetResult();
            // Resuming after yield proves the real consumer already called BeginTerminalCleanup.
            await controlled.TerminalHandled.Task.WaitAsync(MachineScenarioWatchdog);
            SessionEvidenceSnapshot joined = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCleanupIntent.CompletionCleanup, joined.Cleanup.Intent);
            Assert.Equal(SessionCleanupState.InProgress, joined.Cleanup.State);
            Assert.Equal(SessionStopCallState.NotRequested, joined.Cleanup.StopCallState);
            Assert.Equal(NativeTerminationReason.NativeFailure, joined.TerminalResult?.Reason);
            Assert.Equal(0, controlled.StopCalls);
            Assert.Equal(1, controlled.DisposeCalls);
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
            SessionEvidenceSnapshot final = AssertReleasedMachineEvidence(scenario,
                NativeTerminationReason.NativeFailure);
            Assert.Equal(initial.SessionId, final.SessionId);
            Assert.Equal(SessionCleanupIntent.CompletionCleanup, final.Cleanup.Intent);
            Assert.Equal(SessionStopCallState.NotRequested, final.Cleanup.StopCallState);
            Assert.Equal(SessionCleanupState.InProgress, disposing.Cleanup.State);
            Assert.Equal(SessionCleanupIntent.CompletionCleanup, disposing.Cleanup.Intent);
            Assert.Equal(SessionCleanupIntent.CompletionCleanup, joined.Cleanup.Intent);
        });
    }

    /// <summary>Snapshots keep validated same-session attempt transitions ordered and non-destructive.</summary>
    [AvaloniaFact]
    public async Task MachineEvidencePreservesDifferentAttemptObservationsAcrossCleanup()
    {
        await RunMachineScenarioAsync("attempts", async scenario =>
        {
            SessionEvidenceSnapshot initial = await CaptureStartedMachineEvidenceAsync(scenario);
            scenario.TerminalTrigger.Set();
            await AwaitSequenceAsync(scenario.Actions, 8);
            SessionEvidenceSnapshot observed = scenario.Actions.CaptureSessionEvidence();
            NativeLifecycleEventType[] expectedEvents = [NativeLifecycleEventType.NativeReady,
                NativeLifecycleEventType.Connecting, NativeLifecycleEventType.StreamStarted,
                NativeLifecycleEventType.TransportLost, NativeLifecycleEventType.ReconnectScheduled,
                NativeLifecycleEventType.Reconnecting, NativeLifecycleEventType.StreamResumed,
                NativeLifecycleEventType.SessionStopped];
            Assert.Equal(expectedEvents, observed.Events.Select(item => item.EventType));
            Assert.All(observed.Events, item => Assert.Equal(scenario.Native.SessionId, item.SessionId));
            ConnectionAttemptId? firstAttempt = observed.Events[1].ConnectionAttemptId;
            ConnectionAttemptId? secondAttempt = observed.Events[5].ConnectionAttemptId;
            Assert.NotNull(firstAttempt);
            Assert.NotNull(secondAttempt);
            Assert.NotEqual(firstAttempt, secondAttempt);
            Assert.Equal(firstAttempt, observed.Events[2].ConnectionAttemptId);
            Assert.Equal(firstAttempt, observed.Events[3].ConnectionAttemptId);
            Assert.Equal(secondAttempt, observed.Events[6].ConnectionAttemptId);
            SessionEvidenceSnapshot repeated = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(observed.Events, repeated.Events);
            Assert.Equal((ulong)8, repeated.TotalObserved);
            Assert.Single(initial.Events);
            scenario.ExitRelease.Set();
            await AwaitReleasedMachineStatusAsync(scenario.Actions);
            SessionEvidenceSnapshot final = AssertReleasedMachineEvidence(scenario,
                NativeTerminationReason.WindowClosed);
            Assert.Equal(observed.Events, final.Events);
            Assert.Equal(1, scenario.Host.Starts);
        });
    }

    /// <summary>A closed command direction needs no successful Stop acknowledgement to preserve its result.</summary>
    [AvaloniaFact]
    public async Task MachineClosedCommandTerminalRetainsEvidenceWithoutSuccessfulStopAcknowledgement()
    {
        await RunMachineScenarioAsync("closed-command", async scenario =>
        {
            SessionEvidenceSnapshot initial = await CaptureStartedMachineEvidenceAsync(scenario);
            scenario.TerminalTrigger.Set();
            await AwaitSequenceAsync(scenario.Actions, 3);
            Assert.False(scenario.Native.Completion.IsCompleted);
            scenario.ExitRelease.Set();
            Assert.Equal(NativeTerminationReason.NativeFailure,
                (await scenario.Native.Completion.WaitAsync(MachineScenarioWatchdog)).Reason);
            await AwaitReleasedMachineStatusAsync(scenario.Actions);
            Assert.Equal("Native session ended: NativeFailure.", scenario.Actions.Status);
            SessionEvidenceSnapshot final = AssertReleasedMachineEvidence(scenario,
                NativeTerminationReason.NativeFailure);
            Assert.Equal(initial.SessionId, final.SessionId);
            Assert.Equal(new[] { NativeLifecycleEventType.NativeReady, NativeLifecycleEventType.FatalError,
                NativeLifecycleEventType.SessionStopped }, final.Events.Select(item => item.EventType));
            Assert.Equal(SessionCleanupIntent.AutomaticTerminal, final.Cleanup.Intent);
            Assert.Equal(SessionStopCallState.Failed, final.Cleanup.StopCallState);
            Assert.Equal(MachineNativeSession.StopFailure.TerminalFailure, final.Cleanup.StopFailure);
            Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
            // The fixture never reads or acknowledges a Stop; managed queued/written state remains unknown.
            Assert.False(scenario.WireStopReceived.WaitOne(TimeSpan.Zero));
            Assert.True(scenario.Child.HasExited);
        });
    }

    /// <summary>An unsuccessful spontaneous session preserves its result after successful cleanup.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticFailurePreservesSessionResultAfterSettledCleanup()
    {
        await RunMachineScenarioAsync("automatic-failure", async scenario =>
        {
            SessionEvidenceSnapshot initial = await CaptureStartedMachineEvidenceAsync(scenario);
            scenario.TerminalTrigger.Set();
            await scenario.AwaitWireStopAsync();
            Assert.True(scenario.Actions.HasOwnedSession);
            Assert.False(scenario.Native.Completion.IsCompleted);
            SessionEvidenceSnapshot stopping = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCleanupIntent.AutomaticTerminal, stopping.Cleanup.Intent);
            Assert.Equal(SessionCleanupState.InProgress, stopping.Cleanup.State);
            Assert.Equal(SessionStopCallState.InProgress, stopping.Cleanup.StopCallState);
            Assert.Equal(SessionCompletionState.InProgress, stopping.CompletionState);
            Assert.False(stopping.Cleanup.OwnershipReleased);
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
            SessionEvidenceSnapshot final = AssertReleasedMachineEvidence(scenario,
                NativeTerminationReason.NativeFailure);
            Assert.Equal(result, final.TerminalResult);
            Assert.Equal(SessionCleanupIntent.AutomaticTerminal, final.Cleanup.Intent);
            Assert.Equal(SessionStopCallState.Failed, final.Cleanup.StopCallState);
            Assert.Equal(MachineNativeSession.StopFailure.TerminalFailure, final.Cleanup.StopFailure);
            Assert.Equal(SessionCleanupState.NotStarted, initial.Cleanup.State);
            Assert.Equal(SessionCleanupState.InProgress, stopping.Cleanup.State);
            Assert.Single(initial.Events);
            Assert.Equal((ulong)1, initial.TotalObserved);
            Assert.Equal((ulong)3, final.TotalObserved);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<NativeLifecycleObservation>)final.Events).Clear());

            SavedProfileChoice? selectedProfile = scenario.Actions.SelectedLaunchProfile;
            scenario.Actions.SelectedLaunchProfile = null;
            await scenario.Actions.MirrorAsync();
            Assert.Equal(1, scenario.Host.Starts);
            SessionEvidenceSnapshot afterRejectedLaunch = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(final.SessionId, afterRejectedLaunch.SessionId);
            Assert.Equal(final.TerminalResult, afterRejectedLaunch.TerminalResult);
            Assert.Equal(final.Cleanup, afterRejectedLaunch.Cleanup);
            Assert.Equal(final.Events, afterRejectedLaunch.Events);
            scenario.Actions.SelectedLaunchProfile = selectedProfile;

            scenario.Host.OnStart = (_, _) => throw new IOException("Synthetic pre-acquisition start failure.");
            await scenario.Actions.MirrorAsync();
            Assert.Equal(2, scenario.Host.Starts);
            SessionEvidenceSnapshot afterFailedStart = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(final.SessionId, afterFailedStart.SessionId);
            Assert.Equal(final.TerminalResult, afterFailedStart.TerminalResult);
            Assert.Equal(final.Cleanup, afterFailedStart.Cleanup);
            Assert.Equal(final.Events, afterFailedStart.Events);

            scenario.Host.OnStart = null;
            await scenario.Actions.MirrorAsync();
            Assert.Equal(3, scenario.Host.Starts);
            Assert.True(scenario.Actions.HasOwnedSession);
            SessionEvidenceSnapshot replacement = scenario.Actions.CaptureSessionEvidence();
            Assert.NotEqual(final.SessionId, replacement.SessionId);
            Assert.Null(replacement.AcceptedHandshake);
            Assert.Null(replacement.TerminalResult);
            Assert.Equal(SessionCleanupState.NotStarted, replacement.Cleanup.State);
            Assert.Equal(result, final.TerminalResult);
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
            SessionEvidenceSnapshot final = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCleanupIntent.ExplicitStop, final.Cleanup.Intent);
            Assert.Equal(SessionStopCallState.Failed, final.Cleanup.StopCallState);
            Assert.Equal(MachineNativeSession.StopFailure.TerminalFailure, final.Cleanup.StopFailure);
            Assert.Equal(NativeTerminationReason.NativeFailure, final.TerminalResult?.Reason);
            Assert.True(final.Cleanup.OwnershipReleased);
        });
    }

    /// <summary>Normal native-window close retains its observed reason without relaunch.</summary>
    [AvaloniaFact]
    public async Task MachineAutomaticWindowClosePreservesObservedReason()
    {
        await RunMachineScenarioAsync("window-close", async scenario =>
        {
            SessionEvidenceSnapshot initial = await CaptureStartedMachineEvidenceAsync(scenario);
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
            SessionEvidenceSnapshot final = AssertReleasedMachineEvidence(scenario,
                NativeTerminationReason.WindowClosed);
            Assert.Equal(initial.AcceptedHandshake, final.AcceptedHandshake);
            Assert.Equal(SessionCleanupIntent.AutomaticTerminal, final.Cleanup.Intent);
            Assert.Equal(SessionStopCallState.Succeeded, final.Cleanup.StopCallState);
            Assert.Null(final.Cleanup.StopFailure);
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
            SessionEvidenceSnapshot final = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCleanupState.Failed, final.Cleanup.State);
            Assert.Equal(SessionCleanupFailureCategory.DisposalOrOwnedWork, final.Cleanup.FailureCategory);
            Assert.Equal(NativeTerminationReason.NativeFailure, final.TerminalResult?.Reason);
            Assert.True(final.Cleanup.ExactChildExitObserved);
            Assert.False(final.Cleanup.OwnershipReleased);
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
            SessionEvidenceSnapshot final = scenario.Actions.CaptureSessionEvidence();
            Assert.Equal(SessionCleanupIntent.AutomaticTerminal, final.Cleanup.Intent);
            Assert.Equal(SessionCleanupState.Succeeded, final.Cleanup.State);
            Assert.Equal(SessionStopCallState.Failed, final.Cleanup.StopCallState);
            Assert.Equal(MachineNativeSession.StopFailure.Escalated, final.Cleanup.StopFailure);
            Assert.Equal(SessionCleanupFailureCategory.StopOperation, final.Cleanup.FailureCategory);
            Assert.True(final.Cleanup.OwnershipReleased);
        });
    }

    /// <summary>Checks actual accepted values and proves repeated captures do not consume or start work.</summary>
    private static async Task<SessionEvidenceSnapshot> CaptureStartedMachineEvidenceAsync(MachineScenario scenario)
    {
        await AwaitSequenceAsync(scenario.Actions, 1);
        SessionEvidenceSnapshot snapshot = scenario.Actions.CaptureSessionEvidence();
        NativeHandshakeObservation handshake = Assert.IsType<NativeHandshakeObservation>(snapshot.AcceptedHandshake);
        Assert.Same(scenario.Native.AcceptedHandshake, handshake);
        Assert.Equal("scrcpy-seamless", handshake.Product);
        Assert.Equal(1, handshake.ProtocolMajor);
        Assert.Equal(0, handshake.ProtocolMinor);
        Assert.Equal(["focus-window", "lifecycle-v1", "stop"], handshake.Capabilities);
        Assert.Equal(scenario.Native.SessionId.Value, snapshot.SessionId);
        Assert.Equal(scenario.Native.ProcessId, snapshot.ProcessId);
        Assert.True(snapshot.NativeReadyObserved);
        Assert.Equal(SessionCompletionState.InProgress, snapshot.CompletionState);
        Assert.Null(snapshot.TerminalResult);
        Assert.Equal(SessionCleanupState.NotStarted, snapshot.Cleanup.State);
        Assert.Single(snapshot.Events);
        string status = scenario.Actions.Status;
        SessionEvidenceSnapshot repeated = scenario.Actions.CaptureSessionEvidence();
        Assert.Equal(snapshot.Events, repeated.Events);
        Assert.Equal(snapshot.TotalObserved, repeated.TotalObserved);
        Assert.Equal(status, scenario.Actions.Status);
        Assert.True(scenario.Actions.HasOwnedSession);
        Assert.False(scenario.Native.Completion.IsCompleted);
        Assert.False(scenario.Child.HasExited);
        Assert.Equal(1, scenario.Host.Starts);
        Assert.Equal((0, 0), scenario.Native.OwnedCommandCounts);
        Assert.False(scenario.WireStopReceived.WaitOne(TimeSpan.Zero));
        return snapshot;
    }

    /// <summary>Checks detached terminal evidence after exact-child and owner settlement.</summary>
    private static SessionEvidenceSnapshot AssertReleasedMachineEvidence(MachineScenario scenario,
        NativeTerminationReason reason)
    {
        SessionEvidenceSnapshot snapshot = scenario.Actions.CaptureSessionEvidence();
        Assert.Equal(scenario.Native.SessionId.Value, snapshot.SessionId);
        Assert.Equal(scenario.Native.ProcessId, snapshot.ProcessId);
        Assert.Same(scenario.Native.AcceptedHandshake, snapshot.AcceptedHandshake);
        Assert.True(snapshot.NativeReadyObserved);
        Assert.Equal(SessionCompletionState.Completed, snapshot.CompletionState);
        Assert.Equal(reason, snapshot.TerminalResult?.Reason);
        Assert.Equal(SessionCleanupState.Succeeded, snapshot.Cleanup.State);
        Assert.True(snapshot.Cleanup.ExactChildExitObserved);
        Assert.True(snapshot.Cleanup.OwnershipReleased);
        Assert.False(scenario.Actions.HasOwnedSession);
        return snapshot;
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
    private sealed class FailedDisposalSession(MachineNativeSession native) : INativeInteractiveSession,
        INativeHandshakeEvidence
    {
        public SessionId SessionId => native.SessionId;
        public int ProcessId => native.ProcessId;
        public NativeHandshakeObservation? AcceptedHandshake => native.AcceptedHandshake;
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
    private sealed class CompletionFirstSession(MachineNativeSession native) : INativeInteractiveSession,
        INativeHandshakeEvidence
    {
        public SessionId SessionId => native.SessionId;
        public int ProcessId => native.ProcessId;
        public NativeHandshakeObservation? AcceptedHandshake => native.AcceptedHandshake;
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
        function SendLifecycle([int]$sequence, [string]$eventType, [string]$reason, [string]$errorCode, [string]$attempt = '', [string]$subsystem = 'native') {
            $attemptJson = 'null'

            if ($attempt) {
                $attemptJson = '"' + $attempt + '"'
            }

            SendFrame ('{"messageType":"lifecycle","sequence":"' + $sequence + '","utc":"2026-01-01T00:00:01.000Z","monotonicMicroseconds":"1000000","sessionId":"' + $sessionId + '","connectionAttemptId":' + $attemptJson + ',"subsystem":"' + $subsystem + '","eventType":"' + $eventType + '","reason":"' + $reason + '","error":"' + $errorCode + '"}')
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

        if ($mode -eq 'closed-command') {
            SendLifecycle 2 'FatalError' 'nativeFailure' 'internalFailure'
            SendLifecycle 3 'SessionStopped' 'nativeFailure' 'none'
            $exitRelease.WaitOne() | Out-Null
            exit $failureExitCode
        }

        if ($mode -eq 'attempts') {
            $firstAttempt = '11111111-1111-4111-8111-111111111111'
            $secondAttempt = '22222222-2222-4222-8222-222222222222'
            SendLifecycle 2 'Connecting' 'none' 'none' $firstAttempt 'connection'
            SendLifecycle 3 'StreamStarted' 'none' 'none' $firstAttempt 'video'
            SendLifecycle 4 'TransportLost' 'transportLost' 'none' $firstAttempt 'connection'
            SendLifecycle 5 'ReconnectScheduled' 'transportLost' 'none' '' 'connection'
            SendLifecycle 6 'Reconnecting' 'none' 'none' $secondAttempt 'connection'
            SendLifecycle 7 'StreamResumed' 'none' 'none' $secondAttempt 'video'
            SendLifecycle 8 'SessionStopped' 'windowClosed' 'none'
            $exitRelease.WaitOne() | Out-Null
            exit 0
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
