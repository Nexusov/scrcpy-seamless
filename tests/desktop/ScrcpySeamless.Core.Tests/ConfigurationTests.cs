using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects stable profile identity and typed v2 validation.</summary>
public sealed class ConfigurationTests
{
    /// <summary>Changing a discovered network address does not change saved profile identity.</summary>
    [Fact]
    public void ProfileIdentityIsIndependentOfTransportDetails()
    {
        ProfileId stableId = ProfileId.New();
        DeviceProfile usbProfile = new()
        {
            Id = stableId,
            UsbIdentity = new UsbSerial("SERIAL_1"),
            Connection = new ConnectionPreferences { PreferredTransport = TransportPreference.Usb },
        };
        DeviceProfile networkProfile = new()
        {
            Id = stableId,
            MdnsIdentity = new MdnsServiceName("adb-device._adb-tls-connect._tcp"),
            ConnectionEndpoint = NetworkEndpoint.Parse("phone.local:5555"),
            Connection = new ConnectionPreferences { PreferredTransport = TransportPreference.Network },
        };

        Assert.Equal(usbProfile.Id, networkProfile.Id);
        Assert.Empty(usbProfile.Validate("profile"));
        Assert.Empty(networkProfile.Validate("profile"));
    }

    /// <summary>Zero profiles supports a migrated mirroring-only settings file.</summary>
    [Fact]
    public void ConfigurationCanContainOnlyGlobalMirroringSettings()
    {
        ConfigurationV2 configuration = new()
        {
            Mirroring = new MirroringPreferences
            {
                Reconnect = false,
                Options = new Dictionary<string, JsonElement>
                {
                    ["no-audio"] = JsonSerializer.SerializeToElement(true),
                    ["max-size"] = JsonSerializer.SerializeToElement("1920"),
                },
            },
        };

        Assert.Empty(configuration.Validate());
        Assert.False(configuration.Mirroring.Reconnect);
    }

    /// <summary>Duplicate and empty IDs fail before saving configuration.</summary>
    [Fact]
    public void DuplicateAndEmptyProfileIdsAreReported()
    {
        ProfileId identifier = ProfileId.New();
        ConfigurationV2 configuration = new()
        {
            Profiles =
            [
                new DeviceProfile { Id = identifier, UsbIdentity = new UsbSerial("one") },
                new DeviceProfile { Id = identifier, UsbIdentity = new UsbSerial("two") },
                new DeviceProfile { UsbIdentity = new UsbSerial("three") },
            ],
        };

        IReadOnlyList<ValidationIssue> issues = configuration.Validate();

        Assert.Contains(issues, issue => issue.Code == CoreErrorCode.DuplicateProfile && issue.Path == "Profiles[1].Id");
        Assert.Contains(issues, issue => issue.Code == CoreErrorCode.InvalidIdentity && issue.Path == "Profiles[2].Id");
    }

    /// <summary>Unsupported versions and nonlegacy option value shapes are rejected with paths.</summary>
    [Fact]
    public void InvalidSchemaAndOptionValuesAreReported()
    {
        ConfigurationV2 configuration = new()
        {
            SchemaVersion = 3,
            Mirroring = new MirroringPreferences
            {
                Options = new Dictionary<string, JsonElement>
                {
                    ["unknown-future-option"] = JsonSerializer.SerializeToElement(42),
                },
            },
        };

        IReadOnlyList<ValidationIssue> issues = configuration.Validate();

        Assert.Contains(issues, issue => issue.Code == CoreErrorCode.UnsupportedConfigurationVersion && issue.Path == "SchemaVersion");
        Assert.Contains(issues, issue => issue.Code == CoreErrorCode.InvalidConfiguration && issue.Path == "Mirroring.Options");
    }

    /// <summary>Pairing and connection cannot reuse the same endpoint role value.</summary>
    [Fact]
    public void PairingAndConnectionEndpointsMustBeDistinct()
    {
        NetworkEndpoint sharedEndpoint = NetworkEndpoint.Parse("phone.local:5555");
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("usb-A"),
            PairingEndpoint = sharedEndpoint,
            ConnectionEndpoint = sharedEndpoint,
        };

        Assert.Contains(profile.Validate("profile"), issue => issue.Path == "profile.PairingEndpoint");
    }

    /// <summary>Typed v2 configuration survives JSON persistence with its identities and endpoint roles.</summary>
    [Fact]
    public void ConfigurationRoundTripsWithoutLosingDistinctFields()
    {
        ConfigurationV2 source = new()
        {
            Profiles =
            [
                new DeviceProfile
                {
                    Id = ProfileId.New(),
                    DeviceIdentity = DeviceId.New(),
                    UsbIdentity = new UsbSerial("usb-A"),
                    MdnsIdentity = new MdnsServiceName("adb-A._adb-tls-connect._tcp"),
                    PairingEndpoint = NetworkEndpoint.Parse("phone.local:37123"),
                    ConnectionEndpoint = NetworkEndpoint.Parse("phone.local:5555"),
                    Alias = "Test phone",
                    Connection = new ConnectionPreferences { PreferredTransport = TransportPreference.Network },
                },
            ],
        };

        string json = JsonSerializer.Serialize(source);
        ConfigurationV2? restored = JsonSerializer.Deserialize<ConfigurationV2>(json);

        Assert.NotNull(restored);
        Assert.Contains($"\"Id\":\"{source.Profiles[0].Id}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"UsbIdentity\":\"usb-A\"", json, StringComparison.Ordinal);
        Assert.Empty(restored.Validate());
        Assert.Equal(source.Profiles[0].Id, restored.Profiles[0].Id);
        Assert.Equal(source.Profiles[0].UsbIdentity, restored.Profiles[0].UsbIdentity);
        Assert.Equal("phone.local:37123", restored.Profiles[0].PairingEndpoint?.ToString());
        Assert.Equal("phone.local:5555", restored.Profiles[0].ConnectionEndpoint?.ToString());
    }

    /// <summary>External identifiers stay opaque while control characters cannot enter saved data.</summary>
    [Fact]
    public void ExternalIdentitiesPreserveTextAndRejectControlCharacters()
    {
        Assert.Equal("USB:unusual@serial", new UsbSerial("USB:unusual@serial").Value);
        Assert.Throws<ArgumentException>(() => new UsbSerial("device\nserial"));
        Assert.Throws<ArgumentException>(() => new MdnsServiceName("\t"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProfileId(Guid.Empty));
    }

    /// <summary>A v2 file must explicitly state its schema and profile identity.</summary>
    [Fact]
    public void JsonRequiresSchemaAndProfileIdentity()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfigurationV2>("{\"Profiles\":[],\"Mirroring\":{}}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConfigurationV2>("{\"SchemaVersion\":2,\"Profiles\":[{\"UsbIdentity\":\"usb-A\"}],\"Mirroring\":{}}"));
    }
}
