using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
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

/// <summary>Checks that XAML, theme, and compiled binding start together.</summary>
public sealed class FoundationTests
{
    /// <summary>Loads the placeholder window and its compiled binding.</summary>
    [AvaloniaFact]
    public void PlaceholderWindowLoads()
    {
        var window = new MainWindow();
        window.Show();

        var status = window.FindControl<TextBlock>("FoundationStatus");
        Assert.NotNull(status);
        Assert.Equal("Desktop build foundation ready", status.Text);

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
