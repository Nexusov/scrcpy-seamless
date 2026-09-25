using ScrcpySeamless.Core;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Evidence about a channel, without inferring readiness from process existence.</summary>
public enum ChannelEvidence { Unverified, ObservedReady, NotReady }

/// <summary>Evidence about the native stream, separate from transport availability.</summary>
public enum StreamEvidence { Unverified, ObservedStreaming, Recovering, Failed }

/// <summary>Presentation facts supplied by a real adapter later or synthetic preview data now.</summary>
public sealed record DevicePresentation(
    string StableId,
    string DisplayName,
    DeviceAvailability Availability,
    bool UsbAvailable,
    bool WirelessPrepared,
    TransportKind? SelectedTransport,
    bool NativeProcessRunning,
    StreamEvidence Stream,
    ChannelEvidence Video,
    ChannelEvidence Audio,
    ChannelEvidence Control,
    string HintResourceKey);

/// <summary>A deterministic preview case with a stable machine identity.</summary>
public sealed record DeviceScenario(string Id, string LabelResourceKey, DevicePresentation? Device);

/// <summary>Supplies presentation snapshots without an ADB or native-process dependency.</summary>
public interface IDevicePresentationSource
{
    bool IsPreview { get; }

    IReadOnlyList<DeviceScenario> Scenarios { get; }
}

/// <summary>Holds immutable examples or a truthful empty normal-start state.</summary>
public sealed class StaticDevicePresentationSource : IDevicePresentationSource
{
    private StaticDevicePresentationSource(bool isPreview, IReadOnlyList<DeviceScenario> scenarios)
    {
        IsPreview = isPreview;
        Scenarios = scenarios;
    }

    public bool IsPreview { get; }

    public IReadOnlyList<DeviceScenario> Scenarios { get; }

    /// <summary>Creates a normal startup without invented discovery results.</summary>
    public static StaticDevicePresentationSource Empty() => new(false, []);

    /// <summary>Creates fixed synthetic cases with no external adapter construction.</summary>
    public static StaticDevicePresentationSource Preview(PresentationText text)
    {
        const string deviceId = "preview-device-7a31";
        string deviceName = text.Get("devices.preview.name");

        return new(true, Array.AsReadOnly(new[]
        {
            new DeviceScenario("empty", "devices.scenario.empty", null),
            new DeviceScenario("usb", "devices.scenario.usb", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Available, true, false, TransportKind.Usb,
                false, StreamEvidence.Unverified, ChannelEvidence.Unverified, ChannelEvidence.Unverified,
                ChannelEvidence.Unverified, "devices.hint.usb")),
            new DeviceScenario("fallback", "devices.scenario.fallback", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Available, true, true, TransportKind.Usb,
                true, StreamEvidence.ObservedStreaming, ChannelEvidence.ObservedReady,
                ChannelEvidence.ObservedReady, ChannelEvidence.ObservedReady, "devices.hint.fallback")),
            new DeviceScenario("unauthorized", "devices.scenario.unauthorized", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Unauthorized, true, false, null,
                false, StreamEvidence.Unverified, ChannelEvidence.Unverified, ChannelEvidence.Unverified,
                ChannelEvidence.Unverified, "devices.hint.unauthorized")),
            new DeviceScenario("offline", "devices.scenario.offline", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Offline, false, false, null,
                false, StreamEvidence.Unverified, ChannelEvidence.Unverified, ChannelEvidence.Unverified,
                ChannelEvidence.Unverified, "devices.hint.offline")),
            new DeviceScenario("failure", "devices.scenario.failure", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Available, true, false, TransportKind.Usb,
                false, StreamEvidence.Failed, ChannelEvidence.NotReady, ChannelEvidence.Unverified,
                ChannelEvidence.Unverified, "devices.hint.failure")),
            new DeviceScenario("reconnect", "devices.scenario.reconnect", new DevicePresentation(
                deviceId, deviceName, DeviceAvailability.Available, false, true, TransportKind.Network,
                true, StreamEvidence.Recovering, ChannelEvidence.ObservedReady, ChannelEvidence.Unverified,
                ChannelEvidence.ObservedReady, "devices.hint.reconnect")),
        }));
    }
}
