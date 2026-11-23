using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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

public sealed partial class DeviceSessionViewModelTests
{
    [AvaloniaFact]
    public async Task WorkspaceKeepsTwoChildrenIndependentAndRoutesExactFocus()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture, interactive: true);
        DeviceSessionViewModel first = scenario.Workspace.Entries[0];
        await first.MirrorAsync();
        SessionId firstId = first.CurrentSessionId!.Value;
        scenario.Composition.Devices.SelectedDeviceChoice = scenario.Composition.Devices.ObservedDevices[1];
        DeviceSessionViewModel second = scenario.Workspace.AddSession()!;
        second.SelectedLaunchProfile = second.ProfileChoices[1];
        await second.MirrorAsync();
        Assert.True(first.HasOwnedSession && second.HasOwnedSession);
        Assert.NotEqual(first.CurrentSessionId, second.CurrentSessionId);
        Assert.Equal(NativeFocusOutcome.NoWindow, await scenario.Workspace.FocusSessionAsync(firstId));
        Assert.Equal(1, scenario.Interactive[0].FocusCalls);
        Assert.Equal(0, scenario.Interactive[1].FocusCalls);
        Assert.True(await first.StopAsync());
        Assert.True(second.HasOwnedSession);
        Assert.Equal(0, scenario.Interactive[1].StopCalls);
        Assert.True(await scenario.Workspace.ShutdownAsync());
        Assert.All(scenario.Workspace.Entries, entry => Assert.False(entry.HasOwnedSession));
    }

    [AvaloniaFact]
    public async Task WorkspaceReservationSurvivesCleanupFailureUntilTheExactOwnerSettles()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture);
        DeviceSessionViewModel first = scenario.Workspace.Entries[0];
        RetainedDisposalSession retained = new(scenario.Hosts[0].Session);
        scenario.Hosts[0].OnStart = (_, _) => Task.FromResult<INativeSession>(retained);
        await first.MirrorAsync();
        Assert.False(await first.StopAsync());
        DeviceSessionViewModel duplicate = scenario.Workspace.AddSession()!;
        await duplicate.MirrorAsync();
        Assert.Equal(0, scenario.Hosts[1].Starts);
        Assert.Contains("already owns", duplicate.Status);
        Assert.True(first.HasOwnedSession);
        retained.AllowDisposal = true;
        Assert.True(await first.StopAsync());
        await duplicate.MirrorAsync();
        Assert.Equal(1, scenario.Hosts[1].Starts);
        Assert.True(await scenario.Workspace.ShutdownAsync());
    }

    [AvaloniaFact]
    public async Task WorkspaceReservesStartingProfileAndSettlesLateCancelledChild()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture);
        DeviceSessionViewModel first = scenario.Workspace.Entries[0];
        TaskCompletionSource<INativeSession> acquisition = new(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Hosts[0].OnStart = (_, _) => acquisition.Task;
        Task starting = first.MirrorAsync();
        await scenario.Hosts[0].Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        DeviceSessionViewModel duplicate = scenario.Workspace.AddSession()!;
        await duplicate.MirrorAsync();
        Assert.Equal(0, scenario.Hosts[1].Starts);
        Task<bool> stopping = first.StopAsync();
        acquisition.SetResult(scenario.Hosts[0].Session);
        await starting;
        Assert.True(await stopping);
        Assert.Equal(1, scenario.Hosts[0].Session.StopCalls);
        Assert.Equal(1, scenario.Hosts[0].Session.DisposeCalls);
        await duplicate.MirrorAsync();
        Assert.Equal(1, scenario.Hosts[1].Starts);
        Assert.True(await scenario.Workspace.ShutdownAsync());
    }

    [AvaloniaFact]
    public async Task WorkspaceShutdownSettlesOtherChildrenWhileRetainingTheFailure()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture);
        await scenario.Workspace.Entries[0].MirrorAsync();
        scenario.Composition.Devices.SelectedDeviceChoice = scenario.Composition.Devices.ObservedDevices[1];
        DeviceSessionViewModel second = scenario.Workspace.AddSession()!;
        second.SelectedLaunchProfile = second.ProfileChoices[1];
        await second.MirrorAsync();
        scenario.Hosts[0].Session.FailNextStop = true;
        Assert.False(await scenario.Workspace.ShutdownAsync());
        Assert.True(scenario.Workspace.Entries[0].HasOwnedSession);
        Assert.False(second.HasOwnedSession);
        Assert.False(scenario.Workspace.CanAdd);
        scenario.Workspace.ResumeAfterFailedShutdown();
        Assert.True(await scenario.Workspace.ShutdownAsync());
        Assert.Equal(1, scenario.Hosts[1].Session.StopCalls);
    }

    [AvaloniaFact]
    public async Task WorkspaceEntryBoundAndRemovalNeverDropAnOwnedChild()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture);
        await scenario.Workspace.Entries[0].MirrorAsync();
        Assert.False(scenario.Workspace.RemoveSelected());
        for (int index = 1; index < SessionsWorkspaceViewModel.MaximumEntries; index++)
        {
            Assert.NotNull(scenario.Workspace.AddSession());
        }

        Assert.Null(scenario.Workspace.AddSession());
        Assert.Equal(8, scenario.Workspace.Entries.Count);
        Assert.True(scenario.Workspace.RemoveSelected());
        Assert.NotNull(scenario.Workspace.AddSession());
        Assert.True(await scenario.Workspace.ShutdownAsync());
    }

    [AvaloniaFact]
    public async Task WorkspacePagesAndDiagnosticsUseExistingOwnersWithoutStartingDevices()
    {
        using Fixture fixture = new();
        NormalDesktopComposition composition = fixture.CreateComposition("absent-runtime");
        await composition.InitializeAsync(CancellationToken.None);
        Assert.NotNull(composition.Sessions);
        MainWindow window = new(composition.Shell) { Width = 900, Height = 620 };
        window.Show();

        try
        {
            Button sessionsNavigation = window.FindControl<Button>("SessionsNavigation")!;
            Assert.True(sessionsNavigation.IsVisible);
            sessionsNavigation.Command!.Execute(null);
            window.UpdateLayout();
            Assert.Same(composition.Sessions, composition.Shell.CurrentPage);
            ScrcpySeamless.Desktop.Views.SessionsView sessions = Assert.Single(window.GetVisualDescendants()
                .OfType<ScrcpySeamless.Desktop.Views.SessionsView>());
            Assert.Single(sessions.GetVisualDescendants().OfType<ComboBox>(), control =>
                AutomationProperties.GetAutomationId(control) == "sessions.selection");
            Assert.Single(sessions.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.DeviceSessionView>());
            window.FindControl<Button>("DiagnosticsNavigation")!.Command!.Execute(null);
            window.UpdateLayout();
            DiagnosticsWorkspaceViewModel diagnostics = Assert.IsType<DiagnosticsWorkspaceViewModel>(composition.Shell.CurrentPage);
            Assert.Equal("NoSession", Assert.Single(diagnostics.Rows).Facts.Single(fact => fact.AutomationId == "diagnostics.owner").Value);
            ScrcpySeamless.Desktop.Views.DiagnosticsView diagnosticsView = Assert.Single(window.GetVisualDescendants()
                .OfType<ScrcpySeamless.Desktop.Views.DiagnosticsView>());
            Assert.Contains(diagnosticsView.GetVisualDescendants().OfType<TextBlock>(), control => control.Text == "NoSession");
            Assert.False(composition.Devices.IsLiveEnabled);
            composition.Devices.SetupProfilesCommand!.Execute(null);
            Assert.Same(composition.Profiles, composition.Shell.CurrentPage);
            Assert.True(composition.Sessions!.RemoveSelected());
            Assert.Null(composition.DeviceSession);
            Assert.Null(composition.Devices.SessionActions);
            Assert.True(composition.HasLiveOperations);
            Assert.True(await composition.ShutdownLiveAsync());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PreviewCannotNavigateIntoRealSessionOwners()
    {
        ShellViewModel preview = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true), _ => { });
        object initial = preview.CurrentPage;
        preview.ShowSessionsCommand.Execute(null);
        preview.ShowDiagnosticsCommand.Execute(null);
        Assert.Same(initial, preview.CurrentPage);
        Assert.False(preview.IsSessionsAvailable);
        Assert.False(preview.IsDiagnosticsAvailable);
        Assert.Null(preview.Devices.SessionsWorkspace);
    }

    [AvaloniaFact]
    public async Task WorkspaceConsumesIndependentDegradationWhileSettingsIsVisible()
    {
        using Fixture fixture = new();
        WorkspaceScenario scenario = await CreateWorkspaceAsync(fixture, interactive: true);
        DeviceSessionViewModel first = scenario.Workspace.Entries[0];
        await first.MirrorAsync();
        ConnectionAttemptId attempt = ConnectionAttemptId.New();
        await scenario.Interactive[0].EmitAsync(1, NativeLifecycleEventType.Connecting, attempt);
        await scenario.Interactive[0].EmitAsync(2, NativeLifecycleEventType.StreamStarted, attempt,
            subsystem: NativeLifecycleSubsystem.Video);
        await AwaitSequenceAsync(first, 2);
        Assert.Equal("Presented frame observed", first.VideoEvidence);
        Assert.Equal("Unverified", first.AudioEvidence);
        Assert.Equal("Unverified", first.ControlEvidence);
        scenario.Composition.Shell.ShowSettings();
        await scenario.Interactive[0].EmitAsync(3, NativeLifecycleEventType.CapabilityDegraded,
            subsystem: NativeLifecycleSubsystem.Audio);
        await AwaitSequenceAsync(first, 3);
        Assert.Equal("Unavailable", first.AudioEvidence);
        Assert.Equal("Unverified", first.ControlEvidence);
        Assert.Equal("Presented frame observed", first.VideoEvidence);
        Assert.Same(scenario.Composition.Settings, scenario.Composition.Shell.CurrentPage);
        Assert.True(await scenario.Workspace.ShutdownAsync());
    }

    private sealed record WorkspaceScenario(NormalDesktopComposition Composition,
        SessionsWorkspaceViewModel Workspace, List<FakeHost> Hosts, List<InteractiveSession> Interactive);

    private sealed class RetainedDisposalSession(FakeSession inner) : INativeSession
    {
        public volatile bool AllowDisposal;
        public SessionId SessionId => inner.SessionId;
        public Task<NativeExit> Completion => inner.Completion;
        public Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken) =>
            inner.StopAsync(reason, cancellationToken);
        public ValueTask DisposeAsync() => AllowDisposal ? inner.DisposeAsync()
            : ValueTask.FromException(new IOException("Controlled retained disposal failure."));
    }

    private static async Task<WorkspaceScenario> CreateWorkspaceAsync(Fixture fixture, bool interactive = false)
    {
        NormalDesktopComposition composition = fixture.CreateComposition();
        VersionedConfigurationStore store = new(composition.DataPaths.ConfigurationFile);
        ConfigurationV2 saved = new()
        {
            Profiles = [new DeviceProfile { Id = ProfileId.New(), UsbIdentity = new UsbSerial("synthetic-a"), Alias = "First" },
                new DeviceProfile { Id = ProfileId.New(), UsbIdentity = new UsbSerial("synthetic-b"), Alias = "Second" }],
        };
        Assert.Equal(ConfigurationCommitStatus.Committed, store.Commit(null, saved, CancellationToken.None).Status);
        await composition.InitializeAsync(CancellationToken.None);
        MultiGateway gateway = new();
        composition.Devices.AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway), action => action());
        await composition.Devices.RefreshAsync();
        composition.Devices.SelectedDeviceChoice = composition.Devices.ObservedDevices[0];
        List<FakeHost> hosts = [];
        List<InteractiveSession> native = [];
        SessionsWorkspaceViewModel workspace = new(() =>
        {
            FakeHost host = new();
            hosts.Add(host);

            if (interactive)
            {
                InteractiveSession child = new();
                native.Add(child);
                host.OnStart = (request, _) =>
                {
                    child.SessionId = request.SessionId;
                    return Task.FromResult<INativeSession>(child);
                };
            }

            DeviceSessionViewModel entry = new(composition.Devices, composition.Profiles, composition.Configuration,
                store, new RuntimeBundleResult(null, RuntimeBundleStatus.Ready, null), host, action => action());
            entry.SelectedLaunchProfile = entry.ProfileChoices[0];
            return entry;
        }, _ => { }, new PresentationText());
        return new WorkspaceScenario(composition, workspace, hosts, native);
    }

    private sealed class MultiGateway : IAdbGateway
    {
        public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbDevice>>.Success([
                new AdbDevice("synthetic-a", AdbDeviceState.Device, "First"),
                new AdbDevice("synthetic-b", AdbDeviceState.Device, "Second")]));
        public Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbMdnsService>>.Success([]));
        public Task<AdbResult<bool>> PairAsync(NetworkEndpoint endpoint, string code, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No pairing in workspace fixtures.");
        public Task<AdbResult<bool>> ConnectAsync(NetworkEndpoint endpoint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No network in workspace fixtures.");
        public Task<AdbResult<string>> GetDeviceSerialPropertyAsync(NetworkEndpoint endpoint, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No device properties in workspace fixtures.");
    }
}
