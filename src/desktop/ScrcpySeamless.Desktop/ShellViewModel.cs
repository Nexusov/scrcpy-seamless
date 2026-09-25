using System.Windows.Input;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>Coordinates implemented Desktop destinations without owning device or settings logic.</summary>
public sealed class ShellViewModel : ObservableViewModel
{
    private readonly Action<AppTheme> setTheme;
    private object currentPage;
    private ThemeChoice selectedTheme;
    private readonly object? profiles;

    public ShellViewModel(
        DevicesViewModel devices,
        SettingsViewModel settings,
        PresentationText text,
        bool isPreview,
        AppTheme initialTheme,
        Action<AppTheme> setTheme,
        object? profiles = null)
    {
        Devices = devices;
        Settings = settings;
        this.setTheme = setTheme;
        this.profiles = profiles;
        currentPage = devices;
        IsPreview = isPreview;
        ProductName = text.Get("app.title");
        ProductSubtitle = text.Get("app.subtitle");
        ModeLabel = text.Get(isPreview ? "app.preview" : "app.normal");
        DevicesLabel = text.Get("nav.devices");
        SessionsLabel = text.Get("nav.sessions");
        ProfilesLabel = text.Get("nav.profiles");
        SettingsLabel = text.Get("nav.settings");
        DiagnosticsLabel = text.Get("nav.diagnostics");
        UnavailableHint = text.Get("nav.unavailable");
        ThemeLabel = text.Get("app.theme.label");
        Themes = Enum.GetValues<AppTheme>()
            .Select(theme => new ThemeChoice(theme, text.Get($"app.theme.{theme.ToString().ToLowerInvariant()}")))
            .ToArray();
        selectedTheme = Themes.Single(choice => choice.Theme == initialTheme);
        ShowDevicesCommand = new ActionCommand(ShowDevices);
        ShowSettingsCommand = new ActionCommand(ShowSettings);
        ShowProfilesCommand = new ActionCommand(ShowProfiles);
    }

    public DevicesViewModel Devices { get; }
    public SettingsViewModel Settings { get; }
    public bool IsPreview { get; }
    public bool IsProfilesAvailable => profiles is not null;
    public bool IsProfilesUnavailable => profiles is null;
    public string ProductName { get; }
    public string ProductSubtitle { get; }
    public string ModeLabel { get; }
    public string DevicesLabel { get; }
    public string SessionsLabel { get; }
    public string ProfilesLabel { get; }
    public string SettingsLabel { get; }
    public string DiagnosticsLabel { get; }
    public string UnavailableHint { get; }
    public string ThemeLabel { get; }
    public IReadOnlyList<ThemeChoice> Themes { get; }
    public ThemeChoice SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (!SetProperty(ref selectedTheme, value))
            {
                return;
            }

            setTheme(value.Theme);
        }
    }
    public bool IsDevicesSelected => ReferenceEquals(CurrentPage, Devices);
    public bool IsSettingsSelected => ReferenceEquals(CurrentPage, Settings);
    public bool IsProfilesSelected => profiles is not null && ReferenceEquals(CurrentPage, profiles);
    public ICommand ShowDevicesCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowProfilesCommand { get; }

    public object CurrentPage
    {
        get => currentPage;
        private set
        {
            if (!SetProperty(ref currentPage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDevicesSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsProfilesSelected));
        }
    }

    /// <summary>Navigates to the implemented Devices workspace.</summary>
    public void ShowDevices() => CurrentPage = Devices;

    /// <summary>Navigates to the implemented Settings preview.</summary>
    public void ShowSettings() => CurrentPage = Settings;

    /// <summary>Opens saved profiles only when the local configuration editor is available.</summary>
    public void ShowProfiles()
    {
        if (profiles is null)
        {
            return;
        }

        CurrentPage = profiles;
    }

    /// <summary>Synchronizes the sidebar with a temporary or committed appearance selection.</summary>
    public void SetRequestedTheme(AppTheme theme)
    {
        ThemeChoice choice = Themes.Single(item => item.Theme == theme);
        SetProperty(ref selectedTheme, choice, nameof(SelectedTheme));
    }

}

/// <summary>Localized presentation choice for an application theme request.</summary>
public sealed record ThemeChoice(AppTheme Theme, string Label);
