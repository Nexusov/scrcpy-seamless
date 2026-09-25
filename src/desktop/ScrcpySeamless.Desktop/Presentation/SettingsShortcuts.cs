using Avalonia.Input;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>One local gesture source for implemented Settings commands and help.</summary>
public static class SettingsShortcuts
{
    public static KeyGesture FocusSearch { get; } = new(Key.F, KeyModifiers.Control);
    public const string FocusSearchHelpKey = "settings.shortcut.focusSearch";
}
