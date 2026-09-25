using System.Windows.Input;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>Coordinates implemented Desktop destinations without owning device or settings logic.</summary>
public sealed class ShellViewModel : ObservableViewModel
{
    private readonly Action<bool> setDarkTheme;
    private object currentPage;
    private bool isDark;

    public ShellViewModel(
        DevicesViewModel devices,
        SettingsViewModel settings,
        PresentationText text,
        bool isPreview,
        bool initiallyDark,
        Action<bool> setDarkTheme)
    {
        Devices = devices;
        Settings = settings;
        this.setDarkTheme = setDarkTheme;
        isDark = initiallyDark;
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
        UnavailableHint = text.Get("nav.later");
        LightThemeLabel = text.Get("app.theme.light");
        DarkThemeLabel = text.Get("app.theme.dark");
        ShowDevicesCommand = new ActionCommand(ShowDevices);
        ShowSettingsCommand = new ActionCommand(ShowSettings);
        ToggleThemeCommand = new ActionCommand(ToggleTheme);
    }

    public DevicesViewModel Devices { get; }
    public SettingsViewModel Settings { get; }
    public bool IsPreview { get; }
    public string ProductName { get; }
    public string ProductSubtitle { get; }
    public string ModeLabel { get; }
    public string DevicesLabel { get; }
    public string SessionsLabel { get; }
    public string ProfilesLabel { get; }
    public string SettingsLabel { get; }
    public string DiagnosticsLabel { get; }
    public string UnavailableHint { get; }
    public string LightThemeLabel { get; }
    public string DarkThemeLabel { get; }
    public string ThemeButtonLabel => IsDark ? LightThemeLabel : DarkThemeLabel;
    public bool IsDark => isDark;
    public bool IsDevicesSelected => ReferenceEquals(CurrentPage, Devices);
    public bool IsSettingsSelected => ReferenceEquals(CurrentPage, Settings);
    public ICommand ShowDevicesCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ToggleThemeCommand { get; }

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
        }
    }

    /// <summary>Navigates to the implemented Devices workspace.</summary>
    public void ShowDevices() => CurrentPage = Devices;

    /// <summary>Navigates to the implemented Settings preview.</summary>
    public void ShowSettings() => CurrentPage = Settings;

    /// <summary>Changes only the current presentation theme.</summary>
    private void ToggleTheme()
    {
        isDark = !isDark;
        setDarkTheme(isDark);
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(ThemeButtonLabel));
    }
}
