using System.ComponentModel;
using System.Globalization;
using System.IO;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;

namespace ScrcpySeamless.Infrastructure.Adb;

/** Maps ADB process responses to platform-independent application results. */
public sealed class AdbGateway(IAdbProcessRunner runner) : IAdbGateway
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

            if (parsed.MalformedLineCount > 0 && parsed.Items.Count == 0)
            {
                return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.MalformedResponse);
            }

            return parsed.MalformedLineCount == 0
                ? await EnrichUsbRouteAsync(parsed.Items, cancellationToken)
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

    /** Resolves one unknown route only when ADB identifies its live USB transport ID. */
    private async Task<AdbResult<IReadOnlyList<AdbDevice>>> EnrichUsbRouteAsync(
        IReadOnlyList<AdbDevice> snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Any(device => device.TransportKind == AdbTransportKind.Unknown
            && device.TransportId.HasValue))
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        AdbProcessResult probe;

        try
        {
            // -d is USB-scoped on the bundled ADB; no -s/-t selector is combined with it.
            probe = await runner.RunAsync(["-d", "transport-id"], DiscoveryTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }
        catch (Win32Exception)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }
        catch (IOException)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        string reply = probe.StandardOutput.Trim();
        bool parsedTransportId = ulong.TryParse(reply, NumberStyles.None,
            CultureInfo.InvariantCulture, out ulong usbTransportId);
        bool validProbe = probe.ExitCode == 0 && !probe.OutputTruncated
            && string.IsNullOrWhiteSpace(probe.StandardError)
            && parsedTransportId && usbTransportId > 0;

        if (!validProbe)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        int[] matchingIndexes = Enumerable.Range(0, snapshot.Count)
            .Where(index => snapshot[index].TransportId == usbTransportId).Take(2).ToArray();

        if (matchingIndexes.Length != 1)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        int matchingIndex = matchingIndexes[0];

        if (snapshot[matchingIndex].TransportKind != AdbTransportKind.Unknown)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        AdbDevice candidate = snapshot[matchingIndex];

        if (snapshot.Count(device => device.Serial == candidate.Serial) != 1)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(snapshot);
        }

        // Recheck the same server before publishing a verdict across a disconnect.
        AdbProcessResult current;

        try
        {
            current = await runner.RunAsync(["devices", "-l"], DiscoveryTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.TimedOut);
        }
        catch (Win32Exception)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.Unavailable);
        }
        catch (IOException)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.ProcessFailed);
        }

        bool invalidCurrentResponse = current.ExitCode != 0 || current.OutputTruncated ||
            AdbResponseParser.HasErrorDiagnostics(current.StandardError);

        if (invalidCurrentResponse)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.ProcessFailed);
        }

        AdbParseResult<AdbDevice> currentSnapshot = AdbResponseParser.ParseDevices(current.StandardOutput);

        if (currentSnapshot.MalformedLineCount > 0)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.MalformedResponse);
        }

        bool snapshotChanged = currentSnapshot.Items.Count != snapshot.Count ||
            !currentSnapshot.Items.SequenceEqual(snapshot);

        if (snapshotChanged)
        {
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(currentSnapshot.Items);
        }

        AdbDevice[] enriched = snapshot.ToArray();
        enriched[matchingIndex] = candidate with { TransportKind = AdbTransportKind.Usb };
        return AdbResult<IReadOnlyList<AdbDevice>>.Success(enriched);
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

            if (parsed.MalformedLineCount > 0 && parsed.Items.Count == 0)
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MalformedResponse);
            }

            if (parsed.Items.Count > 0)
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Success(parsed.Items);
            }

            // An empty service registry does not prove discovery is available.
            AdbProcessResult check = await runner.RunAsync(["mdns", "check"], DiscoveryTimeout, cancellationToken);

            if (check.ExitCode != 0 || AdbResponseParser.HasErrorDiagnostics(check.StandardError))
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.ProcessFailed);
            }

            if (check.OutputTruncated)
            {
                return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MalformedResponse);
            }

            return AdbResponseParser.ParseMdnsCheck(check.StandardOutput) switch
            {
                AdbMdnsCheckStatus.Available => AdbResult<IReadOnlyList<AdbMdnsService>>.Success(parsed.Items),
                AdbMdnsCheckStatus.Unavailable =>
                    AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MdnsUnavailable),
                _ => AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.MalformedResponse),
            };
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

            if (process.OutputTruncated)
            {
                return AdbResult<bool>.Error(AdbFailureKind.MalformedResponse);
            }

            if (AdbResponseParser.HasErrorDiagnostics(process.StandardError))
            {
                return AdbResult<bool>.Error(AdbFailureKind.ProcessFailed);
            }

            if (AdbResponseParser.IsPairingRejected(process.StandardOutput))
            {
                return AdbResult<bool>.Error(AdbFailureKind.PairingRejected);
            }

            if (process.ExitCode != 0)
            {
                return AdbResult<bool>.Error(AdbFailureKind.ProcessFailed);
            }

            // Exit zero without the expected success line is uncertain, not a rejection.
            return AdbResponseParser.IsPairingSuccessful(process.ExitCode, process.StandardOutput)
                ? AdbResult<bool>.Success(true)
                : AdbResult<bool>.Error(AdbFailureKind.MalformedResponse);
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
