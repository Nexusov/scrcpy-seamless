namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>Selects where application-owned configuration is stored.</summary>
public enum ApplicationDataMode
{
    Portable,
    Installed,
}

/// <summary>Paths for a single application installation, without accessing the filesystem.</summary>
public sealed record ApplicationDataPaths(string Directory, string ConfigurationFile)
{
    /// <summary>Resolves portable or per-user installed storage from explicit roots.</summary>
    public static ApplicationDataPaths Resolve(
        ApplicationDataMode mode,
        string applicationDirectory,
        string? localApplicationDataDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        if (mode == ApplicationDataMode.Installed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        }

        var storageRoot = mode switch
        {
            ApplicationDataMode.Portable => applicationDirectory,
            ApplicationDataMode.Installed => localApplicationDataDirectory
                ?? throw new ArgumentException("Installed mode requires a per-user application data root.", nameof(localApplicationDataDirectory)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var directory = mode switch
        {
            ApplicationDataMode.Portable => Path.Combine(storageRoot, "data"),
            ApplicationDataMode.Installed => Path.Combine(storageRoot, "scrcpy-seamless"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        directory = Path.GetFullPath(directory);
        return new ApplicationDataPaths(directory, Path.Combine(directory, "configuration.v2.json"));
    }
}
