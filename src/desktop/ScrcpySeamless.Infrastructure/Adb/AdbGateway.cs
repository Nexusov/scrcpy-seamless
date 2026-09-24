using System.ComponentModel;
using System.IO;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;

namespace ScrcpySeamless.Infrastructure.Adb;

/** Maps ADB process responses to platform-independent application results. */
public sealed class AdbGateway(AdbProcessRunner runner) : IAdbGateway
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

    public async Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            AdbProcessResult process = await runner.RunAsync(["devices", "-l"], DiscoveryTimeout, cancellationToken);

            if (process.ExitCode != 0 || AdbResponseParser.HasErrorDiagnostics(process.StandardError))
            {
                return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.ProcessFailed);
            }

            if (process.OutputTruncated)
            {
                return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.MalformedResponse);
            }

            AdbParseResult<AdbDevice> parsed = AdbResponseParser.ParseDevices(process.StandardOutput);
            return parsed.MalformedLineCount > 0 && parsed.Items.Count == 0
                ? AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.MalformedResponse)
                : AdbResult<IReadOnlyList<AdbDevice>>.Success(parsed.Items);
        }
        catch (TimeoutException)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.Unavailable);
        }
    }

    public async Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            AdbProcessResult process = await runner.RunAsync(["mdns", "services"], DiscoveryTimeout, cancellationToken);

            if (process.ExitCode != 0 || AdbResponseParser.HasErrorDiagnostics(process.StandardError))
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.ProcessFailed);
            }

            if (process.OutputTruncated)
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MalformedResponse);
            }

            AdbParseResult<AdbMdnsService> parsed = AdbResponseParser.ParseMdnsServices(process.StandardOutput);
            return parsed.MalformedLineCount > 0 && parsed.Items.Count == 0
                ? AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MalformedResponse)
                : AdbResult<IReadOnlyList<AdbMdnsService>>.Success(parsed.Items);
        }
        catch (TimeoutException)
        {
            return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.Unavailable);
        }
    }

    public async Task<AdbResult<bool>> PairAsync(
        NetworkEndpoint pairingEndpoint,
        string pairingCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pairingCode) || pairingCode.Length != 6
            || pairingCode.Any(character => character is < '0' or > '9'))
        {
            return AdbResult<bool>.Error(AdbFailureKind.InvalidInput);
        }

        try
        {
            AdbProcessResult process = await runner.RunAsync(
                ["pair", pairingEndpoint.ToString()],
                PairingTimeout,
                cancellationToken,
                standardInput: pairingCode);
            bool paired = !process.OutputTruncated
                && !AdbResponseParser.HasErrorDiagnostics(process.StandardError)
                && AdbResponseParser.IsPairingSuccessful(process.ExitCode, process.StandardOutput);
            return paired
                ? AdbResult<bool>.Success(true)
                : AdbResult<bool>.Error(AdbFailureKind.PairingRejected);
        }
        catch (TimeoutException)
        {
            return AdbResult<bool>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<bool>.Error(AdbFailureKind.Unavailable);
        }
        catch (IOException)
        {
            return AdbResult<bool>.Error(AdbFailureKind.ProcessFailed);
        }
    }

    public async Task<AdbResult<bool>> ConnectAsync(
        NetworkEndpoint connectionEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            AdbProcessResult process = await runner.RunAsync(
                ["connect", connectionEndpoint.ToString()], ConnectionTimeout, cancellationToken);
            bool connected = !process.OutputTruncated
                && !AdbResponseParser.HasErrorDiagnostics(process.StandardError)
                && AdbResponseParser.IsConnectSuccessful(
                    process.ExitCode,
                    process.StandardOutput,
                    connectionEndpoint);
            return connected
                ? AdbResult<bool>.Success(true)
                : AdbResult<bool>.Error(AdbFailureKind.ConnectionRejected);
        }
        catch (TimeoutException)
        {
            return AdbResult<bool>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<bool>.Error(AdbFailureKind.Unavailable);
        }
    }

    public async Task<AdbResult<string>> GetDeviceSerialPropertyAsync(
        NetworkEndpoint connectionEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            AdbProcessResult process = await runner.RunAsync(
                ["-s", connectionEndpoint.ToString(), "shell", "getprop", "ro.serialno"],
                ConnectionTimeout,
                cancellationToken);

            if (process.ExitCode != 0 || AdbResponseParser.HasErrorDiagnostics(process.StandardError))
            {
                return AdbResult<string>.Error(AdbFailureKind.ProcessFailed);
            }

            string serial = process.StandardOutput.Trim();

            if (process.OutputTruncated || string.IsNullOrWhiteSpace(serial)
                || serial.Contains('\n') || serial.Contains('\r') || serial.Any(char.IsControl))
            {
                return AdbResult<string>.Error(AdbFailureKind.MalformedResponse);
            }

            return AdbResult<string>.Success(serial);
        }
        catch (TimeoutException)
        {
            return AdbResult<string>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<string>.Error(AdbFailureKind.Unavailable);
        }
    }
}
