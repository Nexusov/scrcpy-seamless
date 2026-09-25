using Avalonia;

namespace ScrcpySeamless.Desktop;

/// <summary>Starts the desktop control center or its explicit development preview.</summary>
internal static class Program
{
    /// <summary>Starts a classic desktop lifetime.</summary>
    [STAThread]
    public static void Main(string[] arguments)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);
    }

    /// <summary>Creates the platform-specific application builder.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect();
    }
}
