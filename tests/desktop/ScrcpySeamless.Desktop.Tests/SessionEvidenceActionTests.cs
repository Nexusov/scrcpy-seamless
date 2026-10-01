using System.ComponentModel;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Exercises the packaged DEV binding path with an injected sink, never the real clipboard.</summary>
public sealed class SessionEvidenceActionTests
{
    private static readonly TimeSpan CopyWatchdog = TimeSpan.FromSeconds(10);

    /// <summary>The DEV button copies only on request and keeps failures separate from session status.</summary>
    [AvaloniaFact]
    public async Task DevEvidenceBindingRetriesClipboardFailureWithoutChangingSessionOrFiles()
    {
        using TemporaryRoot root = new();
        int sinkCalls = 0;
        string? copied = null;
        NormalDesktopComposition composition = NormalDesktopFactory.Create(root.Options(deviceEnabled: true),
            _ => { }, _ => { }, font => font, copySessionEvidence: content =>
            {
                sinkCalls++;

                if (sinkCalls == 1)
                {
                    throw new IOException("synthetic-secret-like-clipboard-failure");
                }

                copied = content;
                return Task.CompletedTask;
            });
        await composition.InitializeAsync(TestContext.Current.CancellationToken);
        DeviceSessionViewModel actions = Assert.IsType<DeviceSessionViewModel>(composition.DeviceSession);
        DeviceSessionView view = new() { DataContext = actions };
        Window window = new() { Content = view };
        window.Show();

        try
        {
            Button button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), control =>
                AutomationProperties.GetAutomationId(control) == "devices.copySessionEvidence");
            Assert.True(button.IsVisible);
            Assert.Same(actions.CopySessionEvidenceCommand, button.Command);
            Assert.True(button.Command!.CanExecute(button.CommandParameter));
            Assert.Equal(0, sinkCalls);
            Assert.Empty(System.IO.Directory.GetFiles(root.Directory));
            string originalStatus = actions.Status;
            SessionEvidenceSnapshot original = actions.CaptureSessionEvidence();
            Task failed = AwaitCopyStatusAsync(actions, "Session evidence could not be copied");
            button.Command.Execute(button.CommandParameter);
            await failed;
            Assert.Equal(1, sinkCalls);
            Assert.Equal(originalStatus, actions.Status);
            Assert.False(actions.HasOwnedSession);
            Assert.DoesNotContain("synthetic-secret", actions.EvidenceCopyStatus);
            Assert.Equal(original.Events, actions.CaptureSessionEvidence().Events);

            Task succeeded = AwaitCopyStatusAsync(actions, "Session evidence copied");
            button.Command.Execute(button.CommandParameter);
            await succeeded;
            Assert.Equal(2, sinkCalls);
            using JsonDocument evidence = JsonDocument.Parse(Assert.IsType<string>(copied));
            Assert.Equal("NotRecorded", evidence.RootElement.GetProperty("Handshake")
                .GetProperty("Availability").GetString());
            Assert.Equal("NoSession", evidence.RootElement.GetProperty("ObservationStage").GetString());
            Assert.Equal(originalStatus, actions.Status);
            Assert.Empty(System.IO.Directory.GetFiles(root.Directory));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Settings-only and preview composition expose no device evidence action or sink side effects.</summary>
    [AvaloniaFact]
    public async Task SettingsOnlyAndPreviewDoNotExposeOrInvokeEvidenceCopy()
    {
        using TemporaryRoot root = new();
        int sinkCalls = 0;
        NormalDesktopComposition settingsOnly = NormalDesktopFactory.Create(root.Options(deviceEnabled: false),
            _ => { }, _ => { }, font => font, copySessionEvidence: _ =>
            {
                sinkCalls++;
                return Task.CompletedTask;
            });
        await settingsOnly.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(settingsOnly.DeviceSession);
        Assert.Null(settingsOnly.Devices.SessionActions);
        ShellViewModel preview = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, false), _ => { });
        Assert.Null(preview.Devices.SessionActions);
        Assert.Equal(0, sinkCalls);
        Assert.Empty(System.IO.Directory.GetFiles(root.Directory));
    }

    /// <summary>Waits for the separate observation result through a subscribe-and-recheck barrier.</summary>
    private static async Task AwaitCopyStatusAsync(DeviceSessionViewModel actions, string prefix)
    {
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler changed = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(actions.EvidenceCopyStatus) &&
                actions.EvidenceCopyStatus.StartsWith(prefix, StringComparison.Ordinal))
            {
                observed.TrySetResult();
            }
        };
        actions.PropertyChanged += changed;

        try
        {
            if (!actions.EvidenceCopyStatus.StartsWith(prefix, StringComparison.Ordinal))
            {
                await observed.Task.WaitAsync(CopyWatchdog, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            actions.PropertyChanged -= changed;
        }
    }

    /// <summary>Owns a unique synthetic data root and deliberately absent runtime with no ADB adapter.</summary>
    private sealed class TemporaryRoot : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(),
            "scrcpy-session-evidence-" + Guid.NewGuid().ToString("N"));

        /// <summary>Creates only a private test directory.</summary>
        public TemporaryRoot() => System.IO.Directory.CreateDirectory(Directory);

        /// <summary>Exercises the actual explicit DEV composition without constructing a ready runtime.</summary>
        public DesktopLaunchOptions Options(bool deviceEnabled) => new(false, AppTheme.System, null, false,
            StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: Directory,
            DeviceRuntimeDirectory: deviceEnabled ? Path.Combine(Directory, "absent-runtime") : null);

        /// <summary>Removes only this fully resolved unique test-owned root.</summary>
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
