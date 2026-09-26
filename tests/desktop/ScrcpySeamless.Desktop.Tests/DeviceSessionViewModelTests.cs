using Avalonia.Headless.XUnit;
using Avalonia.Controls;
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
public sealed class DeviceSessionViewModelTests
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

    /// <summary>Stop waits a natural-exit cleanup instead of stopping an already disposed child.</summary>
    [AvaloniaFact]
    public async Task StopDuringNaturalExitAwaitsTheSameCleanup()
    {
        using Fixture fixture = new();
        (_, DeviceSessionViewModel actions, FakeHost host) = await fixture.CreatePreparedSessionAsync();
        await actions.MirrorAsync();
        TaskCompletionSource releaseDisposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Session.DisposeRelease = releaseDisposal;
        host.Session.Exit(NativeTerminationReason.WindowClosed);
        await host.Session.DisposeEntered.Task;

        Task<bool> stop = actions.StopAsync();
        Assert.False(stop.IsCompleted);
        releaseDisposal.SetResult();

        Assert.True(await stop);
        Assert.False(actions.HasOwnedSession);
        Assert.Equal(0, host.Session.StopCalls);
        Assert.Equal(1, host.Session.DisposeCalls);
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
            CreatePreparedSessionAsync()
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
                store, new RuntimeBundleResult(null, RuntimeBundleStatus.Ready, null), host, action => action());
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

            if (DisposeRelease is not null)
            {
                await DisposeRelease.Task;
            }
        }
        public void Exit(NativeTerminationReason reason) => completion.TrySetResult(new NativeExit(SessionId, reason));
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
