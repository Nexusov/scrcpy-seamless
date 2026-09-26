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

    // This is display data, not a binding or execution registry. See docs/development/desktop-shortcuts.md.
    private static readonly GroupSpec[] Source =
    [
        new("navigation",
        [
            new("home", "MOD+H", "device"),
            new("back", "MOD+B · MOD+Backspace", "device"),
            new("recent", "MOD+S", "device"),
            new("menu", "MOD+M", "device"),
            new("notifications", "MOD+N", "device"),
            new("quickSettings", "Hold MOD; press and release N twice", "device"),
            new("collapsePanels", "MOD+Shift+N", "device"),
        ]),
        new("window",
        [
            new("quitMirror", "MOD+Q"),
            new("fullscreen", "MOD+F · F11"),
            new("rotateLeft", "MOD+Left", "video"),
            new("rotateRight", "MOD+Right", "video"),
            new("flipHorizontal", "MOD+Shift+Left · MOD+Shift+Right", "video"),
            new("flipVertical", "MOD+Shift+Up · MOD+Shift+Down", "video"),
            new("pause", "MOD+Z", "video"),
            new("unpause", "MOD+Shift+Z", "video"),
            new("resetVideo", "MOD+Shift+R", "device"),
            new("pixelPerfect", "MOD+G", "video"),
            new("removeBorders", "MOD+W · double-click black border", "video"),
            new("fps", "MOD+I", "stdout"),
        ]),
        new("deviceScreen",
        [
            new("rotateDevice", "MOD+R", "device"),
            new("powerKey", "MOD+P", "device"),
            new("screenOff", "MOD+O", "device"),
            new("screenOn", "MOD+Shift+O", "device"),
            new("volumeUp", "MOD+Up", "deviceNonCamera"),
            new("volumeDown", "MOD+Down", "deviceNonCamera"),
        ]),
        new("clipboard",
        [
            new("copy", "MOD+C", "android7"),
            new("cut", "MOD+X", "android7"),
            new("paste", "MOD+V", "paste"),
            new("pasteText", "MOD+Shift+V", "device"),
        ]),
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
        new("special",
        [
            new("keyboardSettings", "MOD+K", "hid"),
            new("torchOn", "MOD+T", "camera"),
            new("torchOff", "MOD+Shift+T", "camera"),
            new("cameraZoomIn", "MOD+Up", "camera"),
            new("cameraZoomOut", "MOD+Down", "camera"),
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
        Groups = Array.AsReadOnly(Source.Select(group =>
            new NativeShortcutGroup(text.Get($"shortcuts.group.{group.Title}"),
                Array.AsReadOnly(group.Rows.Select(row =>
                    new NativeShortcutRow(text.Get($"shortcuts.action.{row.Action}"), row.Gesture,
                        row.Condition is null ? string.Empty : text.Get($"shortcuts.condition.{row.Condition}")))
                    .ToArray())))
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
