using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Configuration;
using ScrcpySeamless.Infrastructure.Runtime;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Exercises committed launch, duplicate ownership and shutdown without ADB or a phone.</summary>
public sealed partial class DeviceSessionViewModelTests
{
    /// <summary>Device runtime requires an explicit absolute path in the normal DEV scope.</summary>
    [Fact]
    public void DeviceRuntimeLaunchOptionCannotEnterPreviewOrImplicitStorageModes()
    {
        string development = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "synthetic-p05c"));
        string runtime = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "synthetic-p05c-runtime"));
        DesktopLaunchOptions valid = DesktopLaunchOptions.Parse(
            ["--dev-data-dir=" + development, "--device-runtime=" + runtime]);

        Assert.Equal(runtime, valid.DeviceRuntimeDirectory);
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--preview", "--device-runtime=" + runtime]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--portable", "--device-runtime=" + runtime]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(
            ["--dev-data-dir=" + development, "--device-runtime=relative"]));
    }

    /// <summary>An invalid runtime leaves the local settings workspace usable and never starts native.</summary>
    [AvaloniaFact]
    public async Task MissingRuntimeDoesNotBlockSettingsOrLaunchNative()
    {
        using Fixture fixture = new();
        NormalDesktopComposition composition = fixture.CreateComposition("missing-runtime");
        await composition.InitializeAsync(CancellationToken.None);

        Assert.NotNull(composition.DeviceSession);
        Assert.False(composition.Devices.IsLiveEnabled);
        Assert.Contains("Runtime unavailable", composition.DeviceSession.Status);
        await composition.DeviceSession.MirrorAsync();
        Assert.False(composition.DeviceSession.HasOwnedSession);
        Assert.True(composition.Configuration.CanEdit);
    }

    /// <summary>Dirty settings are never silently applied or replaced by a stale committed launch.</summary>
    [AvaloniaFact]
    public async Task DirtySettingsBlockNativeWithoutChangingCommittedBytes()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition composition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        byte[] committed = File.ReadAllBytes(composition.DataPaths.ConfigurationFile);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "010";

        await actions.MirrorAsync();

        Assert.Equal(0, host.Starts);
        Assert.Contains("Apply or Cancel", actions.Status);
        Assert.Equal(committed, File.ReadAllBytes(composition.DataPaths.ConfigurationFile));
    }

    /// <summary>A newly chosen Wi-Fi endpoint cannot silently override the saved reconnect target.</summary>
    [AvaloniaFact]
    public async Task UnsavedSelectedConnectionEndpointBlocksLaunch()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition composition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        composition.Devices.OpenWirelessSetup();
        composition.Devices.ShowManualConnection();
        composition.Devices.ManualConnectionEndpoint = "phone.local:38211";

        await actions.MirrorAsync();

        Assert.Equal(0, host.Starts);
        Assert.Contains("differs from the saved profile", actions.Status);
    }

    /// <summary>A selected committed profile starts once, remains owned and stops once.</summary>
    [AvaloniaFact]
    public async Task DuplicateMirrorCannotStartAnotherChildAndStopSettlesOwnedChild()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();

        await actions.MirrorAsync();
        await actions.MirrorAsync();

        Assert.Equal(1, host.Starts);
        Assert.Equal("synthetic-usb", host.Request!.SelectedAdbSerial);
        Assert.NotNull(host.Request.ConfigurationRevision);
        Assert.True(actions.HasOwnedSession);
        Assert.Contains("unknown", actions.ChannelEvidence);
        Assert.True(await actions.StopAsync());
        Assert.Equal(1, host.Session.StopCalls);
        Assert.False(actions.HasOwnedSession);
    }

    /// <summary>Shutdown during a delayed native start cannot attach its late child to the window.</summary>
    [AvaloniaFact]
    public async Task ShutdownCancelsAnInFlightNativeStart()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        TaskCompletionSource<INativeSession> delayedStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OnStart = (_, _) => delayedStart.Task;
        Task launch = actions.MirrorAsync();
        await host.Started.Task;
        Task<bool> shutdown = actions.ShutdownAsync();
        delayedStart.SetResult(host.Session);

        await launch;
        Assert.True(await shutdown);
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(1, host.Starts);
        Assert.Equal(1, host.Session.StopCalls);
    }

    /// <summary>A late child remains owned when its first stop attempt fails.</summary>
    [AvaloniaFact]
    public async Task FailedStopOfLateChildBlocksShutdownUntilTheSameChildSettles()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        TaskCompletionSource<INativeSession> delayedStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OnStart = (_, _) => delayedStart.Task;
        host.Session.FailNextStop = true;

        Task launch = actions.MirrorAsync();
        await host.Started.Task;
        Task<bool> shutdown = actions.ShutdownAsync();
        delayedStart.SetResult(host.Session);

        await launch;
        Assert.False(await shutdown);
        Assert.True(actions.HasOwnedSession);
        Assert.False(host.Session.Completion.IsCompleted);
        Assert.DoesNotContain("stopped", actions.Status, StringComparison.OrdinalIgnoreCase);

        actions.ResumeAfterFailedShutdown();
        await actions.MirrorAsync();
        Assert.Equal(1, host.Starts);

        Assert.True(await actions.StopAsync());
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(2, host.Session.StopCalls);
        Assert.Equal(1, host.Session.DisposeCalls);
    }

    /// <summary>Caller cancellation after spawn still transfers the child into retryable ownership.</summary>
    [AvaloniaFact]
    public async Task CancelledLaunchRetainsLateChildAfterFailedStop()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<INativeSession> delayedStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OnStart = (_, _) => delayedStart.Task;
        host.Session.FailNextStop = true;

        Task launch = actions.MirrorAsync(cancellation.Token);
        await host.Started.Task;
        cancellation.Cancel();
        delayedStart.SetResult(host.Session);
        await launch;

        Assert.True(actions.HasOwnedSession);
        Assert.False(host.Session.Completion.IsCompleted);
        Assert.Equal(1, host.Session.StopCalls);
        Assert.True(await actions.StopAsync());
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(2, host.Session.StopCalls);
    }

    /// <summary>Concurrent stop requests await the same exact-child cleanup outcome.</summary>
    [AvaloniaFact]
    public async Task ConcurrentStopsShareOneOwnedChildStop()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        TaskCompletionSource releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Session.StopRelease = releaseStop;

        Task<bool> first = actions.StopAsync();
        await host.Session.StopEntered.Task;
        Task<bool> second = actions.StopAsync();

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        releaseStop.SetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(1, host.Session.StopCalls);
        Assert.Equal(1, host.Session.DisposeCalls);
    }

    /// <summary>Stop or shutdown joins natural-exit cleanup without rewriting its initiator or stopping again.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringNaturalExitAwaitsTheSameCleanup(bool shutdown)
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        TaskCompletionSource releaseDisposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Session.DisposeRelease = releaseDisposal;
        host.Session.Exit(NativeTerminationReason.WindowClosed);
        await host.Session.DisposeEntered.Task;

        SessionEvidenceSnapshot disposing = actions.CaptureSessionEvidence();
        Assert.Equal(SessionCleanupIntent.CompletionCleanup, disposing.Cleanup.Intent);
        Assert.Equal(SessionCleanupState.InProgress, disposing.Cleanup.State);
        Task<bool> stop = shutdown ? actions.ShutdownAsync() : actions.StopAsync();
        Assert.False(stop.IsCompleted);
        SessionEvidenceSnapshot joined = actions.CaptureSessionEvidence();
        Assert.Equal(SessionCleanupIntent.CompletionCleanup, joined.Cleanup.Intent);
        Assert.Equal(SessionStopCallState.NotRequested, joined.Cleanup.StopCallState);
        releaseDisposal.SetResult();

        Assert.True(await stop);
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(0, host.Session.StopCalls);
        Assert.Equal(1, host.Session.DisposeCalls);
        SessionEvidenceSnapshot final = actions.CaptureSessionEvidence();
        Assert.Equal(SessionCleanupIntent.CompletionCleanup, final.Cleanup.Intent);
        Assert.Equal(SessionCleanupState.Succeeded, final.Cleanup.State);
        Assert.Equal(SessionStopCallState.NotRequested, final.Cleanup.StopCallState);
        Assert.Equal(NativeTerminationReason.WindowClosed, final.TerminalResult?.Reason);
        Assert.Equal(SessionCleanupState.InProgress, disposing.Cleanup.State);
        Assert.Equal(SessionCleanupState.InProgress, joined.Cleanup.State);
        Assert.Equal(1, host.Starts);
    }

    /// <summary>A later explicit retry after failed disposal initiates new cleanup without losing the native result.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterFailedCleanupRecordsItsNewInitiator(bool shutdown)
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        host.Session.FailNextDisposal = true;
        host.Session.Exit(NativeTerminationReason.WindowClosed);
        await AwaitMachineStatusAsync(actions, () => actions.Status.StartsWith("Native cleanup failed", StringComparison.Ordinal));
        SessionEvidenceSnapshot failed = actions.CaptureSessionEvidence();
        Assert.Equal(SessionCleanupIntent.CompletionCleanup, failed.Cleanup.Intent);
        Assert.Equal(SessionCleanupState.Failed, failed.Cleanup.State);
        Assert.Equal(SessionCleanupFailureCategory.DisposalOrOwnedWork, failed.Cleanup.FailureCategory);
        Assert.Equal(SessionStopCallState.NotRequested, failed.Cleanup.StopCallState);
        Assert.True(actions.HasOwnedSession);
        Assert.False(failed.Cleanup.OwnershipReleased);
        Assert.Equal(0, host.Session.StopCalls);
        Assert.Equal(1, host.Session.DisposeCalls);

        TaskCompletionSource releaseStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Session.StopRelease = releaseStop;
        Task<bool> retry = shutdown ? actions.ShutdownAsync() : actions.StopAsync();
        SessionCleanupIntent expectedIntent = shutdown
            ? SessionCleanupIntent.ApplicationShutdown : SessionCleanupIntent.ExplicitStop;
        SessionEvidenceSnapshot retrying;

        try
        {
            await host.Session.StopEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            retrying = actions.CaptureSessionEvidence();
            Assert.Equal(expectedIntent, retrying.Cleanup.Intent);
            Assert.Equal(SessionCleanupState.InProgress, retrying.Cleanup.State);
            Assert.Equal(SessionStopCallState.InProgress, retrying.Cleanup.StopCallState);
        }
        finally
        {
            // Release the synthetic operation even if an assertion fails; retain the primary failure.
            releaseStop.TrySetResult();
        }

        Assert.True(await retry);
        SessionEvidenceSnapshot final = actions.CaptureSessionEvidence();
        Assert.Equal(expectedIntent, final.Cleanup.Intent);
        Assert.Equal(SessionCleanupState.Succeeded, final.Cleanup.State);
        Assert.Equal(SessionStopCallState.Succeeded, final.Cleanup.StopCallState);
        Assert.Equal(failed.TerminalResult, final.TerminalResult);
        Assert.Equal(NativeTerminationReason.WindowClosed, final.TerminalResult?.Reason);
        Assert.True(final.Cleanup.OwnershipReleased);
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(SessionCleanupIntent.CompletionCleanup, failed.Cleanup.Intent);
        Assert.Equal(SessionCleanupState.Failed, failed.Cleanup.State);
        Assert.Equal(SessionCleanupState.InProgress, retrying.Cleanup.State);
        Assert.Equal(1, host.Session.StopCalls);
        Assert.Equal(2, host.Session.DisposeCalls);
        Assert.Equal(1, host.Starts);
    }

    /// <summary>Failed native close keeps the device workspace and drafts usable for a retry.</summary>
    [AvaloniaFact]
    public async Task FailedNativeCloseKeepsLiveDevicesAvailableUntilRetry()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition baseComposition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        host.Session.FailNextStop = true;
        NormalDesktopComposition composition = new(baseComposition.Shell, baseComposition.Settings,
            baseComposition.Profiles, baseComposition.Configuration, baseComposition.Preferences,
            baseComposition.Devices, actions, baseComposition.DataPaths, new PresentationText());
        DecisionWindow window = new(baseComposition.Shell);
        window.AttachNormalComposition(composition);
        window.Show();

        window.Close();
        await window.StopFailureShown.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(window.IsVisible);
        Assert.True(composition.Devices.IsLiveEnabled);
        Assert.True(actions.HasOwnedSession);
        Assert.True(composition.Configuration.CanEdit);

        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, host.Session.StopCalls);
    }

    /// <summary>Unsettled ADB work keeps close retryable while device actions stay restricted.</summary>
    [AvaloniaFact]
    public async Task FailedAdbSettlementKeepsRestrictedCloseRetryAvailable()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition baseComposition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        NormalDesktopComposition composition = new(baseComposition.Shell, baseComposition.Settings,
            baseComposition.Profiles, baseComposition.Configuration, baseComposition.Preferences,
            baseComposition.Devices, actions, baseComposition.DataPaths, new PresentationText());
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "010";
        TaskCompletionSource releaseDiscovery = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource discoveryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Gateway.ServicesRelease = releaseDiscovery;
        fixture.Gateway.ServicesEntered = discoveryEntered;
        Task refresh = composition.Devices.RefreshAsync();
        await discoveryEntered.Task;
        DecisionWindow window = new(baseComposition.Shell) { NextDecision = DecisionWindow.Decision.Discard };
        window.AttachNormalComposition(composition);
        window.Show();

        window.Close();
        await window.StopFailureShown.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(window.IsVisible);
        Assert.True(composition.Configuration.IsDirty);
        Assert.False(actions.HasOwnedSession);
        Assert.True(actions.IsShuttingDown);
        Assert.False(composition.Devices.IsLiveEnabled);

        releaseDiscovery.SetResult();
        await refresh;
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        await closed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Manually closed native windows terminate the exact session without replacement.</summary>
    [AvaloniaFact]
    public async Task NativeExitDoesNotRestartProcess()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.Status) && actions.Status.Contains("WindowClosed", StringComparison.Ordinal))
            {
                ended.TrySetResult();
            }
        };
        host.Session.Exit(NativeTerminationReason.WindowClosed);
        await ended.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Starts);
        Assert.False(actions.HasOwnedSession);
        Assert.Contains("WindowClosed", actions.Status);
    }

    /// <summary>An immediately exited child cannot leave a stale started status after cleanup.</summary>
    [AvaloniaFact]
    public async Task ChildExitedBeforeStartReturnsReportsItsTerminalState()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        TaskCompletionSource<INativeSession> delayedStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OnStart = (_, _) => delayedStart.Task;
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.Status) && actions.Status.Contains("WindowClosed", StringComparison.Ordinal))
            {
                ended.TrySetResult();
            }
        };

        Task launch = actions.MirrorAsync();
        await host.Started.Task;
        host.Session.Exit(NativeTerminationReason.WindowClosed);
        delayedStart.SetResult(host.Session);
        await launch;
        await ended.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(actions.HasOwnedSession);
        Assert.Equal(1, host.Session.DisposeCalls);
        Assert.Contains("WindowClosed", actions.Status);
    }

    /// <summary>Keep editing retains the mirror; an accepted discard stops it before the window closes.</summary>
    [AvaloniaFact]
    public async Task DirtyCloseDecisionPreservesOrStopsTheOwnedMirror()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition baseComposition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        NormalDesktopComposition composition = new(baseComposition.Shell, baseComposition.Settings,
            baseComposition.Profiles, baseComposition.Configuration, baseComposition.Preferences,
            baseComposition.Devices, actions, baseComposition.DataPaths, new PresentationText());
        baseComposition.Settings.SearchText = "max-size";
        Assert.Single(baseComposition.Settings.VisibleRows).TextValue = "010";
        DecisionWindow window = new(baseComposition.Shell) { NextDecision = DecisionWindow.Decision.KeepEditing };
        window.AttachNormalComposition(composition);
        window.Show();
        window.Close();

        Assert.True(actions.HasOwnedSession);
        Assert.Equal(0, host.Session.StopCalls);
        Assert.True(composition.HasDirtyGroups);

        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.NextDecision = DecisionWindow.Decision.Discard;
        window.Close();
        await closed.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Session.StopCalls);
        Assert.False(actions.HasOwnedSession);
    }

    /// <summary>Early events and later attempts remain owned while another page is visible.</summary>
    [AvaloniaFact]
    public async Task BufferedLifecycleTracksOnlyTheActiveAttemptAcrossNavigation()
    {
        using Fixture fixture = new();
        (NormalDesktopComposition composition, DeviceSessionViewModel actions, FakeHost host) =
            await fixture.CreatePreparedSessionAsync();
        InteractiveSession native = new();
        TaskCompletionSource<INativeSession> releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return releaseStart.Task;
        };
        Task launch = actions.MirrorAsync();
        await host.Started.Task;
        ConnectionAttemptId first = ConnectionAttemptId.New();
        ConnectionAttemptId second = ConnectionAttemptId.New();
        await native.EmitAsync(1, NativeLifecycleEventType.NativeReady);
        await native.EmitAsync(2, NativeLifecycleEventType.Connecting, first);
        await native.EmitAsync(3, NativeLifecycleEventType.StreamStarted, first);
        releaseStart.SetResult(native);
        await launch;
        await AwaitSequenceAsync(actions, 3);
        Assert.Contains("first frame", actions.ChannelEvidence);
        Assert.Contains("unknown", actions.ChannelEvidence);

        composition.Shell.ShowSettings();
        await native.EmitAsync(4, NativeLifecycleEventType.TransportLost, first);
        await native.EmitAsync(5, NativeLifecycleEventType.ReconnectScheduled);
        await native.EmitAsync(6, NativeLifecycleEventType.Reconnecting, second);
        await native.EmitAsync(7, NativeLifecycleEventType.StreamResumed, first);
        await native.EmitAsync(8, NativeLifecycleEventType.Reconnecting, first);
        await native.EmitAsync(9, NativeLifecycleEventType.ReconnectScheduled, first);
        await AwaitSequenceAsync(actions, 9);
        Assert.Contains("not ready", actions.ChannelEvidence);
        await native.EmitAsync(10, NativeLifecycleEventType.StreamResumed, second);
        await AwaitSequenceAsync(actions, 10);
        Assert.Contains("first frame", actions.ChannelEvidence);
        Assert.True(await actions.StopAsync());
    }

    /// <summary>Even after bounded attempt memory rotates, an unscheduled stale retry stays inactive.</summary>
    [AvaloniaFact]
    public async Task StaleRetryAfterAttemptHistoryRotationCannotClaimNewVideo()
    {
        const int ReconnectAttemptsBeyondHistory = 33;
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        InteractiveSession native = new();
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(native);
        };
        await actions.MirrorAsync();
        ConnectionAttemptId oldest = ConnectionAttemptId.New();
        ConnectionAttemptId latest = oldest;
        ulong sequence = 1;
        await native.EmitAsync(sequence++, NativeLifecycleEventType.Connecting, oldest);

        for (int attemptNumber = 0; attemptNumber < ReconnectAttemptsBeyondHistory; attemptNumber++)
        {
            latest = ConnectionAttemptId.New();
            await native.EmitAsync(sequence++, NativeLifecycleEventType.ReconnectScheduled);
            await native.EmitAsync(sequence++, NativeLifecycleEventType.Reconnecting, latest);
        }

        await native.EmitAsync(sequence++, NativeLifecycleEventType.Reconnecting, oldest);
        await native.EmitAsync(sequence++, NativeLifecycleEventType.StreamResumed, oldest);
        await AwaitSequenceAsync(actions, sequence - 1);
        Assert.Contains("not ready", actions.ChannelEvidence);
        await native.EmitAsync(sequence++, NativeLifecycleEventType.StreamResumed, latest);
        await AwaitSequenceAsync(actions, sequence - 1);
        Assert.Contains("first frame", actions.ChannelEvidence);
        Assert.True(await actions.StopAsync());
    }

    /// <summary>A withheld UI dispatcher does not accumulate a post per native observation.</summary>
    [AvaloniaFact]
    public async Task LifecycleFloodRetainsOnlyBoundedHistoryAndOneUiPost()
    {
        using Fixture fixture = new();
        List<Action> queuedUi = [];
        (_, DeviceSessionViewModel delayed, FakeHost host) = await fixture.CreatePreparedSessionAsync(action =>
        {
            lock (queuedUi)
            {
                queuedUi.Add(action);
            }
        });
        InteractiveSession native = new() { ExpectedReadCount = 200 };
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(native);
        };
        await delayed.MirrorAsync();

        for (ulong sequence = 1; sequence <= 200; sequence++)
        {
            await native.EmitAsync(sequence, NativeLifecycleEventType.CapabilityDegraded);
        }

        await native.ExpectedReads.Task.WaitAsync(TestContext.Current.CancellationToken);
        Action update;

        lock (queuedUi)
        {
            update = Assert.Single(queuedUi);
        }

        update();
        Assert.Equal((ulong)200, delayed.LastLifecycleSequence);
        Assert.Equal(64, delayed.RecentLifecycleEvents.Count);
        Assert.Equal((ulong)137, delayed.RecentLifecycleEvents[0].Sequence);
        Assert.Equal((ulong)200, delayed.RecentLifecycleEvents[^1].Sequence);
        // Evidence reads retain the existing bound and account for omitted earlier observations.
        SessionEvidenceSnapshot boundedEvidence = delayed.CaptureSessionEvidence();
        Assert.Equal((ulong)200, boundedEvidence.TotalObserved);
        Assert.Equal(64, boundedEvidence.Events.Count);
        Assert.Equal((ulong)137, boundedEvidence.Events[0].Sequence);
        Assert.Equal((ulong)200, boundedEvidence.Events[^1].Sequence);
        Assert.True(await delayed.StopAsync());
        Assert.Equal(64, delayed.RecentLifecycleEvents.Count);
        Assert.Equal(boundedEvidence.Events, delayed.CaptureSessionEvidence().Events);
    }

    /// <summary>Native terminal observation initiates Stop without waiting inside its own reader.</summary>
    [AvaloniaFact]
    public async Task TerminalLifecycleStartsOwnedCleanupWhileReaderIsStillOpen()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        InteractiveSession native = new() { StopRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(native);
        };
        await actions.MirrorAsync();
        await native.EmitAsync(1, NativeLifecycleEventType.NativeReady);
        await native.EmitAsync(2, NativeLifecycleEventType.SessionStopped,
            reason: NativeLifecycleReason.WindowClosed);
        await native.StopEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(actions.HasOwnedSession);
        Assert.False(native.Completion.IsCompleted);
        Assert.Equal(1, native.StopCalls);
        native.StopRelease.SetResult();
        Assert.True(await actions.StopAsync());
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(1, native.StopCalls);
    }

    /// <summary>Exact activation focus rejects an obsolete session and does not change a newer one.</summary>
    [AvaloniaFact]
    public async Task FocusRequiresExactLiveSessionAndIgnoresLateResult()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        InteractiveSession first = new() { FocusRelease = new TaskCompletionSource<NativeFocusOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously) };
        InteractiveSession second = new();
        Queue<InteractiveSession> launches = new([first, second]);
        host.OnStart = (request, _) =>
        {
            InteractiveSession next = launches.Dequeue();
            next.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(next);
        };
        await actions.MirrorAsync();
        SessionId firstId = actions.CurrentSessionId!.Value;
        Task<NativeFocusOutcome> oldFocus = actions.FocusSessionAsync(firstId);
        await first.FocusEntered.Task;
        first.Exit(NativeTerminationReason.WindowClosed);
        await AwaitUnownedAsync(actions);
        await actions.MirrorAsync();
        SessionId secondId = actions.CurrentSessionId!.Value;
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(NativeFocusOutcome.InvalidState, await actions.FocusSessionAsync(firstId));
        Assert.Equal(0, second.FocusCalls);
        first.FocusRelease.SetResult(NativeFocusOutcome.Applied);
        Assert.Equal(NativeFocusOutcome.InvalidState, await oldFocus);
        Assert.Equal(string.Empty, actions.FocusStatus);
        Assert.True(await actions.StopAsync());
    }

    /// <summary>An exited child is released after Stop failure while preserving the failure report.</summary>
    [AvaloniaFact]
    public async Task FailedStopAfterExactExitKeepsClosureRetryable()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        InteractiveSession native = new() { FailStopAfterExit = true };
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(native);
        };
        await actions.MirrorAsync();
        Assert.False(await actions.StopAsync());
        Assert.False(actions.HasOwnedSession);
        Assert.Contains("failed", actions.Status);
        Assert.True(await actions.StopAsync());
    }

    /// <summary>The Focus button serializes requests and cleanup joins its pending command.</summary>
    [AvaloniaFact]
    public async Task RepeatedFocusCommandKeepsOneOwnedTaskThroughStop()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        InteractiveSession native = new() { FocusRelease = new TaskCompletionSource<NativeFocusOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously) };
        host.OnStart = (request, _) =>
        {
            native.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(native);
        };
        await actions.MirrorAsync();
        actions.FocusCommand.Execute(null);
        actions.FocusCommand.Execute(null);
        await native.FocusEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, native.FocusCalls);
        Task<bool> stopping = actions.StopAsync();
        await native.StopEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(stopping.IsCompleted);
        native.FocusRelease.SetResult(NativeFocusOutcome.Applied);
        Assert.True(await stopping);
        Assert.False(actions.HasOwnedSession);
    }

    /// <summary>A delayed UI callback from a released session cannot mutate a later session.</summary>
    [AvaloniaFact]
    public async Task QueuedOldSessionUiWorkCannotOverwriteNewSession()
    {
        using Fixture fixture = new();
        List<Action> queuedUi = [];
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync(action =>
        {
            lock (queuedUi)
            {
                queuedUi.Add(action);
            }
        });
        InteractiveSession first = new() { ExpectedReadCount = 1 };
        InteractiveSession second = new();
        Queue<InteractiveSession> launches = new([first, second]);
        host.OnStart = (request, _) =>
        {
            InteractiveSession next = launches.Dequeue();
            next.SessionId = request.SessionId;
            return Task.FromResult<INativeSession>(next);
        };
        await actions.MirrorAsync();
        await first.EmitAsync(1, NativeLifecycleEventType.NativeReady);
        await first.ExpectedReads.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(await actions.StopAsync());
        await actions.MirrorAsync();
        string newerStatus = actions.Status;
        SessionEvidenceSnapshot newerEvidence = actions.CaptureSessionEvidence();
        Action oldUpdate;

        lock (queuedUi)
        {
            oldUpdate = queuedUi[0];
        }

        oldUpdate();
        Assert.Equal(newerStatus, actions.Status);
        Assert.Null(actions.LastLifecycleSequence);
        SessionEvidenceSnapshot afterOldUpdate = actions.CaptureSessionEvidence();
        Assert.Equal(newerEvidence.SessionId, afterOldUpdate.SessionId);
        Assert.Equal(newerEvidence.Events, afterOldUpdate.Events);
        Assert.Equal(newerEvidence.TotalObserved, afterOldUpdate.TotalObserved);
        Assert.Equal(newerEvidence.AcceptedHandshake, afterOldUpdate.AcceptedHandshake);
        Assert.Equal(newerEvidence.TerminalResult, afterOldUpdate.TerminalResult);
        Assert.Equal(newerEvidence.Cleanup, afterOldUpdate.Cleanup);
        Assert.True(await actions.StopAsync());
    }

    /// <summary>Waits on an observable sequence, not elapsed time or a blind retry.</summary>
    private static async Task AwaitSequenceAsync(DeviceSessionViewModel actions, ulong sequence)
    {
        if (actions.LastLifecycleSequence >= sequence)
        {
            return;
        }

        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler changed = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.LastLifecycleSequence) &&
                actions.LastLifecycleSequence >= sequence)
            {
                observed.TrySetResult();
            }
        };
        actions.PropertyChanged += changed;

        try
        {
            await observed.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            actions.PropertyChanged -= changed;
        }
    }

    /// <summary>Waits for the exact old child to release without polling process identity.</summary>
    private static async Task AwaitUnownedAsync(DeviceSessionViewModel actions)
    {
        if (!actions.HasOwnedSession)
        {
            return;
        }

        TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        System.ComponentModel.PropertyChangedEventHandler changed = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.Status) && !actions.HasOwnedSession)
            {
                released.TrySetResult();
            }
        };
        actions.PropertyChanged += changed;

        try
        {
            await released.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            actions.PropertyChanged -= changed;
        }
    }

    /// <summary>Uses disposable v2 files and an in-memory ADB gateway only.</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Directory = Path.Combine(Path.GetTempPath(), "scrcpy-p05c-session-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }
        public FakeGateway Gateway { get; } = new();

        public NormalDesktopComposition CreateComposition(string? runtime = null)
        {
            DesktopLaunchOptions options = new(false, AppTheme.System, null, false,
                StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: Directory,
                DeviceRuntimeDirectory: runtime is null ? null : Path.Combine(Directory, runtime));
            return NormalDesktopFactory.Create(options, _ => { }, _ => { }, font => font);
        }

        public async Task<(NormalDesktopComposition Composition, DeviceSessionViewModel Actions, FakeHost Host)>
            CreatePreparedSessionAsync(Action<Action>? dispatch = null)
        {
            NormalDesktopComposition composition = CreateComposition();
            ConfigurationV2 saved = new()
            {
                Profiles = [new DeviceProfile
                {
                    Id = ProfileId.New(),
                    UsbIdentity = new UsbSerial("synthetic-usb"),
                    Alias = "Synthetic phone",
                }],
            };
            VersionedConfigurationStore store = new(composition.DataPaths.ConfigurationFile);
            Assert.Equal(ConfigurationCommitStatus.Committed,
                store.Commit(null, saved, CancellationToken.None).Status);
            await composition.InitializeAsync(CancellationToken.None);
            composition.Devices.AttachLiveServices(new AdbDiscoveryService(Gateway),
                new AdbPairingService(Gateway), action => action());
            await composition.Devices.RefreshAsync();
            composition.Devices.SelectedDeviceChoice = Assert.Single(composition.Devices.ObservedDevices);
            FakeHost host = new();
            DeviceSessionViewModel actions = new(composition.Devices, composition.Profiles, composition.Configuration,
                store, new RuntimeBundleResult(null, RuntimeBundleStatus.Ready, null), host,
                dispatch ?? (action => action()));
            actions.SelectedLaunchProfile = Assert.Single(actions.ProfileChoices);
            return (composition, actions, host);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    /// <summary>Returns one eligible synthetic transport without running ADB.</summary>
    private sealed class FakeGateway : IAdbGateway
    {
        public TaskCompletionSource? ServicesRelease { get; set; }
        public TaskCompletionSource? ServicesEntered { get; set; }

        public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbDevice>>.Success(
                [new AdbDevice("synthetic-usb", AdbDeviceState.Device, "Synthetic phone")]));
        public async Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken)
        {
            ServicesEntered?.TrySetResult();

            if (ServicesRelease is not null)
            {
                await ServicesRelease.Task;
            }

            return AdbResult<IReadOnlyList<AdbMdnsService>>.Success([]);
        }
        public Task<AdbResult<bool>> PairAsync(NetworkEndpoint endpoint, string code, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Pairing is not part of this fixture.");
        public Task<AdbResult<bool>> ConnectAsync(NetworkEndpoint endpoint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connection is not part of this fixture.");
        public Task<AdbResult<string>> GetDeviceSerialPropertyAsync(NetworkEndpoint endpoint,
            CancellationToken cancellationToken) => throw new InvalidOperationException("No device is contacted.");
    }

    /// <summary>Captures native requests and optionally delays the exact synthetic start.</summary>
    private sealed class FakeHost : INativeHost
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeSession Session { get; } = new();
        public Func<NativeStartRequest, CancellationToken, Task<INativeSession>>? OnStart { get; set; }
        public int Starts { get; private set; }
        public NativeStartRequest? Request { get; private set; }

        public Task<INativeSession> StartAsync(NativeStartRequest request, CancellationToken cancellationToken)
        {
            Starts++;
            Request = request;
            Session.SessionId = request.SessionId;
            Started.TrySetResult();
            return OnStart?.Invoke(request, cancellationToken) ?? Task.FromResult<INativeSession>(Session);
        }
    }

    /// <summary>Models only owned stop and terminal completion.</summary>
    private sealed class FakeSession : INativeSession
    {
        private readonly TaskCompletionSource<NativeExit> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SessionId SessionId { get; set; } = SessionId.New();
        public Task<NativeExit> Completion => completion.Task;
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool FailNextStop { get; set; }
        public bool FailNextDisposal { get; set; }
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? StopRelease { get; set; }
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? DisposeRelease { get; set; }

        public async Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken)
        {
            StopCalls++;
            StopEntered.TrySetResult();

            if (FailNextStop)
            {
                FailNextStop = false;
                throw new InvalidOperationException("Synthetic owned-child stop failure.");
            }

            if (StopRelease is not null)
            {
                await StopRelease.Task;
            }

            completion.TrySetResult(new NativeExit(SessionId, reason));
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            DisposeEntered.TrySetResult();

            if (FailNextDisposal)
            {
                FailNextDisposal = false;
                throw new IOException("Synthetic owned-resource disposal failure.");
            }

            if (DisposeRelease is not null)
            {
                await DisposeRelease.Task;
            }
        }
        public void Exit(NativeTerminationReason reason) => completion.TrySetResult(new NativeExit(SessionId, reason));
    }

    /// <summary>Provides a bounded observation stream and controllable exact-child commands.</summary>
    private sealed class InteractiveSession : INativeInteractiveSession
    {
        private readonly Channel<NativeLifecycleObservation> lifecycle = Channel.CreateBounded<NativeLifecycleObservation>(
            new BoundedChannelOptions(128) { SingleReader = true, SingleWriter = false });
        private readonly TaskCompletionSource<NativeExit> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;

        public SessionId SessionId { get; set; } = SessionId.New();
        public Task<NativeExit> Completion => completion.Task;
        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FocusEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExpectedReads { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? StopRelease { get; set; }
        public TaskCompletionSource<NativeFocusOutcome>? FocusRelease { get; set; }
        public int ExpectedReadCount { get; set; }
        public int StopCalls { get; private set; }
        public int FocusCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool FailStopAfterExit { get; set; }

        public async Task EmitAsync(ulong sequence, NativeLifecycleEventType eventType,
            ConnectionAttemptId? attempt = null, NativeLifecycleReason reason = NativeLifecycleReason.None)
        {
            NativeLifecycleObservation observation = new(SessionId, sequence,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sequence, attempt,
                NativeLifecycleSubsystem.Connection, eventType, reason, NativeLifecycleError.None);
            await lifecycle.Writer.WriteAsync(observation, TestContext.Current.CancellationToken);
        }

        public async IAsyncEnumerable<NativeLifecycleObservation> ObserveLifecycleAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (NativeLifecycleObservation observation in lifecycle.Reader.ReadAllAsync(cancellationToken))
            {
                if (Interlocked.Increment(ref reads) == ExpectedReadCount)
                {
                    ExpectedReads.TrySetResult();
                }

                yield return observation;
            }
        }

        public async Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken)
        {
            StopCalls++;
            StopEntered.TrySetResult();

            if (StopRelease is not null)
            {
                await StopRelease.Task.WaitAsync(cancellationToken);
            }

            Exit(reason);

            if (FailStopAfterExit)
            {
                throw new IOException("Synthetic graceful-stop failure after exact exit.");
            }
        }

        public async Task<NativeFocusOutcome> FocusWindowAsync(CancellationToken cancellationToken)
        {
            FocusCalls++;
            FocusEntered.TrySetResult();
            return FocusRelease is null
                ? NativeFocusOutcome.NoWindow
                : await FocusRelease.Task.WaitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            lifecycle.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public void Exit(NativeTerminationReason reason)
        {
            completion.TrySetResult(new NativeExit(SessionId, reason));
            lifecycle.Writer.TryComplete();
        }
    }

    /// <summary>Returns scripted close choices through the real single Closing coordinator.</summary>
    private sealed class DecisionWindow(ShellViewModel shell) : MainWindow(shell)
    {
        public enum Decision { KeepEditing, Discard }
        public Decision NextDecision { get; set; }
        public TaskCompletionSource StopFailureShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<CloseDecision> AskCloseDecisionAsync(string groups) =>
            Task.FromResult(NextDecision == Decision.Discard ? CloseDecision.Discard : CloseDecision.Stay);

        protected override Task ShowStopFailureAsync()
        {
            StopFailureShown.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
