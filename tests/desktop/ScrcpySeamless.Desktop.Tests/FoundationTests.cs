using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Desktop;
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
}
