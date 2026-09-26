using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;

namespace ScrcpySeamless.Infrastructure.Adb;

public sealed record AdbParseResult<T>(IReadOnlyList<T> Items, int MalformedLineCount);

/** Parses untrusted ADB text without treating daemon notices as devices. */
public static class AdbResponseParser
{
    private const string PairingServiceType = "_adb-tls-pairing._tcp";
    private const string ConnectionServiceType = "_adb-tls-connect._tcp";
    private const string PairingCodePrompt = "Enter pairing code: ";
    private const string PairingSuccessPrefix = "Successfully paired to ";

    /** Distinguishes ADB errors on stderr from routine daemon startup notices. */
    public static bool HasErrorDiagnostics(string standardError)
    {
        return ReadDataLines(standardError).Any(line =>
            line.StartsWith("error:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("adb: error:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("failed to ", StringComparison.OrdinalIgnoreCase));
    }

    public static AdbParseResult<AdbDevice> ParseDevices(string output)
    {
        List<AdbDevice> devices = [];
        int malformedLineCount = 0;

        foreach (string line in ReadDataLines(output))
        {
            if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 2 || fields[0].Equals("error:", StringComparison.OrdinalIgnoreCase))
            {
                malformedLineCount++;
                continue;
            }

            AdbDeviceState? state = fields[1] switch
            {
                "device" => AdbDeviceState.Device,
                "offline" => AdbDeviceState.Offline,
                "unauthorized" => AdbDeviceState.Unauthorized,
                "recovery" or "sideload" or "bootloader" => AdbDeviceState.Other,
                _ => null,
            };

            if (state is null)
            {
                malformedLineCount++;
                continue;
            }

            string? modelField = fields.Skip(2)
                .FirstOrDefault(field => field.StartsWith("model:", StringComparison.Ordinal));
            string? model = modelField is null
                ? null
                : modelField["model:".Length..].Replace('_', ' ');
            devices.Add(new AdbDevice(fields[0], state.Value, model));
        }

        return new AdbParseResult<AdbDevice>(devices, malformedLineCount);
    }

    public static AdbParseResult<AdbMdnsService> ParseMdnsServices(string output)
    {
        List<AdbMdnsService> services = [];
        int malformedLineCount = 0;

        foreach (string line in ReadDataLines(output))
        {
            if (line.StartsWith("List of discovered mdns services", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length != 3)
            {
                malformedLineCount++;
                continue;
            }

            string type = fields[1].TrimEnd('.');
            AdbServiceKind? kind = type switch
            {
                PairingServiceType => AdbServiceKind.Pairing,
                ConnectionServiceType => AdbServiceKind.Connection,
                _ => null,
            };

            if (kind is null)
            {
                continue;
            }

            if (!NetworkEndpoint.TryParse(fields[2], out NetworkEndpoint? endpoint) || endpoint is null)
            {
                malformedLineCount++;
                continue;
            }

            services.Add(new AdbMdnsService(fields[0].TrimEnd('.'), kind.Value, endpoint));
        }

        return new AdbParseResult<AdbMdnsService>(services, malformedLineCount);
    }

    /** Accepts ADB success both with and without its non-newline input prompt. */
    public static bool IsPairingSuccessful(int exitCode, string output)
    {
        return exitCode == 0 && ReadDataLines(output)
            .Any(line => line.StartsWith(PairingSuccessPrefix, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(PairingCodePrompt + PairingSuccessPrefix, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsConnectSuccessful(int exitCode, string output, NetworkEndpoint expectedEndpoint)
    {
        if (exitCode != 0)
        {
            return false;
        }

        foreach (string line in ReadDataLines(output))
        {
            string prefix = line.StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase)
                ? "already connected to "
                : "connected to ";

            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string address = line[prefix.Length..].Trim();

            if (NetworkEndpoint.TryParse(address, out NetworkEndpoint? reportedEndpoint)
                && reportedEndpoint is not null
                && reportedEndpoint.Port == expectedEndpoint.Port
                && string.Equals(reportedEndpoint.Host, expectedEndpoint.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ReadDataLines(string output)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('*') || trimmed.StartsWith("adb server version", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return trimmed;
        }
    }

}
