using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Renders one editable generated descriptor with Core validation feedback.</summary>
public partial class OptionRowView : UserControl
{
    /// <summary>Loads the reusable typed option row.</summary>
    public OptionRowView() => AvaloniaXamlLoader.Load(this);
}
