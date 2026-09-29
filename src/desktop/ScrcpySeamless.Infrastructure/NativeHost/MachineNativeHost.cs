using System.Diagnostics;
using System.Globalization;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Starts one explicitly selected machine-protocol native child.</summary>
public sealed class MachineNativeHost : INativeHost
{
    private readonly string nativeExecutablePath;
    private readonly string serverPath;
    private readonly string adbExecutablePath;
    private readonly bool enableOpenScreenMdnsCompatibility;

    /// <summary>Captures absolute, existing runtime components before any child is created.</summary>
    public MachineNativeHost(string nativeExecutablePath, string serverPath, string adbExecutablePath,
        bool enableOpenScreenMdnsCompatibility = false)
    {
        this.nativeExecutablePath = RequireFile(nativeExecutablePath);
        this.serverPath = RequireFile(serverPath);
        this.adbExecutablePath = RequireFile(adbExecutablePath);
        this.enableOpenScreenMdnsCompatibility = enableOpenScreenMdnsCompatibility;
    }

    /// <summary>Transfers ownership only after the binary handshake and NativeReady event.</summary>
    public async Task<INativeSession> StartAsync(NativeStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The machine native parent guard requires Windows.");
        }

        return await MachineNativeSession.StartAsync(request.SessionId, CreateStartInfo(request), cancellationToken);
    }

    /// <summary>Builds discrete, validated arguments from a committed launch snapshot.</summary>
    internal ProcessStartInfo CreateStartInfo(NativeStartRequest request)
    {
        OptionSelectionResult selection = OptionSelectionValidator.Evaluate(request.Mirroring);

        if (!selection.IsValid)
        {
            throw new InvalidOperationException("The native launch contains unsupported or invalid options.");
        }

        using Process parent = Process.GetCurrentProcess();
        long parentCreated = parent.StartTime.ToUniversalTime().ToFileTimeUtc();
        ProcessStartInfo startInfo = new()
        {
            FileName = nativeExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(nativeExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--seamless-machine");
        startInfo.ArgumentList.Add($"--seamless-session-id={request.SessionId.Value:D}");
        startInfo.ArgumentList.Add($"--seamless-parent-pid={parent.Id.ToString(CultureInfo.InvariantCulture)}");
        startInfo.ArgumentList.Add($"--seamless-parent-created={parentCreated.ToString(CultureInfo.InvariantCulture)}");
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
        startInfo.Environment.Remove("SCRCPY_STOP_EVENT");
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

    /// <summary>Refuses relative or missing runtime paths.</summary>
    private static string RequireFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute runtime path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("A required native runtime component is missing.", fullPath);
        }

        return fullPath;
    }
}
