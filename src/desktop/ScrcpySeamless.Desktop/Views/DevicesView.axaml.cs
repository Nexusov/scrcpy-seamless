using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Displays availability and session evidence from one presentation source.</summary>
public partial class DevicesView : UserControl
{
    /// <summary>Loads the typed Devices workspace.</summary>
    public DevicesView() => AvaloniaXamlLoader.Load(this);
}
