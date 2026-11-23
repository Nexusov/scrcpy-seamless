using System.Globalization;
using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Captures one bounded launch plan without changing the framed IPC contract.</summary>
internal static class NativeConnectionPlanSerializer
{
    internal const int MaximumBytes = 4096;
    private const int MaximumIdentityBytes = 256;
    private const int MaximumEndpointBytes = 259;
    private const int MaximumAttempts = 100;
    private const long MaximumDelayMicroseconds = 86_400_000_000;
    private const long TicksPerMicrosecond = 10;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Serialize(NativeStartRequest request)
    {
        ConnectionPolicy policy = request.Plan.Policy;

        if (policy.Failback.Automatic)
        {
            throw new InvalidOperationException("Automatic failback is not supported by this native launch contract.");
        }

        if (policy.Retry.MaximumAttempts > MaximumAttempts)
        {
            throw new InvalidOperationException("The native retry bound exceeds the launch contract.");
        }

        long initialBackoff = Microseconds(policy.Retry.InitialBackoff);
        long maximumBackoff = Microseconds(policy.Retry.MaximumBackoff);
        TransportCandidate[] candidates = [request.Plan.Preferred, .. request.Plan.FallbackCandidates];
        TransportCandidate? selected = candidates.FirstOrDefault(candidate =>
            candidate.Kind == request.SelectedTransport &&
            (candidate.Kind != TransportKind.Usb || candidate.UsbSerial?.Value == request.SelectedAdbSerial));

        if (selected is null || candidates.Length > 2)
        {
            throw new InvalidOperationException("The selected transport is not an eligible plan candidate.");
        }

        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteBoolean("reconnect", request.Mirroring.Reconnect);
            writer.WriteNumber("selected", 0);
            writer.WriteNumber("requiredCapabilities", (int)policy.RequiredCapabilities);
            writer.WriteNumber("desiredCapabilities", (int)policy.DesiredCapabilities);
            writer.WriteNumber("maximumAttempts", policy.Retry.MaximumAttempts);
            writer.WriteString("initialBackoffUs", initialBackoff.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("maximumBackoffUs", maximumBackoff.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartArray("candidates");
            WriteCandidate(writer, selected, request.SelectedAdbSerial);

            foreach (TransportCandidate candidate in candidates.Where(candidate => !ReferenceEquals(candidate, selected)))
            {
                WriteCandidate(writer, candidate, null);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (output.Length > MaximumBytes)
        {
            throw new InvalidOperationException("The native connection plan exceeds its byte bound.");
        }

        return StrictUtf8.GetString(output.ToArray());
    }

    private static long Microseconds(TimeSpan duration)
    {
        if (duration.Ticks < 0 || duration.Ticks % TicksPerMicrosecond != 0 ||
            duration.Ticks / TicksPerMicrosecond > MaximumDelayMicroseconds)
        {
            throw new InvalidOperationException("Native retry durations must be exact microseconds within one day.");
        }

        return duration.Ticks / TicksPerMicrosecond;
    }

    private static void WriteCandidate(Utf8JsonWriter writer, TransportCandidate candidate, string? selectedSerial)
    {
        writer.WriteStartObject();
        writer.WriteString("transport", candidate.Kind == TransportKind.Usb ? "usb" : "network");
        string? serial = selectedSerial ?? candidate.UsbSerial?.Value;

        if (serial is not null)
        {
            int maximumBytes = candidate.Kind == TransportKind.Network && NetworkEndpoint.TryParse(serial, out _)
                ? MaximumEndpointBytes : MaximumIdentityBytes;
            WriteIdentifier(writer, "serial", serial, maximumBytes);
        }

        if (candidate.MdnsService is { } service)
        {
            string normalized = service.Value.TrimEnd('.');

            if (normalized.Length == 0 ||
                (normalized.Contains("._", StringComparison.Ordinal) &&
                 !normalized.EndsWith("._adb-tls-connect._tcp", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("A network plan requires an ADB connection service.");
            }

            WriteIdentifier(writer, "service", service.Value, MaximumIdentityBytes);
        }

        if (candidate.Endpoint is { } endpoint)
        {
            WriteIdentifier(writer, "endpoint", endpoint.ToString(), MaximumEndpointBytes);
        }

        writer.WriteEndObject();
    }

    private static void WriteIdentifier(Utf8JsonWriter writer, string name, string value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || StrictUtf8.GetByteCount(value) > maximumBytes)
        {
            throw new InvalidOperationException("A native connection-plan identifier is outside its bounded contract.");
        }

        writer.WriteString(name, value);
    }
}
