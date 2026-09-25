using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
    /// <summary>Loads the normal empty state and navigates only to implemented destinations.</summary>
    [AvaloniaFact]
    public void ShellLoadsAndNavigates()
    {
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(false, false, null, false), _ => { });
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
        var shell = DesktopComposition.Create(new DesktopLaunchOptions(true, false, "fallback", false), _ => { });
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
        var normal = DesktopComposition.Create(new DesktopLaunchOptions(false, false, null, true), _ => { });
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
