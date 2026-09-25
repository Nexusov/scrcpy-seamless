using System.Diagnostics;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Starts the current native client through its temporary legacy process contract.</summary>
public sealed class LegacyNativeHost : INativeHost
{
    private readonly string nativeExecutablePath;
    private readonly string serverPath;
    private readonly string adbExecutablePath;
    private readonly string sessionDataRoot;
    private readonly bool enableOpenScreenMdnsCompatibility;

    /// <summary>Accepts only explicit bundle components and an isolated mutable session root.</summary>
    public LegacyNativeHost(
        string nativeExecutablePath,
        string serverPath,
        string adbExecutablePath,
        string sessionDataRoot,
        bool enableOpenScreenMdnsCompatibility = false)
    {
        this.nativeExecutablePath = RequireFile(nativeExecutablePath, nameof(nativeExecutablePath));
        this.serverPath = RequireFile(serverPath, nameof(serverPath));
        this.adbExecutablePath = RequireFile(adbExecutablePath, nameof(adbExecutablePath));
        this.sessionDataRoot = RequireFullPath(sessionDataRoot, nameof(sessionDataRoot));
        this.enableOpenScreenMdnsCompatibility = enableOpenScreenMdnsCompatibility;
    }

    /// <summary>Creates the graceful-stop event before launching one owned child.</summary>
    public async Task<INativeSession> StartAsync(NativeStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The legacy native stop event requires Windows.");
        }

        string eventName = $"Local\\scrcpy-seamless-native-stop-{Guid.NewGuid():N}";
        EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, eventName);
        LegacyNativeLifecycleLog? lifecycleLog = null;

        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(request, eventName);
            Directory.CreateDirectory(sessionDataRoot);
            lifecycleLog = new LegacyNativeLifecycleLog(sessionDataRoot, request.SessionId);
            LegacyNativeSession session = LegacyNativeSession.Start(request.SessionId, startInfo, stopEvent, lifecycleLog);

            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None);
                }
                finally
                {
                    await session.DisposeAsync();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return session;
        }
        catch
        {
            lifecycleLog?.Dispose();
            stopEvent.Dispose();
            throw;
        }
    }

    /// <summary>Builds a direct invocation from the captured request without shell interpolation.</summary>
    internal ProcessStartInfo CreateStartInfo(NativeStartRequest request, string stopEventName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(stopEventName);
        OptionSelectionResult selection = OptionSelectionValidator.Evaluate(request.Mirroring);

        if (!selection.IsValid)
        {
            throw new InvalidOperationException("The native launch contains unsupported or invalid options.");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = nativeExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(nativeExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(request.SelectedAdbSerial);

        foreach (string argument in selection.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!request.Mirroring.Options.ContainsKey("window-title"))
        {
            startInfo.ArgumentList.Add("--window-title=Phone-Seamless");
        }

        startInfo.ArgumentList.Add("--pause-on-exit=false");
        startInfo.Environment["ADB"] = adbExecutablePath;
        startInfo.Environment["SCRCPY_SERVER_PATH"] = serverPath;
        startInfo.Environment["SCRCPY_STOP_EVENT"] = stopEventName;
        startInfo.Environment.Remove("SCRCPY_RECONNECT_SERIAL");

        if (request.Mirroring.Reconnect && request.ReconnectEndpoint is not null)
        {
            startInfo.Environment["SCRCPY_RECONNECT_SERIAL"] = request.ReconnectEndpoint.ToString();
        }

        if (enableOpenScreenMdnsCompatibility)
        {
            startInfo.Environment["ADB_MDNS_OPENSCREEN"] = "1";
        }

        return startInfo;
    }

    /// <summary>Rejects missing or relative runtime components before process creation.</summary>
    private static string RequireFile(string path, string parameterName)
    {
        string fullPath = RequireFullPath(path, parameterName);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("A required native runtime component is missing.", fullPath);
        }

        return fullPath;
    }

    /// <summary>Requires an exact absolute path, including directories containing spaces.</summary>
    private static string RequireFullPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute path is required.", parameterName);
        }

        return Path.GetFullPath(path);
    }
}
