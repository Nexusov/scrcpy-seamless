using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>One documented native mirror gesture; it never executes an action.</summary>
public sealed record NativeShortcutRow(string Action, string Gesture, string Condition)
{
    public bool HasCondition => Condition.Length > 0;
}

/// <summary>A compact group of native gestures for the focused mirror window.</summary>
public sealed record NativeShortcutGroup(string Title, IReadOnlyList<NativeShortcutRow> Rows);

/// <summary>Presentation reference checked against the bundled scrcpy 4.0 input handler.</summary>
public sealed class NativeShortcutReference
{
    private sealed record ShortcutSpec(string Action, string Gesture, string? Condition = null);
    private sealed record GroupSpec(string Title, ShortcutSpec[] Rows);

    // Mouse gestures retain their independent native mouse-bind contract.
    private static readonly GroupSpec[] Source =
    [
        new("mouse",
        [
            new("mouseHome", "Middle-click", "defaultMouse"),
            new("mouseBack", "Right-click", "rightMouse"),
            new("mouseRecent", "Fourth mouse button", "defaultMouse"),
            new("mouseNotifications", "Fifth mouse button", "defaultMouse"),
            new("mouseSettings", "Double-click fifth mouse button", "defaultMouse"),
            new("pinch", "Ctrl+click and drag", "gesture"),
            new("tiltVertical", "Shift+click and drag", "gesture"),
            new("tiltHorizontal", "Ctrl+Shift+click and drag", "gesture"),
            new("installApk", "Drop an APK file", "fileDrop"),
            new("pushFile", "Drop a non-APK file", "fileDrop"),
        ]),
    ];

    /// <summary>Resolves native reference copy from Desktop-owned resources without starting adapters.</summary>
    public NativeShortcutReference(PresentationText text)
    {
        Title = text.Get("shortcuts.title");
        Subtitle = text.Get("shortcuts.subtitle");
        ControlCenterTitle = text.Get("shortcuts.controlCenter.title");
        ControlCenterDescription = text.Get("shortcuts.controlCenter.description");
        EditControlCenterLabel = text.Get("shortcuts.controlCenter.edit");
        MirrorTitle = text.Get("shortcuts.mirror.title");
        MirrorDescription = text.Get("shortcuts.mirror.description");
        ModifierLegend = text.Get("shortcuts.modifier.legend");
        ModifierOptionLabel = text.Get("shortcuts.modifier.option");
        ReadOnlyNote = text.Get("shortcuts.readOnly");
        IEnumerable<NativeShortcutGroup> keyboard = GeneratedNativeShortcutCatalog.Actions.GroupBy(action => action.Group)
            .Select(group => new NativeShortcutGroup(text.Get($"shortcuts.group.{group.Key}"),
                Array.AsReadOnly(group.Select(action => new NativeShortcutRow(text.Get($"shortcuts.action.{action.Id}"),
                    action.Tap == 2 ? text.Get("shortcuts.editor.doubleTap") : string.Join(" · ", action.DefaultGestures),
                    action.Condition.Length == 0 ? string.Empty : text.Get($"shortcuts.condition.{action.Condition}"))).ToArray())));
        Groups = Array.AsReadOnly(keyboard.Concat(Source.Select(group =>
            new NativeShortcutGroup(text.Get($"shortcuts.group.{group.Title}"),
                Array.AsReadOnly(group.Rows.Select(row =>
                    new NativeShortcutRow(text.Get($"shortcuts.action.{row.Action}"), row.Gesture,
                        row.Condition is null ? string.Empty : text.Get($"shortcuts.condition.{row.Condition}")))
                    .ToArray()))))
            .ToArray());
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string ControlCenterTitle { get; }
    public string ControlCenterDescription { get; }
    public string EditControlCenterLabel { get; }
    public string MirrorTitle { get; }
    public string MirrorDescription { get; }
    public string ModifierLegend { get; }
    public string ModifierOptionLabel { get; }
    public string ReadOnlyNote { get; }
    public IReadOnlyList<NativeShortcutGroup> Groups { get; }
}
