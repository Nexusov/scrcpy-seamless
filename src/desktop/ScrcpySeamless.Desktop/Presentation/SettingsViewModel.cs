using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>A localized category entry backed by an invariant generated category key.</summary>
public sealed record SettingsCategory(string Id, string Label);

/// <summary>Identifies the two result changes that move the visible list to its start.</summary>
public enum SettingsResultsChange { Category, Search }

/// <summary>Selects one settings editor without discarding either detached draft.</summary>
public enum SettingsSection { Mirroring, Desktop }

/// <summary>Exposes one generated editable option and its detached draft value.</summary>
public sealed class OptionRowViewModel : ObservableViewModel
{
    private readonly InMemoryOptionDraft draft;
    private readonly PresentationText text;
    private readonly Action onChanged;
    private string textValue = string.Empty;
    private string? choiceValue;
    private bool booleanValue;
    private string? validationMessage;

    public OptionRowViewModel(
        OptionDescriptor descriptor,
        InMemoryOptionDraft draft,
        PresentationText text,
        Action onChanged)
    {
        Descriptor = descriptor;
        this.draft = draft;
        this.text = text;
        this.onChanged = onChanged;
        Label = text.Get(descriptor.LabelResourceKey);
        Description = text.Get(descriptor.DescriptionResourceKey);
        ShortDescription = Description.Split('\n')[0];
        HelpLabel = text.Get("settings.row.help");
        ResetLabel = text.Get("settings.row.reset");
        OverrideLabel = text.Get("settings.row.override");
        PortUnavailableLabel = text.Get("settings.portUnavailable");
        IsCompositePort = descriptor.Id == "port";
        IsBoolean = descriptor.ArgumentShape == OptionArgumentShape.None;
        IsChoice = !IsBoolean && descriptor.EnumValues.Count > 0;
        IsText = !IsBoolean && !IsChoice;
        Choices = descriptor.EnumValues;
        ResetCommand = new ActionCommand(Reset);
        LoadValue();
    }

    public OptionDescriptor Descriptor { get; }
    public string Id => Descriptor.Id;
    public string AutomationId => $"option.{Id}";
    public string Label { get; }
    public string Description { get; }
    public string ShortDescription { get; }
    public string HelpLabel { get; }
    public string ResetLabel { get; }
    public string OverrideLabel { get; }
    public string PortUnavailableLabel { get; }
    public bool IsCompositePort { get; }
    public bool CanEdit => true;
    public bool IsBoolean { get; }
    public bool IsChoice { get; }
    public bool IsText { get; }
    public IReadOnlyList<string> Choices { get; }
    public ICommand ResetCommand { get; }
    public bool HasOverride => draft.TryGet(Id, out _);
    public bool IsOverrideSelected
    {
        get => HasOverride;
        set
        {
            if (value == HasOverride)
            {
                return;
            }

            if (!value)
            {
                draft.Remove(Id);
                LoadValue();
                onChanged();
                return;
            }

            if (IsBoolean)
            {
                draft.SetBoolean(Id, BooleanValue);
            }
            else
            {
                draft.SetText(Id, IsChoice ? ChoiceValue ?? string.Empty : TextValue);
            }

            DraftChanged();
        }
    }
    public string OriginLabel => text.Get(HasOverride ? "settings.override" : "settings.default");
    public string DefaultLabel => Descriptor.DefaultValue is string value
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, text.Get("settings.defaultValue"), value)
        : text.Get("settings.defaultUnspecified");
    public string? ValidationMessage
    {
        get => validationMessage;
        private set
        {
            SetProperty(ref validationMessage, value);
            OnPropertyChanged(nameof(HasValidation));
        }
    }

    public bool HasValidation => ValidationMessage is not null;

    public string TextValue
    {
        get => textValue;
        set
        {
            value ??= string.Empty;

            if (!SetProperty(ref textValue, value))
            {
                return;
            }

            draft.SetText(Id, value);
            DraftChanged();
        }
    }

    public string? ChoiceValue
    {
        get => choiceValue;
        set
        {
            if (!SetProperty(ref choiceValue, value) || value is null)
            {
                return;
            }

            draft.SetText(Id, value);
            DraftChanged();
        }
    }

    public bool BooleanValue
    {
        get => booleanValue;
        set
        {
            if (!SetProperty(ref booleanValue, value))
            {
                return;
            }

            draft.SetBoolean(Id, value);
            DraftChanged();
        }
    }

    /// <summary>Displays a semantic Core diagnostic through a Desktop-owned message.</summary>
    public void SetDiagnostic(OptionDiagnostic? diagnostic)
    {
        if (diagnostic?.Code == OptionDiagnosticCode.InvalidValue &&
            Descriptor.Minimum is decimal minimum && Descriptor.Maximum is decimal maximum)
        {
            string rangeKey = Descriptor.ArgumentHint is null ? "settings.validation.range" : "settings.validation.rangeUnit";
            ValidationMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                text.Get(rangeKey), minimum, maximum, Descriptor.ArgumentHint);
            return;
        }

        ValidationMessage = diagnostic is null ? null : text.Get(diagnostic.Code switch
        {
            OptionDiagnosticCode.InvalidValue => "settings.validation.invalid",
            OptionDiagnosticCode.InvalidValueType => "settings.validation.type",
            OptionDiagnosticCode.MissingArgument => "settings.validation.required",
            OptionDiagnosticCode.MissingRequirement => "settings.validation.requirement",
            OptionDiagnosticCode.Conflict => "settings.validation.conflict",
            OptionDiagnosticCode.RuleViolation => "settings.validation.rule",
            _ => "settings.validation.unsupported",
        });
    }

    /// <summary>Refreshes the editor after an in-memory reset without generating a new override.</summary>
    public void LoadValue()
    {
        JsonElement value = draft.TryGet(Id, out JsonElement stored) ? stored : default;
        textValue = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        choiceValue = IsChoice && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        booleanValue = value.ValueKind == JsonValueKind.True;
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(ChoiceValue));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(HasOverride));
        OnPropertyChanged(nameof(IsOverrideSelected));
        OnPropertyChanged(nameof(OriginLabel));
    }

    /// <summary>Removes only this option's draft override.</summary>
    private void Reset()
    {
        draft.Remove(Id);
        LoadValue();
        onChanged();
    }

    /// <summary>Notifies parent validation and origin presentation after an edit.</summary>
    private void DraftChanged()
    {
        OnPropertyChanged(nameof(HasOverride));
        OnPropertyChanged(nameof(IsOverrideSelected));
        OnPropertyChanged(nameof(OriginLabel));
        onChanged();
    }
}

/// <summary>Filters the generated catalogue and validates an isolated global-settings draft.</summary>
public sealed class SettingsViewModel : ObservableViewModel
{
    private readonly InMemoryOptionDraft draft;
    private readonly PresentationText text;
    private readonly IReadOnlyList<OptionRowViewModel> allRows;
    private string searchText = string.Empty;
    private string shortcutHelp;
    private SettingsCategory? selectedCategory;
    private SettingsSection selectedSection = SettingsSection.Mirroring;

    public SettingsViewModel(PresentationText text, InMemoryOptionDraft draft, bool isPreview = true)
    {
        this.draft = draft;
        this.text = text;
        IsPreview = isPreview;
        Eyebrow = text.Get("settings.eyebrow");
        Title = text.Get(isPreview ? "settings.title" : "settings.title.normal");
        Subtitle = text.Get(isPreview ? "settings.subtitle" : "settings.subtitle.normal");
        GlobalLabel = text.Get("settings.global");
        SearchLabel = text.Get("settings.search");
        SearchWatermark = text.Get("settings.searchWatermark");
        shortcutHelp = $"{SettingsShortcuts.FocusSearch}: {text.Get(SettingsShortcuts.FocusSearchHelpKey)}";
        CategoriesLabel = text.Get("settings.categories");
        EmptyLabel = text.Get("settings.noResults");
        UnsavedLabel = text.Get(isPreview ? "settings.unsaved" : "settings.unsaved.normal");
        ResetLabel = text.Get("settings.reset");
        ResetRowLabel = text.Get("settings.row.reset");
        PortUnavailableLabel = text.Get("settings.portUnavailable");
        Categories = Array.AsReadOnly(new[] { new SettingsCategory("all", text.Get("settings.all")) }
            .Concat(GeneratedOptionCatalog.All
                .Where(option => option.Classification == OptionClassification.Editable)
                .Select(option => option.Category)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(category => category, StringComparer.Ordinal)
                .Select(category => new SettingsCategory(category, text.Get($"settings.category.{category}"))))
            .ToArray());
        allRows = GeneratedOptionCatalog.All
            .Where(option => option.Classification == OptionClassification.Editable)
            .Select(option => new OptionRowViewModel(option, draft, text, Validate))
            .ToArray();
        VisibleRows = [];
        ResetDraftCommand = new ActionCommand(ResetDraft);
        ShowMirroringCommand = new ActionCommand(() => SelectedSection = SettingsSection.Mirroring);
        ShowDesktopCommand = new ActionCommand(() => SelectedSection = SettingsSection.Desktop);
        MirroringSectionLabel = text.Get("settings.section.mirroring");
        DesktopSectionLabel = text.Get("settings.section.desktop");
        ConfigurationApplyLabel = text.Get("settings.configuration.apply");
        ConfigurationCancelLabel = text.Get("settings.configuration.cancel");
        ConfigurationReloadLabel = text.Get("settings.configuration.reload");
        MigrationPrepareLabel = text.Get("settings.migration.prepare");
        MigrationCommitLabel = text.Get("settings.migration.commit");
        MigrationCancelLabel = text.Get("settings.migration.cancel");
        SelectedCategory = Categories[0];
        Validate();
    }

    public string Eyebrow { get; }
    public bool IsPreview { get; }
    public bool IsPersistent => !IsPreview;
    public ConfigurationWorkspaceViewModel? Configuration { get; private set; }
    public DesktopPreferencesViewModel? Preferences { get; private set; }
    public string? ConfigurationPath { get; private set; }
    public string MirroringSectionLabel { get; }
    public string DesktopSectionLabel { get; }
    public string ConfigurationApplyLabel { get; }
    public string ConfigurationCancelLabel { get; }
    public string ConfigurationReloadLabel { get; }
    public string MigrationPrepareLabel { get; }
    public string MigrationCommitLabel { get; }
    public string MigrationCancelLabel { get; }
    public ICommand ShowMirroringCommand { get; }
    public ICommand ShowDesktopCommand { get; }
    public SettingsSection SelectedSection
    {
        get => selectedSection;
        private set
        {
            if (!SetProperty(ref selectedSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsMirroringSection));
            OnPropertyChanged(nameof(IsDesktopSection));
        }
    }
    public bool IsMirroringSection => SelectedSection == SettingsSection.Mirroring;
    public bool IsDesktopSection => SelectedSection == SettingsSection.Desktop;
    public bool IsOptionEditorAvailable => IsPreview || Configuration?.CanEdit == true;
    public string ConfigurationStatusMessage
    {
        get
        {
            if (Configuration is null)
            {
                return string.Empty;
            }

            string? migrationKey = Configuration.LastMigrationResult?.Status switch
            {
                Infrastructure.Configuration.LegacyMigrationStatus.NoLegacyData => "settings.migration.noLegacy",
                Infrastructure.Configuration.LegacyMigrationStatus.AlreadyV2 => "settings.migration.alreadyV2",
                Infrastructure.Configuration.LegacyMigrationStatus.InvalidV2 => "settings.migration.invalidV2",
                Infrastructure.Configuration.LegacyMigrationStatus.InvalidLegacy => "settings.migration.invalidLegacy",
                Infrastructure.Configuration.LegacyMigrationStatus.LegacyChanged => "settings.migration.legacyChanged",
                Infrastructure.Configuration.LegacyMigrationStatus.Busy => "settings.migration.busy",
                Infrastructure.Configuration.LegacyMigrationStatus.RevisionConflict => "settings.migration.conflict",
                _ => Configuration.LastMigrationResult?.ErrorKind is not null
                    ? "settings.migration.failure"
                    : null,
            };

            if (migrationKey is not null)
            {
                return $"{text.Get(migrationKey)} {ConfigurationPath}";
            }

            string key = Configuration.LastApplyResult?.Status switch
            {
                Infrastructure.Configuration.ConfigurationSessionApplyStatus.RevisionConflict => "settings.configuration.conflict",
                Infrastructure.Configuration.ConfigurationSessionApplyStatus.Invalid => "settings.configuration.invalidDraft",
                Infrastructure.Configuration.ConfigurationSessionApplyStatus.IoError => "settings.configuration.writeFailure",
                Infrastructure.Configuration.ConfigurationSessionApplyStatus.Busy => "settings.configuration.busy",
                Infrastructure.Configuration.ConfigurationSessionApplyStatus.Applied => "settings.configuration.saved",
                _ => Configuration.Status switch
                {
                    ConfigurationWorkspaceStatus.Loading => "settings.configuration.loading",
                    ConfigurationWorkspaceStatus.Missing => "settings.configuration.missing",
                    ConfigurationWorkspaceStatus.Ready => "settings.configuration.ready",
                    ConfigurationWorkspaceStatus.Invalid => "settings.configuration.invalidFile",
                    ConfigurationWorkspaceStatus.Inaccessible => "settings.configuration.readFailure",
                    _ => "settings.configuration.loading",
                },
            };
            return $"{text.Get(key)} {ConfigurationPath}";
        }
    }
    public string MigrationSummaryMessage => Configuration?.MigrationSummary is { } summary
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, text.Get("settings.migration.summary"),
            summary.ProfileCount, summary.OptionOverrideCount)
        : string.Empty;
    public string Title { get; }
    public string Subtitle { get; }
    public string GlobalLabel { get; }
    public string SearchLabel { get; }
    public string SearchWatermark { get; }
    public string ShortcutHelp { get => shortcutHelp; private set => SetProperty(ref shortcutHelp, value); }
    public string CategoriesLabel { get; }
    public string EmptyLabel { get; }
    public string UnsavedLabel { get; }
    public string ResetLabel { get; }
    public string ResetRowLabel { get; }
    public string PortUnavailableLabel { get; }
    public IReadOnlyList<SettingsCategory> Categories { get; }
    public ObservableCollection<OptionRowViewModel> VisibleRows { get; }
    public ICommand ResetDraftCommand { get; }
    public int VisibleCount => VisibleRows.Count;
    public bool HasNoResults => VisibleCount == 0;
    public IReadOnlyDictionary<string, JsonElement> DraftValues => draft.Values;
    public event Action<SettingsResultsChange>? ResultsChanged;

    /// <summary>Attaches real save groups only after the normal composition selected an explicit data root.</summary>
    public void AttachPersistence(ConfigurationWorkspaceViewModel configuration,
        DesktopPreferencesViewModel preferences, string configurationPath)
    {
        Configuration = configuration;
        Preferences = preferences;
        ConfigurationPath = configurationPath;
        configuration.DraftReplaced += () => RefreshValues(draft.Values);
        configuration.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ConfigurationStatusMessage));
            OnPropertyChanged(nameof(MigrationSummaryMessage));
            OnPropertyChanged(nameof(IsOptionEditorAvailable));
        };
        OnPropertyChanged(nameof(Configuration));
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(ConfigurationStatusMessage));
        OnPropertyChanged(nameof(IsOptionEditorAvailable));
    }

    /// <summary>Keeps visible help aligned with the last committed local command binding.</summary>
    public void SetActiveShortcutHelp(string gesture)
    {
        ShortcutHelp = gesture.Length == 0
            ? text.Get("settings.shortcut.unbound")
            : $"{gesture}: {text.Get(SettingsShortcuts.FocusSearchHelpKey)}";
    }

    /// <summary>Loads a detached persisted snapshot without changing the active search or category.</summary>
    public void RefreshValues(IReadOnlyDictionary<string, JsonElement> values)
    {
        draft.Replace(values);

        foreach (OptionRowViewModel row in allRows)
        {
            row.LoadValue();
        }

        Validate();
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                Filter();
                ResultsChanged?.Invoke(SettingsResultsChange.Search);
            }
        }
    }

    public SettingsCategory? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (SetProperty(ref selectedCategory, value))
            {
                Filter();
                ResultsChanged?.Invoke(SettingsResultsChange.Category);
            }
        }
    }

    /// <summary>Validates the entire detached draft using Core rules.</summary>
    private void Validate()
    {
        OptionSelectionResult result = draft.Validate();

        foreach (OptionRowViewModel row in allRows)
        {
            row.SetDiagnostic(result.Diagnostics.FirstOrDefault(issue => issue.OptionId == row.Id));
        }
    }

    /// <summary>Filters localized labels and descriptions without changing draft values.</summary>
    private void Filter()
    {
        string query = SearchText.Trim();
        string? category = SelectedCategory?.Id;
        IEnumerable<OptionRowViewModel> matches = allRows.Where(row =>
            (category is null or "all" || row.Descriptor.Category == category) &&
            (query.Length == 0 || row.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             row.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
        VisibleRows.Clear();

        foreach (OptionRowViewModel row in matches)
        {
            VisibleRows.Add(row);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>Discards all preview edits and restores the unmodified catalogue view.</summary>
    private void ResetDraft()
    {
        draft.Reset();

        foreach (OptionRowViewModel row in allRows)
        {
            row.LoadValue();
        }

        Validate();
    }
}
