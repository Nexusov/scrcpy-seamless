using System.Collections.ObjectModel;
using System.Windows.Input;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>One observed ADB transport, independent of any saved profile identity.</summary>
public sealed record ObservedDeviceChoice(AdbDevice Device, string Label);

/// <summary>One advertised endpoint with a distinct pairing or connection purpose.</summary>
public sealed record ObservedServiceChoice(AdbMdnsService Service, string Label);

/// <summary>Current authority of the displayed ADB observation.</summary>
public enum LiveDiscoveryState
{
    Initial,
    Loading,
    Ready,
    Empty,
    Error,
}

/// <summary>Explicit ADB discovery and guided pairing, attached only in device-enabled composition.</summary>
public sealed partial class DevicesViewModel
{
    private AdbDiscoveryService? discovery;
    private AdbPairingService? pairing;
    private IAdbGateway? connectionGateway;
    private Action<Action>? dispatch;
    private CancellationTokenSource? discoveryCancellation;
    private CancellationTokenSource? pairingCancellation;
    private CancellationTokenSource? connectionCancellation;
    private Task activeRefresh = Task.CompletedTask;
    private Task activePairing = Task.CompletedTask;
    private Task activeConnection = Task.CompletedTask;
    private long discoveryGeneration;
    private long pairingGeneration;
    private long connectionGeneration;
    private bool liveDisposed;
    private LiveDiscoveryState discoveryState = LiveDiscoveryState.Initial;
    private ObservedDeviceChoice? selectedObservedDevice;
    private ObservedServiceChoice? selectedPairingService;
    private ObservedServiceChoice? selectedConnectionService;
    private string manualPairingEndpoint = string.Empty;
    private string manualConnectionEndpoint = string.Empty;
    private string pairingCode = string.Empty;
    private bool isPairing;
    private bool isConnecting;
    private NetworkEndpoint? connectedEndpoint;
    private string? connectionMessage;
    private string? discoveryMessage;
    private string? serviceMessage;
    private string? pairingMessage;
    private AdbPairingOutcome? pairingOutcome;
    private bool pairingServicesFresh;
    private bool connectionServicesFresh;

    public ObservableCollection<ObservedDeviceChoice> ObservedDevices { get; } = [];
    public ObservableCollection<ObservedServiceChoice> PairingServices { get; } = [];
    public ObservableCollection<ObservedServiceChoice> ConnectionServices { get; } = [];
    public ICommand RefreshCommand => refreshCommand ??= new ActionCommand(() => _ = RefreshAsync());
    public ICommand CancelRefreshCommand => cancelRefreshCommand ??= new ActionCommand(CancelRefresh);
    public ICommand PairCommand => pairCommand ??= new ActionCommand(() => _ = PairAsync());
    public ICommand CancelPairCommand => cancelPairCommand ??= new ActionCommand(CancelPair);
    public ICommand ConnectCommand => connectCommand ??= new ActionCommand(() => _ = ConnectAsync());
    public ICommand CancelConnectCommand => cancelConnectCommand ??= new ActionCommand(CancelConnect);
    private ICommand? refreshCommand;
    private ICommand? cancelRefreshCommand;
    private ICommand? pairCommand;
    private ICommand? cancelPairCommand;
    private ICommand? connectCommand;
    private ICommand? cancelConnectCommand;

    public bool IsLiveEnabled => discovery is not null && !liveDisposed;
    public LiveDiscoveryState DiscoveryState => discoveryState;
    public bool IsDiscovering => discoveryState == LiveDiscoveryState.Loading;
    public bool HasObservedDevices => ObservedDevices.Count > 0;
    public bool IsObservationStale => discoveryState is LiveDiscoveryState.Error or LiveDiscoveryState.Loading;
    public string? DiscoveryMessage { get => discoveryMessage; private set => SetProperty(ref discoveryMessage, value); }
    public string? ServiceMessage { get => serviceMessage; private set => SetProperty(ref serviceMessage, value); }
    public string? PairingMessage { get => pairingMessage; private set => SetProperty(ref pairingMessage, value); }
    public string? ConnectionMessage { get => connectionMessage; private set => SetProperty(ref connectionMessage, value); }
    public bool IsPairing => isPairing;
    public bool IsConnecting => isConnecting;
    public string LiveDiscoveryTitle => text.Get("devices.live.discoveryTitle");
    public string RefreshLabel => text.Get("devices.live.refresh");
    public string CancelRefreshLabel => text.Get("devices.live.cancelRefresh");
    public string StaleMessage => text.Get("devices.live.stale");
    public string LivePairingTitle => text.Get("devices.live.pairingTitle");
    public string PairingDescription => text.Get("devices.live.pairingDescription");
    public string PairingServiceLabel => text.Get("devices.live.pairingService");
    public string ConnectionServiceLabel => text.Get("devices.live.connectionService");
    public string ManualPairingLabel => text.Get("devices.live.manualPairing");
    public string ManualConnectionLabel => text.Get("devices.live.manualConnection");
    public string PairCodeLabel => text.Get("devices.live.pairCode");
    public string PairLabel => text.Get("devices.live.pair");
    public string CancelPairLabel => text.Get("devices.live.cancelPair");
    public string ConnectLabel => text.Get("devices.live.connect");
    public string CancelConnectLabel => text.Get("devices.live.cancelConnect");
    public string DiscoveryStatusLabel => text.Get(discoveryState switch
    {
        LiveDiscoveryState.Loading => "devices.live.loading",
        LiveDiscoveryState.Ready => "devices.live.ready",
        LiveDiscoveryState.Empty => "devices.live.empty",
        LiveDiscoveryState.Error => "devices.live.error",
        _ => "devices.live.initial",
    });
    public AdbPairingOutcome? PairingOutcome
    {
        get => pairingOutcome;
        private set
        {
            if (SetProperty(ref pairingOutcome, value))
            {
                OnPropertyChanged(nameof(ConnectedEndpointLabel));
            }
        }
    }
    public string? ConnectedEndpointLabel => (connectedEndpoint ?? PairingOutcome?.ConnectedEndpoint) is { } endpoint
        ? $"{text.Get("devices.live.connectedEndpoint")}: {endpoint}"
        : null;
    public DeviceSessionViewModel? SessionActions { get; private set; }
    public bool HasSessionActions => SessionActions is not null;
    public bool CanRefresh => IsLiveEnabled && !IsDiscovering;
    public bool CanCancelRefresh => IsLiveEnabled && IsDiscovering;
    public bool CanCancelPair => IsLiveEnabled && IsPairing;
    public bool CanConnect => IsLiveEnabled && connectionGateway is not null && !IsConnecting && !IsPairing &&
        ResolveConnectionEndpoint() is not null;
    public bool CanCancelConnect => IsLiveEnabled && IsConnecting;
    public bool CanPair => IsLiveEnabled && !IsPairing && !IsConnecting && ResolvePairingEndpoint() is not null &&
        pairingCode.Length == PairingCodeLength && pairingCode.All(char.IsAsciiDigit);
    private const int PairingCodeLength = 6;
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

    /// <summary>Hosts session actions supplied by the application composition without starting them.</summary>
    public void AttachSessionActions(DeviceSessionViewModel actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (IsPreview || SessionActions is not null)
        {
            throw new InvalidOperationException("Session actions require an unattached normal Devices workspace.");
        }

        SessionActions = actions;
        OnPropertyChanged(nameof(SessionActions));
        OnPropertyChanged(nameof(HasSessionActions));
    }

    /// <summary>Exposes a fresh, explicit transport choice for launch preflight.</summary>
    public AdbDevice? SelectedObservedDevice =>
        (discoveryState is LiveDiscoveryState.Ready or LiveDiscoveryState.Empty)
        && selectedObservedDevice is not null && ObservedDevices.Contains(selectedObservedDevice)
            ? selectedObservedDevice.Device : null;

    /// <summary>Exposes only an explicitly selected or manually entered connection endpoint.</summary>
    public NetworkEndpoint? SelectedConnectionEndpoint => ResolveConnectionEndpoint();

    public ObservedDeviceChoice? SelectedDeviceChoice
    {
        get => selectedObservedDevice;
        set
        {
            if (SetProperty(ref selectedObservedDevice, value))
            {
                OnPropertyChanged(nameof(SelectedObservedDevice));
            }
        }
    }

    public ObservedServiceChoice? SelectedPairingService
    {
        get => selectedPairingService;
        set
        {
            if (SetProperty(ref selectedPairingService, value))
            {
                OnPropertyChanged(nameof(CanPair));
            }
        }
    }

    public ObservedServiceChoice? SelectedConnectionService
    {
        get => selectedConnectionService;
        set
        {
            if (SetProperty(ref selectedConnectionService, value))
            {
                OnPropertyChanged(nameof(SelectedConnectionEndpoint));
                OnPropertyChanged(nameof(CanConnect));
            }
        }
    }

    public string ManualPairingEndpoint
    {
        get => manualPairingEndpoint;
        set
        {
            if (SetProperty(ref manualPairingEndpoint, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanPair));
            }
        }
    }

    public string ManualConnectionEndpoint
    {
        get => manualConnectionEndpoint;
        set
        {
            if (SetProperty(ref manualConnectionEndpoint, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SelectedConnectionEndpoint));
                OnPropertyChanged(nameof(CanConnect));
            }
        }
    }

    public string PairingCode
    {
        get => pairingCode;
        set
        {
            if (SetProperty(ref pairingCode, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanPair));
            }
        }
    }

    /// <summary>Attaches existing Core operations without contacting a device on construction.</summary>
    public void AttachLiveServices(AdbDiscoveryService discoveryService, AdbPairingService pairingService,
        Action<Action> publishOnUiThread, IAdbGateway? explicitConnectionGateway = null)
    {
        ArgumentNullException.ThrowIfNull(discoveryService);
        ArgumentNullException.ThrowIfNull(pairingService);
        ArgumentNullException.ThrowIfNull(publishOnUiThread);

        if (IsPreview || discovery is not null || liveDisposed)
        {
            throw new InvalidOperationException("Live ADB services require an unattached normal Devices workspace.");
        }

        discovery = discoveryService;
        pairing = pairingService;
        connectionGateway = explicitConnectionGateway;
        dispatch = publishOnUiThread;
        OnPropertyChanged(nameof(IsLiveEnabled));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Refreshes only when explicitly requested, retaining failed observations as stale.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Task operation = RefreshCoreAsync(cancellationToken);
        activeRefresh = operation;
        return operation;
    }

    /// <summary>Runs one owned discovery attempt and ignores superseded completions.</summary>
    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsLiveEnabled)
        {
            return;
        }

        CancelRefresh();
        long generation = ++discoveryGeneration;
        CancellationTokenSource ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCancellation = ownedCancellation;
        pairingServicesFresh = false;
        connectionServicesFresh = false;
        DiscoveryStateChanged(LiveDiscoveryState.Loading, null);
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));

        try
        {
            AdbResult<IReadOnlyList<AdbDevice>> devices = await discovery!.GetDevicesAsync(ownedCancellation.Token);
            AdbResult<IReadOnlyList<AdbMdnsService>> pairingServices = await discovery.GetServicesAsync(
                AdbServiceKind.Pairing, ownedCancellation.Token);
            AdbResult<IReadOnlyList<AdbMdnsService>> connectionServices = await discovery.GetServicesAsync(
                AdbServiceKind.Connection, ownedCancellation.Token);
            dispatch!(() => PublishDiscovery(generation, devices, pairingServices, connectionServices));
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
            dispatch!(() => PublishCancelledDiscovery(generation));
        }
        catch (Exception)
        {
            dispatch!(() => PublishDiscoveryFailure(generation));
        }
        finally
        {
            if (ReferenceEquals(discoveryCancellation, ownedCancellation))
            {
                discoveryCancellation = null;
            }

            ownedCancellation.Dispose();
        }
    }

    /// <summary>Cancels the owned refresh without asserting that ADB state has changed.</summary>
    public void CancelRefresh()
    {
        if (!IsDiscovering)
        {
            return;
        }

        discoveryGeneration++;
        discoveryCancellation?.Cancel();
        pairingServicesFresh = false;
        connectionServicesFresh = false;
        DiscoveryStateChanged(LiveDiscoveryState.Error, text.Get("devices.live.cancelled"));
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Pairs explicitly and optionally connects a separately chosen endpoint.</summary>
    public Task PairAsync(CancellationToken cancellationToken = default)
    {
        Task operation = PairCoreAsync(cancellationToken);
        activePairing = operation;
        return operation;
    }

    /// <summary>Runs a captured pairing attempt without retaining its visible code.</summary>
    private async Task PairCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanPair)
        {
            PairingMessage = text.Get("devices.live.pairInvalid");
            return;
        }

        NetworkEndpoint endpoint = ResolvePairingEndpoint()!;
        NetworkEndpoint? connectionEndpoint = ResolveConnectionEndpoint();
        string code = PairingCode;
        PairingCode = string.Empty;
        long generation = ++pairingGeneration;
        CancellationTokenSource ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pairingCancellation = ownedCancellation;
        isPairing = true;
        PairingOutcome = null;
        PairingMessage = text.Get("devices.live.pairing");
        OnPropertyChanged(nameof(IsPairing));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanCancelPair));
        OnPropertyChanged(nameof(CanConnect));

        try
        {
            AdbResult<AdbPairingOutcome> result = await pairing!.PairAsync(endpoint, code, connectionEndpoint,
                null, ownedCancellation.Token);
            dispatch!(() => PublishPairing(generation, result));
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
            dispatch!(() => PublishPairingCancellation(generation));
        }
        catch (Exception)
        {
            dispatch!(() => PublishPairingFailure(generation));
        }
        finally
        {
            if (ReferenceEquals(pairingCancellation, ownedCancellation))
            {
                pairingCancellation = null;
            }

            ownedCancellation.Dispose();
            code = string.Empty;
        }
    }

    /// <summary>Stops waiting for the current pairing attempt; remote pairing may already have succeeded.</summary>
    public void CancelPair()
    {
        if (!isPairing)
        {
            return;
        }

        pairingCancellation?.Cancel();
        pairingGeneration++;
        isPairing = false;
        PairingCode = string.Empty;
        PairingMessage = text.Get("devices.live.pairCancelled");
        OnPropertyChanged(nameof(IsPairing));
        OnPropertyChanged(nameof(CanCancelPair));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Connects one explicitly chosen endpoint without pairing or saving a profile.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Task operation = ConnectCoreAsync(cancellationToken);
        activeConnection = operation;
        return operation;
    }

    /// <summary>Owns one bounded ADB connect command and rejects superseded results.</summary>
    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (!CanConnect)
        {
            ConnectionMessage = text.Get("devices.live.connectInvalid");
            return;
        }

        NetworkEndpoint endpoint = ResolveConnectionEndpoint()!;
        long generation = ++connectionGeneration;
        CancellationTokenSource ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionCancellation = ownedCancellation;
        isConnecting = true;
        ConnectionMessage = text.Get("devices.live.connecting");
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanCancelConnect));
        OnPropertyChanged(nameof(CanPair));

        try
        {
            AdbResult<bool> result = await connectionGateway!.ConnectAsync(endpoint, ownedCancellation.Token);
            dispatch!(() => PublishConnection(generation, endpoint, result));
        }
        catch (OperationCanceledException) when (ownedCancellation.IsCancellationRequested)
        {
            dispatch!(() => PublishConnectionCancellation(generation));
        }
        catch (Exception)
        {
            dispatch!(() => PublishConnectionFailure(generation));
        }
        finally
        {
            if (ReferenceEquals(connectionCancellation, ownedCancellation))
            {
                connectionCancellation = null;
            }

            ownedCancellation.Dispose();
        }
    }

    /// <summary>Cancels local waiting without claiming that remote ADB state was reversed.</summary>
    public void CancelConnect()
    {
        if (!isConnecting)
        {
            return;
        }

        connectionGeneration++;
        connectionCancellation?.Cancel();
        ConnectionMessage = text.Get("devices.live.connectCancelled");
        FinishConnection();
    }

    /// <summary>Publishes only the latest exact endpoint and refreshes after completed connect.</summary>
    private void PublishConnection(long generation, NetworkEndpoint endpoint, AdbResult<bool> result)
    {
        if (liveDisposed || generation != connectionGeneration)
        {
            return;
        }

        bool connected = result.IsSuccess && result.Value;
        connectedEndpoint = connected ? endpoint : null;
        ConnectionMessage = text.Get(connected ? "devices.live.connectSucceeded" : "devices.live.connectFailed");
        OnPropertyChanged(nameof(ConnectedEndpointLabel));
        FinishConnection();

        if (connected)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Preserves uncertainty when a connect command is cancelled.</summary>
    private void PublishConnectionCancellation(long generation)
    {
        if (liveDisposed || generation != connectionGeneration)
        {
            return;
        }

        ConnectionMessage = text.Get("devices.live.connectCancelled");
        FinishConnection();
    }

    /// <summary>Hides raw ADB output while reporting a connection failure.</summary>
    private void PublishConnectionFailure(long generation)
    {
        if (liveDisposed || generation != connectionGeneration)
        {
            return;
        }

        ConnectionMessage = text.Get("devices.live.connectFailed");
        FinishConnection();
    }

    /// <summary>Settles one connection attempt's presentation state.</summary>
    private void FinishConnection()
    {
        isConnecting = false;
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanCancelConnect));
        OnPropertyChanged(nameof(CanPair));
    }

    /// <summary>Invalidates late results when this workspace leaves the application lifetime.</summary>
    public void DisposeLive()
    {
        liveDisposed = true;
        discoveryGeneration++;
        pairingGeneration++;
        connectionGeneration++;
        discoveryCancellation?.Cancel();
        pairingCancellation?.Cancel();
        connectionCancellation?.Cancel();
        if (isConnecting)
        {
            ConnectionMessage = text.Get("devices.live.connectCancelled");
            FinishConnection();
        }
        PairingCode = string.Empty;
        OnPropertyChanged(nameof(IsLiveEnabled));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Cancels owned ADB work and awaits its settlement before application shutdown.</summary>
    public async Task<bool> ShutdownLiveAsync(CancellationToken cancellationToken = default)
    {
        DisposeLive();

        try
        {
            await Task.WhenAll(activeRefresh, activePairing, activeConnection).WaitAsync(ShutdownWait, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            // Generations are invalidated even when an external gateway fails to honor cancellation.
            return false;
        }
    }

    /// <summary>Publishes a current ADB result without guessing profile or device equivalence.</summary>
    private void PublishDiscovery(long generation, AdbResult<IReadOnlyList<AdbDevice>> devices,
        AdbResult<IReadOnlyList<AdbMdnsService>> pairingServices,
        AdbResult<IReadOnlyList<AdbMdnsService>> connectionServices)
    {
        if (liveDisposed || generation != discoveryGeneration)
        {
            return;
        }

        if (!devices.IsSuccess || devices.Value is null)
        {
            PublishDiscoveryFailure(generation);
            return;
        }

        string? selectedSerial = selectedObservedDevice?.Device.Serial;
        ObservedDevices.Clear();

        foreach (AdbDevice device in devices.Value)
        {
            ObservedDevices.Add(new ObservedDeviceChoice(device,
                $"{device.Model ?? device.Serial} · {device.Serial} · {device.State}"));
        }

        SelectedDeviceChoice = ObservedDevices.FirstOrDefault(choice => choice.Device.Serial == selectedSerial);
        ReplaceServices(PairingServices, pairingServices, AdbServiceKind.Pairing, ref selectedPairingService,
            nameof(SelectedPairingService));
        ReplaceServices(ConnectionServices, connectionServices, AdbServiceKind.Connection,
            ref selectedConnectionService, nameof(SelectedConnectionService));
        pairingServicesFresh = pairingServices.IsSuccess;
        connectionServicesFresh = connectionServices.IsSuccess;
        ServiceMessage = pairingServices.IsSuccess && connectionServices.IsSuccess
            ? null : text.Get("devices.live.serviceFailed");
        DiscoveryStateChanged(ObservedDevices.Count == 0 ? LiveDiscoveryState.Empty : LiveDiscoveryState.Ready,
            null);
        OnPropertyChanged(nameof(HasObservedDevices));
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Preserves a previously observed list on failure and marks it non-authoritative.</summary>
    private void PublishDiscoveryFailure(long generation)
    {
        if (liveDisposed || generation != discoveryGeneration)
        {
            return;
        }

        DiscoveryStateChanged(LiveDiscoveryState.Error, text.Get("devices.live.error"));
        OnPropertyChanged(nameof(SelectedObservedDevice));
    }

    /// <summary>Restores the prior observation authority after an explicit cancellation.</summary>
    private void PublishCancelledDiscovery(long generation)
    {
        if (liveDisposed || generation != discoveryGeneration)
        {
            return;
        }

        DiscoveryStateChanged(LiveDiscoveryState.Error, text.Get("devices.live.cancelled"));
        OnPropertyChanged(nameof(SelectedObservedDevice));
    }

    /// <summary>Accepts only the latest pairing result and keeps persistence explicit.</summary>
    private void PublishPairing(long generation, AdbResult<AdbPairingOutcome> result)
    {
        if (liveDisposed || generation != pairingGeneration)
        {
            return;
        }

        PairingOutcome = result.Value;
        PairingMessage = result.Value?.Paired == true
            ? result.IsSuccess
                ? result.Value.ConnectedEndpoint is null
                    ? text.Get("devices.live.paired")
                    : text.Get("devices.live.connected")
                : text.Get("devices.live.partial")
            : text.Get("devices.live.pairFailed");
        FinishPairing();
        if (result.IsSuccess || result.Value?.Paired == true)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Reports cancellation without claiming to reverse remote ADB pairing.</summary>
    private void PublishPairingCancellation(long generation)
    {
        if (liveDisposed || generation != pairingGeneration)
        {
            return;
        }

        PairingMessage = text.Get("devices.live.pairCancelled");
        FinishPairing();
    }

    /// <summary>Reports a sanitized failure without displaying exception or secret text.</summary>
    private void PublishPairingFailure(long generation)
    {
        if (liveDisposed || generation != pairingGeneration)
        {
            return;
        }

        PairingMessage = text.Get("devices.live.pairFailed");
        FinishPairing();
    }

    /// <summary>Settles only presentation state for one pairing operation.</summary>
    private void FinishPairing()
    {
        isPairing = false;
        PairingCode = string.Empty;
        OnPropertyChanged(nameof(IsPairing));
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(CanCancelPair));
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Updates labeled status without creating an authoritative empty result on failure.</summary>
    private void DiscoveryStateChanged(LiveDiscoveryState state, string? message)
    {
        discoveryState = state;
        DiscoveryMessage = message;
        OnPropertyChanged(nameof(DiscoveryState));
        OnPropertyChanged(nameof(DiscoveryStatusLabel));
        OnPropertyChanged(nameof(IsDiscovering));
        OnPropertyChanged(nameof(IsObservationStale));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCancelRefresh));
    }

    /// <summary>Maps distinct mDNS service purposes and preserves a matching explicit choice.</summary>
    private void ReplaceServices(ObservableCollection<ObservedServiceChoice> destination,
        AdbResult<IReadOnlyList<AdbMdnsService>> result, AdbServiceKind kind,
        ref ObservedServiceChoice? selected, string selectedProperty)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            return;
        }

        AdbMdnsService? previous = selected?.Service;
        destination.Clear();

        foreach (AdbMdnsService service in result.Value.Where(item => item.Kind == kind))
        {
            destination.Add(new ObservedServiceChoice(service, $"{service.InstanceName} · {service.Endpoint}"));
        }

        selected = destination.FirstOrDefault(choice => choice.Service.InstanceName == previous?.InstanceName &&
            choice.Service.Endpoint == previous.Endpoint);
        OnPropertyChanged(selectedProperty);
    }

    /// <summary>Resolves a manually typed endpoint first, without guessing a service port.</summary>
    private NetworkEndpoint? ResolvePairingEndpoint() =>
        manualPairingEndpoint.Length > 0
            ? AdbDiscoveryService.ResolveManualEndpoint(manualPairingEndpoint).Value
            : pairingServicesFresh ? selectedPairingService?.Service.Endpoint : null;

    /// <summary>Resolves the independent connection endpoint only after explicit entry or choice.</summary>
    private NetworkEndpoint? ResolveConnectionEndpoint() =>
        manualConnectionEndpoint.Length > 0
            ? AdbDiscoveryService.ResolveManualEndpoint(manualConnectionEndpoint).Value
            : connectionServicesFresh ? selectedConnectionService?.Service.Endpoint : null;
}
