using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Shows a device's transport and channel facts without conflating them.</summary>
public partial class DeviceCardView : UserControl
{
    /// <summary>Loads the reusable typed device card.</summary>
    public DeviceCardView() => AvaloniaXamlLoader.Load(this);
}
