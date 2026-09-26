using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Guards the committed launch boundary without a device or native process.</summary>
public sealed class NativeLaunchPreflightTests
{
    /// <summary>An explicit observed USB selector produces a detached request with raw native option spelling.</summary>
    [Fact]
    public void CommittedUsbSelectionKeepsRawOptionsAndRevision()
    {
        DeviceProfile profile = Profile();
        ConfigurationV2 configuration = Configuration(profile,
            new Dictionary<string, JsonElement> { ["max-size"] = JsonSerializer.SerializeToElement("0x400") });

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(configuration, "rev-one", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Equal("rev-one", result.Request!.ConfigurationRevision);
        Assert.Equal("SYNTHETIC_USB", result.Request.SelectedAdbSerial);
        Assert.Equal("0x400", result.Request.Mirroring.Options["max-size"].GetString());
        configuration.Mirroring.Options.Clear();
        Assert.Single(result.Request.Mirroring.Options);
    }

    /// <summary>No implicit first-device choice or unsaved profile can authorize native launch.</summary>
    [Fact]
    public void RequiresExplicitCommittedMatchingSelection()
    {
        DeviceProfile profile = Profile();
        ConfigurationV2 configuration = Configuration(profile);
        SessionId sessionId = SessionId.New();

        Assert.Equal(NativeLaunchFailure.UncommittedConfiguration,
            NativeLaunchPreflight.Prepare(configuration, null, profile.Id,
                new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), sessionId).Failure);
        Assert.Equal(NativeLaunchFailure.DeviceUnavailable,
            NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id, null, sessionId).Failure);
        Assert.Equal(NativeLaunchFailure.DeviceUnavailable,
            NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id,
                new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Unauthorized, null), sessionId).Failure);
        Assert.Equal(NativeLaunchFailure.TargetDoesNotMatchProfile,
            NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id,
                new AdbDevice("other:5555", AdbDeviceState.Device, null), sessionId).Failure);
    }

    /// <summary>A network ADB serial is a distinct transport and never becomes a USB identity.</summary>
    [Fact]
    public void NetworkSelectionUsesOnlySavedConnectionEndpoint()
    {
        DeviceProfile profile = Profile();
        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(Configuration(profile), "rev", profile.Id,
            new AdbDevice("127.0.0.1:5555", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Equal(TransportKind.Network, result.Request!.SelectedTransport);
        Assert.Equal("127.0.0.1:5555", result.Request.ReconnectEndpoint!.ToString());
        Assert.Equal("SYNTHETIC_USB", result.Request.Plan.Preferred.UsbSerial!.Value.Value);
    }

    /// <summary>USB recovery uses a saved network target only when fallback is permitted.</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "127.0.0.1:5555")]
    public void UsbNetworkRecoveryRequiresFallbackPermission(bool allowFallback, string? expectedEndpoint)
    {
        DeviceProfile profile = ProfileWithPolicy(allowFallback);

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(Configuration(profile), "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Equal(expectedEndpoint, result.Request!.ReconnectEndpoint?.ToString());
    }

    /// <summary>Network retry remains available when cross-transport fallback is disabled.</summary>
    [Fact]
    public void SelectedNetworkCanRetryItsEndpointWithoutCrossTransportFallback()
    {
        DeviceProfile profile = ProfileWithPolicy(allowFallback: false);

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(Configuration(profile), "rev", profile.Id,
            new AdbDevice("127.0.0.1:5555", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Equal(TransportKind.Network, result.Request!.SelectedTransport);
        Assert.Equal("127.0.0.1:5555", result.Request.ReconnectEndpoint!.ToString());
    }

    /// <summary>An explicit USB choice may use permitted network fallback despite a network preference.</summary>
    [Fact]
    public void SelectedUsbCanFallbackWhenNetworkIsPreferred()
    {
        DeviceProfile profile = ProfileWithPolicy(allowFallback: true,
            preferredTransport: TransportPreference.Network);

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(Configuration(profile), "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Equal(TransportKind.Usb, result.Request!.SelectedTransport);
        Assert.Equal("127.0.0.1:5555", result.Request.ReconnectEndpoint!.ToString());
    }

    /// <summary>Disabling reconnect leaves a saved address unused even with fallback permission.</summary>
    [Fact]
    public void ReconnectDisabledDoesNotEmitSavedEndpoint()
    {
        DeviceProfile profile = ProfileWithPolicy(allowFallback: true);

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(
            Configuration(profile, reconnect: false), "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Null(result.Request!.ReconnectEndpoint);
    }

    /// <summary>USB launch does not invent a network address from absent saved connection data.</summary>
    [Fact]
    public void UsbWithoutConnectionEndpointHasNoLegacyRecoveryTarget()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
        };

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(Configuration(profile), "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Null(result.Request!.ReconnectEndpoint);
    }

    /// <summary>An unused saved endpoint does not make USB recording incompatible with disabled fallback.</summary>
    [Fact]
    public void DisabledFallbackDoesNotRejectUsbRecordingForUnusedEndpoint()
    {
        DeviceProfile profile = ProfileWithPolicy(allowFallback: false);
        ConfigurationV2 configuration = Configuration(profile,
            new Dictionary<string, JsonElement> { ["record"] = JsonSerializer.SerializeToElement("synthetic.mp4") });

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.True(result.IsReady);
        Assert.Null(result.Request!.ReconnectEndpoint);
    }

    /// <summary>Unknown and app-managed options are preserved in storage but blocked for execution.</summary>
    [Theory]
    [InlineData("future-option", "value")]
    [InlineData("serial", "other-device")]
    public void UnsafeStoredOptionCannotReachNative(string optionId, string value)
    {
        DeviceProfile profile = Profile();
        ConfigurationV2 configuration = Configuration(profile,
            new Dictionary<string, JsonElement> { [optionId] = JsonSerializer.SerializeToElement(value) });

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.Equal(NativeLaunchFailure.UnsupportedOption, result.Failure);
        Assert.Null(result.Request);
        Assert.Single(result.OptionDiagnostics);
    }

    /// <summary>Current native reconnect excludes recording rather than silently removing the user's setting.</summary>
    [Fact]
    public void ReconnectRejectsLegacyIncompatibleRecording()
    {
        DeviceProfile profile = Profile();
        ConfigurationV2 configuration = Configuration(profile,
            new Dictionary<string, JsonElement> { ["record"] = JsonSerializer.SerializeToElement("synthetic.mp4") });

        NativeLaunchPreparation result = NativeLaunchPreflight.Prepare(configuration, "rev", profile.Id,
            new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());

        Assert.Equal(NativeLaunchFailure.UnsupportedReconnectCombination, result.Failure);
        Assert.Null(result.Request);
        Assert.Equal("synthetic.mp4", configuration.Mirroring.Options["record"].GetString());
    }

    /// <summary>Builds a synthetic saved identity with independent USB and network selectors.</summary>
    private static DeviceProfile Profile() => new()
    {
        Id = ProfileId.New(),
        UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
        ConnectionEndpoint = NetworkEndpoint.Parse("127.0.0.1:5555"),
    };

    /// <summary>Builds a saved USB/network profile with explicit fallback permission.</summary>
    private static DeviceProfile ProfileWithPolicy(bool allowFallback,
        TransportPreference preferredTransport = TransportPreference.Usb) => new()
    {
        Id = ProfileId.New(),
        UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
        ConnectionEndpoint = NetworkEndpoint.Parse("127.0.0.1:5555"),
        Connection = new ConnectionPreferences
        {
            PreferredTransport = preferredTransport,
            AllowFallback = allowFallback,
        },
    };

    /// <summary>Creates one detached, valid v2 snapshot for execution preflight.</summary>
    private static ConfigurationV2 Configuration(DeviceProfile profile,
        Dictionary<string, JsonElement>? options = null, bool reconnect = true)
        => new()
        {
            Profiles = [profile],
            Mirroring = new MirroringPreferences { Reconnect = reconnect, Options = options ?? [] },
        };
}
