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
    private static NormalDesktopComposition Create(string directory)
    {
        DesktopLaunchOptions options = new(false, AppTheme.System, null, true,
            StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: directory);
        return NormalDesktopFactory.Create(options, _ => { }, _ => { }, requested => requested);
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
