using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Core.Application.NativeHost;

/// <summary>Execution failures that remain separate from stored-document validity.</summary>
public enum NativeLaunchFailure
{
    None,
    UncommittedConfiguration,
    ProfileNotFound,
    DeviceUnavailable,
    TargetDoesNotMatchProfile,
    InvalidConnectionPlan,
    UnsupportedOption,
    UnsupportedReconnectCombination,
}

/// <summary>One immutable native request or a typed execution-blocking result.</summary>
public sealed record NativeLaunchPreparation(
    NativeStartRequest? Request,
    NativeLaunchFailure Failure,
    IReadOnlyList<OptionDiagnostic> OptionDiagnostics)
{
    public bool IsReady => Request is not null && Failure == NativeLaunchFailure.None;
}

/// <summary>Builds a native request only from an explicitly selected committed profile and observed ADB transport.</summary>
public static class NativeLaunchPreflight
{
    private static readonly HashSet<string> ReconnectIncompatibleSwitches =
        ["no-window", "no-playback", "no-video", "no-video-playback", "record", "time-limit", "otg"];
    private static readonly HashSet<string> AoaInputOptions = ["keyboard", "mouse", "gamepad"];

    /// <summary>Validates a detached persisted snapshot without mutating its options or selecting a device implicitly.</summary>
    public static NativeLaunchPreparation Prepare(
        ConfigurationV2 configuration,
        string? revision,
        ProfileId profileId,
        AdbDevice? selectedDevice,
        SessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(revision))
        {
            return Failed(NativeLaunchFailure.UncommittedConfiguration);
        }

        DeviceProfile? profile = configuration.Profiles.SingleOrDefault(item => item.Id == profileId);

        if (profile is null)
        {
            return Failed(NativeLaunchFailure.ProfileNotFound);
        }

        if (selectedDevice is null || selectedDevice.State != AdbDeviceState.Device)
        {
            return Failed(NativeLaunchFailure.DeviceUnavailable);
        }

        TransportKind? transport = ResolveTransport(profile, selectedDevice.Serial);

        if (transport is null)
        {
            return Failed(NativeLaunchFailure.TargetDoesNotMatchProfile);
        }

        if (!ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _))
        {
            return Failed(NativeLaunchFailure.InvalidConnectionPlan);
        }

        OptionSelectionResult options = OptionSelectionValidator.Evaluate(configuration.Mirroring);

        if (!options.IsValid)
        {
            return new NativeLaunchPreparation(null, NativeLaunchFailure.UnsupportedOption, options.Diagnostics);
        }

        NetworkEndpoint? reconnectEndpoint = configuration.Mirroring.Reconnect
            ? profile.ConnectionEndpoint
            : null;

        if (reconnectEndpoint is not null && HasReconnectIncompatibleOptions(configuration.Mirroring))
        {
            return Failed(NativeLaunchFailure.UnsupportedReconnectCombination);
        }

        NativeStartRequest request = new(sessionId, plan!, configuration.Mirroring,
            selectedDevice.Serial, transport.Value, reconnectEndpoint, revision);
        return new NativeLaunchPreparation(request, NativeLaunchFailure.None, []);
    }

    /// <summary>Matches USB and network selectors independently without treating one as physical identity proof.</summary>
    private static TransportKind? ResolveTransport(DeviceProfile profile, string serial)
    {
        if (profile.UsbIdentity is { } usb && string.Equals(serial, usb.Value, StringComparison.Ordinal))
        {
            return TransportKind.Usb;
        }

        if (profile.ConnectionEndpoint is { } endpoint &&
            string.Equals(serial, endpoint.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return TransportKind.Network;
        }

        return null;
    }

    /// <summary>Rejects settings that the current native in-process reconnect explicitly excludes.</summary>
    private static bool HasReconnectIncompatibleOptions(MirroringPreferences mirroring)
    {
        foreach ((string optionId, System.Text.Json.JsonElement value) in mirroring.Options)
        {
            bool enabledSwitch = value.ValueKind == System.Text.Json.JsonValueKind.True ||
                value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString()!.Length > 0;

            if (!enabledSwitch)
            {
                continue;
            }

            if (ReconnectIncompatibleSwitches.Contains(optionId) ||
                AoaInputOptions.Contains(optionId) && value.GetString() == "aoa")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns one structured rejection without inventing a launch request.</summary>
    private static NativeLaunchPreparation Failed(NativeLaunchFailure failure) => new(null, failure, []);
}
