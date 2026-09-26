using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects explicit device selection and secret-free pairing presentation.</summary>
public sealed class LiveDevicesTests
{
    /// <summary>A manual connection endpoint works without mDNS or a new pairing operation.</summary>
    [Fact]
    public async Task ManualConnectKeepsProfilePersistenceExplicit()
    {
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.ShowManualConnection();
        devices.ManualConnectionEndpoint = "phone.local:38211";

        Assert.True(devices.CanConnect);
        await devices.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal("phone.local:38211", gateway.ConnectedEndpoint?.ToString());
        Assert.Equal(0, gateway.PairingCalls);
        Assert.Contains("Save the endpoint", devices.ConnectionMessage);
        Assert.Contains("phone.local:38211", devices.ConnectedEndpointLabel);
        Assert.Empty(devices.ObservedDevices);
    }

    /// <summary>Close cancellation prevents a late connect result from updating the workspace.</summary>
    [Fact]
    public async Task ShutdownCancelsManualConnectAndItsLatePublication()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new()
        {
            ConnectAsyncOverride = async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AdbResult<bool>.Success(true);
            },
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.ShowManualConnection();
        devices.ManualConnectionEndpoint = "phone.local:38211";
        Task connecting = devices.ConnectAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(await devices.ShutdownLiveAsync(TestContext.Current.CancellationToken));
        await connecting;
        Assert.False(devices.IsLiveEnabled);
        Assert.Null(devices.ConnectedEndpointLabel);
    }

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

    /// <summary>Only entering wireless setup starts discovery, never pairing.</summary>
    [Fact]
    public void OpeningWirelessSetupStartsOneSnapshotWithoutPairing()
    {
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);

        devices.OpenWirelessSetup();

        Assert.True(devices.IsWirelessSetupOpen);
        Assert.Equal(1, gateway.ServiceCalls);
        Assert.Equal(1, gateway.DiscoveryCalls);
        Assert.Equal(0, gateway.PairingCalls);
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

    /// <summary>A single fresh pairing advertisement becomes the visible target.</summary>
    [Fact]
    public async Task RefreshPreselectsOnePairingServiceWithoutChoosingAConnectService()
    {
        FakeGateway gateway = new()
        {
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
            [
                new AdbMdnsService("pair", AdbServiceKind.Pairing,
                    NetworkEndpoint.Parse("192.0.2.8:37123")),
                new AdbMdnsService("connect", AdbServiceKind.Connection,
                    NetworkEndpoint.Parse("192.0.2.8:39123")),
            ]),
        };
        DevicesViewModel devices = CreateDevices(gateway);

        devices.OpenWirelessSetup();

        Assert.Equal("192.0.2.8:37123", devices.SelectedPairingService?.Service.Endpoint.ToString());
        devices.PairingCode = "123456";
        Assert.True(devices.CanPair);
        Assert.Equal(1, gateway.ServiceCalls);
    }

    /// <summary>Successful empty pairing discovery stays distinct from a visible USB transport.</summary>
    [Fact]
    public void EmptyPairingDiscoveryExplainsTheManualFallback()
    {
        FakeGateway gateway = new()
        {
            Devices = AdbResult<IReadOnlyList<AdbDevice>>.Success(
                [new AdbDevice("usb-a", AdbDeviceState.Device, "Phone")]),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.PairingCode = "123456";

        Assert.Equal(LiveDiscoveryState.Ready, devices.DiscoveryState);
        Assert.Contains("No pairing device found", devices.PairingDiscoveryStatusLabel);
        Assert.False(devices.CanPair);
        Assert.Contains("No pairing device found", devices.PairEligibilityMessage);

        devices.ShowManualPairing();
        devices.ManualPairingEndpoint = "192.0.2.8:37123";
        devices.PairingCode = "123456";
        Assert.True(devices.CanPair);
    }

    /// <summary>Two pairing targets require a choice; a connect-only row cannot be paired.</summary>
    [Fact]
    public async Task MultiplePairingCandidatesRequireAnExplicitTarget()
    {
        FakeGateway gateway = new()
        {
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
            [
                PairingService("pair-a", "192.0.2.8:37123"),
                PairingService("pair-b", "192.0.2.9:37124"),
                new AdbMdnsService("connect-only", AdbServiceKind.Connection,
                    NetworkEndpoint.Parse("192.0.2.10:39123")),
            ]),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.PairingCode = "123456";

        Assert.Equal(2, devices.PairingServices.Count);
        Assert.Null(devices.SelectedPairingService);
        Assert.False(devices.CanPair);
        Assert.Contains("Choose the device", devices.PairEligibilityMessage);
        devices.SelectedPairingService = devices.PairingServices[1];
        Assert.Equal(string.Empty, devices.PairingCode);
        devices.PairingCode = "123456";
        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal("192.0.2.9:37124", gateway.PairedEndpoint?.ToString());
        Assert.Null(gateway.ConnectedEndpoint);
    }

    /// <summary>A hidden manual address never overrides a visible discovered target.</summary>
    [Fact]
    public async Task ManualPairingRequiresAnExplicitSourceAndValidAddress()
    {
        FakeGateway gateway = new()
        {
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
                [PairingService("pair", "192.0.2.8:37123")]),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.ManualPairingEndpoint = "not-an-endpoint";
        devices.PairingCode = "123456";
        Assert.True(devices.CanPair);

        devices.ShowManualPairing();
        Assert.Equal(string.Empty, devices.PairingCode);
        devices.PairingCode = "123456";
        Assert.False(devices.CanPair);
        Assert.Contains("valid pairing address", devices.PairEligibilityMessage);
        devices.ManualPairingEndpoint = "192.0.2.9:37124";
        devices.PairingCode = "123456";
        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal("192.0.2.9:37124", gateway.PairedEndpoint?.ToString());
    }

    /// <summary>A disappeared service cannot silently reuse a code for a replacement target.</summary>
    [Fact]
    public async Task ReplacedPairingServiceClearsTheCodeAndRequiresSelection()
    {
        FakeGateway gateway = new()
        {
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
                [PairingService("pair-a", "192.0.2.8:37123")]),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        devices.PairingCode = "123456";
        gateway.Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
            [PairingService("pair-b", "192.0.2.9:37124")]);

        await devices.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Null(devices.SelectedPairingService);
        Assert.Equal(string.Empty, devices.PairingCode);
        Assert.False(devices.CanPair);
        Assert.Contains("changed", devices.PairEligibilityMessage);
        devices.SelectedPairingService = Assert.Single(devices.PairingServices);
        Assert.Contains("six-digit code", devices.PairEligibilityMessage);
        Assert.Equal(0, gateway.PairingCalls);
    }

    /// <summary>A still-advertised selected target survives refresh with its code unchanged.</summary>
    [Fact]
    public async Task RefreshPreservesTheSamePairingTarget()
    {
        FakeGateway gateway = new()
        {
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
                [PairingService("pair-a", "192.0.2.8:37123")]),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        ObservedServiceChoice chosen = Assert.Single(devices.PairingServices);
        devices.PairingCode = "123456";

        await devices.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Same(chosen, devices.SelectedPairingService);
        Assert.Equal("123456", devices.PairingCode);
        Assert.True(devices.CanPair);
    }

    /// <summary>mDNS failure does not erase a valid USB observation or become an empty result.</summary>
    [Fact]
    public void PairingDiscoveryFailureRemainsDistinctFromTransportDiscovery()
    {
        FakeGateway gateway = new()
        {
            Devices = AdbResult<IReadOnlyList<AdbDevice>>.Success(
                [new AdbDevice("usb-a", AdbDeviceState.Device, "Phone")]),
            Services = AdbResult<IReadOnlyList<AdbMdnsService>>.Error(AdbFailureKind.TimedOut),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();

        Assert.Equal(LiveDiscoveryState.Ready, devices.DiscoveryState);
        Assert.Contains("discovery failed", devices.PairingDiscoveryStatusLabel);
        Assert.False(devices.CanPair);
        Assert.Equal(1, gateway.ServiceCalls);
    }

    /// <summary>A superseded refresh settles before the next ADB snapshot begins.</summary>
    [Fact]
    public async Task SupersededRefreshWaitsForTheCancelledSnapshot()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new();
        gateway.DeviceResults.Enqueue(async cancellationToken =>
        {
            firstStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AdbResult<IReadOnlyList<AdbDevice>>.Success(
                [new AdbDevice("stale", AdbDeviceState.Device, null)]);
        });
        gateway.DeviceResults.Enqueue(_ => Task.FromResult(AdbResult<IReadOnlyList<AdbDevice>>.Success(
            [new AdbDevice("latest", AdbDeviceState.Device, null)])));
        DevicesViewModel devices = CreateDevices(gateway);

        Task stale = devices.RefreshAsync(TestContext.Current.CancellationToken);
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await devices.RefreshAsync(TestContext.Current.CancellationToken);
        devices.SelectedDeviceChoice = Assert.Single(devices.ObservedDevices);
        await stale;

        Assert.Equal("latest", devices.SelectedObservedDevice?.Serial);
        Assert.Equal("latest", Assert.Single(devices.ObservedDevices).Device.Serial);
    }

    /// <summary>Pairing waits until a cancelled ADB discovery command has released its process slot.</summary>
    [Fact]
    public async Task PairingWaitsForCancellationResistantDiscovery()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<AdbResult<IReadOnlyList<AdbDevice>>> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new();
        gateway.DeviceResults.Enqueue(_ =>
        {
            started.SetResult();
            return release.Task;
        });
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        devices.CancelRefresh();
        devices.ShowManualPairing();
        devices.ManualPairingEndpoint = "192.0.2.8:37123";
        devices.PairingCode = "123456";

        Task pendingPair = devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.PairingCalls);
        Assert.False(pendingPair.IsCompleted);
        release.SetResult(AdbResult<IReadOnlyList<AdbDevice>>.Success([]));
        await pendingPair.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, gateway.PairingCalls);
    }

    /// <summary>Shutdown waits for every old snapshot, not only the newest queued refresh.</summary>
    [Fact]
    public async Task ShutdownWaitsForSupersededCancellationResistantDiscovery()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<AdbResult<IReadOnlyList<AdbDevice>>> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new();
        gateway.DeviceResults.Enqueue(_ =>
        {
            started.SetResult();
            return release.Task;
        });
        DevicesViewModel devices = CreateDevices(gateway);
        Task first = devices.RefreshAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task second = devices.RefreshAsync(TestContext.Current.CancellationToken);

        Task<bool> shutdown = devices.ShutdownLiveAsync(TestContext.Current.CancellationToken);
        await second.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(shutdown.IsCompleted);
        release.SetResult(AdbResult<IReadOnlyList<AdbDevice>>.Success([]));
        await first.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(await shutdown.WaitAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Closing setup cannot cancel a separate transport refresh or hide an active pair.</summary>
    [Fact]
    public async Task ClosingWirelessSetupKeepsOtherOperationsVisibleAndOwned()
    {
        TaskCompletionSource<AdbResult<IReadOnlyList<AdbDevice>>> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);
        devices.OpenWirelessSetup();
        gateway.DeviceResults.Enqueue(_ => release.Task);
        Task refresh = devices.RefreshAsync(TestContext.Current.CancellationToken);

        devices.CloseWirelessSetup();

        Assert.False(devices.IsWirelessSetupOpen);
        Assert.True(devices.IsDiscovering);
        release.SetResult(AdbResult<IReadOnlyList<AdbDevice>>.Success([]));
        await refresh.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LiveDiscoveryState.Empty, devices.DiscoveryState);

        devices.OpenWirelessSetup();
        devices.ShowManualPairing();
        devices.ManualPairingEndpoint = "192.0.2.8:37123";
        devices.PairingCode = "123456";
        TaskCompletionSource<AdbResult<bool>> pairRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.PairAsyncOverride = (_, _, _) => pairRelease.Task;
        Task pair = devices.PairAsync(TestContext.Current.CancellationToken);
        devices.CloseWirelessSetup();

        Assert.True(devices.IsWirelessSetupOpen);
        Assert.False(devices.CanCloseWirelessSetup);
        pairRelease.SetResult(AdbResult<bool>.Success(true));
        await pair.WaitAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Pairing stays separate from even an explicitly entered connection endpoint.</summary>
    [Fact]
    public async Task PairingSuccessDoesNotConnectOrSaveAutomatically()
    {
        FakeGateway gateway = new()
        {
            PairingResult = AdbResult<bool>.Success(true),
            ConnectionResult = AdbResult<bool>.Error(AdbFailureKind.ConnectionRejected),
        };
        DevicesViewModel devices = CreateDevices(gateway);
        PrepareManualPairing(devices, "phone.local:37123");
        devices.ShowManualConnection();
        devices.ManualConnectionEndpoint = "phone.local:39123";

        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, devices.PairingCode);
        Assert.True(devices.PairingOutcome?.Paired);
        Assert.Null(devices.PairingOutcome?.ConnectedEndpoint);
        Assert.Contains("Connection and profile save remain separate", devices.PairingMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", devices.PairingMessage);
        Assert.Equal("phone.local:37123", gateway.PairedEndpoint?.ToString());
        Assert.Null(gateway.ConnectedEndpoint);
    }

    /// <summary>A successful connection shows its endpoint but never claims profile persistence.</summary>
    [Fact]
    public async Task SuccessfulConnectionOffersEndpointForExplicitProfileEdit()
    {
        FakeGateway gateway = new();
        DevicesViewModel devices = CreateDevices(gateway);
        PrepareManualPairing(devices, "phone.local:37123");
        devices.ShowManualConnection();
        devices.ManualConnectionEndpoint = "phone.local:39123";

        await devices.PairAsync(TestContext.Current.CancellationToken);
        await devices.RefreshAsync(TestContext.Current.CancellationToken);
        await devices.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Null(devices.PairingOutcome?.ConnectedEndpoint);
        Assert.Contains("phone.local:39123", devices.ConnectedEndpointLabel);
        Assert.Contains("Profiles editor", devices.ConnectedEndpointLabel);
        Assert.Contains("Save the endpoint", devices.ConnectionMessage);
        Assert.True(gateway.DiscoveryCalls >= 1);
    }

    /// <summary>Late pairing completion after cancellation cannot replace the current status.</summary>
    [Fact]
    public async Task CancelledPairingDoesNotPublishLateSuccess()
    {
        TaskCompletionSource<AdbResult<bool>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new() { PairAsyncOverride = (_, _, _) => completion.Task };
        DevicesViewModel devices = CreateDevices(gateway);
        PrepareManualPairing(devices, "phone.local:37123");

        Task pending = devices.PairAsync(TestContext.Current.CancellationToken);
        devices.CancelPair();
        completion.SetResult(AdbResult<bool>.Success(true));
        await pending;

        Assert.Null(devices.PairingOutcome);
        Assert.Contains("cancelled", devices.PairingMessage);
        Assert.Equal(string.Empty, devices.PairingCode);
    }

    /// <summary>Shutdown cancels the active direct ADB command and waits for its settlement.</summary>
    [Fact]
    public async Task ShutdownCancelsAndAwaitsOwnedPairing()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeGateway gateway = new()
        {
            PairAsyncOverride = async (_, _, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AdbResult<bool>.Success(true);
            },
        };
        DevicesViewModel devices = CreateDevices(gateway);
        PrepareManualPairing(devices, "phone.local:37123");
        Task pending = devices.PairAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        bool settled = await devices.ShutdownLiveAsync(TestContext.Current.CancellationToken);
        await pending;

        Assert.True(settled);
        Assert.False(devices.IsLiveEnabled);
        Assert.Null(devices.PairingOutcome);
        Assert.Equal(string.Empty, devices.PairingCode);
    }

    /// <summary>Queued UI publications after shutdown cannot revive observations or pairing status.</summary>
    [Fact]
    public async Task ShutdownInvalidatesQueuedDeviceAndPairingResults()
    {
        Queue<Action> queuedPublications = new();
        FakeGateway gateway = new()
        {
            Devices = AdbResult<IReadOnlyList<AdbDevice>>.Success(
                [new AdbDevice("usb-a", AdbDeviceState.Device, null)]),
        };
        DevicesViewModel devices = CreateDevices(gateway, action => queuedPublications.Enqueue(action));
        await devices.RefreshAsync(TestContext.Current.CancellationToken);
        PrepareManualPairing(devices, "phone.local:37123");
        await devices.PairAsync(TestContext.Current.CancellationToken);

        Assert.True(await devices.ShutdownLiveAsync(TestContext.Current.CancellationToken));
        while (queuedPublications.TryDequeue(out Action? publication))
        {
            publication();
        }

        Assert.Empty(devices.ObservedDevices);
        Assert.Null(devices.PairingOutcome);
        Assert.Null(devices.SelectedObservedDevice);
        Assert.Equal(string.Empty, devices.PairingCode);
    }

    /// <summary>Opens explicit wireless setup and chooses a synthetic manual target.</summary>
    private static void PrepareManualPairing(DevicesViewModel devices, string endpoint)
    {
        devices.OpenWirelessSetup();
        devices.ShowManualPairing();
        devices.ManualPairingEndpoint = endpoint;
        devices.PairingCode = "123456";
    }

    /// <summary>Creates a synthetic pairing advertisement with no real device identity.</summary>
    private static AdbMdnsService PairingService(string name, string endpoint) =>
        new(name, AdbServiceKind.Pairing, NetworkEndpoint.Parse(endpoint));

    /// <summary>Constructs a normal view model with fake ADB and an immediate test dispatcher.</summary>
    private static DevicesViewModel CreateDevices(FakeGateway gateway, Action<Action>? dispatcher = null)
    {
        DevicesViewModel devices = new(StaticDevicePresentationSource.Empty(),
            new PresentationText());
        devices.AttachLiveServices(new AdbDiscoveryService(gateway), new AdbPairingService(gateway),
            dispatcher ?? (action => action()), gateway);
        return devices;
    }

    /// <summary>Provides deterministic ADB responses without a daemon or phone.</summary>
    private sealed class FakeGateway : IAdbGateway
    {
        public Queue<Func<CancellationToken, Task<AdbResult<IReadOnlyList<AdbDevice>>>>> DeviceResults { get; } = new();
        public AdbResult<IReadOnlyList<AdbDevice>> Devices { get; set; } =
            AdbResult<IReadOnlyList<AdbDevice>>.Success([]);
        public AdbResult<IReadOnlyList<AdbMdnsService>> Services { get; set; } =
            AdbResult<IReadOnlyList<AdbMdnsService>>.Success([]);
        public AdbResult<bool> PairingResult { get; set; } = AdbResult<bool>.Success(true);
        public AdbResult<bool> ConnectionResult { get; set; } = AdbResult<bool>.Success(true);
        public Func<NetworkEndpoint, string, CancellationToken, Task<AdbResult<bool>>>? PairAsyncOverride { get; set; }
        public Func<NetworkEndpoint, CancellationToken, Task<AdbResult<bool>>>? ConnectAsyncOverride { get; set; }
        public int DiscoveryCalls { get; private set; }
        public int ServiceCalls { get; private set; }
        public int PairingCalls { get; private set; }
        public NetworkEndpoint? PairedEndpoint { get; private set; }
        public NetworkEndpoint? ConnectedEndpoint { get; private set; }

        public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            return DeviceResults.Count == 0 ? Task.FromResult(Devices) : DeviceResults.Dequeue()(cancellationToken);
        }

        public Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken)
        {
            ServiceCalls++;
            return Task.FromResult(Services);
        }

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
            return ConnectAsyncOverride?.Invoke(connectionEndpoint, cancellationToken)
                ?? Task.FromResult(ConnectionResult);
        }

        public Task<AdbResult<string>> GetDeviceSerialPropertyAsync(NetworkEndpoint connectionEndpoint,
            CancellationToken cancellationToken) => Task.FromResult(AdbResult<string>.Success("observed-serial"));
    }
}
