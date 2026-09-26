using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Shows explicit mirror controls and only observed process evidence.</summary>
public partial class DeviceSessionView : UserControl
{
    /// <summary>Loads the typed session view.</summary>
    public DeviceSessionView() => AvaloniaXamlLoader.Load(this);
}
