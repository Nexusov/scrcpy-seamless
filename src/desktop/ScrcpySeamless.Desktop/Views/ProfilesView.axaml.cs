using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using ScrcpySeamless.Desktop.Presentation;

namespace ScrcpySeamless.Desktop.Views;

/// <summary>Displays the detached saved-profile editor and explicit draft actions.</summary>
public partial class ProfilesView : UserControl
{
    private const double CompactFieldsWidth = 640;

    /// <summary>Loads the typed Profiles workspace.</summary>
    public ProfilesView()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, _) => UpdateFieldLayout();
    }

    /// <summary>Applies the shared configuration draft after staging valid profile fields.</summary>
    private async void OnApplyClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is ProfilesViewModel profiles)
        {
            await profiles.ApplyAsync(CancellationToken.None);
        }
    }

    /// <summary>Stacks editable fields at compact widths while preserving scroll reachability.</summary>
    private void UpdateFieldLayout()
    {
        Grid fields = this.FindControl<Grid>("ProfileFields")!;
        bool compact = Bounds.Width < CompactFieldsWidth;
        fields.ColumnDefinitions = compact ? new ColumnDefinitions("*") : new ColumnDefinitions("*,*");
        fields.RowDefinitions = compact
            ? new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto")
            : new RowDefinitions("Auto,Auto,Auto,Auto");
        fields.ColumnSpacing = compact ? 0 : 12;

        SetPosition("AliasField", 0, 0);
        SetPosition("UsbField", compact ? 1 : 0, compact ? 0 : 1);
        SetPosition("MdnsField", compact ? 2 : 1, 0);
        SetPosition("PairingField", compact ? 3 : 1, compact ? 0 : 1);
        SetPosition("ConnectionField", compact ? 4 : 2, 0);
        SetPosition("TransportField", compact ? 5 : 2, compact ? 0 : 1);
        SetPosition("FallbackField", compact ? 6 : 3, 0);
        Grid.SetColumnSpan(this.FindControl<CheckBox>("FallbackField")!, compact ? 1 : 2);
    }

    /// <summary>Moves one editor field without creating another binding or view.</summary>
    private void SetPosition(string name, int row, int column)
    {
        Control field = this.FindControl<Control>(name)!;
        Grid.SetRow(field, row);
        Grid.SetColumn(field, column);
    }
}
