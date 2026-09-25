using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>A localized category entry backed by an invariant generated category key.</summary>
public sealed record SettingsCategory(string Id, string Label);

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
        ResetLabel = text.Get("settings.row.reset");
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
    public string ResetLabel { get; }
    public string PortUnavailableLabel { get; }
    public bool IsCompositePort { get; }
    public bool CanEdit => !IsCompositePort;
    public bool IsBoolean { get; }
    public bool IsChoice { get; }
    public bool IsText { get; }
    public IReadOnlyList<string> Choices { get; }
    public ICommand ResetCommand { get; }
    public bool HasOverride => draft.TryGet(Id, out _);
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
        OnPropertyChanged(nameof(OriginLabel));
        onChanged();
    }
}

/// <summary>Filters the generated catalogue and validates an isolated global-settings draft.</summary>
public sealed class SettingsViewModel : ObservableViewModel
{
    private readonly InMemoryOptionDraft draft;
    private readonly IReadOnlyList<OptionRowViewModel> allRows;
    private string searchText = string.Empty;
    private SettingsCategory? selectedCategory;

    public SettingsViewModel(PresentationText text, InMemoryOptionDraft draft)
    {
        this.draft = draft;
        Eyebrow = text.Get("settings.eyebrow");
        Title = text.Get("settings.title");
        Subtitle = text.Get("settings.subtitle");
        GlobalLabel = text.Get("settings.global");
        SearchLabel = text.Get("settings.search");
        SearchWatermark = text.Get("settings.searchWatermark");
        CategoriesLabel = text.Get("settings.categories");
        EmptyLabel = text.Get("settings.noResults");
        UnsavedLabel = text.Get("settings.unsaved");
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
        SelectedCategory = Categories[0];
        Validate();
    }

    public string Eyebrow { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string GlobalLabel { get; }
    public string SearchLabel { get; }
    public string SearchWatermark { get; }
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

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                Filter();
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
