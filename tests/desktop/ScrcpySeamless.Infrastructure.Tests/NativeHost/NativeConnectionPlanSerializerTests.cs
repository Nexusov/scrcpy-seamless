using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

public sealed class NativeConnectionPlanSerializerTests
{
    [Fact]
    public void SelectedNetworkRoutePrecedesPreferredUsbWithoutLeakingProfileMetadata()
    {
        NativeStartRequest request = CreateRequest(selected: TransportKind.Network);
        string json = NativeConnectionPlanSerializer.Serialize(request);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(0, root.GetProperty("selected").GetInt32());
        Assert.Equal("network", root.GetProperty("candidates")[0].GetProperty("transport").GetString());
        Assert.Equal("synthetic.example:37123", root.GetProperty("candidates")[0].GetProperty("serial").GetString());
        Assert.Equal("usb", root.GetProperty("candidates")[1].GetProperty("transport").GetString());
        Assert.Equal("1000000", root.GetProperty("initialBackoffUs").GetString());
        Assert.Equal("8000000", root.GetProperty("maximumBackoffUs").GetString());
        Assert.DoesNotContain(request.ProfileId.Value.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-revision", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FallbackDisabledRetainsOneCandidateAndExplicitReconnectChoice()
    {
        NativeStartRequest request = CreateRequest(allowFallback: false, reconnect: false);
        using JsonDocument document = JsonDocument.Parse(NativeConnectionPlanSerializer.Serialize(request));
        Assert.False(document.RootElement.GetProperty("reconnect").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [Theory]
    [InlineData(101, 0, 0)]
    [InlineData(3, 1, 10)]
    [InlineData(3, 0, 864000000010)]
    public void UnsupportedPolicyBoundsAreRejectedWithoutRounding(int attempts, long initialTicks, long maximumTicks)
    {
        ConnectionPolicy policy = new(ConnectionCapabilities.Video, ConnectionCapabilities.Video,
            new RetryPolicy(attempts, TimeSpan.FromTicks(initialTicks), TimeSpan.FromTicks(maximumTicks)),
            FailbackPolicy.Disabled);
        NativeStartRequest request = CreateRequest(policy: policy);
        Assert.Throws<InvalidOperationException>(() => NativeConnectionPlanSerializer.Serialize(request));
    }

    [Fact]
    public void AutomaticFailbackIsRejectedInsteadOfSilentlyIgnored()
    {
        ConnectionPolicy policy = new(ConnectionCapabilities.Video, ConnectionCapabilities.Video,
            ConnectionPolicy.Default.Retry, new FailbackPolicy(true, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => NativeConnectionPlanSerializer.Serialize(CreateRequest(policy: policy)));
    }

    [Fact]
    public void ZeroAndMaximumExactDurationsPreserveTheirCanonicalIntegerSpelling()
    {
        ConnectionPolicy policy = new(ConnectionCapabilities.Video, ConnectionCapabilities.Video,
            new RetryPolicy(100, TimeSpan.Zero, TimeSpan.FromDays(1)), FailbackPolicy.Disabled);
        using JsonDocument document = JsonDocument.Parse(NativeConnectionPlanSerializer.Serialize(CreateRequest(policy: policy)));
        Assert.Equal(100, document.RootElement.GetProperty("maximumAttempts").GetInt32());
        Assert.Equal("0", document.RootElement.GetProperty("initialBackoffUs").GetString());
        Assert.Equal("86400000000", document.RootElement.GetProperty("maximumBackoffUs").GetString());
    }

    [Fact]
    public void MaximumLegalSelectedNetworkEndpointIsCarriedWithoutTruncation()
    {
        string endpoint = MaximumEndpoint();
        Assert.Equal(259, System.Text.Encoding.UTF8.GetByteCount(endpoint));
        NativeStartRequest request = CreateRequest(selected: TransportKind.Network,
            preferred: TransportPreference.Network, allowFallback: false, endpointText: endpoint);
        using JsonDocument document = JsonDocument.Parse(NativeConnectionPlanSerializer.Serialize(request));
        Assert.Equal(endpoint, document.RootElement.GetProperty("candidates")[0].GetProperty("serial").GetString());
        Assert.Equal(endpoint, document.RootElement.GetProperty("candidates")[0].GetProperty("endpoint").GetString());
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void IdentityLimitCountsUtf8Bytes(int bytes, bool accepted)
    {
        string serial = new('x', bytes);
        NativeStartRequest request = CreateRequest(serial: serial);

        if (!accepted)
        {
            Assert.Throws<InvalidOperationException>(() => NativeConnectionPlanSerializer.Serialize(request));
            return;
        }

        using JsonDocument document = JsonDocument.Parse(NativeConnectionPlanSerializer.Serialize(request));
        Assert.Equal(serial, document.RootElement.GetProperty("candidates")[0].GetProperty("serial").GetString());
    }

    [Fact]
    public void UnicodeAndArgumentMetacharactersRoundTripAsData()
    {
        const string serial = "synthetic \"quoted\" \\ путь";
        string json = NativeConnectionPlanSerializer.Serialize(CreateRequest(serial: serial));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(serial, document.RootElement.GetProperty("candidates")[0].GetProperty("serial").GetString());
    }

    [Fact]
    public void IneligibleSelectedRouteIsNotInvented()
    {
        NativeStartRequest request = CreateRequest(allowFallback: false, selected: TransportKind.Network);
        Assert.Throws<InvalidOperationException>(() => NativeConnectionPlanSerializer.Serialize(request));
    }

    [Fact]
    public void CommittedPlansMatchIndependentExpectationsAndExportOnlyWhenRequested()
    {
        ConnectionPolicy networkPolicy = new(ConnectionCapabilities.Video,
            ConnectionCapabilities.Video | ConnectionCapabilities.Audio,
            new RetryPolicy(5, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)), FailbackPolicy.Disabled);
        (string Name, NativeStartRequest Request, string FirstTransport, int Count, int Required, int Desired,
            int Attempts, string Initial, string Maximum)[] cases =
        [
            ("usb-network", CreateRequest(service: true), "usb", 2, 5, 7, 3, "1000000", "8000000"),
            ("network-no-fallback", CreateRequest(policy: networkPolicy, selected: TransportKind.Network,
                preferred: TransportPreference.Network, allowFallback: false, service: true),
                "network", 1, 1, 3, 5, "500000", "1000000"),
            ("unicode-boundary", CreateRequest(serial: new string('é', 128), allowFallback: false, reconnect: false),
                "usb", 1, 5, 7, 3, "1000000", "8000000"),
            ("selected-network", CreateRequest(selected: TransportKind.Network, service: true),
                "network", 2, 5, 7, 3, "1000000", "8000000"),
            ("maximum-endpoint", CreateRequest(selected: TransportKind.Network,
                preferred: TransportPreference.Network, allowFallback: false, endpointText: MaximumEndpoint()),
                "network", 1, 5, 7, 3, "1000000", "8000000"),
        ];
        string? exportDirectory = Environment.GetEnvironmentVariable("SC_CONNECTION_PLAN_MANAGED");

        foreach (var item in cases)
        {
            string json = NativeConnectionPlanSerializer.Serialize(item.Request);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.Equal(item.FirstTransport, root.GetProperty("candidates")[0].GetProperty("transport").GetString());
            Assert.Equal(item.Count, root.GetProperty("candidates").GetArrayLength());
            Assert.Equal(item.Required, root.GetProperty("requiredCapabilities").GetInt32());
            Assert.Equal(item.Desired, root.GetProperty("desiredCapabilities").GetInt32());
            Assert.Equal(item.Attempts, root.GetProperty("maximumAttempts").GetInt32());
            Assert.Equal(item.Initial, root.GetProperty("initialBackoffUs").GetString());
            Assert.Equal(item.Maximum, root.GetProperty("maximumBackoffUs").GetString());

            if (exportDirectory is not null)
            {
                Directory.CreateDirectory(exportDirectory);
                File.WriteAllText(Path.Combine(exportDirectory, item.Name + ".json"), json,
                    new System.Text.UTF8Encoding(false));
            }
        }
    }

    [Fact]
    public void Utf8OverflowAndInvalidCapabilityPoliciesAreRejectedAtTheirOwningBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NativeConnectionPlanSerializer.Serialize(CreateRequest(serial: new string('é', 129))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionPolicy((ConnectionCapabilities)8,
            (ConnectionCapabilities)8, ConnectionPolicy.Default.Retry, FailbackPolicy.Disabled));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionPolicy(ConnectionCapabilities.Control,
            ConnectionCapabilities.Video, ConnectionPolicy.Default.Retry, FailbackPolicy.Disabled));
    }

    [Theory]
    [InlineData("adb-synthetic", true)]
    [InlineData("adb-synthetic.", true)]
    [InlineData("adb-synthetic._adb-tls-connect._tcp", true)]
    [InlineData("adb-synthetic._adb-tls-connect._tcp.", true)]
    [InlineData("adb-synthetic._adb-tls-pairing._tcp", false)]
    [InlineData("adb-synthetic._ssh._tcp", false)]
    [InlineData(".", false)]
    public void ServiceIdentityRetainsBareInstanceCompatibilityWithoutAdmittingWrongTypes(string serviceName, bool accepted)
    {
        NativeStartRequest request = CreateRequest(serviceName: serviceName);

        if (!accepted)
        {
            Assert.Throws<InvalidOperationException>(() => NativeConnectionPlanSerializer.Serialize(request));
            return;
        }

        using JsonDocument document = JsonDocument.Parse(NativeConnectionPlanSerializer.Serialize(request));
        Assert.Equal(serviceName, document.RootElement.GetProperty("candidates")[1].GetProperty("service").GetString());
    }

    private static NativeStartRequest CreateRequest(ConnectionPolicy? policy = null,
        TransportKind selected = TransportKind.Usb, bool allowFallback = true, bool reconnect = true,
        string serial = "SYNTHETIC_USB", bool service = false,
        TransportPreference preferred = TransportPreference.Automatic, string? serviceName = null,
        string endpointText = "synthetic.example:37123")
    {
        Assert.True(NetworkEndpoint.TryParse(endpointText, out NetworkEndpoint? endpoint));
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial(serial),
            ConnectionEndpoint = endpoint,
            MdnsIdentity = serviceName is not null ? new MdnsServiceName(serviceName)
                : service ? new MdnsServiceName("adb-synthetic._adb-tls-connect._tcp") : null,
            Connection = new ConnectionPreferences { AllowFallback = allowFallback, PreferredTransport = preferred },
        };
        Assert.True(ConnectionPlan.TryCreate(profile, policy ?? ConnectionPolicy.Default, out ConnectionPlan? plan, out _));
        return new NativeStartRequest(SessionId.New(), plan!, new MirroringPreferences { Reconnect = reconnect },
            selected == TransportKind.Usb ? serial : endpoint!.ToString(), selected, endpoint, "private-revision");
    }

    private static string MaximumEndpoint() =>
        string.Join('.', new string('a', 63), new string('a', 63), new string('a', 63), new string('a', 61)) + ":65535";
}
