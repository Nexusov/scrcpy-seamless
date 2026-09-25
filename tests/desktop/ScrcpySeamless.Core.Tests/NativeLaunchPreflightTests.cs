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

    /// <summary>Creates one detached, valid v2 snapshot for execution preflight.</summary>
    private static ConfigurationV2 Configuration(DeviceProfile profile, Dictionary<string, JsonElement>? options = null)
        => new()
        {
            Profiles = [profile],
            Mirroring = new MirroringPreferences { Options = options ?? [] },
        };
}
