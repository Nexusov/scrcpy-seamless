using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects explicit device selection and secret-free pairing presentation.</summary>
public sealed class LiveDevicesTests
{
    /// <summary>Attaching live services is inert until a user asks for refresh.</summary>
    [Fact]
    public void AttachmentNeverDiscoversOrPairsImplicitly()
    {
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);

        Assert.True(devices.IsLiveEnabled);
        Assert.Equal(LiveDiscoveryState.Initial, devices.DiscoveryState);
        Assert.Null(devices.SelectedObservedDevice);
        Assert.Equal(0, gateway.DiscoveryCalls);
        Assert.Equal(0, gateway.PairingCalls);
        Assert.Throws<InvalidOperationException>(() =>
            new DevicesViewModel(StaticDevicePresentationSource.Preview(new PresentationText()), new PresentationText())
                .AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway), action => action()));
    }

    /// <summary>Observed transports remain distinct and multiple candidates never select themselves.</summary>
    [Fact]
    public async Task RefreshRequiresExplicitCandidateSelectionAndReportsDeviceStates()
    {
        FakeGateway gateway = new()
        {
            Devices = AdbResult<IReadOnlyList<AdbDevice>>.Success(
            [
                new AdbDevice("usb-a", AdbDeviceState.Device, "Phone"),
                new AdbDevice("usb-b", AdbDeviceState.Unauthorized, null),
                new AdbDevice("usb-c", AdbDeviceState.Offline, null),
            ]),
        };
        DevicesViewModel devices = CreateDevices(gateway);

        await devices.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LiveDiscoveryState.Ready, devices.DiscoveryState);
        Assert.Equal(3, devices.ObservedDevices.Count);
        Assert.Null(devices.SelectedObservedDevice);
        Assert.Contains(devices.ObservedDevices, choice => choice.Device.State == AdbDeviceState.Unauthorized);
        Assert.Contains(devices.ObservedDevices, choice => choice.Device.State == AdbDeviceState.Offline);
        devices.SelectedDeviceChoice = devices.ObservedDevices[0];
        Assert.Equal("usb-a", devices.SelectedObservedDevice?.Serial);

        gateway.Devices = AdbResult<IReadOnlyList<AdbDevice>>.Error(AdbFailureKind.Unavailable);
        await devices.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LiveDiscoveryState.Error, devices.DiscoveryState);
        Assert.Equal(3, devices.ObservedDevices.Count);
        Assert.True(devices.IsObservationStale);
        Assert.Null(devices.SelectedObservedDevice);
    }

    /// <summary>A superseded refresh cannot overwrite the latest observation or selection.</summary>
    [Fact]
    public async Task SupersededRefreshCannotReplaceCurrentResults()
    {
        TaskCompletionSource<AdbResult<IReadOnlyList<AdbDevice>>> first = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new();
        gateway.DeviceResults.Enqueue(_ => first.Task);
        gateway.DeviceResults.Enqueue(_ => Task.FromResult(AdbResult<IReadOnlyList<AdbDevice>>.Success(
            [new AdbDevice("latest", AdbDeviceState.Device, null)])));
        DevicesViewModel devices = CreateDevices(gateway);

        Task stale = devices.RefreshAsync(TestContext.Current.CancellationToken);
        await devices.RefreshAsync(TestContext.Current.CancellationToken);
        devices.SelectedDeviceChoice = Assert.Single(devices.ObservedDevices);
        first.SetResult(AdbResult<IReadOnlyList<AdbDevice>>.Success(
            [new AdbDevice("stale", AdbDeviceState.Device, null)]));
        await stale;

        Assert.Equal("latest", devices.SelectedObservedDevice?.Serial);
        Assert.Equal("latest", Assert.Single(devices.ObservedDevices).Device.Serial);
    }

    /// <summary>Pairing keeps the code transient and a partial connect result distinct from profile save.</summary>
    [Fact]
    public async Task PairingSuccessAndConnectFailureRemainDistinctWithoutSaving()
    {
        FakeGateway gateway = new()
        {
            PairingResult = AdbResult<bool>.Success(true),
            ConnectionResult = AdbResult<bool>.Error(AdbFailureKind.ConnectionRejected),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.ManualPairingEndpoint = "phone.local:37123";
        devices.ManualConnectionEndpoint = "phone.local:39123";
        devices.PairingCode = "123456";

        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, devices.PairingCode);
        Assert.True(devices.PairingOutcome?.Paired);
        Assert.Null(devices.PairingOutcome?.ConnectedEndpoint);
        Assert.Contains("connection or verification failed", devices.PairingMessage);
        Assert.DoesNotContain("123456", devices.PairingMessage);
        Assert.Equal("phone.local:37123", gateway.PairedEndpoint?.ToString());
        Assert.Equal("phone.local:39123", gateway.ConnectedEndpoint?.ToString());
    }

    /// <summary>A successful connection shows its endpoint but never claims profile persistence.</summary>
    [Fact]
    public async Task SuccessfulConnectionOffersEndpointForExplicitProfileEdit()
    {
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);
        devices.ManualPairingEndpoint = "phone.local:37123";
        devices.ManualConnectionEndpoint = "phone.local:39123";
        devices.PairingCode = "123456";

        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal("phone.local:39123", devices.PairingOutcome?.ConnectedEndpoint?.ToString());
        Assert.Contains("phone.local:39123", devices.ConnectedEndpointLabel);
        Assert.Contains("Profiles editor", devices.ConnectedEndpointLabel);
        Assert.Contains("Save a profile explicitly", devices.PairingMessage);
        Assert.Equal(1, gateway.DiscoveryCalls);
    }

    /// <summary>Late pairing completion after cancellation cannot replace the current status.</summary>
    [Fact]
    public async Task CancelledPairingDoesNotPublishLateSuccess()
    {
        TaskCompletionSource<AdbResult<bool>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new() { PairAsyncOverride = (_, _, _) => completion.Task };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.ManualPairingEndpoint = "phone.local:37123";
        devices.PairingCode = "123456";

        Task pending = devices.PairAsync(TestContext.Current.CancellationToken);
        devices.CancelPair();
        completion.SetResult(AdbResult<bool>.Success(true));
        await pending;

        Assert.Null(devices.PairingOutcome);
        Assert.Contains("cancelled", devices.PairingMessage);
        Assert.Equal(string.Empty, devices.PairingCode);
    }

    /// <summary>Constructs a normal view model with fake ADB and an immediate test dispatcher.</summary>
    private static DevicesViewModel CreateDevices(FakeGateway gateway)
    {
        DevicesViewModel devices = new(StaticDevicePresentationSource.Empty(),
            new PresentationText());
        devices.AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway),
            action => action());
        return devices;
    }

    /// <summary>Provides deterministic ADB responses without a daemon or phone.</summary>
    private sealed class FakeGateway : IAdbGateway
    {
        public Queue<Func<CancellationToken, Task<AdbResult<IReadOnlyList<AdbDevice>>>>> DeviceResults { get; } = new();
        public AdbResult<IReadOnlyList<AdbDevice>> Devices { get; set; } =
            AdbResult<IReadOnlyList<AdbDevice>>.Success([]);
        public AdbResult<bool> PairingResult { get; set; } = AdbResult<bool>.Success(true);
        public AdbResult<bool> ConnectionResult { get; set; } = AdbResult<bool>.Success(true);
        public Func<NetworkEndpoint, string, CancellationToken, Task<AdbResult<bool>>>? PairAsyncOverride { get; set; }
        public int DiscoveryCalls { get; private set; }
        public int PairingCalls { get; private set; }
        public NetworkEndpoint? PairedEndpoint { get; private set; }
        public NetworkEndpoint? ConnectedEndpoint { get; private set; }

        public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            return DeviceResults.Count == 0 ? Task.FromResult(Devices) : DeviceResults.Dequeue()(cancellationToken);
        }

        public Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbMdnsService>>.Success([]));

        public Task<AdbResult<bool>> PairAsync(NetworkEndpoint pairingEndpoint, string pairingCode,
            CancellationToken cancellationToken)
        {
            PairingCalls++;
            PairedEndpoint = pairingEndpoint;
            return PairAsyncOverride?.Invoke(pairingEndpoint, pairingCode, cancellationToken)
                ?? Task.FromResult(PairingResult);
        }

        public Task<AdbResult<bool>> ConnectAsync(NetworkEndpoint connectionEndpoint,
            CancellationToken cancellationToken)
        {
            ConnectedEndpoint = connectionEndpoint;
            return Task.FromResult(ConnectionResult);
        }

        public Task<AdbResult<string>> GetDeviceSerialPropertyAsync(NetworkEndpoint connectionEndpoint,
            CancellationToken cancellationToken) => Task.FromResult(AdbResult<string>.Success("observed-serial"));
    }
}
