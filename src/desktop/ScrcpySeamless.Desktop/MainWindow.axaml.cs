using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop;

/// <summary>Shows only the Phase 2 build-foundation placeholder.</summary>
public partial class MainWindow : Window
{
    /// <summary>Loads the compiled XAML and placeholder model.</summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
    }

    /// <summary>Loads the window markup.</summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
