using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Activation;
using ControlCenterActivationKind = ScrcpySeamless.Core.Application.Activation.ActivationKind;

namespace ScrcpySeamless.Desktop;

/// <summary>Provides the Desktop lifetime and explicit launch composition.</summary>
public partial class App : Application
{
    /// <summary>Loads the application theme.</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Resources["UiFontFamily"] = FontManager.Current.DefaultFontFamily;
    }

    /// <summary>Creates one shell from normal or explicitly requested preview sources.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            DesktopLaunchOptions options;

            try
            {
                options = DesktopLaunchOptions.Parse(desktopLifetime.Args ?? []);
            }
            catch (ArgumentException)
            {
                desktopLifetime.MainWindow = new Window
                {
                    Title = "scrcpy Seamless — launch options",
                    Width = 600,
                    Height = 180,
                    Content = new TextBlock
                    {
                        Text = "Select --preview, --portable, --installed, or one absolute --dev-data-dir=<path>. " +
                            "Do not combine storage modes.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(24),
                    },
                };
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (options.Preview)
            {
                ApplyMetricScale(options.UiScale);
                RequestedThemeVariant = ResolveTheme(options.Theme);
                desktopLifetime.MainWindow = new MainWindow(DesktopComposition.Create(options,
                    theme => RequestedThemeVariant = ResolveTheme(theme)));
            }
            else
            {
                WindowsControlCenterActivation activation = new(
                    NormalDesktopFactory.ResolveDataPaths(options).Directory);
                ActivationDisposition disposition;

                try
                {
                    disposition = activation.AcquireOrForwardAsync(
                        new ActivationRequest(ControlCenterActivationKind.ShowControlCenter), CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    activation.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw;
                }

                if (disposition == ActivationDisposition.ForwardedToPrimary)
                {
                    activation.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    Dispatcher.UIThread.Post(() => desktopLifetime.Shutdown());
                    base.OnFrameworkInitializationCompleted();
                    return;
                }

                MainWindow? normalWindow = null;
                NormalDesktopComposition composition = NormalDesktopFactory.Create(options,
                    ApplyAppearance,
                    shortcuts => normalWindow?.ApplyShortcuts(shortcuts),
                    ResolveEffectiveFont);
                normalWindow = new MainWindow(composition.Shell);
                normalWindow.AttachNormalComposition(composition);
                desktopLifetime.MainWindow = normalWindow;
                CancellationTokenSource activationCancellation = new();
                _ = ObserveActivationAsync(activation, normalWindow, activationCancellation.Token);
                desktopLifetime.Exit += (_, _) =>
                {
                    activationCancellation.Cancel();
                    activation.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    activationCancellation.Dispose();
                };
                normalWindow.Opened += async (_, _) =>
                {
                    try
                    {
                        await composition.InitializeAsync(CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        normalWindow.ShowStartupFailure(exception.GetType().Name);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows only this scope's window when its same-user owner receives activation.</summary>
    private static async Task ObserveActivationAsync(WindowsControlCenterActivation activation,
        MainWindow window, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ActivationRequest request in activation.ObserveRequestsAsync(cancellationToken))
            {
                if (request.Kind == ControlCenterActivationKind.ShowControlCenter)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        window.WindowState = WindowState.Normal;
                        window.Show();
                        window.Activate();
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Maps the local choice onto Avalonia's inherited system theme.</summary>
    public static ThemeVariant ResolveTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Renders one requested appearance from immutable theme and metric bases.</summary>
    public void ApplyAppearance(DesktopAppearancePreferences appearance)
    {
        RequestedThemeVariant = appearance.Theme switch
        {
            DesktopTheme.Light => ThemeVariant.Light,
            DesktopTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        ApplyMetricScale(appearance.ScalePercent / 100d);
        string? installedFont = ResolveEffectiveFont(appearance.FontFamily);
        Resources["UiFontFamily"] = installedFont is null
            ? FontManager.Current.DefaultFontFamily
            : new FontFamily(installedFont);
        ApplyAccent(appearance.AccentColor);
    }

    /// <summary>Uses a locally installed font or leaves the saved request intact with a safe fallback.</summary>
    public static string? ResolveEffectiveFont(string? requested)
    {
        return requested is not null && FontManager.Current.SystemFonts.Any(font =>
            string.Equals(font.Name, requested, StringComparison.OrdinalIgnoreCase))
            ? requested
            : null;
    }

    /// <summary>Adjusts a custom accent separately for light and dark semantic surfaces.</summary>
    private void ApplyAccent(string? requested)
    {
        if (Resources.ThemeDictionaries.TryGetValue(ThemeVariant.Light, out var light) &&
            Resources.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out var dark) &&
            light is ResourceDictionary lightResources && dark is ResourceDictionary darkResources)
        {
            Color lightColor = requested is null ? Color.Parse("#1459B5") :
                EnsureContrast(Color.Parse(requested), Color.Parse("#F5F7FA"), false);
            Color darkColor = requested is null ? Color.Parse("#A9C7FA") :
                EnsureContrast(Color.Parse(requested), Color.Parse("#171819"), true);
            lightResources["AccentBrush"] = new SolidColorBrush(lightColor);
            darkResources["AccentBrush"] = new SolidColorBrush(darkColor);
        }
    }

    /// <summary>Moves a custom accent toward a legible foreground for one fixed standard surface.</summary>
    private static Color EnsureContrast(Color color, Color background, bool lighten)
    {
        const double minimumContrast = 4.5;
        const double step = 0.05;
        Color target = lighten ? Colors.White : Colors.Black;
        Color candidate = color;

        for (double blend = 0; Contrast(candidate, background) < minimumContrast && blend <= 1; blend += step)
        {
            candidate = Color.FromRgb(
                (byte)Math.Round(color.R * (1 - blend) + target.R * blend),
                (byte)Math.Round(color.G * (1 - blend) + target.G * blend),
                (byte)Math.Round(color.B * (1 - blend) + target.B * blend));
        }

        return candidate;
    }

    /// <summary>Computes foreground/background contrast for the small supported accent palette.</summary>
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color value)
        {
            static double Channel(byte component)
            {
                double linear = component / 255d;
                return linear <= 0.04045 ? linear / 12.92 : Math.Pow((linear + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(value.R) + 0.7152 * Channel(value.G) + 0.0722 * Channel(value.B);
        }

        double firstLuminance = Luminance(first);
        double secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

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
        Resources["SpaceSection"] = new Thickness(32 * scale);
        Resources["IconSize"] = 18 * scale;
    }
}
