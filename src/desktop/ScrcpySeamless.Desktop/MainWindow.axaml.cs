using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop;

/// <summary>Hosts the typed Desktop navigation shell.</summary>
public partial class MainWindow : Window
{
    /// <summary>Provides a truthful empty design-time shell.</summary>
    public MainWindow() : this(DesktopComposition.Create(new DesktopLaunchOptions(false, false, null, false), _ => { }))
    {
    }

    /// <summary>Loads the same product shell for normal and preview composition.</summary>
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Loads the window markup.</summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
