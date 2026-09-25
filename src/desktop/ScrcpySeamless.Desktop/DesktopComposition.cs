using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>In-memory application theme request, independent of system display scaling.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>Selects one explicit normal-mode application data root.</summary>
public enum DesktopStorageMode { None, Portable, Installed, Development }

/// <summary>Explicit Desktop composition options; preview is opt-in.</summary>
public sealed record DesktopLaunchOptions(
    bool Preview,
    AppTheme Theme,
    string? ScenarioId,
    bool SettingsPage,
    string? OptionSearch = null,
    bool ExpandedDetails = false,
    double UiScale = 1,
    DesktopStorageMode StorageMode = DesktopStorageMode.None,
    string? DevelopmentDataDirectory = null,
    string? LegacyDevelopmentDirectory = null,
    bool ProfilesPage = false)
{
    /// <summary>Rejects ambiguous storage modes before any persistent adapter is built.</summary>
    public static DesktopLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        bool preview = arguments.Contains("--preview", StringComparer.Ordinal);
        bool portable = arguments.Contains("--portable", StringComparer.Ordinal);
        bool installed = arguments.Contains("--installed", StringComparer.Ordinal);
        AppTheme theme = arguments.Contains("--theme=dark", StringComparer.Ordinal) ? AppTheme.Dark :
            arguments.Contains("--theme=light", StringComparer.Ordinal) ? AppTheme.Light : AppTheme.System;
        bool expandedDetails = arguments.Contains("--details=expanded", StringComparer.Ordinal);
        string? scaleArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--ui-scale=", StringComparison.Ordinal));
        bool validScale = scaleArgument is null || double.TryParse(scaleArgument["--ui-scale=".Length..],
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
        double uiScale = scaleArgument is null || !validScale ? 1 :
            double.Parse(scaleArgument["--ui-scale=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        string? scenarioArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--scenario=", StringComparison.Ordinal));
        string? scenarioId = scenarioArgument is null ? null : scenarioArgument["--scenario=".Length..];
        string? searchArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--search=", StringComparison.Ordinal));
        string? optionSearch = searchArgument is null ? null : searchArgument["--search=".Length..];
        string? developmentArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--dev-data-dir=", StringComparison.Ordinal));
        string? developmentDirectory = developmentArgument?["--dev-data-dir=".Length..];
        string? legacyArgument = arguments.FirstOrDefault(argument => argument.StartsWith("--legacy-dev-dir=", StringComparison.Ordinal));
        string? legacyDirectory = legacyArgument?["--legacy-dev-dir=".Length..];
        bool settingsPage = arguments.Contains("--page=settings", StringComparer.Ordinal);
        bool profilesPage = arguments.Contains("--page=profiles", StringComparer.Ordinal);
        bool invalidArgument = arguments.Any(argument => argument is not ("--preview" or "--portable" or "--installed" or
            "--theme=system" or "--theme=light" or "--theme=dark" or "--page=devices" or
            "--page=settings" or "--page=profiles" or "--details=expanded") &&
            !argument.StartsWith("--scenario=", StringComparison.Ordinal) &&
            !argument.StartsWith("--search=", StringComparison.Ordinal) &&
            !argument.StartsWith("--ui-scale=", StringComparison.Ordinal) &&
            !argument.StartsWith("--dev-data-dir=", StringComparison.Ordinal) &&
            !argument.StartsWith("--legacy-dev-dir=", StringComparison.Ordinal));
        int selectedModes = arguments.Count(argument => argument is "--portable" or "--installed" ||
            argument.StartsWith("--dev-data-dir=", StringComparison.Ordinal));
        bool repeatedSelection = arguments.Count(argument => argument.StartsWith("--legacy-dev-dir=", StringComparison.Ordinal)) > 1 ||
            arguments.Count(argument => argument.StartsWith("--scenario=", StringComparison.Ordinal)) > 1 ||
            arguments.Count(argument => argument.StartsWith("--search=", StringComparison.Ordinal)) > 1 ||
            arguments.Count(argument => argument.StartsWith("--ui-scale=", StringComparison.Ordinal)) > 1 ||
            arguments.Count(argument => argument.StartsWith("--theme=", StringComparison.Ordinal)) > 1 ||
            arguments.Count(argument => argument.StartsWith("--page=", StringComparison.Ordinal)) > 1;
        bool invalidPaths = developmentDirectory is not null && !Path.IsPathFullyQualified(developmentDirectory) ||
            legacyDirectory is not null && !Path.IsPathFullyQualified(legacyDirectory);
        bool invalidPreview = preview && (selectedModes != 0 || legacyDirectory is not null || profilesPage);
        bool invalidNormal = !preview && (selectedModes != 1 || scenarioId is not null || optionSearch is not null ||
            expandedDetails || scaleArgument is not null || theme != AppTheme.System);

        if (invalidArgument || repeatedSelection || !validScale || invalidPaths || invalidPreview || invalidNormal ||
            (scaleArgument is not null && uiScale is not (1 or 1.1 or 1.25 or 1.5)) ||
            (legacyDirectory is not null && developmentDirectory is null) ||
            (settingsPage && profilesPage))
        {
            throw new ArgumentException("Unsupported Desktop launch argument.", nameof(arguments));
        }

        DesktopStorageMode storageMode = portable ? DesktopStorageMode.Portable : installed ? DesktopStorageMode.Installed :
            developmentDirectory is not null ? DesktopStorageMode.Development : DesktopStorageMode.None;
        return new DesktopLaunchOptions(preview, theme, scenarioId, settingsPage, optionSearch, expandedDetails,
            uiScale, storageMode, developmentDirectory, legacyDirectory, profilesPage);
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
