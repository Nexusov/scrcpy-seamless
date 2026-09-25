using System.Security;
using System.Text.Json;
using System.Windows.Input;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Presentation state for the independently persisted device configuration group.</summary>
public enum ConfigurationWorkspaceStatus
{
    Unloaded,
    Loading,
    Missing,
    Ready,
    Invalid,
    Inaccessible,
    Saving,
    PreparingMigration,
    Migrating,
}

/// <summary>A migration preview summary that omits device identifiers and secret values.</summary>
public sealed record ConfigurationMigrationSummary(int ProfileCount, int OptionOverrideCount);

/// <summary>One explicit migration action and optional reload of the authoritative v2 document.</summary>
public sealed record ConfigurationWorkspaceMigrationResult(
    LegacyMigrationStatus? Status,
    IReadOnlyList<LegacyMigrationProblem> Problems,
    ConfigurationSessionLoadResult? ReloadResult = null,
    string? ErrorKind = null);

/// <summary>Coordinates a detached v2 draft and explicit legacy migration for the Desktop UI.</summary>
public sealed class ConfigurationWorkspaceViewModel : ObservableViewModel
{
    private readonly ConfigurationEditSession session;
    private readonly InMemoryOptionDraft optionDraft;
    private readonly LegacyMigrationCoordinator? migration;
    private LegacyMigrationProposal? preparedMigration;
    private ConfigurationWorkspaceStatus status = ConfigurationWorkspaceStatus.Unloaded;
    private ConfigurationSessionLoadResult? lastLoadResult;
    private ConfigurationSessionApplyResult? lastApplyResult;
    private ConfigurationWorkspaceMigrationResult? lastMigrationResult;
    private ConfigurationMigrationSummary? migrationSummary;
    private int operationActive;
    private string? lastCommandErrorKind;
    private ProfilesViewModel? profileEditor;

    /// <summary>Constructs an in-memory workspace; the caller selects persistent adapters explicitly.</summary>
    public ConfigurationWorkspaceViewModel(
        ConfigurationEditSession session,
        InMemoryOptionDraft optionDraft,
        LegacyMigrationCoordinator? migration = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.optionDraft = optionDraft ?? throw new ArgumentNullException(nameof(optionDraft));
        this.migration = migration;
        optionDraft.Changed += OnOptionChanged;
        ApplyCommand = new AsyncActionCommand(
            async () => { await ApplyAsync(CancellationToken.None); },
            () => CanEdit && IsDirty && !HasPendingProfileEdit,
            ReportCommandError);
        CancelCommand = new AsyncActionCommand(
            () => { CancelChanges(); return Task.CompletedTask; },
            () => CanEdit && IsDirty,
            ReportCommandError);
        PrepareMigrationCommand = new AsyncActionCommand(
            async () => { await PrepareMigrationAsync(CancellationToken.None); },
            () => migration is not null && Status == ConfigurationWorkspaceStatus.Missing && !IsDirty && !IsBusy,
            ReportCommandError);
        CommitMigrationCommand = new AsyncActionCommand(
            async () => { await CommitMigrationAsync(CancellationToken.None); },
            () => HasPreparedMigration && !IsBusy,
            ReportCommandError);
        CancelMigrationCommand = new AsyncActionCommand(
            () => { CancelMigration(); return Task.CompletedTask; },
            () => HasPreparedMigration && !IsBusy,
            ReportCommandError);
        ReloadCommand = new AsyncActionCommand(
            async () => { await LoadAsync(CancellationToken.None); },
            () => !IsBusy && (!ReloadBlockedByUnsavedChanges || RequiresReload),
            ReportCommandError);
    }

    public ConfigurationWorkspaceStatus Status
    {
        get => status;
        private set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(HasError));
                NotifyCommands();
            }
        }
    }
    public ConfigurationSessionLoadResult? LastLoadResult { get => lastLoadResult; private set => SetProperty(ref lastLoadResult, value); }
    public ConfigurationSessionApplyResult? LastApplyResult { get => lastApplyResult; private set => SetProperty(ref lastApplyResult, value); }
    public ConfigurationWorkspaceMigrationResult? LastMigrationResult { get => lastMigrationResult; private set => SetProperty(ref lastMigrationResult, value); }
    public ConfigurationMigrationSummary? MigrationSummary { get => migrationSummary; private set => SetProperty(ref migrationSummary, value); }
    public ConfigurationV2? Draft => session.Draft;
    public string? Revision => session.Revision;
    public bool IsDirty => session.IsDirty;
    public bool HasPendingProfileEdit => profileEditor?.HasUnstagedChanges == true;
    public bool ReloadBlockedByUnsavedChanges => IsDirty || HasPendingProfileEdit;
    public bool ReloadRequiresDiscard => RequiresReload && ReloadBlockedByUnsavedChanges;
    public bool IsBusy => Volatile.Read(ref operationActive) != 0 || session.IsBusy;
    public bool RequiresReload => session.RequiresReload;
    public bool HasPreparedMigration => preparedMigration is not null;
    public bool CanPrepareMigration => migration is not null && Status == ConfigurationWorkspaceStatus.Missing &&
        !IsDirty && !IsBusy;
    public bool CanEdit => Status is ConfigurationWorkspaceStatus.Ready or ConfigurationWorkspaceStatus.Missing &&
        !IsBusy && !RequiresReload;
    public bool HasError => Status is ConfigurationWorkspaceStatus.Invalid or ConfigurationWorkspaceStatus.Inaccessible ||
        LastApplyResult?.Status is ConfigurationSessionApplyStatus.Invalid or ConfigurationSessionApplyStatus.RevisionConflict or
            ConfigurationSessionApplyStatus.Busy or ConfigurationSessionApplyStatus.IoError ||
        LastMigrationResult?.ErrorKind is not null || LastCommandErrorKind is not null;
    public string? LastCommandErrorKind { get => lastCommandErrorKind; private set => SetProperty(ref lastCommandErrorKind, value); }
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PrepareMigrationCommand { get; }
    public ICommand CommitMigrationCommand { get; }
    public ICommand CancelMigrationCommand { get; }
    public event Action? DraftReplaced;
    public ICommand ReloadCommand { get; }

    /// <summary>Keeps Settings actions from omitting or replacing a separate profile editor buffer.</summary>
    public void AttachProfileEditor(ProfilesViewModel editor)
    {
        profileEditor = editor;
        editor.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ProfilesViewModel.HasUnstagedChanges))
            {
                RefreshPendingProfileState();
            }
        };
        RefreshPendingProfileState();
    }

    /// <summary>Updates save and reload controls when the profile buffer changes.</summary>
    private void RefreshPendingProfileState()
    {
        OnPropertyChanged(nameof(HasPendingProfileEdit));
        OnPropertyChanged(nameof(ReloadBlockedByUnsavedChanges));
        OnPropertyChanged(nameof(ReloadRequiresDiscard));
        NotifyCommands();
    }

    /// <summary>Loads selected v2 authority and hydrates the detached option editor on success.</summary>
    public async Task<ConfigurationSessionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return new ConfigurationSessionLoadResult(ConfigurationSessionLoadStatus.Busy, []);
        }

        try
        {
            Status = ConfigurationWorkspaceStatus.Loading;
            ConfigurationSessionLoadResult loaded = await session.LoadAsync(cancellationToken);
            LastLoadResult = loaded;
            LastApplyResult = null;
            LastMigrationResult = null;
            LastCommandErrorKind = null;
            Status = StatusFor(loaded.Status);

            if (loaded.Status is ConfigurationSessionLoadStatus.Ready or ConfigurationSessionLoadStatus.Missing)
            {
                optionDraft.Replace(session.Draft!.Mirroring.Options);
                DraftReplaced?.Invoke();
                preparedMigration = null;
                MigrationSummary = null;
                OnPropertyChanged(nameof(HasPreparedMigration));
            }

            RefreshDerived();
            return loaded;
        }
        finally
        {
            End();
        }
    }

    /// <summary>Applies an immutable candidate and retains the draft and diagnostics after failure.</summary>
    public async Task<ConfigurationSessionApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return new ConfigurationSessionApplyResult(ConfigurationSessionApplyStatus.Busy, session.Revision, [], []);
        }

        ConfigurationWorkspaceStatus previousStatus = Status;

        try
        {
            Status = ConfigurationWorkspaceStatus.Saving;
            ConfigurationSessionApplyResult applied = await session.ApplyAsync(cancellationToken);
            LastApplyResult = applied;
            LastCommandErrorKind = null;
            Status = applied.Status == ConfigurationSessionApplyStatus.Applied
                ? ConfigurationWorkspaceStatus.Ready
                : previousStatus;
            RefreshDerived();
            return applied;
        }
        finally
        {
            End();
        }
    }

    /// <summary>Restores the last loaded or applied baseline without writing or clearing saved overrides.</summary>
    public void CancelChanges()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Configuration cannot be cancelled during another operation.");
        }

        session.CancelChanges();
        optionDraft.Replace(session.Draft!.Mirroring.Options);
        DraftReplaced?.Invoke();
        LastApplyResult = null;
        LastCommandErrorKind = null;
        RefreshDerived();
    }

    /// <summary>Inspects selected synthetic or user-chosen legacy input without creating v2 state.</summary>
    public async Task<ConfigurationWorkspaceMigrationResult> PrepareMigrationAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return new ConfigurationWorkspaceMigrationResult(LegacyMigrationStatus.Busy, []);
        }

        ConfigurationWorkspaceStatus previousStatus = Status;

        try
        {
            EnsureMigrationAllowed();
            Status = ConfigurationWorkspaceStatus.PreparingMigration;
            LegacyMigrationOperation prepared = await migration!.PrepareAsync(cancellationToken);
            preparedMigration = prepared.Status == LegacyMigrationStatus.Ready ? prepared.Proposal : null;
            MigrationSummary = preparedMigration is null ? null : new ConfigurationMigrationSummary(
                preparedMigration.Configuration.Profiles.Count,
                preparedMigration.Configuration.Mirroring.Options.Count);
            OnPropertyChanged(nameof(HasPreparedMigration));
            ConfigurationWorkspaceMigrationResult result = new(prepared.Status, prepared.Problems);
            LastMigrationResult = result;
            OnPropertyChanged(nameof(HasError));
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            ConfigurationWorkspaceMigrationResult result = new(null, [], ErrorKind: exception.GetType().Name);
            LastMigrationResult = result;
            OnPropertyChanged(nameof(HasError));
            return result;
        }
        finally
        {
            Status = previousStatus;
            End();
        }
    }

    /// <summary>Confirms the prepared proposal through the migration coordinator and reloads committed v2.</summary>
    public async Task<ConfigurationWorkspaceMigrationResult> CommitMigrationAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return new ConfigurationWorkspaceMigrationResult(LegacyMigrationStatus.Busy, []);
        }

        ConfigurationWorkspaceStatus previousStatus = Status;

        try
        {
            EnsureMigrationAllowed();
            LegacyMigrationProposal proposal = preparedMigration
                ?? throw new InvalidOperationException("A prepared migration must be reviewed before confirmation.");
            Status = ConfigurationWorkspaceStatus.Migrating;
            LegacyMigrationOperation committed = await Task.Run(
                () => migration!.CommitPrepared(proposal, cancellationToken), cancellationToken);

            if (committed.Status == LegacyMigrationStatus.Migrated)
            {
                ConfigurationSessionLoadResult reloaded = await session.LoadAsync(cancellationToken);
                LastLoadResult = reloaded;
                Status = StatusFor(reloaded.Status);

                if (reloaded.Status == ConfigurationSessionLoadStatus.Ready)
                {
                    optionDraft.Replace(session.Draft!.Mirroring.Options);
                    DraftReplaced?.Invoke();
                }

                preparedMigration = null;
                MigrationSummary = null;
                OnPropertyChanged(nameof(HasPreparedMigration));
                ConfigurationWorkspaceMigrationResult migrated = new(committed.Status, committed.Problems, reloaded);
                LastMigrationResult = migrated;
                OnPropertyChanged(nameof(HasError));
                RefreshDerived();
                return migrated;
            }

            if (committed.Status is LegacyMigrationStatus.LegacyChanged or LegacyMigrationStatus.RevisionConflict or LegacyMigrationStatus.InvalidLegacy)
            {
                preparedMigration = null;
                MigrationSummary = null;
                OnPropertyChanged(nameof(HasPreparedMigration));
            }

            ConfigurationWorkspaceMigrationResult result = new(committed.Status, committed.Problems);
            LastMigrationResult = result;
            OnPropertyChanged(nameof(HasError));
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            ConfigurationWorkspaceMigrationResult result = new(null, [], ErrorKind: exception.GetType().Name);
            LastMigrationResult = result;
            OnPropertyChanged(nameof(HasError));
            return result;
        }
        finally
        {
            if (Status == ConfigurationWorkspaceStatus.Migrating)
            {
                Status = previousStatus;
            }

            End();
        }
    }

    /// <summary>Discards only the uncommitted migration proposal.</summary>
    public void CancelMigration()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Migration cannot be cancelled during another operation.");
        }

        preparedMigration = null;
        MigrationSummary = null;
        LastMigrationResult = null;
        OnPropertyChanged(nameof(HasPreparedMigration));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>Routes one editor override change to the same detached configuration session.</summary>
    private void OnOptionChanged(string optionId, JsonElement? value)
    {
        if (!CanEdit)
        {
            optionDraft.Replace(session.Draft?.Mirroring.Options ?? new Dictionary<string, JsonElement>());
            DraftReplaced?.Invoke();
            LastCommandErrorKind = "EditUnavailable";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        if (value.HasValue)
        {
            session.SetOption(optionId, value.Value);
        }
        else
        {
            session.ResetOption(optionId);
        }

        LastApplyResult = null;
        LastCommandErrorKind = null;
        RefreshDerived();
    }

    /// <summary>Limits migration to explicit missing-v2 state with no unsaved configuration draft.</summary>
    private void EnsureMigrationAllowed()
    {
        if (migration is null || Status != ConfigurationWorkspaceStatus.Missing || session.IsDirty || session.RequiresReload)
        {
            throw new InvalidOperationException("Migration requires an unchanged missing-v2 configuration state.");
        }
    }

    /// <summary>Maps storage authority into presentation state without inventing defaults after failure.</summary>
    private static ConfigurationWorkspaceStatus StatusFor(ConfigurationSessionLoadStatus loaded) => loaded switch
    {
        ConfigurationSessionLoadStatus.Ready => ConfigurationWorkspaceStatus.Ready,
        ConfigurationSessionLoadStatus.Missing => ConfigurationWorkspaceStatus.Missing,
        ConfigurationSessionLoadStatus.Invalid => ConfigurationWorkspaceStatus.Invalid,
        ConfigurationSessionLoadStatus.Inaccessible => ConfigurationWorkspaceStatus.Inaccessible,
        _ => ConfigurationWorkspaceStatus.Unloaded,
    };

    /// <summary>Reserves one workspace action at a time.</summary>
    private bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref operationActive, 1, 0) != 0)
        {
            return false;
        }

        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPrepareMigration));
        NotifyCommands();
        return true;
    }

    /// <summary>Releases workspace action ownership after an awaited operation.</summary>
    private void End()
    {
        Volatile.Write(ref operationActive, 0);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPrepareMigration));
        NotifyCommands();
    }

    /// <summary>Refreshes derived state after an owned draft or revision transition.</summary>
    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(ReloadBlockedByUnsavedChanges));
        OnPropertyChanged(nameof(ReloadRequiresDiscard));
        OnPropertyChanged(nameof(RequiresReload));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPrepareMigration));
        OnPropertyChanged(nameof(HasError));
        NotifyCommands();
    }

    /// <summary>Surfaces unexpected command failures without losing them in a void UI callback.</summary>
    private void ReportCommandError(Exception exception)
    {
        LastCommandErrorKind = exception.GetType().Name;
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>Updates all conditional command states after a draft or operation transition.</summary>
    private void NotifyCommands()
    {
        ((AsyncActionCommand)ApplyCommand).NotifyCanExecuteChanged();
        ((AsyncActionCommand)CancelCommand).NotifyCanExecuteChanged();
        ((AsyncActionCommand)PrepareMigrationCommand).NotifyCanExecuteChanged();
        ((AsyncActionCommand)CommitMigrationCommand).NotifyCanExecuteChanged();
        ((AsyncActionCommand)CancelMigrationCommand).NotifyCanExecuteChanged();
        ((AsyncActionCommand)ReloadCommand).NotifyCanExecuteChanged();
    }
}
