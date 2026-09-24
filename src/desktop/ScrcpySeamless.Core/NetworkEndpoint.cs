using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrcpySeamless.Core;

/// <summary>The syntax category of a network endpoint host.</summary>
public enum EndpointHostKind
{
    Hostname,
    Ipv4,
    Ipv6,
}

/// <summary>A validated connect or pairing endpoint with invariant formatting.</summary>
[JsonConverter(typeof(NetworkEndpointJsonConverter))]
public sealed record NetworkEndpoint
{
    private const int MinimumPort = 1;
    private const int MaximumPort = 65535;
    private const int MaximumPortDigits = 5;
    private const int Ipv4OctetCount = 4;
    private const int MaximumIpv4OctetDigits = 3;
    private const int MaximumHostnameLength = 253;
    private const int MaximumHostnameLabelLength = 63;
    private const int MaximumEndpointLength = MaximumHostnameLength + MaximumPortDigits + 1;

    public string Host { get; }
    public int Port { get; }
    public EndpointHostKind HostKind { get; }

    private NetworkEndpoint(string host, int port, EndpointHostKind hostKind)
    {
        Host = host;
        Port = port;
        HostKind = hostKind;
    }

    /// <summary>Parses an endpoint or raises a format error.</summary>
    public static NetworkEndpoint Parse(string value)
    {
        if (!TryParse(value, out NetworkEndpoint? endpoint))
        {
            throw new FormatException("The endpoint must contain a valid host and port.");
        }

        return endpoint;
    }

    /// <summary>Parses a host, strict IPv4 address, or bracketed IPv6 address and port.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out NetworkEndpoint? endpoint)
    {
        endpoint = null;

        if (string.IsNullOrEmpty(value) || value.Length > MaximumEndpointLength)
        {
            return false;
        }

        string host;
        string portText;
        EndpointHostKind hostKind;

        if (value[0] == '[')
        {
            int closingBracket = value.IndexOf(']');

            if (closingBracket < 2 || closingBracket + 1 >= value.Length || value[closingBracket + 1] != ':')
            {
                return false;
            }

            string addressText = value[1..closingBracket];

            if (addressText.Contains('%') ||
                !IPAddress.TryParse(addressText, out IPAddress? address) ||
                address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            host = address.ToString().ToLowerInvariant();
            portText = value[(closingBracket + 2)..];
            hostKind = EndpointHostKind.Ipv6;
        }
        else
        {
            int delimiter = value.IndexOf(':');

            if (delimiter < 1 || delimiter != value.LastIndexOf(':'))
            {
                return false;
            }

            host = value[..delimiter];
            portText = value[(delimiter + 1)..];

            if (IsDottedNumber(host))
            {
                if (!TryNormalizeIpv4(host, out string normalizedAddress))
                {
                    return false;
                }

                host = normalizedAddress;
                hostKind = EndpointHostKind.Ipv4;
            }
            else
            {
                if (!IsValidHostname(host))
                {
                    return false;
                }

                host = host.ToLowerInvariant();
                hostKind = EndpointHostKind.Hostname;
            }
        }

        if (portText.Length is < MinimumPort or > MaximumPortDigits ||
            !portText.All(char.IsAsciiDigit) ||
            !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < MinimumPort or > MaximumPort)
        {
            return false;
        }

        endpoint = new NetworkEndpoint(host, port, hostKind);
        return true;
    }

    /// <summary>Formats an endpoint without culture-specific digits or ambiguous IPv6 colons.</summary>
    public override string ToString()
    {
        string host = HostKind == EndpointHostKind.Ipv6 ? $"[{Host}]" : Host;
        return $"{host}:{Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Recognizes numeric dotted input that must not fall back to a hostname.</summary>
    private static bool IsDottedNumber(string host) => host.Contains('.') && host.All(character => char.IsAsciiDigit(character) || character == '.');

    /// <summary>Validates all four IPv4 octets without permissive legacy number forms.</summary>
    private static bool TryNormalizeIpv4(string host, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        string[] octets = host.Split('.');

        if (octets.Length != Ipv4OctetCount)
        {
            return false;
        }

        foreach (string octet in octets)
        {
            if (octet.Length is < 1 or > MaximumIpv4OctetDigits ||
                (octet.Length > 1 && octet[0] == '0') ||
                !byte.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        normalizedAddress = host;
        return true;
    }

    /// <summary>Validates ASCII DNS labels without accepting URI, bracket, or whitespace syntax.</summary>
    private static bool IsValidHostname(string host)
    {
        if (host.Length is < 1 or > MaximumHostnameLength)
        {
            return false;
        }

        foreach (string label in host.Split('.'))
        {
            if (label.Length is < 1 or > MaximumHostnameLabelLength ||
                !IsAsciiAlphanumeric(label[0]) ||
                !IsAsciiAlphanumeric(label[^1]) ||
                !label.All(character => IsAsciiAlphanumeric(character) || character == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Matches only ASCII DNS letters and digits.</summary>
    private static bool IsAsciiAlphanumeric(char character) => char.IsAsciiLetter(character) || char.IsAsciiDigit(character);
}

/// <summary>Stores endpoints as their validated canonical host:port form.</summary>
public sealed class NetworkEndpointJsonConverter : JsonConverter<NetworkEndpoint>
{
    /// <summary>Rejects malformed endpoint strings during JSON deserialization.</summary>
    public override NetworkEndpoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !NetworkEndpoint.TryParse(reader.GetString(), out NetworkEndpoint? endpoint))
        {
            throw new JsonException("Invalid network endpoint.");
        }

        return endpoint;
    }

    /// <summary>Writes the invariant endpoint representation.</summary>
    public override void Write(Utf8JsonWriter writer, NetworkEndpoint value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
