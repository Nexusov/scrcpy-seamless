using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>In-memory application theme request, independent of system display scaling.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>Explicit Desktop composition options; preview is opt-in.</summary>
public sealed record DesktopLaunchOptions(
    bool Preview,
    AppTheme Theme,
    string? ScenarioId,
    bool SettingsPage,
    string? OptionSearch = null,
    bool ExpandedDetails = false,
    double UiScale = 1)
{
    /// <summary>Parses only supported, side-effect-free development switches.</summary>
    public static DesktopLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        bool preview = arguments.Contains("--preview", StringComparer.Ordinal);
        AppTheme theme = arguments.Contains("--theme=dark", StringComparer.Ordinal) ? AppTheme.Dark :
            arguments.Contains("--theme=light", StringComparer.Ordinal) ? AppTheme.Light : AppTheme.System;
        bool expandedDetails = arguments.Contains("--details=expanded", StringComparer.Ordinal);
        string? scaleArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--ui-scale=", StringComparison.Ordinal));
        double uiScale = scaleArgument is null ? 1 : double.Parse(scaleArgument["--ui-scale=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        string? scenarioArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--scenario=", StringComparison.Ordinal));
        string? scenarioId = scenarioArgument is null ? null : scenarioArgument["--scenario=".Length..];
        string? searchArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--search=", StringComparison.Ordinal));
        string? optionSearch = searchArgument is null ? null : searchArgument["--search=".Length..];
        bool settingsPage = arguments.Contains("--page=settings", StringComparer.Ordinal);
        bool invalidArgument = arguments.Any(argument => argument is not ("--preview" or "--theme=system" or "--theme=light" or
            "--theme=dark" or "--page=devices" or "--page=settings" or "--details=expanded") &&
            !argument.StartsWith("--scenario=", StringComparison.Ordinal) &&
            !argument.StartsWith("--search=", StringComparison.Ordinal) &&
            !argument.StartsWith("--ui-scale=", StringComparison.Ordinal));

        if (invalidArgument || ((scenarioId is not null || optionSearch is not null || expandedDetails || scaleArgument is not null) && !preview) ||
            (scaleArgument is not null && uiScale is not (1 or 1.1 or 1.25 or 1.5)))
        {
            throw new ArgumentException("Unsupported Desktop launch argument.", nameof(arguments));
        }

        return new DesktopLaunchOptions(preview, theme, scenarioId, settingsPage, optionSearch, expandedDetails, uiScale);
    }
}

/// <summary>Builds product ViewModels from pure sources for normal or preview startup.</summary>
public static class DesktopComposition
{
    /// <summary>Creates a UI with no real adapters; preview adds only synthetic presentation data.</summary>
    public static ShellViewModel Create(DesktopLaunchOptions options, Action<AppTheme> setTheme)
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

        if (options.ExpandedDetails)
        {
            foreach (DeviceCardViewModel card in devices.Cards)
            {
                card.DetailsExpanded = true;
            }
        }

        SettingsViewModel settings = new(text, draft);

        if (options.OptionSearch is not null)
        {
            settings.SearchText = options.OptionSearch;
        }

        ShellViewModel shell = new(devices, settings, text, options.Preview, options.Theme, setTheme);

        if (options.SettingsPage)
        {
            shell.ShowSettings();
        }

        return shell;
    }
}
