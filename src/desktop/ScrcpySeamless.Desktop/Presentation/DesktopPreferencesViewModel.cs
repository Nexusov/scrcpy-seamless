using System.Windows.Input;
using Avalonia.Threading;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Edits separate Desktop preferences without writing on selection or keystrokes.</summary>
public sealed class DesktopPreferencesViewModel : ObservableViewModel
{
    private readonly VersionedDesktopPreferencesStore store;
    private readonly Action<DesktopAppearancePreferences> previewAppearance;
    private readonly Action<DesktopShortcutPreferences> activateShortcuts;
    private readonly Func<string?, string?> resolveEffectiveFont;
    private readonly Func<CancellationToken, Task>? beforeCommit;
    private Func<bool>? closeSaveActive;
    private DesktopPreferences? baseline;
    private string? revision;
    private DesktopTheme theme = DesktopTheme.System;
    private string accentColor = string.Empty;
    private string fontFamily = string.Empty;
    private int scalePercent = 100;
    private string focusSearchBinding = "Control+F";
    private string showDevicesBinding = "Control+1";
    private string showSettingsBinding = "Control+2";
    private string statusMessage = string.Empty;
    private string validationMessage = string.Empty;
    private string fontSubstitutionMessage = string.Empty;
    private bool isBusy;
    private bool isReady;

    public DesktopPreferencesViewModel(
        PresentationText text,
        VersionedDesktopPreferencesStore store,
        Action<DesktopAppearancePreferences> previewAppearance,
        Action<DesktopShortcutPreferences> activateShortcuts,
        Func<string?, string?>? resolveEffectiveFont = null,
        Func<CancellationToken, Task>? beforeCommit = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.previewAppearance = previewAppearance ?? throw new ArgumentNullException(nameof(previewAppearance));
        this.activateShortcuts = activateShortcuts ?? throw new ArgumentNullException(nameof(activateShortcuts));
        this.resolveEffectiveFont = resolveEffectiveFont ?? (requested => requested);
        this.beforeCommit = beforeCommit;
        Title = text.Get("preferences.title");
        Subtitle = text.Get("preferences.subtitle");
        AppearanceLabel = text.Get("preferences.appearance");
        ShortcutsLabel = text.Get("preferences.shortcuts");
        ThemeLabel = text.Get("preferences.theme");
        AccentLabel = text.Get("preferences.accent");
        FontLabel = text.Get("preferences.font");
        ScaleLabel = text.Get("preferences.scale");
        FocusSearchLabel = text.Get("preferences.focusSearch");
        ShowDevicesLabel = text.Get("preferences.showDevices");
        ShowSettingsLabel = text.Get("preferences.showSettings");
        ApplyLabel = text.Get("preferences.apply");
        CancelLabel = text.Get("preferences.cancel");
        ResetLabel = text.Get("preferences.reset");
        ReloadLabel = text.Get("preferences.reload");
        DefaultHint = text.Get("preferences.defaultHint");
        UnboundHint = text.Get("preferences.unboundHint");
        ThemeChoices = Enum.GetValues<DesktopTheme>();
        ScaleChoices = [100, 110, 125, 150];
        CancelCommand = new ActionCommand(CancelChanges);
        ResetCommand = new ActionCommand(ResetToDefaults);
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string AppearanceLabel { get; }
    public string ShortcutsLabel { get; }
    public string ThemeLabel { get; }
    public string AccentLabel { get; }
    public string FontLabel { get; }
    public string ScaleLabel { get; }
    public string FocusSearchLabel { get; }
    public string ShowDevicesLabel { get; }
    public string ShowSettingsLabel { get; }
    public string ApplyLabel { get; }
    public string CancelLabel { get; }
    public string ResetLabel { get; }
    public string ReloadLabel { get; }
    public string DefaultHint { get; }
    public string UnboundHint { get; }
    public IReadOnlyList<DesktopTheme> ThemeChoices { get; }
    public IReadOnlyList<int> ScaleChoices { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetCommand { get; }
    public bool IsBusy => isBusy;
    public bool IsReady => isReady;
    public bool CanEdit => IsReady && !IsBusy && closeSaveActive?.Invoke() != true;
    public bool CanReload => !IsBusy && closeSaveActive?.Invoke() != true;
    public bool IsDirty => isReady && !MatchesBaseline();
    public bool CanApply => CanEdit && IsDirty && ValidationMessage.Length == 0;
    public bool CanCancel => CanEdit && IsDirty;

    /// <summary>Shares close-time edit ownership with the preference editor.</summary>
    public void AttachCloseSaveGuard(Func<bool> isActive) => closeSaveActive = isActive;

    /// <summary>Refreshes edit commands when the close workflow takes or releases ownership.</summary>
    public void RefreshCloseSaveState() => NotifyDraftState();
    public bool HasStatus => StatusMessage.Length > 0;
    public bool HasValidation => ValidationMessage.Length > 0;
    public bool HasFontSubstitution => FontSubstitutionMessage.Length > 0;

    public DesktopTheme Theme
    {
        get => theme;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref theme, value))
            {
                DraftChanged(preview: true);
            }
        }
    }

    public string AccentColor
    {
        get => accentColor;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref accentColor, value ?? string.Empty))
            {
                DraftChanged(preview: true);
            }
        }
    }

    public string FontFamily
    {
        get => fontFamily;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref fontFamily, value ?? string.Empty))
            {
                DraftChanged(preview: true);
            }
        }
    }

    public int ScalePercent
    {
        get => scalePercent;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref scalePercent, value))
            {
                DraftChanged(preview: true);
            }
        }
    }

    public string FocusSearchBinding
    {
        get => focusSearchBinding;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref focusSearchBinding, value ?? string.Empty))
            {
                DraftChanged(preview: false);
            }
        }
    }

    public string ShowDevicesBinding
    {
        get => showDevicesBinding;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref showDevicesBinding, value ?? string.Empty))
            {
                DraftChanged(preview: false);
            }
        }
    }

    public string ShowSettingsBinding
    {
        get => showSettingsBinding;
        set
        {
            if (!CanEdit)
            {
                return;
            }

            if (SetProperty(ref showSettingsBinding, value ?? string.Empty))
            {
                DraftChanged(preview: false);
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set
        {
            if (SetProperty(ref validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidation));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public string FontSubstitutionMessage
    {
        get => fontSubstitutionMessage;
        private set
        {
            if (SetProperty(ref fontSubstitutionMessage, value))
            {
                OnPropertyChanged(nameof(HasFontSubstitution));
            }
        }
    }

    /// <summary>Loads the selected document without creating it and activates only validated saved bindings.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!CanReload)
        {
            return;
        }

        SetBusy(true);

        try
        {
            DesktopPreferencesReadResult loaded = await store.ReadAsync(cancellationToken);
            await OnUiThreadAsync(() =>
            {
                if (loaded.Status is DesktopPreferencesReadStatus.Loaded or DesktopPreferencesReadStatus.Missing)
                {
                    baseline = ClonePreferences(loaded.Preferences!);
                    revision = loaded.Revision;
                    SetReady(true);
                    RestoreFromBaseline();
                    activateShortcuts(CloneShortcuts(baseline.Shortcuts));
                    StatusMessage = loaded.Status == DesktopPreferencesReadStatus.Missing
                        ? "No saved Desktop preferences; defaults are in memory."
                        : "Desktop preferences loaded.";
                    return;
                }

                baseline = null;
                revision = loaded.Revision;
                SetReady(false);
                RestoreSafeDefaults();
                activateShortcuts(CloneShortcuts(new DesktopPreferences().Shortcuts));
                StatusMessage = loaded.Status == DesktopPreferencesReadStatus.Unsupported
                    ? $"Desktop preferences use an unsupported version at {store.PreferencesFile}. Original file preserved; safe defaults are shown."
                    : $"Desktop preferences are invalid at {store.PreferencesFile}. Original file preserved; safe defaults are shown.";
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await OnUiThreadAsync(() =>
            {
                baseline = null;
                SetReady(false);
                RestoreSafeDefaults();
                activateShortcuts(CloneShortcuts(new DesktopPreferences().Shortcuts));
                StatusMessage = $"Desktop preferences could not be read at {store.PreferencesFile} ({FailureCategory(error)}). Original state was not changed.";
            });
        }
        finally
        {
            await OnUiThreadAsync(() => SetBusy(false));
        }
    }

    /// <summary>Validates and atomically saves one captured preferences candidate away from the UI thread.</summary>
    public async Task<bool> ApplyAsync(CancellationToken cancellationToken)
        => await ApplyCoreAsync(cancellationToken, closeOwned: false);

    /// <summary>Commits preferences while the close workflow owns both save groups.</summary>
    internal Task<bool> ApplyForCloseAsync(CancellationToken cancellationToken)
        => ApplyCoreAsync(cancellationToken, closeOwned: true);

    /// <summary>Validates and commits one immutable preference candidate.</summary>
    private async Task<bool> ApplyCoreAsync(CancellationToken cancellationToken, bool closeOwned)
    {
        if (IsBusy || !IsReady || (!closeOwned && closeSaveActive?.Invoke() == true))
        {
            return false;
        }

        DesktopPreferences candidate = CaptureDraft();
        IReadOnlyList<ScrcpySeamless.Core.ValidationIssue> issues = candidate.Validate();

        if (issues.Count > 0)
        {
            ValidationMessage = $"Check {string.Join(", ", issues.Select(issue => issue.Path))} before Apply.";
            return false;
        }

        if (!IsDirty)
        {
            StatusMessage = "No Desktop preference changes to save.";
            return true;
        }

        SetBusy(true);

        try
        {
            if (beforeCommit is not null)
            {
                await beforeCommit(cancellationToken);
            }

            DesktopPreferencesCommitResult result = await Task.Run(
                () => store.Commit(revision, candidate, cancellationToken), cancellationToken);
            return await OnUiThreadAsync(() => HandleCommit(result, candidate));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await OnUiThreadAsync(() => StatusMessage = $"Desktop preferences were not saved at {store.PreferencesFile} ({FailureCategory(error)}). The draft remains available.");
            return false;
        }
        catch (OperationCanceledException)
        {
            await OnUiThreadAsync(() => StatusMessage = "Desktop preference save was cancelled before commit.");
            return false;
        }
        finally
        {
            await OnUiThreadAsync(() => SetBusy(false));
        }
    }

    /// <summary>Discards only the Desktop draft and restores the most recently committed appearance.</summary>
    public void CancelChanges()
    {
        if (!CanEdit)
        {
            return;
        }

        RestoreFromBaseline();
        StatusMessage = "Desktop preference changes discarded.";
    }

    /// <summary>Explicitly discards the current draft and re-reads the selected file after a conflict.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        return LoadAsync(cancellationToken);
    }

    /// <summary>Places safe defaults in the draft; persistence still requires Apply.</summary>
    public void ResetToDefaults()
    {
        if (!CanEdit)
        {
            return;
        }

        AssignDraft(new DesktopPreferences());
        StatusMessage = "Defaults are previewed; Apply to save them.";
    }

    /// <summary>Sets all draft fields without passing through per-field edit handlers.</summary>
    private void AssignDraft(DesktopPreferences preferences)
    {
        theme = preferences.Appearance.Theme;
        accentColor = preferences.Appearance.AccentColor ?? string.Empty;
        fontFamily = preferences.Appearance.FontFamily ?? string.Empty;
        scalePercent = preferences.Appearance.ScalePercent;
        focusSearchBinding = preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.FocusSettingsSearch);
        showDevicesBinding = preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.ShowDevices);
        showSettingsBinding = preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.ShowSettings);

        foreach (string property in new[]
        {
            nameof(Theme), nameof(AccentColor), nameof(FontFamily), nameof(ScalePercent),
            nameof(FocusSearchBinding), nameof(ShowDevicesBinding), nameof(ShowSettingsBinding),
        })
        {
            OnPropertyChanged(property);
        }

        ValidateDraft();
        previewAppearance(CaptureDraft().Appearance);
        UpdateFontSubstitution();
        NotifyDraftState();
    }

    /// <summary>Restores the loaded or last committed baseline without a disk write.</summary>
    private void RestoreFromBaseline()
    {
        AssignDraft(baseline ?? new DesktopPreferences());
    }

    /// <summary>Uses safe rendering defaults after an invalid or unreadable file.</summary>
    private void RestoreSafeDefaults()
    {
        AssignDraft(new DesktopPreferences());
    }

    /// <summary>Builds a detached immutable-in-flight candidate from the editor fields.</summary>
    private DesktopPreferences CaptureDraft()
    {
        return new DesktopPreferences
        {
            Appearance = new DesktopAppearancePreferences
            {
                Theme = Theme,
                AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? null : AccentColor.Trim(),
                FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily.Trim(),
                ScalePercent = ScalePercent,
            },
            Shortcuts = new DesktopShortcutPreferences
            {
                Bindings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [DesktopCommandIds.FocusSettingsSearch] = FocusSearchBinding.Trim(),
                    [DesktopCommandIds.ShowDevices] = ShowDevicesBinding.Trim(),
                    [DesktopCommandIds.ShowSettings] = ShowSettingsBinding.Trim(),
                },
            },
        };
    }

    /// <summary>Reports a field change and previews valid appearance without activating draft shortcuts.</summary>
    private void DraftChanged(bool preview)
    {
        if (IsBusy || !IsReady)
        {
            return;
        }

        ValidateDraft();

        if (preview && CaptureDraft().Appearance.Validate(nameof(DesktopPreferences.Appearance)).Count == 0)
        {
            previewAppearance(CaptureDraft().Appearance);
            UpdateFontSubstitution();
        }

        StatusMessage = string.Empty;
        NotifyDraftState();
    }

    /// <summary>Shows a local validation state without writing or normalizing saved gestures.</summary>
    private void ValidateDraft()
    {
        IReadOnlyList<ScrcpySeamless.Core.ValidationIssue> issues = CaptureDraft().Validate();
        ValidationMessage = issues.Count == 0
            ? string.Empty
            : $"Check {string.Join(", ", issues.Select(issue => issue.Path))}: use an RGB accent, supported scale and unique safe shortcuts.";
    }

    /// <summary>Explains when the saved font request cannot be rendered on this machine.</summary>
    private void UpdateFontSubstitution()
    {
        string? requested = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily.Trim();
        string? effective = resolveEffectiveFont(requested);
        FontSubstitutionMessage = requested is not null &&
            !string.Equals(requested, effective, StringComparison.OrdinalIgnoreCase)
            ? $"Font ‘{requested}’ is unavailable; showing ‘{effective ?? "system default"}’. The requested name is preserved."
            : string.Empty;
    }

    /// <summary>Commits the candidate as baseline only after a successful compare-and-swap.</summary>
    private bool HandleCommit(DesktopPreferencesCommitResult result, DesktopPreferences candidate)
    {
        if (result.Status is DesktopPreferencesCommitStatus.Committed or DesktopPreferencesCommitStatus.Unchanged)
        {
            baseline = ClonePreferences(candidate);
            revision = result.Revision;
            activateShortcuts(CloneShortcuts(candidate.Shortcuts));
            StatusMessage = result.Status == DesktopPreferencesCommitStatus.Unchanged
                ? "Desktop preferences were already current."
                : "Desktop preferences saved.";
            NotifyDraftState();
            return true;
        }

        StatusMessage = result.Status switch
        {
            DesktopPreferencesCommitStatus.RevisionConflict => "Desktop preferences changed elsewhere. Reload explicitly before saving again.",
            DesktopPreferencesCommitStatus.Busy => "Desktop preferences are being saved elsewhere. Try again.",
            DesktopPreferencesCommitStatus.InvalidExistingDocument => "The saved Desktop preferences are invalid; original file preserved.",
            _ => "Desktop preferences are invalid and were not saved.",
        };
        return false;
    }

    /// <summary>Updates derived Apply and Cancel availability after an edit or commit.</summary>
    private void NotifyDraftState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanReload));
    }

    /// <summary>Compares only documented preference fields in the detached edit group.</summary>
    private bool MatchesBaseline()
    {
        if (baseline is null)
        {
            return true;
        }

        DesktopPreferences draft = CaptureDraft();
        DesktopAppearancePreferences saved = baseline.Appearance;
        DesktopAppearancePreferences edited = draft.Appearance;

        return saved.Theme == edited.Theme &&
            string.Equals(saved.AccentColor, edited.AccentColor, StringComparison.Ordinal) &&
            string.Equals(saved.FontFamily, edited.FontFamily, StringComparison.Ordinal) &&
            saved.ScalePercent == edited.ScalePercent &&
            DesktopCommandIds.Supported.All(commandId =>
                string.Equals(baseline.Shortcuts.EffectiveBinding(commandId),
                    draft.Shortcuts.EffectiveBinding(commandId), StringComparison.Ordinal));
    }

    /// <summary>Detaches the baseline from I/O results and active-shortcut callbacks.</summary>
    private static DesktopPreferences ClonePreferences(DesktopPreferences source)
    {
        return new DesktopPreferences
        {
            SchemaVersion = source.SchemaVersion,
            Appearance = new DesktopAppearancePreferences
            {
                Theme = source.Appearance.Theme,
                AccentColor = source.Appearance.AccentColor,
                FontFamily = source.Appearance.FontFamily,
                ScalePercent = source.Appearance.ScalePercent,
            },
            Shortcuts = CloneShortcuts(source.Shortcuts),
        };
    }

    /// <summary>Copies editable bindings before another component may activate them.</summary>
    private static DesktopShortcutPreferences CloneShortcuts(DesktopShortcutPreferences source)
    {
        return new DesktopShortcutPreferences
        {
            Bindings = new Dictionary<string, string>(source.Bindings, StringComparer.Ordinal),
        };
    }

    /// <summary>Prevents duplicate edits or commits while an owned operation is in flight.</summary>
    private void SetBusy(bool value)
    {
        if (SetProperty(ref isBusy, value, nameof(IsBusy)))
        {
            NotifyDraftState();
        }
    }

    /// <summary>Notifies the view when a safe editable document becomes available.</summary>
    private void SetReady(bool value)
    {
        if (SetProperty(ref isReady, value, nameof(IsReady)))
        {
            NotifyDraftState();
        }
    }

    /// <summary>Applies asynchronous I/O results to bindings and visual callbacks on the UI thread.</summary>
    private static async Task OnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>Applies an async result and returns its value on the UI thread.</summary>
    private static async Task<T> OnUiThreadAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        return await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>Describes an I/O category without including the exception's potentially sensitive text.</summary>
    private static string FailureCategory(Exception error)
    {
        return error is UnauthorizedAccessException ? "access denied" : "I/O failure";
    }
}
