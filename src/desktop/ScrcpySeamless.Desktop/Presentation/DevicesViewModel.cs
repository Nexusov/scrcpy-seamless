using System.Collections.ObjectModel;
using ScrcpySeamless.Core;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Localized picker item retaining the scenario's stable identity.</summary>
public sealed record DeviceScenarioChoice(DeviceScenario Scenario, string Label);

/// <summary>A labeled status fact with a stable automation identity.</summary>
public sealed record StatusMetric(string AutomationId, string Label, string Value);

/// <summary>Projects one device's independent availability, transport and session evidence.</summary>
public sealed class DeviceCardViewModel
{
    public DeviceCardViewModel(DevicePresentation device, PresentationText text, bool isPreview)
    {
        StableId = device.StableId;
        DisplayName = device.DisplayName;
        Availability = text.Get($"devices.availability.{device.Availability.ToString().ToLowerInvariant()}");
        SelectedTransport = text.Get(device.SelectedTransport switch
        {
            TransportKind.Usb => "devices.transport.usb",
            TransportKind.Network => "devices.transport.network",
            _ => "devices.transport.none",
        });
        Usb = text.Get(device.UsbAvailable ? "devices.usb.available" : "devices.usb.unavailable");
        Wireless = text.Get(device.WirelessPrepared ? "devices.wireless.prepared" : "devices.wireless.unprepared");
        NativeProcess = text.Get(device.NativeProcessRunning ? "devices.process.running" : "devices.process.stopped");
        Streaming = text.Get(device.Stream switch
        {
            StreamEvidence.ObservedStreaming => "devices.stream.observed",
            StreamEvidence.Recovering => "devices.stream.recovering",
            StreamEvidence.Failed => "devices.stream.failed",
            _ => "devices.stream.unknown",
        });
        Video = ChannelText(device.Video, text);
        Audio = ChannelText(device.Audio, text);
        Control = ChannelText(device.Control, text);
        Hint = text.Get(device.HintResourceKey);
        AvailableHeading = text.Get("devices.section.available");
        SessionHeading = text.Get("devices.section.session");
        PreviewNote = text.Get("devices.recovery.note");
        IsProblem = device.Availability is DeviceAvailability.Offline or DeviceAvailability.Unauthorized ||
            device.Stream == StreamEvidence.Failed;
        IsInformational = !IsProblem;
        IsPreview = isPreview;
        AvailabilityMetrics =
        [
            new($"device.{StableId}.availability", text.Get("devices.availability"), Availability),
            new($"device.{StableId}.transport", text.Get("devices.transport"), SelectedTransport),
            new($"device.{StableId}.usb", text.Get("devices.usb"), Usb),
            new($"device.{StableId}.wireless", text.Get("devices.wireless"), Wireless),
        ];
        SessionMetrics =
        [
            new($"device.{StableId}.process", text.Get("devices.process"), NativeProcess),
            new($"device.{StableId}.stream", text.Get("devices.streaming"), Streaming),
            new($"device.{StableId}.video", text.Get("devices.video"), Video),
            new($"device.{StableId}.audio", text.Get("devices.audio"), Audio),
            new($"device.{StableId}.control", text.Get("devices.control"), Control),
        ];
    }

    public string StableId { get; }
    public string DisplayName { get; }
    public string Availability { get; }
    public string SelectedTransport { get; }
    public string Usb { get; }
    public string Wireless { get; }
    public string NativeProcess { get; }
    public string Streaming { get; }
    public string Video { get; }
    public string Audio { get; }
    public string Control { get; }
    public string Hint { get; }
    public string AvailableHeading { get; }
    public string SessionHeading { get; }
    public string PreviewNote { get; }
    public bool IsProblem { get; }
    public bool IsInformational { get; }
    public bool IsPreview { get; }
    public IReadOnlyList<StatusMetric> AvailabilityMetrics { get; }
    public IReadOnlyList<StatusMetric> SessionMetrics { get; }

    /// <summary>Displays an evidence category without interpreting unknown as ready.</summary>
    private static string ChannelText(ChannelEvidence evidence, PresentationText text)
    {
        return text.Get(evidence switch
        {
            ChannelEvidence.ObservedReady => "devices.ready",
            ChannelEvidence.NotReady => "devices.notReady",
            _ => "devices.unverified",
        });
    }
}

/// <summary>Owns the Devices workspace and deterministic preview selection.</summary>
public sealed class DevicesViewModel : ObservableViewModel
{
    private readonly PresentationText text;
    private DeviceScenarioChoice? selectedScenario;

    public DevicesViewModel(IDevicePresentationSource source, PresentationText text)
    {
        this.text = text;
        IsPreview = source.IsPreview;
        Scenarios = source.Scenarios
            .Select(scenario => new DeviceScenarioChoice(scenario, text.Get(scenario.LabelResourceKey)))
            .ToArray();
        Cards = [];
        Eyebrow = text.Get("devices.eyebrow");
        Title = text.Get("devices.title");
        Subtitle = text.Get("devices.subtitle");
        ScenarioLabel = text.Get("devices.previewScenario");
        EmptyTitle = text.Get("devices.empty.title");
        EmptyBody = text.Get(source.IsPreview ? "devices.empty.previewBody" : "devices.empty.body");
        AvailableHeading = text.Get("devices.section.available");
        SessionHeading = text.Get("devices.section.session");
        AvailabilityLabel = text.Get("devices.availability");
        TransportLabel = text.Get("devices.transport");
        UsbLabel = text.Get("devices.usb");
        WirelessLabel = text.Get("devices.wireless");
        ProcessLabel = text.Get("devices.process");
        StreamingLabel = text.Get("devices.streaming");
        VideoLabel = text.Get("devices.video");
        AudioLabel = text.Get("devices.audio");
        ControlLabel = text.Get("devices.control");
        RecoveryNote = text.Get("devices.recovery.note");
        ActionUnavailable = text.Get("devices.action.unavailable");
        SelectedScenario = Scenarios.FirstOrDefault();
    }

    public bool IsPreview { get; }
    public IReadOnlyList<DeviceScenarioChoice> Scenarios { get; }
    public ObservableCollection<DeviceCardViewModel> Cards { get; }
    public string Eyebrow { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ScenarioLabel { get; }
    public string EmptyTitle { get; }
    public string EmptyBody { get; }
    public string AvailableHeading { get; }
    public string SessionHeading { get; }
    public string AvailabilityLabel { get; }
    public string TransportLabel { get; }
    public string UsbLabel { get; }
    public string WirelessLabel { get; }
    public string ProcessLabel { get; }
    public string StreamingLabel { get; }
    public string VideoLabel { get; }
    public string AudioLabel { get; }
    public string ControlLabel { get; }
    public string RecoveryNote { get; }
    public string ActionUnavailable { get; }
    public bool IsEmpty => Cards.Count == 0;

    public DeviceScenarioChoice? SelectedScenario
    {
        get => selectedScenario;
        set
        {
            if (!SetProperty(ref selectedScenario, value))
            {
                return;
            }

            Cards.Clear();

            if (value?.Scenario.Device is DevicePresentation device)
            {
                Cards.Add(new DeviceCardViewModel(device, text, IsPreview));
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Selects one fixed case by stable ID without a timer or external effect.</summary>
    public void SelectScenario(string scenarioId)
    {
        SelectedScenario = Scenarios.FirstOrDefault(choice => choice.Scenario.Id == scenarioId)
            ?? throw new ArgumentException($"Unknown preview scenario: {scenarioId}", nameof(scenarioId));
    }
}
