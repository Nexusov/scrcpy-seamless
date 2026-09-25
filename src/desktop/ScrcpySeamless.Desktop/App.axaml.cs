using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ScrcpySeamless.Desktop;

/// <summary>Provides the Desktop lifetime and explicit launch composition.</summary>
public partial class App : Application
{
    /// <summary>Loads the application theme.</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Creates one shell from normal or explicitly requested preview sources.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            DesktopLaunchOptions options = DesktopLaunchOptions.Parse(desktopLifetime.Args ?? []);
            RequestedThemeVariant = options.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            desktopLifetime.MainWindow = new MainWindow(DesktopComposition.Create(options,
                dark => RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
