using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Exercises the selected DEV root through real Desktop composition and restart.</summary>
public sealed class NormalCompositionTests
{
    /// <summary>Missing files remain absent until each independent save group is deliberately applied.</summary>
    [AvaloniaFact]
    public async Task NormalSettingsPersistAcrossFreshCompositionWithoutEagerWrites()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition first = Create(root.Path);
        await first.InitializeAsync(CancellationToken.None);
        Assert.False(File.Exists(first.DataPaths.ConfigurationFile));
        Assert.False(File.Exists(first.DataPaths.DesktopPreferencesFile));
        Assert.Equal(ConfigurationWorkspaceStatus.Missing, first.Configuration.Status);

        first.Settings.SearchText = "max-size";
        OptionRowViewModel row = Assert.Single(first.Settings.VisibleRows);
        row.TextValue = "010";
        first.Profiles.BeginNewProfile();
        first.Profiles.Alias = "Synthetic saved profile";
        first.Profiles.UsbSerial = "synthetic-usb";
        Assert.True(first.Profiles.TrySaveToDraft());
        string profileId = Assert.Single(first.Profiles.Profiles).Id.ToString();
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await first.Configuration.ApplyAsync(CancellationToken.None)).Status);
        first.Preferences.Theme = DesktopTheme.Dark;
        first.Preferences.ScalePercent = 125;
        Assert.True(await first.Preferences.ApplyAsync(CancellationToken.None));

        byte[] configurationBytes = await File.ReadAllBytesAsync(first.DataPaths.ConfigurationFile);
        byte[] preferenceBytes = await File.ReadAllBytesAsync(first.DataPaths.DesktopPreferencesFile);
        NormalDesktopComposition restarted = Create(root.Path);
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Equal(ConfigurationWorkspaceStatus.Ready, restarted.Configuration.Status);
        Assert.Equal("010", restarted.Configuration.Draft!.Mirroring.Options["max-size"].GetString());
        Assert.Equal(profileId, Assert.Single(restarted.Profiles.Profiles).Id.ToString());
        Assert.Equal(DesktopTheme.Dark, restarted.Preferences.Theme);
        Assert.Equal(125, restarted.Preferences.ScalePercent);

        restarted.Settings.SearchText = "audio";
        restarted.Configuration.CancelChanges();
        restarted.Preferences.CancelChanges();
        Assert.Equal(configurationBytes, await File.ReadAllBytesAsync(first.DataPaths.ConfigurationFile));
        Assert.Equal(preferenceBytes, await File.ReadAllBytesAsync(first.DataPaths.DesktopPreferencesFile));
    }

    /// <summary>Two Desktop writers retain a stale draft instead of losing an earlier commit.</summary>
    [AvaloniaFact]
    public async Task NormalCompositionReportsRevisionConflict()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition first = Create(root.Path);
        NormalDesktopComposition second = Create(root.Path);
        await first.InitializeAsync(CancellationToken.None);
        await second.InitializeAsync(CancellationToken.None);
        first.Settings.SearchText = "max-size";
        Assert.Single(first.Settings.VisibleRows).TextValue = "1024";
        second.Settings.SearchText = "max-size";
        Assert.Single(second.Settings.VisibleRows).TextValue = "2048";

        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await first.Configuration.ApplyAsync(CancellationToken.None)).Status);
        Assert.Equal(ConfigurationSessionApplyStatus.RevisionConflict,
            (await second.Configuration.ApplyAsync(CancellationToken.None)).Status);
        Assert.True(second.Configuration.IsDirty);
        Assert.Equal("2048", second.Configuration.Draft!.Mirroring.Options["max-size"].GetString());
    }

    /// <summary>Profile actions save and cancel the same configuration group as option edits.</summary>
    [AvaloniaFact]
    public async Task ProfileActionsShareOptionDraftAndBaseline()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Profiles.BeginNewProfile();
        composition.Profiles.Alias = "Saved synthetic profile";
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await composition.Profiles.ApplyAsync(CancellationToken.None))?.Status);
        Assert.Single(composition.Configuration.Draft!.Profiles);

        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        Assert.True(composition.Configuration.IsDirty);
        composition.Profiles.Cancel();
        Assert.False(composition.Configuration.IsDirty);
        Assert.Empty(composition.Configuration.Draft.Mirroring.Options);
        Assert.Single(composition.Configuration.Draft.Profiles);
    }

    /// <summary>An invalid external replacement supersedes an earlier successful Apply message.</summary>
    [AvaloniaFact]
    public async Task ExplicitReloadShowsCurrentInvalidAuthority()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await composition.Configuration.ApplyAsync(CancellationToken.None)).Status);
        File.WriteAllText(composition.DataPaths.ConfigurationFile, "{invalid synthetic document");

        await composition.Configuration.LoadAsync(CancellationToken.None);

        Assert.Equal(ConfigurationWorkspaceStatus.Invalid, composition.Configuration.Status);
        Assert.Contains("invalid", composition.Settings.ConfigurationStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(composition.Configuration.CanEdit);
        ScrcpySeamless.Desktop.Views.SettingsView view = new() { DataContext = composition.Settings };
        Window window = new() { Content = view };

        try
        {
            window.Show();
            TextBlock status = view.FindControl<TextBlock>("SettingsConfigurationStatus")!;
            Assert.Contains(composition.DataPaths.ConfigurationFile, status.Text);
            Assert.Equal(status.Text, ToolTip.GetTip(status));
            Assert.Equal(status.Text, Avalonia.Automation.AutomationProperties.GetHelpText(status));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>An invalid authority can be retried with an explicitly labelled discard after repair.</summary>
    [AvaloniaFact]
    public async Task InvalidReloadCanRecoverWithoutTrappingTheDirtyDraft()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await composition.Configuration.ApplyAsync(CancellationToken.None)).Status);
        byte[] savedBytes = await File.ReadAllBytesAsync(composition.DataPaths.ConfigurationFile);

        Assert.Single(composition.Settings.VisibleRows).TextValue = "2048";
        await File.WriteAllTextAsync(composition.DataPaths.ConfigurationFile, "{invalid synthetic document");
        Assert.Equal(ConfigurationSessionLoadStatus.Invalid,
            (await composition.Configuration.LoadAsync(CancellationToken.None)).Status);
        Assert.True(composition.Configuration.RequiresReload);
        Assert.True(composition.Configuration.IsDirty);
        Assert.True(composition.Configuration.ReloadCommand.CanExecute(null));
        Assert.Equal("Discard edits and reload", composition.Settings.ConfigurationReloadLabel);

        await File.WriteAllBytesAsync(composition.DataPaths.ConfigurationFile, savedBytes);
        TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        composition.Configuration.DraftReplaced += () => reloaded.TrySetResult();
        composition.Configuration.ReloadCommand.Execute(null);
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ConfigurationWorkspaceStatus.Ready, composition.Configuration.Status);
        Assert.False(composition.Configuration.IsDirty);
        Assert.Equal("1024", composition.Configuration.Draft!.Mirroring.Options["max-size"].GetString());
    }

    /// <summary>Migration preparation refuses valid and invalid unstaged profile work.</summary>
    [AvaloniaFact]
    public async Task MigrationPreparationPreservesPendingProfileEditor()
    {
        using TemporaryDataRoot root = new();
        string legacyDirectory = CreateSyntheticLegacySource(root.Path);
        string legacyFile = Path.Combine(legacyDirectory, "phone.json");
        byte[] legacyBytes = await File.ReadAllBytesAsync(legacyFile);
        NormalDesktopComposition composition = Create(root.Path, legacyDirectory);
        await composition.InitializeAsync(CancellationToken.None);
        ScriptedCloseWindow window = new(composition.Shell)
        {
            NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose,
        };
        window.AttachNormalComposition(composition);
        window.Show();

        try
        {
            composition.Shell.ShowProfiles();
            composition.Profiles.BeginNewProfile();
            composition.Profiles.Alias = "Pending synthetic profile";
            composition.Profiles.UsbSerial = "SYNTHETIC_PENDING";
            await AssertMigrationPreparationBlocked(composition, legacyFile, legacyBytes);
            composition.Profiles.CancelEditorChanges();

            composition.Profiles.BeginNewProfile();
            composition.Profiles.ConnectionEndpoint = "invalid-endpoint";
            Assert.True(composition.Profiles.HasValidation);
            await AssertMigrationPreparationBlocked(composition, legacyFile, legacyBytes);
            composition.Profiles.CancelEditorChanges();

            Assert.True(composition.Configuration.PrepareMigrationCommand.CanExecute(null));
            Assert.Equal(LegacyMigrationStatus.Ready,
                (await composition.Configuration.PrepareMigrationAsync(CancellationToken.None)).Status);
            Assert.Equal(LegacyMigrationStatus.Migrated,
                (await composition.Configuration.CommitMigrationAsync(CancellationToken.None)).Status);
            Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyFile));
            Assert.True(File.Exists(composition.DataPaths.ConfigurationFile));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A prepared migration cannot replace a later valid or invalid profile editor buffer.</summary>
    [AvaloniaFact]
    public async Task MigrationConfirmationPreservesPendingProfileEditor()
    {
        using TemporaryDataRoot root = new();
        string legacyDirectory = CreateSyntheticLegacySource(root.Path);
        string legacyFile = Path.Combine(legacyDirectory, "phone.json");
        byte[] legacyBytes = await File.ReadAllBytesAsync(legacyFile);
        NormalDesktopComposition composition = Create(root.Path, legacyDirectory);
        await composition.InitializeAsync(CancellationToken.None);
        ScriptedCloseWindow window = new(composition.Shell)
        {
            NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose,
        };
        window.AttachNormalComposition(composition);
        window.Show();

        try
        {
            Assert.Equal(LegacyMigrationStatus.Ready,
                (await composition.Configuration.PrepareMigrationAsync(CancellationToken.None)).Status);
            Assert.True(composition.Configuration.HasPreparedMigration);
            composition.Shell.ShowProfiles();

            composition.Profiles.BeginNewProfile();
            composition.Profiles.Alias = "Pending synthetic profile";
            composition.Profiles.UsbSerial = "SYNTHETIC_PENDING";
            await AssertMigrationConfirmationBlocked(composition, legacyFile, legacyBytes);
            composition.Profiles.CancelEditorChanges();

            composition.Profiles.BeginNewProfile();
            composition.Profiles.ConnectionEndpoint = "invalid-endpoint";
            Assert.True(composition.Profiles.HasValidation);
            await AssertMigrationConfirmationBlocked(composition, legacyFile, legacyBytes);
            composition.Profiles.CancelEditorChanges();

            Assert.True(composition.Configuration.CommitMigrationCommand.CanExecute(null));
            Assert.Equal(LegacyMigrationStatus.Migrated,
                (await composition.Configuration.CommitMigrationAsync(CancellationToken.None)).Status);
            Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyFile));
            Assert.True(File.Exists(composition.DataPaths.ConfigurationFile));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Settings actions cannot replace or omit an unstaged profile editor buffer.</summary>
    [AvaloniaFact]
    public async Task SettingsCommandsPreservePendingProfileEdits()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Profiles.BeginNewProfile();
        composition.Profiles.Alias = "Saved profile";
        composition.Profiles.UsbSerial = "SYNTHETIC_SERIAL";
        Assert.True(composition.Profiles.TrySaveToDraft());
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await composition.Configuration.ApplyAsync(CancellationToken.None)).Status);

        string profileId = composition.Profiles.EditingId!;
        composition.Profiles.Alias = "Pending name";
        Assert.True(composition.Profiles.HasUnstagedChanges);
        Assert.False(composition.Configuration.ReloadCommand.CanExecute(null));
        Assert.False(composition.Configuration.ApplyCommand.CanExecute(null));
        composition.Configuration.ReloadCommand.Execute(null);
        composition.Shell.ShowSettings();
        composition.Shell.ShowProfiles();
        Assert.Equal("Pending name", composition.Profiles.Alias);
        Assert.Equal(profileId, composition.Profiles.EditingId);
        Assert.Equal("Saved profile", Assert.Single(composition.Configuration.Draft!.Profiles).Alias);

        composition.Profiles.ConnectionEndpoint = "invalid-endpoint";
        Assert.True(composition.Profiles.HasValidation);
        Assert.False(composition.Configuration.ReloadCommand.CanExecute(null));
        Assert.Equal("invalid-endpoint", composition.Profiles.ConnectionEndpoint);

        composition.Profiles.CancelEditorChanges();
        composition.Profiles.BeginNewProfile();
        string newProfileId = composition.Profiles.EditingId!;
        Assert.False(composition.Configuration.ReloadCommand.CanExecute(null));
        composition.Configuration.ReloadCommand.Execute(null);
        Assert.Equal(newProfileId, composition.Profiles.EditingId);

        composition.Profiles.CancelEditorChanges();
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        Assert.False(composition.Configuration.ReloadCommand.CanExecute(null));
        composition.Profiles.BeginNewProfile();
        composition.Profiles.Alias = "New pending profile";
        composition.Profiles.UsbSerial = "NEW_SYNTHETIC_SERIAL";
        Assert.False(composition.Configuration.ApplyCommand.CanExecute(null));
        Assert.True(composition.Profiles.TrySaveToDraft());
        Assert.True(composition.Configuration.ApplyCommand.CanExecute(null));
    }

    /// <summary>Real window Closing honours the theme group's decision without writing on Stay or Discard.</summary>
    [AvaloniaFact]
    public async Task WindowClosePromptsForSidebarThemeAndPreservesDiscardedFiles()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        ScriptedCloseWindow window = new(composition.Shell);
        window.AttachNormalComposition(composition);
        window.Show();

        composition.Shell.SelectedTheme = Assert.Single(composition.Shell.Themes,
            choice => choice.Theme == AppTheme.Dark);
        Assert.True(composition.HasDirtyGroups);
        Assert.Equal("Desktop appearance and keyboard shortcuts", composition.DirtyGroupsLabel);

        window.NextDecision = ScriptedCloseWindow.Decision.KeepEditing;
        window.Close();
        Assert.True(window.IsVisible);
        Assert.True(composition.Preferences.IsDirty);
        Assert.False(File.Exists(composition.DataPaths.DesktopPreferencesFile));

        window.NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose;
        window.Close();
        Assert.False(window.IsVisible);
        Assert.Equal(2, window.PromptCount);
        Assert.False(File.Exists(composition.DataPaths.DesktopPreferencesFile));
    }

    /// <summary>Repeated close requests share one pending confirmation and leave the editor intact.</summary>
    [AvaloniaFact]
    public async Task RepeatedCloseRequestsDoNotStackConfirmations()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Preferences.Theme = DesktopTheme.Dark;
        ScriptedCloseWindow window = new(composition.Shell);
        window.AttachNormalComposition(composition);
        TaskCompletionSource<ScriptedCloseWindow.Decision> decision = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        window.PendingDecision = decision.Task;
        window.Show();

        window.Close();
        window.Close();
        Assert.Equal(1, window.PromptCount);
        Assert.True(window.IsVisible);
        decision.SetResult(ScriptedCloseWindow.Decision.KeepEditing);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
            Avalonia.Threading.DispatcherPriority.Loaded);
        Assert.True(window.IsVisible);
        Assert.True(composition.Preferences.IsDirty);

        window.PendingDecision = null;
        window.NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose;
        window.Close();
        Assert.Equal(2, window.PromptCount);
        Assert.False(File.Exists(composition.DataPaths.DesktopPreferencesFile));
    }

    /// <summary>Sequential close saves advance only the successful group's baseline after a preference conflict.</summary>
    [AvaloniaFact]
    public async Task PartialCloseSaveKeepsFailedPreferenceDraft()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        composition.Preferences.Theme = DesktopTheme.Dark;
        Assert.Contains("saved devices and mirroring settings", composition.DirtyGroupsLabel);
        Assert.Contains("Desktop appearance and keyboard shortcuts", composition.DirtyGroupsLabel);

        VersionedDesktopPreferencesStore otherWriter = new(composition.DataPaths.DesktopPreferencesFile);
        Assert.Equal(DesktopPreferencesCommitStatus.Committed,
            otherWriter.Commit(null, new DesktopPreferences(), CancellationToken.None).Status);

        Assert.False(await composition.ApplyDirtyGroupsAsync(CancellationToken.None));
        Assert.False(composition.Configuration.IsDirty);
        Assert.True(composition.Preferences.IsDirty);
        Assert.Equal("1024", composition.Configuration.Draft!.Mirroring.Options["max-size"].GetString());
        Assert.True(File.Exists(composition.DataPaths.ConfigurationFile));
        Assert.Equal(DesktopTheme.System,
            (await otherWriter.ReadAsync(CancellationToken.None)).Preferences!.Appearance.Theme);
    }

    /// <summary>Window Save closes only after both files commit and a fresh composition loads them.</summary>
    [AvaloniaFact]
    public async Task WindowSaveAndCloseCommitsBothGroupsBeforeShutdown()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        composition.Preferences.Theme = DesktopTheme.Dark;
        ScriptedCloseWindow window = new(composition.Shell);
        window.AttachNormalComposition(composition);
        window.NextDecision = ScriptedCloseWindow.Decision.SaveAndClose;
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();

        window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        NormalDesktopComposition restarted = Create(root.Path);
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Equal("1024", restarted.Configuration.Draft!.Mirroring.Options["max-size"].GetString());
        Assert.Equal(DesktopTheme.Dark, restarted.Preferences.Theme);
        Assert.Equal(1, window.PromptCount);
    }

    /// <summary>A failed second write keeps the actual close request open and retains its draft.</summary>
    [AvaloniaFact]
    public async Task WindowCloseStaysOpenAfterPartialSave()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Settings.SearchText = "max-size";
        Assert.Single(composition.Settings.VisibleRows).TextValue = "1024";
        composition.Preferences.Theme = DesktopTheme.Dark;
        VersionedDesktopPreferencesStore otherWriter = new(composition.DataPaths.DesktopPreferencesFile);
        otherWriter.Commit(null, new DesktopPreferences(), CancellationToken.None);
        ScriptedCloseWindow window = new(composition.Shell);
        window.AttachNormalComposition(composition);
        window.NextDecision = ScriptedCloseWindow.Decision.SaveAndClose;
        window.Show();

        window.Close();
        await window.FailureShown.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(window.IsVisible);
        Assert.False(composition.Configuration.IsDirty);
        Assert.True(composition.Preferences.IsDirty);
        Assert.Equal(1, window.PromptCount);
        window.NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose;
        window.Close();
        Assert.False(window.IsVisible);
    }

    /// <summary>An invalid unstaged profile prevents close-time writes and keeps its raw editor text.</summary>
    [AvaloniaFact]
    public async Task WindowCloseKeepsInvalidPendingProfileOpen()
    {
        using TemporaryDataRoot root = new();
        NormalDesktopComposition composition = Create(root.Path);
        await composition.InitializeAsync(CancellationToken.None);
        composition.Profiles.BeginNewProfile();
        composition.Profiles.ConnectionEndpoint = "invalid-endpoint";
        string profileId = composition.Profiles.EditingId!;
        ScriptedCloseWindow window = new(composition.Shell);
        window.AttachNormalComposition(composition);
        window.NextDecision = ScriptedCloseWindow.Decision.SaveAndClose;
        window.Show();

        window.Close();
        await window.FailureShown.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(window.IsVisible);
        Assert.Equal(profileId, composition.Profiles.EditingId);
        Assert.Equal("invalid-endpoint", composition.Profiles.ConnectionEndpoint);
        Assert.False(File.Exists(composition.DataPaths.ConfigurationFile));
        window.NextDecision = ScriptedCloseWindow.Decision.DiscardAndClose;
        window.Close();
    }

    /// <summary>Preview composition ignores an explicitly supplied poisoned data root.</summary>
    [Fact]
    public void PreviewDoesNotOpenOrRewriteNearbyConfiguration()
    {
        using TemporaryDataRoot root = new();
        string file = Path.Combine(root.Path, "configuration.v2.json");
        File.WriteAllText(file, "{invalid synthetic document");
        byte[] before = File.ReadAllBytes(file);
        DesktopLaunchOptions options = new(true, AppTheme.System, null, true,
            StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: root.Path);
        ShellViewModel preview = DesktopComposition.Create(options, _ => { });

        Assert.True(preview.IsPreview);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(Path.Combine(root.Path, "desktop-preferences.json")));
    }

    /// <summary>Every validated shortcut spelling activates the corresponding Avalonia key.</summary>
    [AvaloniaFact]
    public void SavedCommaAndPeriodBindingsBecomeActiveGestures()
    {
        ShellViewModel shell = DesktopComposition.Create(
            new DesktopLaunchOptions(false, AppTheme.System, null, false), _ => { });
        MainWindow window = new(shell);

        try
        {
            window.ApplyShortcuts(new DesktopShortcutPreferences
            {
                Bindings = new Dictionary<string, string>
                {
                    [DesktopCommandIds.FocusSettingsSearch] = "Control+Comma",
                    [DesktopCommandIds.ShowDevices] = "Control+Period",
                    [DesktopCommandIds.ShowSettings] = "Control+2",
                },
            });

            Assert.Contains(window.KeyBindings, binding => binding.Gesture?.Key == Key.OemComma);
            Assert.Contains(window.KeyBindings, binding => binding.Gesture?.Key == Key.OemPeriod);
            Assert.Equal(3, window.KeyBindings.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Normal-mode actions and option viewport remain reachable at enlarged laptop metrics.</summary>
    [AvaloniaFact]
    public async Task NormalSettingsKeepsEditorViewportAtEnlargedMetrics()
    {
        using TemporaryDataRoot root = new();
        App application = Assert.IsType<App>(Avalonia.Application.Current);
        application.ApplyMetricScale(1.5);
        NormalDesktopComposition composition = Create(root.Path);
        MainWindow window = new(composition.Shell) { Width = 800, Height = 500 };

        try
        {
            window.Show();
            await composition.InitializeAsync(CancellationToken.None);
            window.UpdateLayout();
            ScrcpySeamless.Desktop.Views.SettingsView settings = Assert.Single(
                window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.SettingsView>());
            ScrollViewer options = settings.FindControl<ScrollViewer>("OptionScroll")!;
            Button apply = settings.FindControl<Button>("CompactConfigurationApplyButton")!;
            Button cancel = settings.FindControl<Button>("CompactConfigurationCancelButton")!;
            Grid rootGrid = settings.FindControl<Grid>("SettingsRoot")!;
            Border statusBlock = settings.FindControl<Border>("SettingsStatusBlock")!;
            Assert.True(options.Bounds.Height > 60,
                $"Normal option viewport={options.Bounds.Height}, settings={settings.Bounds.Height}, " +
                $"root={rootGrid.Bounds.Height}, status={statusBlock.Bounds.Height}, columns=" +
                $"{settings.FindControl<Grid>("SettingsColumns")!.Bounds.Height} logical pixels.");
            Assert.True(apply.Bounds.Height > 0);
            Assert.True(cancel.Bounds.Height > 0);
        }
        finally
        {
            window.Close();
            application.ApplyMetricScale(1);
        }
    }

    /// <summary>Builds a real local settings shell with no native or network adapters.</summary>
    private static NormalDesktopComposition Create(string directory, string? legacyDirectory = null)
    {
        DesktopLaunchOptions options = new(false, AppTheme.System, null, true,
            StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: directory,
            LegacyDevelopmentDirectory: legacyDirectory);
        return NormalDesktopFactory.Create(options, _ => { }, _ => { }, requested => requested);
    }

    /// <summary>Creates only the synthetic legacy input used by migration-boundary tests.</summary>
    private static string CreateSyntheticLegacySource(string dataDirectory)
    {
        string legacyDirectory = Path.Combine(dataDirectory, "synthetic-legacy");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "phone.json"),
            """{"UsbSerial":"SYNTHETIC_USB","ConnectionMode":"usb"}""");
        return legacyDirectory;
    }

    /// <summary>Proves a blocked preparation retains raw editor and disk state.</summary>
    private static async Task AssertMigrationPreparationBlocked(
        NormalDesktopComposition composition, string legacyFile, byte[] legacyBytes)
    {
        string profileId = composition.Profiles.EditingId!;
        string alias = composition.Profiles.Alias;
        string endpoint = composition.Profiles.ConnectionEndpoint;
        Assert.True(composition.Profiles.HasUnstagedChanges);
        Assert.False(composition.Configuration.CanPrepareMigration);
        Assert.False(composition.Configuration.PrepareMigrationCommand.CanExecute(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composition.Configuration.PrepareMigrationAsync(CancellationToken.None));
        Assert.Equal(profileId, composition.Profiles.EditingId);
        Assert.Equal(alias, composition.Profiles.Alias);
        Assert.Equal(endpoint, composition.Profiles.ConnectionEndpoint);
        Assert.Empty(composition.Configuration.Draft!.Profiles);
        Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyFile));
        Assert.False(File.Exists(composition.DataPaths.ConfigurationFile));
    }

    /// <summary>Proves a blocked confirmation keeps its proposal and editor intact.</summary>
    private static async Task AssertMigrationConfirmationBlocked(
        NormalDesktopComposition composition, string legacyFile, byte[] legacyBytes)
    {
        string profileId = composition.Profiles.EditingId!;
        string alias = composition.Profiles.Alias;
        string endpoint = composition.Profiles.ConnectionEndpoint;
        Assert.True(composition.Profiles.HasUnstagedChanges);
        Assert.False(composition.Configuration.CommitMigrationCommand.CanExecute(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composition.Configuration.CommitMigrationAsync(CancellationToken.None));
        Assert.True(composition.Configuration.HasPreparedMigration);
        Assert.Equal(profileId, composition.Profiles.EditingId);
        Assert.Equal(alias, composition.Profiles.Alias);
        Assert.Equal(endpoint, composition.Profiles.ConnectionEndpoint);
        Assert.Empty(composition.Configuration.Draft!.Profiles);
        Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(legacyFile));
        Assert.False(File.Exists(composition.DataPaths.ConfigurationFile));
    }

    /// <summary>Feeds deterministic confirmation results through the actual window Closing handler.</summary>
    private sealed class ScriptedCloseWindow(ShellViewModel shell) : MainWindow(shell)
    {
        private readonly TaskCompletionSource failureShown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public enum Decision { KeepEditing, DiscardAndClose, SaveAndClose }

        public Decision NextDecision { get; set; }
        public Task<Decision>? PendingDecision { get; set; }
        public int PromptCount { get; private set; }
        public Task FailureShown => failureShown.Task;

        protected override async Task<CloseDecision> AskCloseDecisionAsync(string groups)
        {
            PromptCount++;
            Decision decision = PendingDecision is null ? NextDecision : await PendingDecision;
            return decision switch
            {
                Decision.SaveAndClose => CloseDecision.Apply,
                Decision.DiscardAndClose => CloseDecision.Discard,
                _ => CloseDecision.Stay,
            };
        }

        protected override Task ShowCloseFailureAsync()
        {
            failureShown.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>Owns only synthetic test state and removes it after assertions.</summary>
    private sealed class TemporaryDataRoot : IDisposable
    {
        public TemporaryDataRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcpy-p05b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
