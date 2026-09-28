using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Displays offline, read-only native shortcuts and links to existing settings.</summary>
public partial class ShortcutsReferenceView : UserControl
{
    /// <summary>Loads the scrollable reference without constructing runtime adapters.</summary>
    public ShortcutsReferenceView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
