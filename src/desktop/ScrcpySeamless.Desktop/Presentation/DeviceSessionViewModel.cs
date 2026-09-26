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
    private readonly DevicesViewModel devices;
    private readonly ProfilesViewModel profiles;
    private readonly ConfigurationWorkspaceViewModel configuration;
    private readonly VersionedConfigurationStore store;
    private readonly RuntimeBundleResult runtime;
    private readonly INativeHost? host;
    private readonly Action<Action> dispatch;
    private readonly object gate = new();
    private INativeSession? session;
    private Task<bool>? sessionCleanupTask;
    private Task? startTask;
    private Task<bool>? stopTask;
    private CancellationTokenSource? startCancellation;
    private bool stopping;
    private bool shuttingDown;
    private string status;
    private SavedProfileChoice? selectedLaunchProfile;
    private int? processId;
    private nint windowHandle;

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
        status = runtime.Status == RuntimeBundleStatus.Ready
            ? "Select a saved profile and refresh/select an available ADB transport."
            : $"Runtime unavailable ({runtime.Status}: {runtime.Component ?? "manifest"}). Settings remain editable.";
        MirrorCommand = new ActionCommand(() => _ = MirrorAsync());
        StopCommand = new ActionCommand(() => _ = StopAsync());
    }

    public ICommand MirrorCommand { get; }
    public ICommand StopCommand { get; }
    public ObservableCollection<SavedProfileChoice> ProfileChoices => profiles.Profiles;
    public SavedProfileChoice? SelectedLaunchProfile
    {
        get => selectedLaunchProfile;
        set => SetProperty(ref selectedLaunchProfile, value);
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public int? ProcessId { get => processId; private set => SetProperty(ref processId, value); }
    public nint WindowHandle { get => windowHandle; private set => SetProperty(ref windowHandle, value); }
    public string ChannelEvidence => "Video, audio and control: unknown until hardware observation.";
    public bool HasOwnedSession { get { lock (gate) { return session is not null || startTask is not null; } } }
    public bool IsShuttingDown { get { lock (gate) { return shuttingDown; } } }

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
                Status = $"Runtime unavailable ({runtime.Status}: {runtime.Component ?? "manifest"}).";
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

            lock (gate)
            {
                mustStop = shuttingDown || stopping || cancellationToken.IsCancellationRequested;
                session = started;

                if (mustStop && !stopping)
                {
                    stopping = true;
                    stopTask = StopLateSessionAsync(started);
                    lateStop = stopTask;
                }
            }

            _ = ObserveExitAsync(started);

            if (mustStop)
            {
                if (lateStop is not null)
                {
                    await lateStop;
                }

                return;
            }

            ProcessId = (started as LegacyNativeSession)?.ProcessId;
            WindowHandle = (started as LegacyNativeSession)?.MainWindowHandle ?? nint.Zero;
            Status = $"Native session {started.SessionId.Value:D} started. Channel readiness is unverified.";
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

    /// <summary>Observes the exact child exit without starting a replacement.</summary>
    private async Task ObserveExitAsync(INativeSession owned)
    {
        try
        {
            NativeExit result = await owned.Completion;
            dispatch(() => _ = CompleteExitedSessionAsync(owned, result));
        }
        catch (Exception exception)
        {
            dispatch(() =>
            {
                lock (gate)
                {
                    if (!ReferenceEquals(session, owned))
                    {
                        return;
                    }
                }

                Status = $"Native completion failed ({exception.GetType().Name}); check owned session.";
            });
        }
    }

    /// <summary>Awaits exact-child cleanup after a spontaneous exit before releasing ownership.</summary>
    private async Task CompleteExitedSessionAsync(INativeSession owned, NativeExit result)
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
                    if (session is null && startTask is null && !stopping)
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
            Status = $"Native cleanup failed ({exception.GetType().Name}); session ownership retained.";
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
            return true;
        }
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

    /// <summary>Shares one stop attempt across concurrent callers until cleanup settles.</summary>
    private async Task<bool> StopCoreAsync(NativeTerminationReason reason)
    {
        await Task.Yield();
        Task? starting;
        INativeSession? owned;
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
                await owned.Completion;
                await SettleSessionAsync(owned);
            }

            Status = "Native session stopped.";
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
