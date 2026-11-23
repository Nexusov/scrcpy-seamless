using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Application.Updates;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Adb;
using ScrcpySeamless.Infrastructure.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using ScrcpySeamless.Infrastructure.Runtime;
using Avalonia.Threading;
using System.Reflection;

namespace ScrcpySeamless.Desktop;

/// <summary>Owns the two independent normal-mode save groups and their explicit data root.</summary>
public sealed class NormalDesktopComposition
{
    private readonly PresentationText text;
    private bool closeSaveActive;
    private bool exitReserved;
    private bool liveShutdownPending;
    private readonly DeviceSessionViewModel? initialDeviceSession;

    public NormalDesktopComposition(
        ShellViewModel shell,
        SettingsViewModel settings,
        ProfilesViewModel profiles,
        ConfigurationWorkspaceViewModel configuration,
        DesktopPreferencesViewModel preferences,
        DevicesViewModel devices,
        DeviceSessionViewModel? deviceSession,
        ApplicationDataPaths dataPaths,
        PresentationText text,
        SessionsWorkspaceViewModel? sessions = null,
        ReleaseCheckViewModel? releases = null)
    {
        Shell = shell;
        Settings = settings;
        Profiles = profiles;
        Configuration = configuration;
        Preferences = preferences;
        Devices = devices;
        initialDeviceSession = deviceSession;
        Sessions = sessions;
        Releases = releases;
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
    public DeviceSessionViewModel? DeviceSession => Sessions is null ? initialDeviceSession : Sessions.SelectedSession;
    public SessionsWorkspaceViewModel? Sessions { get; }
    internal ReleaseCheckViewModel? Releases { get; }
    public ApplicationDataPaths DataPaths { get; }
    public bool HasDirtyGroups => Configuration.IsDirty || Profiles.HasUnstagedChanges || Preferences.IsDirty;
    public bool IsBusy => Configuration.IsBusy || Preferences.IsBusy;
    public bool IsCloseSaveActive => closeSaveActive || exitReserved;
    public bool IsLiveShutdownPending => liveShutdownPending;
    public bool HasLiveOperations => Sessions is not null || initialDeviceSession is not null;

    /// <summary>Stops the owned native child and settles explicit ADB operations after exit acceptance.</summary>
    public async Task<bool> ShutdownLiveAsync()
    {
        if (DeviceSession is null && Sessions is null)
        {
            return true;
        }

        exitReserved = true;
        Configuration.RefreshCloseSaveState();
        Preferences.RefreshCloseSaveState();
        Shell.RefreshThemeEditState();
        bool stopped = Sessions is not null
            ? await Sessions.ShutdownAsync() : await DeviceSession!.ShutdownAsync();

        if (!stopped)
        {
            liveShutdownPending = false;
            if (Sessions is not null)
            {
                Sessions.ResumeAfterFailedShutdown();
            }
            else
            {
                DeviceSession!.ResumeAfterFailedShutdown();
            }
            exitReserved = false;
            Configuration.RefreshCloseSaveState();
            Preferences.RefreshCloseSaveState();
            Shell.RefreshThemeEditState();
            return false;
        }

        Devices.DisposeLive();
        bool settled = await Devices.ShutdownLiveAsync();

        if (!settled)
        {
            // Native has stopped, but device cleanup is still owned: keep Mirror reserved.
            liveShutdownPending = true;
            exitReserved = false;
            Configuration.RefreshCloseSaveState();
            Preferences.RefreshCloseSaveState();
            Shell.RefreshThemeEditState();
        }
        else
        {
            liveShutdownPending = false;
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
        Func<CancellationToken, Task>? beforePreferencesCommit = null,
        Func<string, Task>? copySessionEvidence = null,
        RuntimeBundleResult? validatedRuntime = null,
        IReleaseChecker? releaseChecker = null,
        Func<Uri, CancellationToken, Task>? openReleaseLink = null)
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
        string? legacyDirectory = options.LegacyImportDirectory ?? options.LegacyDevelopmentDirectory;
        LegacyMigrationCoordinator? migration = legacyDirectory is null
            ? null
            : new LegacyMigrationCoordinator(legacyDirectory, store);
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
        SessionsWorkspaceViewModel? sessions = null;

        string? runtimeDirectory = ResolveDeviceRuntimeDirectory(options, AppContext.BaseDirectory);

        if (runtimeDirectory is not null)
        {
            RuntimeBundleResult runtime = validatedRuntime ?? ValidateRuntime(options, runtimeDirectory, default);
            bool enableOpenScreenMdnsCompatibility = runtime.Bundle is { } validatedBundle &&
                BundledAdbCompatibility.RequiresOpenScreenMdns(validatedBundle.Manifest.Files["adb.exe"]);
            // Only a validated machine-capable bundle can supply the normal device-enabled host.
            INativeHost? nativeHost = runtime.Bundle is { } bundle
                ? new MachineNativeHost(bundle.NativeExecutablePath, bundle.ServerPath,
                    bundle.AdbExecutablePath, enableOpenScreenMdnsCompatibility)
                : null;
            sessions = new SessionsWorkspaceViewModel(() => new DeviceSessionViewModel(devices, profiles,
                configuration, store, runtime, nativeHost, action => Dispatcher.UIThread.Post(action),
                options.StorageMode == DesktopStorageMode.Development ? copySessionEvidence : null),
                devices.SelectSessionActions, text);
            deviceSession = sessions.SelectedSession;
            devices.AttachSessionsWorkspace(sessions, shell.ShowProfilesCommand);

            if (runtime.Bundle is { } readyBundle)
            {
                // This affects only children that may start ADB; an existing shared server keeps its own backend.
                IReadOnlyDictionary<string, string>? adbChildEnvironment = enableOpenScreenMdnsCompatibility
                    ? new Dictionary<string, string> { ["ADB_MDNS_OPENSCREEN"] = "1" }
                    : null;
                AdbGateway gateway = new(new AdbProcessRunner(readyBundle.AdbExecutablePath,
                    adbChildEnvironment));
                devices.AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway),
                    action => Dispatcher.UIThread.Post(action), gateway);
                profiles.AttachObservedDevices(devices);
            }
        }

        ReleaseCheckViewModel? releases = null;

        if (releaseChecker is not null && openReleaseLink is not null)
        {
            string productVersion = typeof(NormalDesktopFactory).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
            releases = new ReleaseCheckViewModel(releaseChecker, productVersion, text, openReleaseLink);
        }

        if (sessions is not null || releases is not null)
        {
            shell.AttachSessionWorkspaces(sessions, new DiagnosticsWorkspaceViewModel(sessions, text, releases));
        }

        return new NormalDesktopComposition(shell, settings, profiles, configuration, preferences,
            devices, deviceSession, paths, text, sessions, releases);
    }

    /// <summary>Uses the package runtime for normal modes and only an explicit runtime for DEV.</summary>
    internal static string? ResolveDeviceRuntimeDirectory(DesktopLaunchOptions options, string applicationDirectory)
    {
        if (options.Preview)
        {
            return null;
        }

        if (options.StorageMode == DesktopStorageMode.Development)
        {
            return options.DeviceRuntimeDirectory;
        }

        return options.StorageMode is DesktopStorageMode.Portable or DesktopStorageMode.Installed
            ? Path.Combine(applicationDirectory, "runtime") : null;
    }

    internal static Task<RuntimeBundleResult?> ValidateRuntimeAsync(DesktopLaunchOptions options,
        CancellationToken cancellationToken)
    {
        string? directory = ResolveDeviceRuntimeDirectory(options, AppContext.BaseDirectory);
        return directory is null
            ? Task.FromResult<RuntimeBundleResult?>(null)
            : Task.Run<RuntimeBundleResult?>(() => ValidateRuntime(options, directory, cancellationToken), cancellationToken);
    }

    private static RuntimeBundleResult ValidateRuntime(DesktopLaunchOptions options, string directory,
        CancellationToken cancellationToken) => options.StorageMode == DesktopStorageMode.Development
        ? DeviceRuntimeBundle.Validate(directory, cancellationToken)
        : DeviceRuntimeBundle.ValidateProduction(directory, cancellationToken);

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
