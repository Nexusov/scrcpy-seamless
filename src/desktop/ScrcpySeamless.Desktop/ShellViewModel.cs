using System.Windows.Input;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop;

/// <summary>Coordinates implemented Desktop destinations without owning device or settings logic.</summary>
public sealed class ShellViewModel : ObservableViewModel
{
    private readonly Action<AppTheme> setTheme;
    private Func<bool>? canEditTheme;
    private object currentPage;
    private ThemeChoice selectedTheme;
    private readonly object? profiles;
    private SessionsWorkspaceViewModel? sessions;
    private DiagnosticsWorkspaceViewModel? diagnostics;

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
        ShowSessionsCommand = new ActionCommand(ShowSessions);
        ShowDiagnosticsCommand = new ActionCommand(ShowDiagnostics);
    }

    public DevicesViewModel Devices { get; }
    public SettingsViewModel Settings { get; }
    public bool IsPreview { get; }
    public bool IsProfilesAvailable => profiles is not null;
    public bool IsProfilesUnavailable => profiles is null;
    public bool IsSessionsAvailable => sessions is not null;
    public bool IsSessionsUnavailable => sessions is null;
    public bool IsDiagnosticsAvailable => diagnostics is not null;
    public bool IsDiagnosticsUnavailable => diagnostics is null;
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
    public bool CanEditTheme => canEditTheme?.Invoke() ?? true;
    public ThemeChoice SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (canEditTheme?.Invoke() == false)
            {
                return;
            }

            if (!SetProperty(ref selectedTheme, value))
            {
                return;
            }

            setTheme(value.Theme);
        }
    }

    /// <summary>Prevents sidebar theme edits during an owned close-time save.</summary>
    public void AttachThemeEditGuard(Func<bool> canEdit) => canEditTheme = canEdit;

    /// <summary>Updates the sidebar theme control when save ownership changes.</summary>
    public void RefreshThemeEditState() => OnPropertyChanged(nameof(CanEditTheme));
    public bool IsDevicesSelected => ReferenceEquals(CurrentPage, Devices);
    public bool IsSettingsSelected => ReferenceEquals(CurrentPage, Settings);
    public bool IsProfilesSelected => profiles is not null && ReferenceEquals(CurrentPage, profiles);
    public bool IsSessionsSelected => sessions is not null && ReferenceEquals(CurrentPage, sessions);
    public bool IsDiagnosticsSelected => diagnostics is not null && ReferenceEquals(CurrentPage, diagnostics);
    public ICommand ShowDevicesCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowProfilesCommand { get; }
    public ICommand ShowSessionsCommand { get; }
    public ICommand ShowDiagnosticsCommand { get; }
    internal DiagnosticsWorkspaceViewModel? Diagnostics => diagnostics;

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
            OnPropertyChanged(nameof(IsSessionsSelected));
            OnPropertyChanged(nameof(IsDiagnosticsSelected));
        }
    }

    /// <summary>Navigates to the implemented Devices workspace.</summary>
    public void ShowDevices() => CurrentPage = Devices;

    /// <summary>Navigates to the implemented Settings preview.</summary>
    public void ShowSettings() => CurrentPage = Settings;

    public void AttachSessionWorkspaces(SessionsWorkspaceViewModel? workspace, DiagnosticsWorkspaceViewModel observations)
    {
        sessions = workspace;
        diagnostics = observations;
        OnPropertyChanged(nameof(IsSessionsAvailable));
        OnPropertyChanged(nameof(IsSessionsUnavailable));
        OnPropertyChanged(nameof(IsDiagnosticsAvailable));
        OnPropertyChanged(nameof(IsDiagnosticsUnavailable));
    }

    public void ShowSessions()
    {
        if (sessions is not null)
        {
            CurrentPage = sessions;
        }
    }

    public void ShowDiagnostics()
    {
        if (diagnostics is not null)
        {
            diagnostics.Refresh();
            CurrentPage = diagnostics;
        }
    }

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
