using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Activation;
using ScrcpySeamless.Infrastructure.Updates;
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
                        Text = "Start normally for portable mode, or select --preview, --installed, " +
                            "or one absolute --dev-data-dir=<path>. Do not combine storage modes.",
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
                StartNormalDesktop(desktopLifetime, options);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartNormalDesktop(IClassicDesktopStyleApplicationLifetime lifetime, DesktopLaunchOptions options)
    {
        var status = new TextBlock
        {
            Text = "Opening scrcpy Seamless…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
        };
        var loading = new Window { Title = "scrcpy Seamless", Width = 440, Height = 130, Content = status };
        lifetime.MainWindow = loading;
        loading.Opened += OpenNormalAsync;

        async void OpenNormalAsync(object? sender, EventArgs eventArgs)
        {
            loading.Opened -= OpenNormalAsync;
            using var startupCancellation = new CancellationTokenSource();
            bool closeRequested = false;

            void RequestClose(object? owner, WindowClosingEventArgs args)
            {
                args.Cancel = true;
                closeRequested = true;
                startupCancellation.Cancel();
            }

            loading.Closing += RequestClose;
            WindowsControlCenterActivation? activation = null;
            CancellationTokenSource? activationCancellation = null;
            Task? observation = null;
            GitHubReleaseChecker? releaseChecker = null;
            bool transferred = false;
            bool forwarded = false;

            try
            {
                CancellationToken token = startupCancellation.Token;
                activation = new WindowsControlCenterActivation(NormalDesktopFactory.ResolveDataPaths(options).Directory);
                ActivationDisposition disposition = await activation.AcquireOrForwardAsync(
                    new ActivationRequest(ControlCenterActivationKind.ShowControlCenter), token);
                forwarded = disposition == ActivationDisposition.ForwardedToPrimary;

                if (!forwarded)
                {
                    var runtime = await NormalDesktopFactory.ValidateRuntimeAsync(options, token);
                    token.ThrowIfCancellationRequested();
                    MainWindow? window = null;
                    GitHubReleaseChecker checker = new();
                    releaseChecker = checker;
                    NormalDesktopComposition composition = NormalDesktopFactory.Create(options, ApplyAppearance,
                        shortcuts => window?.ApplyShortcuts(shortcuts), ResolveEffectiveFont,
                        copySessionEvidence: async evidence =>
                        {
                            var clipboard = window?.Clipboard ??
                                throw new InvalidOperationException("Clipboard is unavailable.");
                            await clipboard.SetTextAsync(evidence);
                        },
                        validatedRuntime: runtime,
                        releaseChecker: checker,
                        openReleaseLink: async (uri, cancellationToken) =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (window is null || !await window.Launcher.LaunchUriAsync(uri))
                            {
                                throw new InvalidOperationException("The release link could not be opened.");
                            }
                        });
                    window = new MainWindow(composition.Shell);

                    try
                    {
                        await composition.InitializeAsync(token);
                    }
                    catch (Exception exception) when (!token.IsCancellationRequested)
                    {
                        window.ShowStartupFailure(exception.GetType().Name);
                    }

                    token.ThrowIfCancellationRequested();
                    var observerLifetime = new CancellationTokenSource();
                    activationCancellation = observerLifetime;
                    Task observer = ObserveActivationAsync(activation, window, composition, observerLifetime.Token);
                    observation = observer;
                    WindowsControlCenterActivation primary = activation;
                    window.AttachNormalComposition(composition, async () =>
                    {
                        using (checker)
                        {
                            try
                            {
                                if (composition.Releases is { } releases)
                                {
                                    await releases.CloseAsync();
                                }
                            }
                            finally
                            {
                                await SettleActivationAsync(primary, observerLifetime, observer);
                            }
                        }
                    });
                    lifetime.MainWindow = window;
                    window.Show();
                    transferred = true;
                }
            }
            catch (OperationCanceledException) when (startupCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                status.Text = $"Could not open scrcpy Seamless ({exception.GetType().Name}).";
            }
            finally
            {
                try
                {
                    if (!transferred && activation is not null)
                    {
                        releaseChecker?.Dispose();
                        await SettleActivationAsync(activation, activationCancellation, observation);
                    }
                }
                catch (Exception exception)
                {
                    status.Text = $"Could not release startup resources ({exception.GetType().Name}).";
                }
                finally
                {
                    loading.Closing -= RequestClose;
                }
            }

            if (transferred || forwarded || closeRequested)
            {
                loading.Close();
            }
        }
    }

    private static async Task SettleActivationAsync(WindowsControlCenterActivation activation,
        CancellationTokenSource? cancellation, Task? observation)
    {
        using (cancellation)
        {
            cancellation?.Cancel();

            try
            {
                if (observation is not null)
                {
                    await observation;
                }
            }
            finally
            {
                await activation.DisposeAsync();
            }
        }
    }

    /// <summary>Separates control-center activation from exact-session native focus requests.</summary>
    private static async Task ObserveActivationAsync(WindowsControlCenterActivation activation,
        MainWindow window, NormalDesktopComposition composition, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ActivationRequest request in activation.ObserveRequestsAsync(cancellationToken))
            {
                if (request.Kind == ControlCenterActivationKind.ShowControlCenter)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        window.WindowState = WindowState.Normal;
                        window.Show();
                        window.Activate();
                    });

                    continue;
                }

                if (request.Kind == ControlCenterActivationKind.FocusSession &&
                    request.SessionId is { } sessionId && composition.DeviceSession is { } deviceSession)
                {
                    try
                    {
                        Task<NativeFocusOutcome> focus = await Dispatcher.UIThread.InvokeAsync<Task<NativeFocusOutcome>>(
                            () => composition.Sessions?.FocusSessionAsync(sessionId, cancellationToken)
                                ?? deviceSession.FocusSessionAsync(sessionId, cancellationToken));
                        await focus;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError("Native focus activation failed: {0}",
                            exception.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Control-center activation observation failed: {0}",
                exception.GetType().Name);
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
