using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Checks detached Desktop preference edits and reachable controls under Avalonia.Headless.</summary>
public sealed class DesktopPreferencesTests
{
    /// <summary>Cancel restores the last applied appearance while draft shortcuts remain inactive.</summary>
    [AvaloniaFact]
    public async Task ApplyThenEditThenCancelRestoresLastCommit()
    {
        using PreferencesTestDirectory directory = new();
        VersionedDesktopPreferencesStore store = new(Path.Combine(directory.Path, "desktop-preferences.json"));
        List<DesktopAppearancePreferences> appearances = [];
        List<DesktopShortcutPreferences> activeShortcuts = [];
        DesktopPreferencesViewModel model = new(new PresentationText(), store, appearances.Add, activeShortcuts.Add);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(directory.Path, "desktop-preferences.json")));
        Assert.False(model.IsDirty);
        Assert.Equal("Control+F", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));

        model.Theme = DesktopTheme.Dark;
        model.ScalePercent = 125;
        model.FocusSearchBinding = "Control+3";

        Assert.True(model.IsDirty);
        Assert.Equal(DesktopTheme.Dark, appearances[^1].Theme);
        Assert.Equal("Control+F", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));
        Assert.True(await model.ApplyAsync(TestContext.Current.CancellationToken));
        Assert.False(model.IsDirty);
        Assert.Equal("Control+3", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));

        model.Theme = DesktopTheme.Light;
        model.FocusSearchBinding = "Control+4";
        model.CancelChanges();

        Assert.Equal(DesktopTheme.Dark, model.Theme);
        Assert.Equal(125, model.ScalePercent);
        Assert.Equal("Control+3", model.FocusSearchBinding);
        Assert.Equal(DesktopTheme.Dark, appearances[^1].Theme);
        Assert.Equal("Control+3", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));
        Assert.False(model.IsDirty);

        VersionedDesktopPreferencesStore restarted = new(Path.Combine(directory.Path, "desktop-preferences.json"));
        DesktopPreferencesReadResult read = await restarted.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DesktopTheme.Dark, read.Preferences!.Appearance.Theme);
    }

    /// <summary>Collision and stale revision keep the working draft and do not activate shortcut edits.</summary>
    [AvaloniaFact]
    public async Task InvalidBindingAndRevisionConflictPreserveDraft()
    {
        using PreferencesTestDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);
        store.Commit(null, new DesktopPreferences(), TestContext.Current.CancellationToken);
        List<DesktopShortcutPreferences> activeShortcuts = [];
        DesktopPreferencesViewModel model = new(new PresentationText(), store, _ => { }, activeShortcuts.Add);
        await model.LoadAsync(TestContext.Current.CancellationToken);

        model.ShowDevicesBinding = "Control+F";
        Assert.True(model.HasValidation);
        Assert.False(await model.ApplyAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Control+1", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.ShowDevices));

        model.ShowDevicesBinding = "Control+3";
        DesktopPreferences external = new()
        {
            Appearance = new DesktopAppearancePreferences { Theme = DesktopTheme.Dark },
        };
        DesktopPreferencesReadResult previous = await store.ReadAsync(TestContext.Current.CancellationToken);
        store.Commit(previous.Revision, external, TestContext.Current.CancellationToken);

        Assert.False(await model.ApplyAsync(TestContext.Current.CancellationToken));
        Assert.Contains("changed elsewhere", model.StatusMessage);
        Assert.True(model.IsDirty);
        Assert.Equal("Control+3", model.ShowDevicesBinding);
        Assert.Equal("Control+1", activeShortcuts[^1].EffectiveBinding(DesktopCommandIds.ShowDevices));
    }

    /// <summary>Invalid source remains untouched while safe appearance can still be rendered.</summary>
    [AvaloniaFact]
    public async Task InvalidSourceUsesSafeReadOnlyFallback()
    {
        using PreferencesTestDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        List<DesktopAppearancePreferences> appearances = [];
        DesktopPreferencesViewModel model = new(new PresentationText(), new VersionedDesktopPreferencesStore(path),
            appearances.Add, _ => { });

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(model.IsReady);
        Assert.False(model.CanApply);
        Assert.Equal(DesktopTheme.System, appearances[^1].Theme);
        Assert.Contains("invalid", model.StatusMessage);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    /// <summary>An unavailable saved font uses a visible fallback without rewriting the request.</summary>
    [AvaloniaFact]
    public async Task MissingFontFallbackPreservesSavedChoiceUntilExplicitApply()
    {
        using PreferencesTestDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);
        DesktopPreferences saved = new()
        {
            Appearance = new DesktopAppearancePreferences { FontFamily = "Unavailable Test Font" },
        };
        store.Commit(null, saved, TestContext.Current.CancellationToken);
        byte[] before = File.ReadAllBytes(path);
        DesktopPreferencesViewModel model = new(new PresentationText(), store, _ => { }, _ => { },
            requested => requested == "Unavailable Test Font" ? "Segoe UI" : requested);

        await model.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unavailable Test Font", model.FontFamily);
        Assert.True(model.HasFontSubstitution);
        Assert.Contains("Segoe UI", model.FontSubstitutionMessage);
        Assert.False(model.IsDirty);
        Assert.Equal(before, File.ReadAllBytes(path));

        model.ResetToDefaults();
        Assert.True(model.IsDirty);
        Assert.Equal(before, File.ReadAllBytes(path));
        model.CancelChanges();
        Assert.Equal("Unavailable Test Font", model.FontFamily);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>Apply and recovery controls remain reachable with enlarged metrics and a short window.</summary>
    [AvaloniaFact]
    public async Task EnlargedPreferencesEditorKeepsActionsVisible()
    {
        using PreferencesTestDirectory directory = new();
        App application = Assert.IsType<App>(Application.Current);
        application.ApplyMetricScale(1.5);

        try
        {
            DesktopPreferencesViewModel model = new(new PresentationText(),
                new VersionedDesktopPreferencesStore(Path.Combine(directory.Path, "desktop-preferences.json")),
                _ => { }, _ => { });
            await model.LoadAsync(TestContext.Current.CancellationToken);
            model.Theme = DesktopTheme.Dark;
            DesktopPreferencesView view = new() { DataContext = model };
            Window window = new() { Width = 800, Height = 500, Content = view };
            window.Show();
            window.UpdateLayout();

            Button apply = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "PreferencesApply");
            ScrollViewer scroll = view.FindControl<ScrollViewer>("PreferencesScroll")!;
            Assert.True(apply.Bounds.Height > 0);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(model.CanApply);
            window.Close();
        }
        finally
        {
            application.ApplyMetricScale(1);
        }
    }
}

/// <summary>Owns only its generated synthetic preferences test directory.</summary>
internal sealed class PreferencesTestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "scrcpy-desktop-preferences-" + Guid.NewGuid().ToString("N"));

    public PreferencesTestDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        string fullPath = System.IO.Path.GetFullPath(Path);
        string temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());

        if (!fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Preferences test path is outside the temporary root.");
        }

        Directory.Delete(fullPath, recursive: true);
    }
}
