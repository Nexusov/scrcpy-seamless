using ScrcpySeamless.Core.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Configuration;

/// <summary>Protects migration of sanitized 1.x phone and mirroring document shapes.</summary>
public sealed class LegacyConfigurationMigratorTests
{
    /// <summary>Maps the overloaded wireless service to a dedicated mDNS field.</summary>
    [Fact]
    public void MdnsFixtureMigratesWithoutChangingLegacyOptionValues()
    {
        LegacyMigrationResult result = LegacyConfigurationMigrator.Migrate(
            Fixture("legacy-phone-mdns.json"),
            Fixture("legacy-settings.json"));

        Assert.True(result.Succeeded);
        DeviceProfile profile = Assert.Single(result.Configuration!.Profiles);
        Assert.Equal("TEST_DEVICE_01", profile.UsbIdentity?.Value);
        Assert.Equal("adb-TEST_DEVICE_01-paired._adb-tls-connect._tcp", profile.MdnsIdentity?.Value);
        Assert.Null(profile.ConnectionEndpoint);
        Assert.Null(profile.PairingEndpoint);
        Assert.True(result.Configuration.Mirroring.Reconnect);
        Assert.Equal("60", result.Configuration.Mirroring.Options["max-fps"].GetString());
        Assert.False(result.Configuration.Mirroring.Options["no-audio"].GetBoolean());
    }

    /// <summary>Repeated conversion computes the same initial identity for the same phone.</summary>
    [Fact]
    public void MigrationIsDeterministicAcrossEndpointChanges()
    {
        LegacyMigrationResult first = LegacyConfigurationMigrator.Migrate(Fixture("legacy-phone-usb.json"), null);
        LegacyMigrationResult second = LegacyConfigurationMigrator.Migrate(Fixture("legacy-phone-address.json"), null);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(Assert.Single(first.Configuration!.Profiles).Id, Assert.Single(second.Configuration!.Profiles).Id);
    }

    /// <summary>Older phone files infer automatic mode; settings alone never invent a profile.</summary>
    [Fact]
    public void MissingOptionalPhoneModeAndMissingPhoneAreSupported()
    {
        LegacyMigrationResult inferred = LegacyConfigurationMigrator.Migrate(Fixture("legacy-phone-inferred-mode.json"), null);
        LegacyMigrationResult settingsOnly = LegacyConfigurationMigrator.Migrate(null, Fixture("legacy-settings.json"));

        Assert.True(inferred.Succeeded);
        Assert.Equal(TransportPreference.Automatic, Assert.Single(inferred.Configuration!.Profiles).Connection.PreferredTransport);
        Assert.Equal("192.0.2.42:41235", Assert.Single(inferred.Configuration.Profiles).ConnectionEndpoint?.ToString());
        Assert.True(settingsOnly.Succeeded);
        Assert.Empty(settingsOnly.Configuration!.Profiles);
        Assert.Equal("60", settingsOnly.Configuration.Mirroring.Options["max-fps"].GetString());
    }

    /// <summary>Unknown, malformed, or unrepresentable legacy values stop the entire migration.</summary>
    [Theory]
    [InlineData("{broken", LegacyMigrationProblemCode.InvalidJson)]
    [InlineData("{\"UsbSerial\":\"TEST\",\"Mystery\":42}", LegacyMigrationProblemCode.UnsupportedField)]
    [InlineData("{\"UsbSerial\":\"TEST\",\"ConnectionMode\":\"wifi\"}", LegacyMigrationProblemCode.InconsistentTransport)]
    [InlineData("{\"UsbSerial\":\"TEST\",\"WirelessService\":\"bad endpoint\"}", LegacyMigrationProblemCode.UnsupportedValue)]
    [InlineData("{\"UsbSerial\":\"A:B\"}", LegacyMigrationProblemCode.UnsupportedValue)]
    [InlineData("{\"UsbSerial\":\"TEST\",\"WirelessService\":\"adb-OTHER-token._adb-tls-connect._tcp\",\"ConnectionMode\":\"wifi\"}", LegacyMigrationProblemCode.UnsupportedValue)]
    public void UnsafePhoneInputIsReportedWithoutPartialConfiguration(string phoneJson, LegacyMigrationProblemCode expectedCode)
    {
        LegacyMigrationResult result = LegacyConfigurationMigrator.Migrate(phoneJson, Fixture("legacy-settings.json"));

        Assert.False(result.Succeeded);
        Assert.Null(result.Configuration);
        Assert.Contains(result.Problems, problem => problem.Code == expectedCode);
    }

    /// <summary>Settings values that cannot be preserved fail instead of disappearing.</summary>
    [Fact]
    public void UnsupportedSettingsValueIsReported()
    {
        const string settingsJson = "{\"SchemaVersion\":1,\"Reconnect\":true,\"Options\":{\"max-fps\":60}}";

        LegacyMigrationResult result = LegacyConfigurationMigrator.Migrate(null, settingsJson);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, problem => problem.Code == LegacyMigrationProblemCode.UnsupportedValue);
    }

    /// <summary>Unknown JSON keys are never copied into diagnostic fields.</summary>
    [Fact]
    public void UnknownPropertyNameIsRedactedFromMigrationProblems()
    {
        const string secretLikeProperty = "synthetic-secret-123456";
        string phoneJson = "{\"UsbSerial\":\"TEST\",\"" + secretLikeProperty + "\":42}";

        LegacyMigrationResult result = LegacyConfigurationMigrator.Migrate(phoneJson, null);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(secretLikeProperty, string.Join(" ", result.Problems.Select(problem => problem.Field)));
    }

    /// <summary>Loads an immutable synthetic shape rather than user configuration.</summary>
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
