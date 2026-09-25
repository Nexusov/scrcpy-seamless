using Avalonia.Controls;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Renders one editable generated descriptor with Core validation feedback.</summary>
public partial class OptionRowView : UserControl
{
    /// <summary>Loads the reusable typed option row.</summary>
    public OptionRowView()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, _) => UpdateEditorLayout();
    }

    /// <summary>Keeps the editor inside the measured row at narrow widths.</summary>
    private void UpdateEditorLayout()
    {
        Grid row = this.FindControl<Grid>("RowGrid")!;
        StackPanel editor = this.FindControl<StackPanel>("OptionControls")!;
        double threshold = Application.Current?.Resources["EditorBelowWidth"] is double value ? value : 620;
        bool stacked = Bounds.Width < threshold;
        row.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("*,220");
        row.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        row.ColumnSpacing = stacked ? 0 : 16;
        row.RowSpacing = stacked ? 8 : 0;
        Grid.SetColumn(editor, stacked ? 0 : 1);
        Grid.SetRow(editor, stacked ? 1 : 0);
    }
}
