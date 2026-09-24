using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ScrcpySeamless.Desktop;

/// <summary>Provides the minimal desktop application lifetime.</summary>
public partial class App : Application
{
    /// <summary>Loads the application theme.</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Creates the placeholder window for the desktop lifetime.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
