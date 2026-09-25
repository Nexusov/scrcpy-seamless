using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>Explicit Desktop composition options; preview is opt-in.</summary>
public sealed record DesktopLaunchOptions(bool Preview, bool Dark, string? ScenarioId, bool SettingsPage)
{
    /// <summary>Parses only supported, side-effect-free development switches.</summary>
    public static DesktopLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        bool preview = arguments.Contains("--preview", StringComparer.Ordinal);
        bool dark = arguments.Contains("--theme=dark", StringComparer.Ordinal);
        string? scenarioArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--scenario=", StringComparison.Ordinal));
        string? scenarioId = scenarioArgument is null ? null : scenarioArgument["--scenario=".Length..];
        bool settingsPage = arguments.Contains("--page=settings", StringComparer.Ordinal);
        bool invalidArgument = arguments.Any(argument => argument is not ("--preview" or "--theme=light" or
            "--theme=dark" or "--page=devices" or "--page=settings") &&
            !argument.StartsWith("--scenario=", StringComparison.Ordinal));

        if (invalidArgument || (scenarioId is not null && !preview))
        {
            throw new ArgumentException("Unsupported Desktop launch argument.", nameof(arguments));
        }

        return new DesktopLaunchOptions(preview, dark, scenarioId, settingsPage);
    }
}

/// <summary>Builds product ViewModels from pure sources for normal or preview startup.</summary>
public static class DesktopComposition
{
    /// <summary>Creates a UI with no real adapters; preview adds only synthetic presentation data.</summary>
    public static ShellViewModel Create(DesktopLaunchOptions options, Action<bool> setDarkTheme)
    {
        PresentationText text = new();
        IDevicePresentationSource deviceSource = options.Preview
            ? StaticDevicePresentationSource.Preview(text)
            : StaticDevicePresentationSource.Empty();
        InMemoryOptionDraft draft = new();

        if (options.Preview)
        {
            draft.SetText("audio-output-buffer", "1001");
        }

        DevicesViewModel devices = new(deviceSource, text);

        if (options.ScenarioId is not null)
        {
            devices.SelectScenario(options.ScenarioId);
        }

        SettingsViewModel settings = new(text, draft);
        ShellViewModel shell = new(devices, settings, text, options.Preview, options.Dark, setDarkTheme);

        if (options.SettingsPage)
        {
            shell.ShowSettings();
        }

        return shell;
    }
}
