using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;
using System.Text.Json;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ScrcpySeamless.Desktop.Tests.TestApplicationBuilder))]

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Builds the real application against the in-memory platform.</summary>
public static class TestApplicationBuilder
{
    /// <summary>Configures the application without a visible operating-system window.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

/// <summary>Checks that the product shell and typed views load under Avalonia.Headless.</summary>
public sealed class FoundationTests
{
    /// <summary>Measures the actual bounded Settings viewport and reaches the final option at laptop sizes.</summary>
    [AvaloniaFact]
    public void SettingsControlsRemainReachableAtReferenceSizes()
    {
        foreach ((double width, double height) in new[] { (800d, 500d), (900d, 620d), (1080d, 720d), (1440d, 900d) })
        {
            var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true), _ => { });
            var window = new MainWindow(shell) { Width = width, Height = height };
            window.Show();
            window.UpdateLayout();
            var settings = Assert.Single(window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.SettingsView>());
            var options = settings.FindControl<ScrollViewer>("OptionScroll")!;
            var picker = settings.FindControl<ComboBox>("CompactCategoryPicker")!;
            var categories = settings.FindControl<ListBox>("CategoryList")!;
            var reset = settings.FindControl<Button>("ResetDraftButton")!;
            Assert.True(options.Viewport.Height > 0, $"No option viewport at {width}x{height}");
            Assert.True(options.Extent.Height > options.Viewport.Height, $"Options do not scroll at {width}x{height}");
            Assert.True(reset.Bounds.Right <= settings.Bounds.Width, $"Reset clipped at {width}x{height}");
            Assert.True(picker.IsVisible || categories.Bounds.Height <= settings.Bounds.Height,
                $"Category navigation unbounded at {width}x{height}");
            var firstOption = window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.OptionRowView>().First();
            var editor = firstOption.FindControl<StackPanel>("OptionControls")!;
            Assert.True(Grid.GetRow(editor) == (width == 800 ? 1 : 0),
                $"Unexpected editor flow at {width}x{height}: row {Grid.GetRow(editor)}, card {firstOption.Bounds}");
            options.Offset = new Vector(0, options.Extent.Height);
            window.UpdateLayout();
            Assert.True(options.Offset.Y > 0, $"Last option is unreachable at {width}x{height}");
            var lastRow = window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.OptionRowView>().Last();
            Point? lastPosition = lastRow.TranslatePoint(new Point(0, 0), options);
            Assert.NotNull(lastPosition);
            Assert.True(lastPosition.Value.Y + lastRow.Bounds.Height <= options.Viewport.Height + 2,
                $"Final option remains clipped at {width}x{height}");
            window.Close();
        }
    }

    /// <summary>Uses the same preview metric tokens to validate enlarged controls at a small client size.</summary>
    [AvaloniaFact]
    public void EnlargedMetricsKeepNavigationAndSettingsReachable()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.ApplyMetricScale(1.5);

        try
        {
            var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true), _ => { });
            var window = new MainWindow(shell) { Width = 800, Height = 500 };
            window.Show();
            window.UpdateLayout();
            var settings = Assert.Single(window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.SettingsView>());
            var scroll = settings.FindControl<ScrollViewer>("OptionScroll")!;
            var reset = settings.FindControl<Button>("ResetDraftButton")!;
            var picker = settings.FindControl<ComboBox>("CompactCategoryPicker")!;
            var theme = window.FindControl<ComboBox>("ThemePicker")!;
            var option = window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.OptionRowView>().First();
            var editor = option.FindControl<StackPanel>("OptionControls")!;
            Assert.True(picker.IsVisible);
            Assert.Equal(1, Grid.GetRow(editor));
            Assert.True(reset.Bounds.Right <= settings.Bounds.Width);
            Assert.True(scroll.Viewport.Height > 0,
                $"Window={window.Bounds} Settings={settings.Bounds} Scroll={scroll.Bounds} Viewport={scroll.Viewport} Reset={reset.Bounds}");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(theme.Bounds.Height > 0);
            Assert.Contains(window.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => viewer.Extent.Height > viewer.Viewport.Height);
            window.Close();
        }
        finally
        {
            application.ApplyMetricScale(1);
        }
    }

    /// <summary>Only result changes reset the real option scroller; drafts and search focus survive.</summary>
    [AvaloniaFact]
    public async Task SettingsResultsResetOnlyForCategoryAndSearch()
    {
        MainWindow? changingWindow = null;
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true),
            theme => changingWindow!.RequestedThemeVariant = App.ResolveTheme(theme));
        var window = changingWindow = new MainWindow(shell) { Width = 1080, Height = 720 };
        window.Show();
        window.UpdateLayout();
        var settings = Assert.Single(window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.SettingsView>());
        var options = settings.FindControl<ScrollViewer>("OptionScroll")!;
        var search = settings.FindControl<TextBox>("OptionSearch")!;
        var audio = Assert.Single(shell.Settings.Categories, category => category.Id == "Audio");
        options.Offset = new Vector(0, 300);
        window.UpdateLayout();
        Assert.True(options.Offset.Y > 0);
        shell.Settings.SelectedCategory = audio;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        Assert.Equal(0, options.Offset.Y);

        shell.Settings.SelectedCategory = shell.Settings.Categories[0];
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        options.Offset = new Vector(0, 250);
        window.UpdateLayout();
        var row = Assert.Single(shell.Settings.VisibleRows, option => option.Id == "audio-output-buffer");
        row.TextValue = "1001";
        window.UpdateLayout();
        Assert.True(options.Offset.Y > 0);
        Assert.Contains("0–1000 ms", row.ValidationMessage);
        shell.SelectedTheme = Assert.Single(shell.Themes, choice => choice.Theme == AppTheme.Dark);
        window.UpdateLayout();
        Assert.True(options.Offset.Y > 0);
        window.Width = 1100;
        window.UpdateLayout();
        Assert.True(options.Offset.Y > 0);

        search.Focus();
        search.Text = "audio-output-buffer";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        Assert.True(search.IsFocused);
        Assert.Equal(0, options.Offset.Y);
        Assert.Equal("1001", shell.Settings.DraftValues[row.Id].GetString());
        window.Close();
    }

    /// <summary>Expanded diagnostics keep unknown channel evidence and long copy inside the card.</summary>
    [AvaloniaFact]
    public void DeviceDetailsAndLongContentRemainHonestAndReachable()
    {
        var text = new PresentationText(new Dictionary<string, string>
        {
            ["devices.preview.name"] = string.Concat(Enumerable.Repeat("Long Android device name ", 12)),
            ["devices.hint.failure"] = string.Concat(Enumerable.Repeat("Connection details remain readable. ", 16)),
        });
        var devices = new DevicesViewModel(StaticDevicePresentationSource.Preview(text), text);
        var settings = new SettingsViewModel(text, new InMemoryOptionDraft());
        var shell = new ShellViewModel(devices, settings, text, true, AppTheme.System, _ => { });
        devices.SelectScenario("reconnect");
        var card = Assert.Single(devices.Cards);
        Assert.Equal("Unverified", card.Audio);
        Assert.Equal("Recovering", card.Streaming);
        card.DetailsExpanded = true;
        var window = new MainWindow(shell) { Width = 800, Height = 500 };
        window.Show();
        window.UpdateLayout();
        var details = Assert.Single(window.GetVisualDescendants().OfType<Expander>(),
            expander => expander.Header?.ToString() == card.DetailsLabel);
        Assert.True(details.IsExpanded);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Unverified");

        devices.SelectScenario("failure");
        window.UpdateLayout();
        var longHint = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
            block => block.IsVisible &&
                block.Text?.StartsWith("Connection details remain readable.", StringComparison.Ordinal) == true);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, longHint.TextWrapping);
        Assert.True(longHint.Bounds.Width <= window.Bounds.Width);
        window.Close();
    }

    /// <summary>The displayed Settings gesture is the binding that focuses its local search.</summary>
    [AvaloniaFact]
    public void ShortcutHelpMatchesImplementedLocalBinding()
    {
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, true), _ => { });
        var window = new MainWindow(shell);
        window.Show();
        var settings = Assert.Single(window.GetVisualDescendants().OfType<ScrcpySeamless.Desktop.Views.SettingsView>());
        var binding = Assert.Single(settings.KeyBindings);
        Assert.Equal(SettingsShortcuts.FocusSearch, binding.Gesture);
        Assert.Contains(SettingsShortcuts.FocusSearch.ToString(), shell.Settings.ShortcutHelp);
        settings.FindControl<Button>("ResetDraftButton")!.Focus();
        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        Assert.True(settings.FindControl<TextBox>("OptionSearch")!.IsFocused);
        window.Close();
    }

    /// <summary>System request inherits framework changes; explicit choices keep their variant.</summary>
    [AvaloniaFact]
    public void ThemeChoiceUsesFrameworkInheritance()
    {
        var application = Application.Current!;
        application.RequestedThemeVariant = ThemeVariant.Light;
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(false, AppTheme.System, null, false),
            theme => application.RequestedThemeVariant = App.ResolveTheme(theme));
        var window = new MainWindow(shell);
        window.Show();
        Assert.Equal(AppTheme.System, shell.SelectedTheme.Theme);
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
        application.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        shell.SelectedTheme = Assert.Single(shell.Themes, choice => choice.Theme == AppTheme.Light);
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
        shell.SelectedTheme = Assert.Single(shell.Themes, choice => choice.Theme == AppTheme.System);
        Assert.Equal(ThemeVariant.Default, application.RequestedThemeVariant);
        window.Close();
        application.RequestedThemeVariant = ThemeVariant.Default;
    }
    /// <summary>Loads the normal empty state and navigates only to implemented destinations.</summary>
    [AvaloniaFact]
    public void ShellLoadsAndNavigates()
    {
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(false, AppTheme.System, null, false), _ => { });
        var window = new MainWindow(shell);
        window.Show();

        var devices = window.FindControl<Button>("DevicesNavigation");
        var settings = window.FindControl<Button>("SettingsNavigation");
        var content = window.FindControl<ContentControl>("WorkspaceContent");
        Assert.NotNull(devices);
        Assert.NotNull(settings);
        Assert.NotNull(content);
        Assert.Same(shell.Devices, content.Content);
        Assert.True(shell.Devices.IsEmpty);

        settings.Command!.Execute(null);
        Assert.Same(shell.Settings, content.Content);
        devices.Command!.Execute(null);
        Assert.Same(shell.Devices, content.Content);

        window.Close();
    }

    /// <summary>Checks practical laptop dimensions and both Fluent theme variants.</summary>
    [AvaloniaFact]
    public void ShellLaysOutInLightAndDark()
    {
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, "fallback", false), _ => { });
        var window = new MainWindow(shell) { Width = 900, Height = 620, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        window.UpdateLayout();
        Assert.True(window.Bounds.Width >= 780);
        Assert.True(window.Bounds.Height >= 580);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.UpdateLayout();
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        Assert.Single(shell.Devices.Cards);
        window.Close();
    }

    /// <summary>Mounting and searching Settings does not create an override or hide validation.</summary>
    [AvaloniaFact]
    public void SettingsViewKeepsDraftDetachedAndShowsCoreError()
    {
        var normal = DesktopComposition.Create(new DesktopLaunchOptions(false, AppTheme.System, null, true), _ => { });
        var window = new MainWindow(normal) { Width = 900, Height = 620 };
        window.Show();
        window.UpdateLayout();
        Assert.Empty(normal.Settings.DraftValues);

        normal.Settings.SearchText = "max-size";
        window.UpdateLayout();
        Assert.Empty(normal.Settings.DraftValues);

        var row = Assert.Single(normal.Settings.VisibleRows);
        var editor = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "OptionEditor");
        editor.Text = "08";
        window.UpdateLayout();
        Assert.Equal("08", normal.Settings.DraftValues["max-size"].GetString());
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block =>
            block.Text == row.ValidationMessage && block.IsVisible);

        var reset = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "ResetDraftButton");
        reset.Command!.Execute(null);
        window.UpdateLayout();
        Assert.Empty(normal.Settings.DraftValues);
        Assert.False(row.HasOverride);
        window.Close();
    }

    /// <summary>Keeps localized option text in presentation while Core exposes only stable keys.</summary>
    [Fact]
    public void OptionResourcesBelongToDesktopAssembly()
    {
        const string resourceName = "ScrcpySeamless.Desktop.Resources.Options.GeneratedOptionResources.en.json";
        var coreResources = typeof(GeneratedOptionCatalog).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain(coreResources, name => name.Contains("GeneratedOptionResources", StringComparison.Ordinal));

        using var stream = typeof(App).Assembly.GetManifestResourceStream(resourceName);
        Assert.True(stream is not null, string.Join(", ", typeof(App).Assembly.GetManifestResourceNames()));
        using var resources = JsonDocument.Parse(stream);
        foreach (var descriptor in GeneratedOptionCatalog.All)
        {
            Assert.True(resources.RootElement.TryGetProperty(descriptor.LabelResourceKey, out _));
            Assert.True(resources.RootElement.TryGetProperty(descriptor.DescriptionResourceKey, out _));
        }
    }
}
