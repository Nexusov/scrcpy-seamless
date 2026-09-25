using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Adb;
using ScrcpySeamless.Infrastructure.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using ScrcpySeamless.Infrastructure.Runtime;
using Avalonia.Threading;

namespace ScrcpySeamless.Desktop;

/// <summary>Owns the two independent normal-mode save groups and their explicit data root.</summary>
public sealed class NormalDesktopComposition
{
    private readonly PresentationText text;
    private bool closeSaveActive;
    private bool exitReserved;

    public NormalDesktopComposition(
        ShellViewModel shell,
        SettingsViewModel settings,
        ProfilesViewModel profiles,
        ConfigurationWorkspaceViewModel configuration,
        DesktopPreferencesViewModel preferences,
        DevicesViewModel devices,
        DeviceSessionViewModel? deviceSession,
        ApplicationDataPaths dataPaths,
        PresentationText text)
    {
        Shell = shell;
        Settings = settings;
        Profiles = profiles;
        Configuration = configuration;
        Preferences = preferences;
        Devices = devices;
        DeviceSession = deviceSession;
        DataPaths = dataPaths;
        this.text = text;
        Configuration.AttachCloseSaveGuard(() => closeSaveActive || exitReserved);
        Preferences.AttachCloseSaveGuard(() => closeSaveActive || exitReserved);
        Shell.AttachThemeEditGuard(() => !closeSaveActive && !exitReserved);
    }

    public ShellViewModel Shell { get; }
    public SettingsViewModel Settings { get; }
    public ProfilesViewModel Profiles { get; }
    public ConfigurationWorkspaceViewModel Configuration { get; }
    public DesktopPreferencesViewModel Preferences { get; }
    public DevicesViewModel Devices { get; }
    public DeviceSessionViewModel? DeviceSession { get; }
    public ApplicationDataPaths DataPaths { get; }
    public bool HasDirtyGroups => Configuration.IsDirty || Profiles.HasUnstagedChanges || Preferences.IsDirty;
    public bool IsBusy => Configuration.IsBusy || Preferences.IsBusy;
    public bool IsCloseSaveActive => closeSaveActive || exitReserved;
    public bool HasLiveOperations => DeviceSession is not null;

    /// <summary>Stops the owned native child and settles explicit ADB operations after exit acceptance.</summary>
    public async Task<bool> ShutdownLiveAsync()
    {
        if (DeviceSession is null)
        {
            return true;
        }

        exitReserved = true;
        Configuration.RefreshCloseSaveState();
        Preferences.RefreshCloseSaveState();
        Shell.RefreshThemeEditState();
        Devices.DisposeLive();
        bool stopped = await DeviceSession.ShutdownAsync();

        if (!stopped)
        {
            DeviceSession.ResumeAfterFailedShutdown();
            exitReserved = false;
            Configuration.RefreshCloseSaveState();
            Preferences.RefreshCloseSaveState();
            Shell.RefreshThemeEditState();
            return false;
        }

        bool settled = await Devices.ShutdownLiveAsync();

        if (!settled)
        {
            DeviceSession.ResumeAfterFailedShutdown();
            exitReserved = false;
            Configuration.RefreshCloseSaveState();
            Preferences.RefreshCloseSaveState();
            Shell.RefreshThemeEditState();
        }

        return settled;
    }
    public string DirtyGroupsLabel => string.Join(" and ", new[]
    {
        Configuration.IsDirty || Profiles.HasUnstagedChanges ? text.Get("close.configurationGroup") : null,
        Preferences.IsDirty ? text.Get("close.desktopGroup") : null,
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

    /// <summary>Owns both close-time writes while keeping every editor unavailable until settlement.</summary>
    public async Task<bool> SaveAndCloseAsync(CancellationToken cancellationToken)
    {
        if (closeSaveActive || IsBusy)
        {
            return false;
        }

        if (Profiles.HasUnstagedChanges && !Profiles.TrySaveToDraft())
        {
            return false;
        }

        closeSaveActive = true;
        Configuration.RefreshCloseSaveState();
        Preferences.RefreshCloseSaveState();
        Shell.RefreshThemeEditState();

        try
        {
            if (Configuration.IsDirty)
            {
                ConfigurationSessionApplyResult applied = await Configuration.ApplyForCloseAsync(cancellationToken);

                if (applied.Status is not (ConfigurationSessionApplyStatus.Applied or ConfigurationSessionApplyStatus.NoChanges))
                {
                    return false;
                }

                Profiles.RefreshFromDraft();
            }

            if (Preferences.IsDirty && !await Preferences.ApplyForCloseAsync(cancellationToken))
            {
                return false;
            }

            return !HasDirtyGroups;
        }
        finally
        {
            closeSaveActive = false;
            Configuration.RefreshCloseSaveState();
            Preferences.RefreshCloseSaveState();
            Shell.RefreshThemeEditState();
        }
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
        Func<string?, string?> resolveEffectiveFont,
        Func<CancellationToken, Task>? beforePreferencesCommit = null)
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
            }, resolveEffectiveFont, beforePreferencesCommit);
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

        DeviceSessionViewModel? deviceSession = null;

        if (options.DeviceRuntimeDirectory is not null)
        {
            RuntimeBundleResult runtime = DeviceRuntimeBundle.Validate(options.DeviceRuntimeDirectory);
            LegacyNativeHost? nativeHost = runtime.Bundle is { } bundle
                ? new LegacyNativeHost(bundle.NativeExecutablePath, bundle.ServerPath,
                    bundle.AdbExecutablePath, Path.Combine(paths.Directory, "native-sessions"))
                : null;
            deviceSession = new DeviceSessionViewModel(devices, profiles, configuration, store,
                runtime, nativeHost, action => Dispatcher.UIThread.Post(action));
            devices.AttachSessionActions(deviceSession);

            if (runtime.Bundle is { } readyBundle)
            {
                AdbGateway gateway = new(new AdbProcessRunner(readyBundle.AdbExecutablePath));
                devices.AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway),
                    action => Dispatcher.UIThread.Post(action));
            }
        }

        return new NormalDesktopComposition(shell, settings, profiles, configuration, preferences,
            devices, deviceSession, paths, text);
    }

    /// <summary>Resolves paths from selected application and user roots, never from working directory.</summary>
    public static ApplicationDataPaths ResolveDataPaths(DesktopLaunchOptions options)
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
