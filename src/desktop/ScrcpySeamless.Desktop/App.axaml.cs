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
            ApplyMetricScale(options.UiScale);
            RequestedThemeVariant = ResolveTheme(options.Theme);
            desktopLifetime.MainWindow = new MainWindow(DesktopComposition.Create(options,
                theme => RequestedThemeVariant = ResolveTheme(theme)));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Maps the local choice onto Avalonia's inherited system theme.</summary>
    public static ThemeVariant ResolveTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Applies preview-only layout metrics before constructing the window.</summary>
    public void ApplyMetricScale(double scale)
    {
        Dictionary<string, double> metricBases = new()
        {
            ["BodyFontSize"] = 13,
            ["SmallFontSize"] = 11,
            ["TitleFontSize"] = 28,
            ["SectionFontSize"] = 16,
            ["SectionGap"] = 14,
            ["EditorBelowWidth"] = 620,
        };

        foreach ((string key, double value) in metricBases)
        {
            Resources[key] = value * scale;
        }

        Resources["PageInset"] = new Thickness(24 * scale, 16 * scale);
        Resources["SpaceLarge"] = new Thickness(24 * scale);
        Resources["SpaceMedium"] = new Thickness(16 * scale);
        Resources["SpaceSmall"] = new Thickness(8 * scale);
        Resources["ControlPadding"] = new Thickness(10 * scale, 7 * scale);
        Resources["NavPadding"] = new Thickness(14 * scale, 11 * scale);
    }
}
