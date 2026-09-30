using System.Windows.Input;
using System.Collections.ObjectModel;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using ScrcpySeamless.Infrastructure.Runtime;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Owns one explicit native session launched from a committed v2 snapshot.</summary>
public sealed class DeviceSessionViewModel : ObservableViewModel
{
    private static readonly TimeSpan LifecycleSettleTimeout = TimeSpan.FromSeconds(5);
    private readonly DevicesViewModel devices;
    private readonly ProfilesViewModel profiles;
    private readonly ConfigurationWorkspaceViewModel configuration;
    private readonly VersionedConfigurationStore store;
    private readonly RuntimeBundleResult runtime;
    private readonly INativeHost? host;
    private readonly Action<Action> dispatch;
    private readonly PresentationText text;
    private readonly object gate = new();
    private INativeSession? session;
    private Task<bool>? sessionCleanupTask;
    private Task? lifecycleTask;
    private Task? exitObservationTask;
    private Task? focusCommandTask;
    private NativeLifecycleProjection? lifecycleProjection;
    private IReadOnlyList<NativeLifecycleObservation> completedLifecycleEvents = [];
    private CancellationTokenSource? lifecycleCancellation;
    private bool lifecycleUiPending;
    private long sessionGeneration;
    private Task? startTask;
    private Task<bool>? stopTask;
    private CancellationTokenSource? startCancellation;
    private bool stopping;
    private bool shuttingDown;
    private string status;
    private SavedProfileChoice? selectedLaunchProfile;
    private int? processId;
    private nint windowHandle;
    private string channelEvidence;
    private string focusStatus;
    private ulong? lastLifecycleSequence;

    /// <summary>Attaches existing boundaries without contacting a device.</summary>
    public DeviceSessionViewModel(DevicesViewModel devices, ProfilesViewModel profiles,
        ConfigurationWorkspaceViewModel configuration, VersionedConfigurationStore store,
        RuntimeBundleResult runtime, INativeHost? host, Action<Action> dispatch)
    {
        this.devices = devices;
        this.profiles = profiles;
        this.configuration = configuration;
        this.store = store;
        this.runtime = runtime;
        this.host = host;
        this.dispatch = dispatch;
        text = new PresentationText();
        status = runtime.Status == RuntimeBundleStatus.Ready
            ? "Select a saved profile and refresh/select an available ADB transport."
            : DescribeUnavailableRuntime();
        channelEvidence = text.Get("devices.machine.channelsUnknown");
        focusStatus = string.Empty;
        MirrorCommand = new ActionCommand(() => _ = MirrorAsync());
        StopCommand = new ActionCommand(() => _ = StopAsync());
        FocusCommand = new ActionCommand(StartFocusCommand);
    }

    public ICommand MirrorCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand FocusCommand { get; }
    public string FocusLabel => text.Get("devices.focusMirror");
    public string NativeHostName => host?.GetType().Name ?? "Unavailable";
    public string HostModeStatus => text.Get(host switch
    {
        MachineNativeHost => "devices.machine.hostMachine",
        LegacyNativeHost => "devices.machine.hostLegacy",
        null => "devices.machine.hostUnavailable",
        _ => "devices.machine.hostSynthetic",
    });
    public SessionId? CurrentSessionId { get { lock (gate) { return session?.SessionId; } } }
    public IReadOnlyList<NativeLifecycleObservation> RecentLifecycleEvents
    {
        get { lock (gate) { return lifecycleProjection?.RecentObservations ?? completedLifecycleEvents; } }
    }
    public ObservableCollection<SavedProfileChoice> ProfileChoices => profiles.Profiles;
    public SavedProfileChoice? SelectedLaunchProfile
    {
        get => selectedLaunchProfile;
        set => SetProperty(ref selectedLaunchProfile, value);
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public int? ProcessId { get => processId; private set => SetProperty(ref processId, value); }
    public nint WindowHandle
    {
        get => windowHandle;
        private set
        {
            if (SetProperty(ref windowHandle, value))
            {
                OnPropertyChanged(nameof(HasObservedWindowHandle));
            }
        }
    }
    public bool HasObservedWindowHandle => windowHandle != nint.Zero;
    public string ChannelEvidence { get => channelEvidence; private set => SetProperty(ref channelEvidence, value); }
    public string FocusStatus { get => focusStatus; private set => SetProperty(ref focusStatus, value); }
    public ulong? LastLifecycleSequence
    {
        get => lastLifecycleSequence;
        private set => SetProperty(ref lastLifecycleSequence, value);
    }
    public bool HasOwnedSession { get { lock (gate) { return session is not null || startTask is not null; } } }
    public bool IsShuttingDown { get { lock (gate) { return shuttingDown; } } }

    /// <summary>Explains a rejected DEV runtime while leaving configuration editing available.</summary>
    private string DescribeUnavailableRuntime()
    {
        string guidance = runtime.Status switch
        {
            RuntimeBundleStatus.IncompatibleMachineContract => text.Get("devices.runtime.incompatibleMachine"),
            RuntimeBundleStatus.Missing => text.Get("devices.runtime.missing"),
            RuntimeBundleStatus.MissingComponent => text.Get("devices.runtime.missingComponent"),
            RuntimeBundleStatus.HashMismatch => text.Get("devices.runtime.hashMismatch"),
            _ => text.Get("devices.runtime.invalidManifest"),
        };
        return $"{guidance} ({runtime.Status}: {runtime.Component ?? "manifest"}).";
    }

    /// <summary>Rejects another launch while an owned launch or child is active.</summary>
    public Task MirrorAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (shuttingDown || stopping || session is not null || startTask is not null)
            {
                Status = "A session or launch is already active, or shutdown is in progress.";
                return Task.CompletedTask;
            }

            startCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startTask = StartCoreAsync(startCancellation.Token);
            return startTask;
        }
    }

    /// <summary>Reads the committed revision and validates all execution inputs before starting.</summary>
    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        try
        {
            if (host is null || runtime.Status != RuntimeBundleStatus.Ready)
            {
                Status = DescribeUnavailableRuntime();
                return;
            }

            bool pendingConfiguration = configuration.Draft is null || configuration.IsDirty ||
                profiles.HasUnstagedChanges || configuration.IsBusy || configuration.RequiresReload;

            if (pendingConfiguration)
            {
                Status = "Apply or Cancel pending profile/mirroring edits before Mirror.";
                return;
            }

            SavedProfileChoice? selectedProfile = SelectedLaunchProfile;
            AdbDevice? selectedDevice = devices.SelectedObservedDevice;
            NetworkEndpoint? selectedConnectionEndpoint = devices.SelectedConnectionEndpoint;

            if (selectedProfile is null || !ProfileChoices.Contains(selectedProfile) || selectedDevice is null)
            {
                Status = "Select a saved profile and a fresh, explicitly observed ADB transport.";
                return;
            }

            string? selectedRevision = configuration.Revision;
            ConfigurationReadResult committed = await store.ReadAsync(cancellationToken);
            bool selectionChanged = SelectedLaunchProfile != selectedProfile ||
                !ProfileChoices.Contains(selectedProfile) ||
                devices.SelectedObservedDevice != selectedDevice ||
                devices.SelectedConnectionEndpoint != selectedConnectionEndpoint ||
                configuration.Revision != selectedRevision;
            bool editsArrived = configuration.IsDirty || profiles.HasUnstagedChanges ||
                configuration.IsBusy || configuration.RequiresReload;

            if (selectionChanged || editsArrived)
            {
                Status = "Profile, transport or mirroring settings changed during launch; retry after Apply/Cancel.";
                return;
            }

            if (committed.Status != ConfigurationReadStatus.Loaded || committed.Configuration is null ||
                !string.Equals(committed.Revision, selectedRevision, StringComparison.Ordinal))
            {
                Status = "Committed configuration changed or is unavailable; reload before Mirror.";
                return;
            }

            DeviceProfile? committedProfile = committed.Configuration.Profiles.SingleOrDefault(profile =>
                profile.Id == selectedProfile.Id);

            if (selectedConnectionEndpoint is not null &&
                committedProfile?.ConnectionEndpoint != selectedConnectionEndpoint)
            {
                Status = "Selected Wi-Fi endpoint differs from the saved profile; Apply the intended endpoint first.";
                return;
            }

            NativeLaunchPreparation preparation = NativeLaunchPreflight.Prepare(committed.Configuration,
                committed.Revision, selectedProfile.Id, selectedDevice, SessionId.New());

            if (!preparation.IsReady)
            {
                Status = $"Launch blocked: {preparation.Failure}.";
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Status = "Starting the selected native session…";
            INativeSession started = await host.StartAsync(preparation.Request!, cancellationToken);
            bool mustStop;
            Task<bool>? lateStop = null;
            long generation;

            lock (gate)
            {
                mustStop = shuttingDown || stopping || cancellationToken.IsCancellationRequested;
                session = started;
                generation = ++sessionGeneration;
                lifecycleProjection = started is INativeInteractiveSession
                    ? new NativeLifecycleProjection(started.SessionId)
                    : null;
                completedLifecycleEvents = [];
                lifecycleCancellation = started is INativeInteractiveSession
                    ? new CancellationTokenSource()
                    : null;
                lifecycleUiPending = false;

            }

            ProcessId = started switch
            {
                MachineNativeSession machine => machine.ProcessId,
                LegacyNativeSession legacy => legacy.ProcessId,
                _ => null,
            };
            WindowHandle = (started as LegacyNativeSession)?.MainWindowHandle ?? nint.Zero;
            ChannelEvidence = text.Get("devices.machine.channelsUnknown");
            FocusStatus = string.Empty;
            LastLifecycleSequence = null;

            if (!mustStop)
            {
                Status = $"Native session {started.SessionId.Value:D} started. Channel readiness is unverified.";
            }

            lock (gate)
            {
                if (started is INativeInteractiveSession interactive)
                {
                    lifecycleTask = ConsumeLifecycleAsync(interactive, generation, lifecycleCancellation!.Token);
                }

                exitObservationTask = ObserveExitAsync(started, generation);
                mustStop = shuttingDown || stopping || cancellationToken.IsCancellationRequested;

                if (mustStop && !stopping)
                {
                    // Attach both observers before a cancelled Start can dispose its late child.
                    stopping = true;
                    stopTask = StopLateSessionAsync(started);
                    lateStop = stopTask;
                }
            }

            if (mustStop)
            {
                if (lateStop is not null)
                {
                    await lateStop;
                }

                return;
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Launch cancelled.";
        }
        catch (Exception exception)
        {
            bool hasRetainedChild;

            lock (gate)
            {
                hasRetainedChild = session is not null;
            }

            Status = hasRetainedChild
                ? $"Native stop failed ({exception.GetType().Name}); session ownership retained."
                : $"Launch failed ({exception.GetType().Name}).";
        }
        finally
        {
            lock (gate)
            {
                startTask = null;
                startCancellation?.Dispose();
                startCancellation = null;
            }
        }
    }

    /// <summary>Settles a child returned after caller cancellation without awaiting its own start task.</summary>
    private async Task<bool> StopLateSessionAsync(INativeSession owned)
    {
        await Task.Yield();

        try
        {
            await owned.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None);
            await owned.Completion;
            await SettleSessionAsync(owned);
            Status = "Launch cancelled.";
            ProcessId = null;
            WindowHandle = nint.Zero;
            return true;
        }
        catch (Exception exception)
        {
            Status = $"Native stop failed ({exception.GetType().Name}); session ownership retained.";
            return false;
        }
        finally
        {
            lock (gate)
            {
                stopping = false;
                stopTask = null;
            }
        }
    }

    /// <summary>Consumes the one bounded machine stream independently of visible navigation.</summary>
    private async Task ConsumeLifecycleAsync(INativeInteractiveSession owned, long generation,
        CancellationToken cancellationToken)
    {
        await Task.Yield();

        try
        {
            await foreach (NativeLifecycleObservation observation in owned.ObserveLifecycleAsync(cancellationToken))
            {
                bool publish;
                bool terminal;

                lock (gate)
                {
                    if (!ReferenceEquals(session, owned) || sessionGeneration != generation ||
                        lifecycleProjection?.Apply(observation) != true)
                    {
                        continue;
                    }

                    publish = !lifecycleUiPending;
                    lifecycleUiPending = true;
                    terminal = observation.EventType is NativeLifecycleEventType.SessionStopped or
                        NativeLifecycleEventType.FatalError;
                }

                if (publish)
                {
                    dispatch(() => PublishLifecycle(owned, generation));
                }

                if (terminal)
                {
                    NativeTerminationReason reason = observation.Reason switch
                    {
                        NativeLifecycleReason.UserStop => NativeTerminationReason.UserStop,
                        NativeLifecycleReason.WindowClosed => NativeTerminationReason.WindowClosed,
                        NativeLifecycleReason.TransportLost => NativeTerminationReason.TransportLost,
                        NativeLifecycleReason.ProtocolError => NativeTerminationReason.ProtocolError,
                        _ => NativeTerminationReason.NativeFailure,
                    };
                    BeginTerminalCleanup(owned, generation, reason);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Successful exact-child cleanup ends the per-session observation lifetime.
        }
        catch (Exception)
        {
            dispatch(() =>
            {
                lock (gate)
                {
                    if (ReferenceEquals(session, owned) && sessionGeneration == generation)
                    {
                        Status = text.Get("devices.machine.observationFailed");
                    }
                }
            });
            BeginTerminalCleanup(owned, generation, NativeTerminationReason.NativeFailure);
        }
    }

    /// <summary>Publishes the latest processed state with one bounded UI post per session.</summary>
    private void PublishLifecycle(INativeSession owned, long generation)
    {
        lock (gate)
        {
            if (!ReferenceEquals(session, owned) || sessionGeneration != generation ||
                lifecycleProjection is null)
            {
                return;
            }

            lifecycleUiPending = false;
            LastLifecycleSequence = lifecycleProjection.LastSequence;
            string eventKey = lifecycleProjection.EventType switch
            {
                NativeLifecycleEventType.NativeReady => "nativeReady",
                NativeLifecycleEventType.Connecting => "connecting",
                NativeLifecycleEventType.StreamStarted => "streamStarted",
                NativeLifecycleEventType.TransportLost => "transportLost",
                NativeLifecycleEventType.ReconnectScheduled => "reconnectScheduled",
                NativeLifecycleEventType.Reconnecting => "reconnecting",
                NativeLifecycleEventType.StreamResumed => "streamResumed",
                NativeLifecycleEventType.CapabilityDegraded => "capabilityDegraded",
                NativeLifecycleEventType.SessionStopped => "sessionStopped",
                NativeLifecycleEventType.FatalError => "fatalError",
                _ => "nativeReady",
            };
            Status = text.Get("devices.machine." + eventKey);
            ChannelEvidence = text.Get(lifecycleProjection.Video switch
            {
                NativeVideoEvidence.ObservedFrame => "devices.machine.channelsVideoFrame",
                NativeVideoEvidence.NotReady => "devices.machine.channelsVideoUnavailable",
                _ => "devices.machine.channelsVideoUnknown",
            });
        }
    }

    /// <summary>Starts bounded cleanup outside the observation reader to avoid a Completion cycle.</summary>
    private void BeginTerminalCleanup(INativeSession owned, long generation, NativeTerminationReason reason)
    {
        lock (gate)
        {
            if (!ReferenceEquals(session, owned) || sessionGeneration != generation || stopTask is not null)
            {
                return;
            }

            stopping = true;
            startCancellation?.Cancel();
            stopTask = StopCoreAsync(reason, automaticTerminalSession: owned);
        }
    }

    /// <summary>Observes the exact child exit without starting a replacement.</summary>
    private async Task ObserveExitAsync(INativeSession owned, long generation)
    {
        try
        {
            NativeExit result = await owned.Completion;
            dispatch(() =>
            {
                // This method observes its own cleanup failures; sessionCleanupTask owns disposal.
                _ = CompleteExitedSessionAsync(owned, generation, result);
            });
        }
        catch (Exception exception)
        {
            dispatch(() =>
            {
                lock (gate)
                {
                    if (!ReferenceEquals(session, owned) || sessionGeneration != generation)
                    {
                        return;
                    }
                }

                Status = $"Native completion failed ({exception.GetType().Name}); check owned session.";
            });
        }
    }

    /// <summary>Awaits exact-child cleanup after a spontaneous exit before releasing ownership.</summary>
    private async Task CompleteExitedSessionAsync(INativeSession owned, long generation, NativeExit result)
    {
        try
        {
            if (await SettleSessionAsync(owned, skipDuringStop: true))
            {
                Task? starting;

                lock (gate)
                {
                    starting = startTask;
                }

                if (starting is not null)
                {
                    await starting;
                }

                lock (gate)
                {
                    if (session is null && startTask is null && !stopping &&
                        sessionGeneration == generation)
                    {
                        Status = $"Native session ended: {result.Reason}.";
                        ProcessId = null;
                        WindowHandle = nint.Zero;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (ReferenceEquals(session, owned) && sessionGeneration == generation)
                {
                    Status = $"Native cleanup failed ({exception.GetType().Name}); session ownership retained.";
                }
            }
        }
    }

    /// <summary>Shares one disposal task and releases only the exact child after cleanup succeeds.</summary>
    private Task<bool> SettleSessionAsync(INativeSession owned, bool skipDuringStop = false)
    {
        lock (gate)
        {
            if (!ReferenceEquals(session, owned) || (skipDuringStop && stopping))
            {
                return Task.FromResult(false);
            }

            sessionCleanupTask ??= CleanupSessionAsync(owned);
            return sessionCleanupTask;
        }
    }

    /// <summary>Includes disposal and exact-session release in one shared cleanup task.</summary>
    private async Task<bool> CleanupSessionAsync(INativeSession owned)
    {
        await Task.Yield();

        try
        {
            await owned.DisposeAsync();
            Task? observing;
            Task? observingExit;
            Task? focusCommand;
            CancellationTokenSource? cancellation;

            lock (gate)
            {
                observing = lifecycleTask;
                observingExit = exitObservationTask;
                focusCommand = focusCommandTask;
                cancellation = lifecycleCancellation;
            }

            cancellation?.Cancel();
            // Completion closes the machine stream; cancellation also settles a synthetic observer.
            await Task.WhenAll(new[] { observing, observingExit, focusCommand }
                .OfType<Task>()).WaitAsync(LifecycleSettleTimeout);

            cancellation?.Dispose();
        }
        catch
        {
            lock (gate)
            {
                if (ReferenceEquals(session, owned))
                {
                    sessionCleanupTask = null;
                }
            }

            throw;
        }

        lock (gate)
        {
            if (!ReferenceEquals(session, owned))
            {
                return false;
            }

            session = null;
            sessionCleanupTask = null;
            completedLifecycleEvents = lifecycleProjection?.RecentObservations ?? completedLifecycleEvents;
            lifecycleTask = null;
            exitObservationTask = null;
            focusCommandTask = null;
            lifecycleCancellation = null;
            lifecycleProjection = null;
            lifecycleUiPending = false;
            return true;
        }
    }

    /// <summary>Targets the currently owned machine mirror from the explicit local button.</summary>
    public Task<NativeFocusOutcome> FocusAsync(CancellationToken cancellationToken = default) =>
        FocusCoreAsync(null, cancellationToken);

    /// <summary>Accepts activation only for the exact named live machine session.</summary>
    public Task<NativeFocusOutcome> FocusSessionAsync(SessionId sessionId,
        CancellationToken cancellationToken = default) => FocusCoreAsync(sessionId, cancellationToken);

    /// <summary>Observes a command-triggered Focus failure without leaving a faulted task behind.</summary>
    private void StartFocusCommand()
    {
        lock (gate)
        {
            if (focusCommandTask is { IsCompleted: false })
            {
                return;
            }

            // The local button serializes its work; direct FocusAsync callers own their tasks.
            focusCommandTask = ExecuteFocusCommandAsync();
        }
    }

    /// <summary>Handles the local Focus command without an unobserved exception.</summary>
    private async Task ExecuteFocusCommandAsync()
    {
        try
        {
            await FocusAsync();
        }
        catch (Exception)
        {
            FocusStatus = text.Get("devices.machine.focusFailed");
        }
    }

    /// <summary>Reports native main-thread dispatch without claiming Windows foreground permission.</summary>
    private async Task<NativeFocusOutcome> FocusCoreAsync(SessionId? requestedSession,
        CancellationToken cancellationToken)
    {
        INativeInteractiveSession? target;
        long generation;

        lock (gate)
        {
            bool matchesRequested = requestedSession is null || session?.SessionId == requestedSession.Value;
            target = !stopping && !shuttingDown && matchesRequested
                ? session as INativeInteractiveSession
                : null;
            generation = sessionGeneration;
        }

        if (target is null)
        {
            if (requestedSession is null)
            {
                FocusStatus = text.Get("devices.machine.focusUnsupported");
            }

            return NativeFocusOutcome.InvalidState;
        }

        NativeFocusOutcome outcome;

        try
        {
            outcome = await target.FocusWindowAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            outcome = NativeFocusOutcome.Failed;
        }

        lock (gate)
        {
            if (!ReferenceEquals(session, target) || sessionGeneration != generation || stopping)
            {
                return NativeFocusOutcome.InvalidState;
            }
        }

        string key = outcome switch
        {
            NativeFocusOutcome.Applied => "devices.machine.focusApplied",
            NativeFocusOutcome.NoWindow => "devices.machine.focusNoWindow",
            NativeFocusOutcome.InvalidState => "devices.machine.focusInvalidState",
            _ => "devices.machine.focusFailed",
        };
        dispatch(() =>
        {
            lock (gate)
            {
                if (ReferenceEquals(session, target) && sessionGeneration == generation && !stopping)
                {
                    FocusStatus = text.Get(key);
                }
            }
        });
        return outcome;
    }

    /// <summary>Cancels an in-flight start and stops only its owned child.</summary>
    public Task<bool> StopAsync(NativeTerminationReason reason = NativeTerminationReason.UserStop)
    {
        lock (gate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            stopping = true;
            startCancellation?.Cancel();
            stopTask = StopCoreAsync(reason);
            return stopTask;
        }
    }

    /// <summary>Shares cleanup while keeping automatic termination distinct from explicit Stop.</summary>
    private async Task<bool> StopCoreAsync(NativeTerminationReason reason,
        INativeSession? automaticTerminalSession = null)
    {
        await Task.Yield();
        bool automaticTerminalCleanup = automaticTerminalSession is not null;
        Task? starting;
        INativeSession? owned = null;
        Task<bool>? pendingCleanup;

        lock (gate)
        {
            starting = startTask;
        }

        try
        {
            if (starting is not null)
            {
                await starting;
            }

            lock (gate)
            {
                owned = session;
                pendingCleanup = sessionCleanupTask;
            }

            if (pendingCleanup is not null)
            {
                await pendingCleanup;

                lock (gate)
                {
                    owned = session;
                }
            }

            if (owned is not null)
            {
                await owned.StopAsync(reason, CancellationToken.None);
                NativeExit exit = await owned.Completion;
                await SettleSessionAsync(owned);
                // Automatic cleanup does not imply an explicit Stop or successful mirroring.
                bool explicitStop = !automaticTerminalCleanup &&
                    reason is NativeTerminationReason.UserStop or NativeTerminationReason.ApplicationShutdown;
                Status = explicitStop
                    ? "Native session stopped."
                    : $"Native session ended: {exit.Reason}.";
            }
            else
            {
                // Completion may already have settled the captured session before terminal cleanup resumed.
                Status = automaticTerminalSession is null
                    ? "Native session stopped."
                    : $"Native session ended: {(await automaticTerminalSession.Completion).Reason}.";
            }

            ProcessId = null;
            WindowHandle = nint.Zero;
            return true;
        }
        catch (Exception exception)
        {
            bool childExited = owned?.Completion.IsCompletedSuccessfully == true;

            if (childExited && owned is not null)
            {
                try
                {
                    await SettleSessionAsync(owned);
                    ProcessId = null;
                    WindowHandle = nint.Zero;
                    NativeExit exit = await owned.Completion;
                    bool settledUnsuccessfulSession = automaticTerminalCleanup &&
                        exception is MachineNativeSession.StopException
                            { Failure: MachineNativeSession.StopFailure.TerminalFailure } terminalFailure &&
                        terminalFailure.Exit == exit && exit.SessionId == owned.SessionId &&
                        exit.Reason == NativeTerminationReason.NativeFailure;

                    if (settledUnsuccessfulSession)
                    {
                        // The typed session failure remains truthful; disposal separately proved cleanup.
                        Status = $"Native session ended: {exit.Reason}.";
                        return true;
                    }
                }
                catch (Exception cleanupException)
                {
                    Status = $"Native cleanup failed ({cleanupException.GetType().Name}); session ownership retained.";
                    return false;
                }
            }

            if (automaticTerminalCleanup)
            {
                string failure = exception is MachineNativeSession.StopException machineFailure
                    ? machineFailure.Failure.ToString()
                    : exception.GetType().Name;
                Status = childExited
                    ? $"Native cleanup failed ({failure}); the exact child exited and was released."
                    : $"Native cleanup failed ({failure}); session ownership retained.";
                return false;
            }

            Status = childExited
                ? text.Get("devices.machine.stopFailedReleased")
                : $"Native stop failed ({exception.GetType().Name}); session ownership retained.";
            return false;
        }
        finally
        {
            lock (gate)
            {
                stopping = false;
                stopTask = null;
            }
        }
    }

    /// <summary>Reserves exit against new launches and settles the owned child.</summary>
    public Task<bool> ShutdownAsync()
    {
        lock (gate)
        {
            shuttingDown = true;
        }

        return StopAsync(NativeTerminationReason.ApplicationShutdown);
    }

    /// <summary>Releases exit reservation after an unsuccessful child stop.</summary>
    public void ResumeAfterFailedShutdown()
    {
        lock (gate)
        {
            shuttingDown = false;
        }
    }
}
