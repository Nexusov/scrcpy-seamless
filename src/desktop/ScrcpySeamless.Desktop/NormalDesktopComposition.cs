using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Configuration;

namespace ScrcpySeamless.Desktop;

/// <summary>Owns the two independent normal-mode save groups and their explicit data root.</summary>
public sealed class NormalDesktopComposition
{
    public NormalDesktopComposition(
        ShellViewModel shell,
        SettingsViewModel settings,
        ProfilesViewModel profiles,
        ConfigurationWorkspaceViewModel configuration,
        DesktopPreferencesViewModel preferences,
        ApplicationDataPaths dataPaths)
    {
        Shell = shell;
        Settings = settings;
        Profiles = profiles;
        Configuration = configuration;
        Preferences = preferences;
        DataPaths = dataPaths;
    }

    public ShellViewModel Shell { get; }
    public SettingsViewModel Settings { get; }
    public ProfilesViewModel Profiles { get; }
    public ConfigurationWorkspaceViewModel Configuration { get; }
    public DesktopPreferencesViewModel Preferences { get; }
    public ApplicationDataPaths DataPaths { get; }
    public bool HasDirtyGroups => Configuration.IsDirty || Profiles.HasUnstagedChanges || Preferences.IsDirty;
    public bool IsBusy => Configuration.IsBusy || Preferences.IsBusy;
    public string DirtyGroupsLabel => string.Join(" and ", new[]
    {
        Configuration.IsDirty || Profiles.HasUnstagedChanges ? "Device/Profiles/Mirroring" : null,
        Preferences.IsDirty ? "Desktop Appearance/Shortcuts" : null,
    }.Where(group => group is not null));

    /// <summary>Loads selected files without creating absent documents or probing any device.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Configuration.LoadAsync(cancellationToken);

        if (Configuration.Draft is { } draft)
        {
            Settings.RefreshValues(draft.Mirroring.Options);
        }

        Profiles.RefreshFromDraft();
        await Preferences.LoadAsync(cancellationToken);
    }

    /// <summary>Applies two independent groups sequentially and reports a genuine partial save.</summary>
    public async Task<bool> ApplyDirtyGroupsAsync(CancellationToken cancellationToken)
    {
        if (Profiles.HasUnstagedChanges && !Profiles.TrySaveToDraft())
        {
            return false;
        }

        if (Configuration.IsDirty)
        {
            ConfigurationSessionApplyResult applied = await Configuration.ApplyAsync(cancellationToken);

            if (applied.Status is not (ConfigurationSessionApplyStatus.Applied or ConfigurationSessionApplyStatus.NoChanges))
            {
                return false;
            }

            Profiles.RefreshFromDraft();
        }

        if (Preferences.IsDirty && !await Preferences.ApplyAsync(cancellationToken))
        {
            return false;
        }

        return true;
    }

}

/// <summary>Constructs real adapters only after normal mode selects one explicit root.</summary>
public static class NormalDesktopFactory
{
    /// <summary>Creates one local settings shell without device discovery, ADB or native launch.</summary>
    public static NormalDesktopComposition Create(
        DesktopLaunchOptions options,
        Action<DesktopAppearancePreferences> applyAppearance,
        Action<DesktopShortcutPreferences> activateShortcuts,
        Func<string?, string?> resolveEffectiveFont)
    {
        if (options.Preview || options.StorageMode == DesktopStorageMode.None)
        {
            throw new ArgumentException("Normal Desktop requires one explicit storage mode.", nameof(options));
        }

        ApplicationDataPaths paths = ResolveDataPaths(options);
        PresentationText text = new();
        InMemoryOptionDraft optionDraft = new();
        VersionedConfigurationStore store = new(paths.ConfigurationFile);
        ConfigurationEditSession session = new(store);
        LegacyMigrationCoordinator? migration = options.LegacyDevelopmentDirectory is null
            ? null
            : new LegacyMigrationCoordinator(options.LegacyDevelopmentDirectory, store);
        ConfigurationWorkspaceViewModel configuration = new(session, optionDraft, migration);
        ProfilesViewModel profiles = new(text, session);
        profiles.AttachWorkspace(configuration);
        configuration.DraftReplaced += profiles.RefreshFromDraft;
        SettingsViewModel settings = new(text, optionDraft, isPreview: false);
        DevicesViewModel devices = new(StaticDevicePresentationSource.Empty(), text);
        ShellViewModel? shell = null;
        DesktopPreferencesViewModel preferences = new(text,
            new VersionedDesktopPreferencesStore(paths.DesktopPreferencesFile),
            appearance =>
            {
                applyAppearance(appearance);
                shell?.SetRequestedTheme(appearance.Theme switch
                {
                    DesktopTheme.Light => AppTheme.Light,
                    DesktopTheme.Dark => AppTheme.Dark,
                    _ => AppTheme.System,
                });
            },
            shortcuts =>
            {
                activateShortcuts(shortcuts);
                settings.SetActiveShortcutHelp(shortcuts.EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));
            }, resolveEffectiveFont);
        settings.AttachPersistence(configuration, preferences, paths.ConfigurationFile);
        shell = new ShellViewModel(devices, settings, text, false, AppTheme.System,
            theme => preferences.Theme = theme switch
            {
                AppTheme.Light => DesktopTheme.Light,
                AppTheme.Dark => DesktopTheme.Dark,
                _ => DesktopTheme.System,
            }, profiles);

        if (options.SettingsPage)
        {
            shell.ShowSettings();
        }

        if (options.ProfilesPage)
        {
            shell.ShowProfiles();
        }

        return new NormalDesktopComposition(shell, settings, profiles, configuration, preferences, paths);
    }

    /// <summary>Resolves paths from selected application and user roots, never from working directory.</summary>
    private static ApplicationDataPaths ResolveDataPaths(DesktopLaunchOptions options)
    {
        string applicationDirectory = AppContext.BaseDirectory;

        if (options.StorageMode == DesktopStorageMode.Development)
        {
            string directory = Path.GetFullPath(options.DevelopmentDataDirectory!);
            return new ApplicationDataPaths(directory,
                Path.Combine(directory, "configuration.v2.json"));
        }

        string localDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ApplicationDataMode mode = options.StorageMode switch
        {
            DesktopStorageMode.Portable => ApplicationDataMode.Portable,
            DesktopStorageMode.Installed => ApplicationDataMode.Installed,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        return ApplicationDataPaths.Resolve(mode, applicationDirectory, localDataDirectory);
    }
}
