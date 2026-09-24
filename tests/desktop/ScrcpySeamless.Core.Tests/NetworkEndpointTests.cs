using System.Globalization;
using System.Text.Json;
using ScrcpySeamless.Core;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects endpoint parsing against ambiguous or unsafe input.</summary>
public sealed class NetworkEndpointTests
{
    /// <summary>Accepts each supported host syntax and emits one canonical form.</summary>
    [Theory]
    [InlineData("Phone.LOCAL:37123", EndpointHostKind.Hostname, "phone.local:37123")]
    [InlineData("127.0.0.1:1", EndpointHostKind.Ipv4, "127.0.0.1:1")]
    [InlineData("192.0.2.42:65535", EndpointHostKind.Ipv4, "192.0.2.42:65535")]
    [InlineData("[2001:0DB8:0:0::1]:03712", EndpointHostKind.Ipv6, "[2001:db8::1]:3712")]
    [InlineData("[::ffff:192.0.2.1]:5555", EndpointHostKind.Ipv6, "[::ffff:192.0.2.1]:5555")]
    public void AcceptedEndpointsHaveInvariantCanonicalForm(string input, EndpointHostKind kind, string canonical)
    {
        NetworkEndpoint endpoint = NetworkEndpoint.Parse(input);

        Assert.Equal(kind, endpoint.HostKind);
        Assert.Equal(canonical, endpoint.ToString());
        Assert.Equal(endpoint, NetworkEndpoint.Parse(endpoint.ToString()));
    }

    /// <summary>Rejects malformed and ambiguous forms without permissive IP parsing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" phone.local:5555")]
    [InlineData("phone.local:0")]
    [InlineData("phone.local:65536")]
    [InlineData("phone.local:-1")]
    [InlineData("phone.local:+1")]
    [InlineData("phone.local:1extra")]
    [InlineData("phone.local")]
    [InlineData("phone..local:5555")]
    [InlineData("-phone.local:5555")]
    [InlineData("phone.local.:5555")]
    [InlineData("127.1:5555")]
    [InlineData("192.0.2.999:5555")]
    [InlineData("192.168.001.1:5555")]
    [InlineData("2001:db8::1:5555")]
    [InlineData("[2001:db8::1]5555")]
    [InlineData("[2001:db8::1]:")]
    [InlineData("[fe80::1%3]:5555")]
    [InlineData("[127.0.0.1]:5555")]
    [InlineData("https://phone.local:5555")]
    [InlineData("phone.local:5555\n")]
    public void InvalidEndpointsAreRejected(string input)
    {
        Assert.False(NetworkEndpoint.TryParse(input, out NetworkEndpoint? endpoint));
        Assert.Null(endpoint);
        Assert.Throws<FormatException>(() => NetworkEndpoint.Parse(input));
    }

    /// <summary>Rejects oversized endpoint and DNS label input at the parser boundary.</summary>
    [Fact]
    public void OversizedEndpointIsRejected()
    {
        string oversizedHost = new('a', 254);
        string oversizedLabel = new('b', 64);

        Assert.False(NetworkEndpoint.TryParse($"{oversizedHost}:5555", out _));
        Assert.False(NetworkEndpoint.TryParse($"{oversizedLabel}.local:5555", out _));
    }

    /// <summary>Does not format ports with the machine's active digit culture.</summary>
    [Fact]
    public void EndpointFormattingUsesInvariantCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG");
            Assert.Equal("example.org:12345", NetworkEndpoint.Parse("EXAMPLE.ORG:12345").ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>JSON round-trips the same validated endpoint syntax.</summary>
    [Fact]
    public void JsonConversionRejectsMalformedEndpoint()
    {
        NetworkEndpoint endpoint = NetworkEndpoint.Parse("[2001:db8::1]:5555");

        Assert.Equal("\"[2001:db8::1]:5555\"", JsonSerializer.Serialize(endpoint));
        Assert.Equal(endpoint, JsonSerializer.Deserialize<NetworkEndpoint>(JsonSerializer.Serialize(endpoint)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NetworkEndpoint>("\"[2001:db8::1]:0\""));
    }
}
