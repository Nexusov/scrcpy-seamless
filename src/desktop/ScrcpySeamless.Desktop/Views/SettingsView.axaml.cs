using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Shows a searchable, categorized in-memory draft of generated settings.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Loads the typed Settings preview.</summary>
    public SettingsView() => AvaloniaXamlLoader.Load(this);
}
