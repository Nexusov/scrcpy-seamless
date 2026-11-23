using System.Text.Json;
using System.Windows.Input;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Edits one invocation option in the existing detached configuration draft.</summary>
public sealed class NativeShortcutEditor : ObservableViewModel
{
    private readonly InMemoryOptionDraft draft;
    private readonly PresentationText text;
    private readonly Action changed;
    private bool loading;
    private bool canEdit;
    private string validationMessage = string.Empty;

    public NativeShortcutEditor(InMemoryOptionDraft draft, PresentationText text, Action changed)
    {
        this.draft = draft;
        this.text = text;
        this.changed = changed;
        Rows = Array.AsReadOnly(GeneratedNativeShortcutCatalog.Actions
            .Select(action => new NativeShortcutEditorRow(action, text, Save)).ToArray());
        ResetCommand = new ActionCommand(() =>
        {
            draft.Remove(NativeShortcutBindings.OptionId);
            Reload();
            changed();
        });
        Reload();
    }

    public IReadOnlyList<NativeShortcutEditorRow> Rows { get; }
    public ICommand ResetCommand { get; }
    public string Help => text.Get("shortcuts.editor.help");
    public string ResetLabel => text.Get("shortcuts.editor.resetAll");
    public string ValidationMessage { get => validationMessage; private set => SetProperty(ref validationMessage, value); }
    public bool HasValidation => ValidationMessage.Length != 0;
    public bool CanEdit { get => canEdit; private set => SetProperty(ref canEdit, value); }

    /// <summary>Reload never repairs or discards malformed persisted bindings implicitly.</summary>
    public void Reload()
    {
        loading = true;
        string raw = "{}";
        bool stringValue = !draft.TryGet(NativeShortcutBindings.OptionId, out JsonElement value)
            || value.ValueKind == JsonValueKind.String;

        if (value.ValueKind == JsonValueKind.String)
        {
            raw = value.GetString()!;
        }
        CanEdit = stringValue && NativeShortcutBindings.TryParse(raw, out _);
        SetValidation(CanEdit ? string.Empty : text.Get("shortcuts.editor.invalidStored"));
        Dictionary<string, string> overrides = CanEdit
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(raw)! : [];

        foreach (NativeShortcutEditorRow row in Rows)
        {
            row.Load(overrides.TryGetValue(row.Id, out string? gesture) ? gesture : null);
        }
        loading = false;
    }

    private void Save()
    {
        if (loading || !CanEdit)
        {
            return;
        }
        Dictionary<string, string?> overrides = new(StringComparer.Ordinal);

        foreach (NativeShortcutEditorRow row in Rows)
        {
            if (row.IsCustom || row.IsDisabled)
            {
                overrides.Add(row.Id, row.IsDisabled ? string.Empty : row.Gesture.Length == 0 ? null : row.Gesture);
            }
        }

        if (overrides.Count == 0)
        {
            draft.Remove(NativeShortcutBindings.OptionId);
            SetValidation(string.Empty);
            changed();
            return;
        }
        string raw = JsonSerializer.Serialize(overrides);
        bool valid = NativeShortcutBindings.TryParse(raw, out _, out IReadOnlyList<NativeShortcutBindingIssue> issues);

        if (!valid)
        {
            string actions = string.Join(", ", issues.Select(issue => issue.ActionId).Where(id => id.Length != 0).Distinct());
            SetValidation($"{text.Get("shortcuts.editor.invalid")} {actions}".Trim());
        }
        else
        {
            SetValidation(string.Empty);
        }
        // Invalid text stays in this detached draft so Apply's Core validation can reject it.
        draft.SetText(NativeShortcutBindings.OptionId, raw);
        changed();
    }

    private void SetValidation(string message)
    {
        ValidationMessage = message;
        OnPropertyChanged(nameof(HasValidation));
    }
}

/// <summary>Default retains aliases; a custom gesture replaces them; disabled emits no binding.</summary>
public sealed class NativeShortcutEditorRow : ObservableViewModel
{
    private readonly Action changed;
    private readonly string defaultMode;
    private readonly string customMode;
    private readonly string disabledMode;
    private string mode;
    private string gesture = string.Empty;

    public NativeShortcutEditorRow(NativeShortcutDefinition action, PresentationText text, Action changed)
    {
        this.changed = changed;
        Id = action.Id;
        Action = text.Get($"shortcuts.action.{action.Id}");
        Condition = action.Condition.Length == 0 ? string.Empty : text.Get($"shortcuts.condition.{action.Condition}");
        DefaultGesture = action.Tap == 2 ? text.Get("shortcuts.editor.doubleTap")
            : string.Join(" В· ", action.DefaultGestures);
        defaultMode = text.Get("shortcuts.editor.default");
        customMode = text.Get("shortcuts.editor.custom");
        disabledMode = text.Get("shortcuts.editor.disabled");
        Modes = Array.AsReadOnly(new[] { defaultMode, customMode, disabledMode });
        mode = defaultMode;
        ModeLabel = text.Get("shortcuts.editor.mode");
        GestureLabel = text.Get("shortcuts.editor.gesture");
    }

    public string Id { get; }
    public string Action { get; }
    public string Condition { get; }
    public string DefaultGesture { get; }
    public IReadOnlyList<string> Modes { get; }
    public string ModeLabel { get; }
    public string GestureLabel { get; }
    public string ModeAutomationId => $"shortcut.{Id}.mode";
    public string GestureAutomationId => $"shortcut.{Id}.gesture";
    public string ModeAccessibleName => $"{Action}: {ModeLabel}";
    public string GestureAccessibleName => $"{Action}: {GestureLabel}";
    public bool IsCustom => Mode == customMode;
    public bool IsDisabled => Mode == disabledMode;
    public string Mode
    {
        get => mode;
        set
        {
            if (!Modes.Contains(value, StringComparer.Ordinal) || !SetProperty(ref mode, value))
            {
                return;
            }
            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsDisabled));
            changed();
        }
    }
    public string Gesture
    {
        get => gesture;
        set
        {
            if (SetProperty(ref gesture, value ?? string.Empty))
            {
                changed();
            }
        }
    }

    internal void Load(string? value)
    {
        Gesture = value ?? string.Empty;
        Mode = value is null ? defaultMode : value.Length == 0 ? disabledMode : customMode;
    }
}
