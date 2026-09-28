using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Text.Json;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Anchors the static native reference and its side-effect-free Settings surface.</summary>
public sealed class ShortcutsReferenceTests
{
    /// <summary>Matches representative native handler cases and mode qualifiers independently.</summary>
    [Fact]
    public void NativeReferenceMatchesBundledHandler()
    {
        NativeShortcutReference reference = new(new PresentationText());
        NativeShortcutRow[] rows = reference.Groups.SelectMany(group => group.Rows).ToArray();

        Assert.Equal("MOD+H", Assert.Single(rows, row => row.Action == "Home").Gesture);
        Assert.Equal("MOD+B · MOD+Backspace", Assert.Single(rows, row => row.Action == "Back").Gesture);
        Assert.Equal("MOD+F · F11", Assert.Single(rows, row => row.Action == "Toggle fullscreen").Gesture);
        Assert.Equal("MOD+Left", Assert.Single(rows, row => row.Action == "Rotate displayed image left on PC").Gesture);
        Assert.Equal("MOD+R", Assert.Single(rows, row => row.Action == "Request Android device display rotation").Gesture);
        Assert.Contains("Left Alt or Left Windows", reference.ModifierLegend);
        Assert.Contains("next mirror launch", reference.ModifierLegend);
        Assert.Contains("press and release N twice", Assert.Single(rows,
            row => row.Action == "Expand quick settings").Gesture);
        Assert.Contains("Android 7 or newer", Assert.Single(rows,
            row => row.Action == "Copy device selection to computer clipboard").Condition);
        Assert.Contains("HID keyboard", Assert.Single(rows,
            row => row.Action == "Open device keyboard settings").Condition);
        Assert.Contains("Camera mode", Assert.Single(rows,
            row => row.Action == "Turn camera torch on").Condition);
        Assert.Contains("Mouse mode or mouse-bind", Assert.Single(rows,
            row => row.Action == "Back or wake screen by mouse").Condition);
        Assert.True(rows.Length >= 40);
    }

    /// <summary>Opens reference-only preview by keyboard without changing settings drafts.</summary>
    [AvaloniaFact]
    public void PreviewShortcutNavigationIsAccessibleAndDoesNotEditDraft()
    {
        ShellViewModel shell = DesktopComposition.Create(
            new DesktopLaunchOptions(true, AppTheme.Dark, null, true), _ => { });
        SettingsViewModel settings = shell.Settings;
        Dictionary<string, JsonElement> baseline = settings.DraftValues.ToDictionary();
        shell.ShowSettings();
        MainWindow window = new(shell) { Width = 660, Height = 460 };
        window.Show();
        window.UpdateLayout();

        SettingsView view = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
        Button tab = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
            button => button.IsEffectivelyVisible &&
                AutomationProperties.GetAutomationId(button) == "settings.section.shortcuts");
        Assert.Equal("Shortcuts", AutomationProperties.GetName(tab));
        tab.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.UpdateLayout();
        Assert.True(settings.IsShortcutsSection);
        Assert.Equal(baseline, settings.DraftValues);
        Assert.Null(settings.Configuration);
        Assert.Null(settings.Preferences);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetAutomationId(button) == "shortcuts.editControlCenter" && button.IsVisible);

        Button modifier = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetAutomationId(button) == "shortcuts.modifierOption");
        modifier.Command!.Execute(null);
        Assert.True(settings.IsMirroringSection);
        Assert.Equal("shortcut-mod", settings.SearchText);
        Assert.Equal(baseline, settings.DraftValues);
        window.Close();
    }

    /// <summary>Keeps the final read-only entry reachable in both themes at enlarged laptop metrics.</summary>
    [AvaloniaFact]
    public void ReferenceScrollsAtSmallWindowAndEnlargedMetrics()
    {
        App application = Assert.IsType<App>(Application.Current);
        application.ApplyMetricScale(1.5);

        try
        {
            foreach (AppTheme theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ShellViewModel shell = DesktopComposition.Create(
                    new DesktopLaunchOptions(true, theme, null, true), _ => { });
                shell.ShowSettings();
                shell.Settings.ShowShortcutsCommand.Execute(null);
                MainWindow window = new(shell) { Width = 660, Height = 460 };
                window.Show();
                window.UpdateLayout();
                ShortcutsReferenceView reference = Assert.Single(
                    window.GetVisualDescendants().OfType<ShortcutsReferenceView>());
                ScrollViewer scroll = reference.FindControl<ScrollViewer>("ShortcutReferenceScroll")!;
                Assert.True(scroll.Viewport.Height > 0,
                    $"The reference has no viewport at {theme} and 150%: {reference.Bounds}");
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                window.UpdateLayout();
                Assert.True(scroll.Offset.Y > 0);
                Assert.Contains(reference.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.Text == "Zoom camera out");
                window.Close();
            }
        }
        finally
        {
            application.ApplyMetricScale(1);
        }
    }

    /// <summary>Links normal mode to the same preferences editor without writing a new document.</summary>
    [AvaloniaFact]
    public void ControlCenterLinkReusesTheExistingEditor()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"scrcpy-shortcuts-{Guid.NewGuid():N}");
        DesktopLaunchOptions options = new(false, AppTheme.System, null, true,
            StorageMode: DesktopStorageMode.Development, DevelopmentDataDirectory: directory);
        NormalDesktopComposition composition = NormalDesktopFactory.Create(
            options, _ => { }, _ => { }, requested => requested);
        SettingsViewModel settings = composition.Settings;
        settings.ShowShortcutsCommand.Execute(null);
        MainWindow window = new(composition.Shell) { Width = 900, Height = 620 };
        window.Show();
        window.UpdateLayout();

        ShortcutsReferenceView reference = Assert.Single(
            window.GetVisualDescendants().OfType<ShortcutsReferenceView>());
        Button editorLink = Assert.Single(reference.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetAutomationId(button) == "shortcuts.editControlCenter");
        Assert.True(editorLink.IsVisible);
        editorLink.Command!.Execute(null);
        window.UpdateLayout();
        Assert.True(settings.IsDesktopSection);
        Assert.Same(composition.Preferences, settings.Preferences);
        Assert.Single(window.GetVisualDescendants().OfType<DesktopPreferencesView>());
        Assert.False(composition.Preferences.IsDirty);
        Assert.False(Directory.Exists(directory));
        window.Close();
    }
}
