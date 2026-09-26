using System.Collections.ObjectModel;
using System.Windows.Input;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>One observed ADB transport, independent of any saved profile identity.</summary>
public sealed record ObservedDeviceChoice(AdbDevice Device, string Label);

/// <summary>One advertised endpoint with a distinct pairing or connection purpose.</summary>
public sealed record ObservedServiceChoice(AdbMdnsService Service, string Label);

/// <summary>One current pairing decision and the matching visible explanation.</summary>
internal sealed record PairEligibility(bool IsAvailable, string MessageKey, NetworkEndpoint? Endpoint);

/// <summary>Current authority of the displayed ADB observation.</summary>
public enum LiveDiscoveryState
{
    Initial,
    Loading,
    Ready,
    Empty,
    Error,
    Cancelled,
    Unavailable,
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
    private readonly SemaphoreSlim discoveryGate = new(1, 1);
    private readonly object adbTaskLock = new();
    private readonly HashSet<Task> activeAdbOperations = [];
    private readonly HashSet<Task> activeDirectOperations = [];
    private long wirelessSetupRefreshGeneration;
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
    private LiveDiscoveryState pairingDiscoveryState = LiveDiscoveryState.Initial;
    private bool isWirelessSetupOpen;
    private bool isManualPairing;
    private bool isManualConnection;
    private bool pairingSelectionInvalidated;
    private bool updatingPairingServices;
    private bool updatingConnectionServices;

    public ObservableCollection<ObservedDeviceChoice> ObservedDevices { get; } = [];
    public ObservableCollection<ObservedServiceChoice> PairingServices { get; } = [];
    public ObservableCollection<ObservedServiceChoice> ConnectionServices { get; } = [];
    public ICommand RefreshCommand => refreshCommand ??= new ActionCommand(() => _ = RefreshAsync());
    public ICommand CancelRefreshCommand => cancelRefreshCommand ??= new ActionCommand(CancelRefresh);
    public ICommand PairCommand => pairCommand ??= new ActionCommand(() => _ = PairAsync());
    public ICommand CancelPairCommand => cancelPairCommand ??= new ActionCommand(CancelPair);
    public ICommand ConnectCommand => connectCommand ??= new ActionCommand(() => _ = ConnectAsync());
    public ICommand CancelConnectCommand => cancelConnectCommand ??= new ActionCommand(CancelConnect);
    public ICommand OpenWirelessSetupCommand => openWirelessSetupCommand ??= new ActionCommand(OpenWirelessSetup);
    public ICommand CloseWirelessSetupCommand => closeWirelessSetupCommand ??= new ActionCommand(CloseWirelessSetup);
    public ICommand ShowManualPairingCommand => showManualPairingCommand ??= new ActionCommand(ShowManualPairing);
    public ICommand UseDiscoveredPairingCommand => useDiscoveredPairingCommand ??= new ActionCommand(UseDiscoveredPairing);
    public ICommand ShowManualConnectionCommand => showManualConnectionCommand ??= new ActionCommand(ShowManualConnection);
    public ICommand UseDiscoveredConnectionCommand => useDiscoveredConnectionCommand ??= new ActionCommand(UseDiscoveredConnection);
    private ICommand? refreshCommand;
    private ICommand? cancelRefreshCommand;
    private ICommand? pairCommand;
    private ICommand? cancelPairCommand;
    private ICommand? connectCommand;
    private ICommand? cancelConnectCommand;
    private ICommand? openWirelessSetupCommand;
    private ICommand? closeWirelessSetupCommand;
    private ICommand? showManualPairingCommand;
    private ICommand? useDiscoveredPairingCommand;
    private ICommand? showManualConnectionCommand;
    private ICommand? useDiscoveredConnectionCommand;

    public bool IsLiveEnabled => discovery is not null && !liveDisposed;
    public LiveDiscoveryState DiscoveryState => discoveryState;
    public bool IsDiscovering => discoveryState == LiveDiscoveryState.Loading;
    public bool HasObservedDevices => ObservedDevices.Count > 0;
    public bool IsObservationStale => discoveryState is LiveDiscoveryState.Error or LiveDiscoveryState.Loading
        or LiveDiscoveryState.Cancelled;
    public string? DiscoveryMessage { get => discoveryMessage; private set => SetProperty(ref discoveryMessage, value); }
    public string? ServiceMessage { get => serviceMessage; private set => SetProperty(ref serviceMessage, value); }
    public string? PairingMessage { get => pairingMessage; private set => SetProperty(ref pairingMessage, value); }
    public string? ConnectionMessage { get => connectionMessage; private set => SetProperty(ref connectionMessage, value); }
    public bool IsPairing => isPairing;
    public bool IsConnecting => isConnecting;
    public bool IsWirelessSetupOpen => isWirelessSetupOpen;
    public bool IsWirelessSetupClosed => !isWirelessSetupOpen;
    public bool IsManualPairing => isManualPairing;
    public bool IsDiscoveredPairing => !isManualPairing;
    public bool IsManualConnection => isManualConnection;
    public bool IsDiscoveredConnection => !isManualConnection;
    public string LiveDiscoveryTitle => text.Get("devices.live.discoveryTitle");
    public string RefreshLabel => text.Get("devices.live.refresh");
    public string CancelRefreshLabel => text.Get("devices.live.cancelRefresh");
    public string StaleMessage => text.Get("devices.live.stale");
    public string LivePairingTitle => text.Get("devices.live.pairingTitle");
    public string PairingDescription => text.Get("devices.live.pairingDescription");
    public string OpenWirelessSetupLabel => text.Get("devices.live.openWirelessSetup");
    public string CloseWirelessSetupLabel => text.Get("devices.live.closeWirelessSetup");
    public string ShowManualPairingLabel => text.Get("devices.live.showManualPairing");
    public string UseDiscoveredPairingLabel => text.Get("devices.live.useDiscoveredPairing");
    public string ShowManualConnectionLabel => text.Get("devices.live.showManualConnection");
    public string UseDiscoveredConnectionLabel => text.Get("devices.live.useDiscoveredConnection");
    public string PairingServiceLabel => text.Get("devices.live.pairingService");
    public string ConnectionServiceLabel => text.Get("devices.live.connectionService");
    public string ManualPairingLabel => text.Get("devices.live.manualPairing");
    public string ManualConnectionLabel => text.Get("devices.live.manualConnection");
    public string PairCodeLabel => text.Get("devices.live.pairCode");
    public string PairLabel => text.Get("devices.live.pair");
    public string CancelPairLabel => text.Get("devices.live.cancelPair");
    public string ConnectLabel => text.Get("devices.live.connect");
    public string CancelConnectLabel => text.Get("devices.live.cancelConnect");
    public string PairEligibilityMessage => text.Get(EvaluatePairEligibility().MessageKey);
    public string PairingDiscoveryStatusLabel => text.Get(pairingDiscoveryState switch
    {
        LiveDiscoveryState.Loading => "devices.live.pairingSearching",
        LiveDiscoveryState.Ready => "devices.live.pairingFound",
        LiveDiscoveryState.Empty => "devices.live.pairingEmpty",
        LiveDiscoveryState.Error => "devices.live.pairingDiscoveryFailed",
        LiveDiscoveryState.Cancelled => "devices.live.pairingDiscoveryCancelled",
        LiveDiscoveryState.Unavailable => "devices.live.pairingDiscoveryUnavailable",
        _ => "devices.live.pairingNotSearched",
    });
    public string DiscoveryStatusLabel => text.Get(discoveryState switch
    {
        LiveDiscoveryState.Loading => "devices.live.loading",
        LiveDiscoveryState.Ready => "devices.live.ready",
        LiveDiscoveryState.Empty => "devices.live.empty",
        LiveDiscoveryState.Error => "devices.live.error",
        LiveDiscoveryState.Cancelled => "devices.live.cancelled",
        LiveDiscoveryState.Unavailable => "devices.live.error",
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
    public bool CanRefresh => IsLiveEnabled && !IsDiscovering && !IsPairing && !IsConnecting;
    public bool CanCancelRefresh => IsLiveEnabled && IsDiscovering;
    public bool CanCancelPair => IsLiveEnabled && IsPairing;
    public bool CanConnect => IsLiveEnabled && isWirelessSetupOpen && connectionGateway is not null
        && !IsConnecting && !IsPairing && !IsDiscovering && ResolveConnectionEndpoint() is not null;
    public bool CanCancelConnect => IsLiveEnabled && IsConnecting;
    public bool CanCloseWirelessSetup
    {
        get
        {
            lock (adbTaskLock)
            {
                return activeDirectOperations.All(operation => operation.IsCompleted)
                    && pairingCancellation is null && connectionCancellation is null;
            }
        }
    }
    public bool CanPair => EvaluatePairEligibility().IsAvailable;
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
            if (updatingPairingServices)
            {
                return;
            }

            if (ReferenceEquals(selectedPairingService, value))
            {
                return;
            }

            if (SetProperty(ref selectedPairingService, value))
            {
                pairingSelectionInvalidated = false;
                PairingCode = string.Empty;
                NotifyPairEligibility();
            }
        }
    }

    public ObservedServiceChoice? SelectedConnectionService
    {
        get => selectedConnectionService;
        set
        {
            if (updatingConnectionServices)
            {
                return;
            }

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
                if (isManualPairing)
                {
                    PairingCode = string.Empty;
                }

                NotifyPairEligibility();
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
                NotifyPairEligibility();
            }
        }
    }

    /// <summary>Starts one explicit discovery snapshot when wireless setup is opened.</summary>
    public void OpenWirelessSetup()
    {
        if (!IsLiveEnabled || isWirelessSetupOpen)
        {
            return;
        }

        isWirelessSetupOpen = true;
        isManualPairing = false;
        PairingCode = string.Empty;
        OnPropertyChanged(nameof(IsWirelessSetupOpen));
        OnPropertyChanged(nameof(IsWirelessSetupClosed));
        OnPropertyChanged(nameof(IsManualPairing));
        OnPropertyChanged(nameof(IsDiscoveredPairing));
        NotifyPairEligibility();
        _ = RefreshAsync();
        wirelessSetupRefreshGeneration = discoveryGeneration;
    }

    /// <summary>Closes wireless setup and cancels its pending discovery snapshot.</summary>
    public void CloseWirelessSetup()
    {
        if (!isWirelessSetupOpen || !CanCloseWirelessSetup)
        {
            return;
        }

        isWirelessSetupOpen = false;
        PairingCode = string.Empty;
        // Closing owns only the snapshot started by opening this section.
        if (IsDiscovering && discoveryGeneration == wirelessSetupRefreshGeneration)
        {
            CancelRefresh();
        }
        OnPropertyChanged(nameof(IsWirelessSetupOpen));
        OnPropertyChanged(nameof(IsWirelessSetupClosed));
        NotifyPairEligibility();
    }

    /// <summary>Selects an explicit manual pairing target without a hidden service override.</summary>
    public void ShowManualPairing()
    {
        SetManualPairingMode(true);
    }

    /// <summary>Returns to a fresh advertised pairing target without using hidden manual text.</summary>
    public void UseDiscoveredPairing()
    {
        SetManualPairingMode(false);

        if (pairingServicesFresh && PairingServices.Count == 1 && selectedPairingService is null
            && !pairingSelectionInvalidated)
        {
            SelectedPairingService = PairingServices[0];
        }
    }

    /// <summary>Uses a separately entered connection address only by explicit choice.</summary>
    public void ShowManualConnection()
    {
        SetManualConnectionMode(true);
    }

    /// <summary>Returns to a current discovered connection service.</summary>
    public void UseDiscoveredConnection()
    {
        SetManualConnectionMode(false);
    }

    /** Switches the connection address source without a hidden manual override. */
    private void SetManualConnectionMode(bool manual)
    {
        if (isManualConnection == manual)
        {
            return;
        }

        isManualConnection = manual;
        OnPropertyChanged(nameof(IsManualConnection));
        OnPropertyChanged(nameof(IsDiscoveredConnection));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        OnPropertyChanged(nameof(CanConnect));
    }

    /** Changes the endpoint source and invalidates any code entered for another target. */
    private void SetManualPairingMode(bool manual)
    {
        if (isManualPairing == manual)
        {
            return;
        }

        isManualPairing = manual;
        PairingCode = string.Empty;
        OnPropertyChanged(nameof(IsManualPairing));
        OnPropertyChanged(nameof(IsDiscoveredPairing));
        NotifyPairEligibility();
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
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Refreshes only when explicitly requested, retaining failed observations as stale.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return TrackAdbOperation(() => RefreshCoreAsync(cancellationToken));
    }

    /// <summary>Reserves ownership before starting an ADB command and retains it through cleanup.</summary>
    private Task TrackAdbOperation(Func<Task> start, bool direct = false)
    {
        TaskCompletionSource ownership = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = ownership.Task;

        lock (adbTaskLock)
        {
            activeAdbOperations.Add(operation);

            if (direct)
            {
                activeDirectOperations.Add(operation);
            }
        }

        _ = operation.ContinueWith(completed =>
        {
            lock (adbTaskLock)
            {
                activeAdbOperations.Remove(completed);
                activeDirectOperations.Remove(completed);
            }

            if (direct && dispatch is not null)
            {
                dispatch(() => OnPropertyChanged(nameof(CanCloseWirelessSetup)));
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            _ = CompleteAdbOperationAsync(start(), ownership);
        }
        catch (Exception exception)
        {
            ownership.TrySetException(exception);
        }

        return operation;
    }

    /// <summary>Settles the ownership reservation only after the actual operation has finished.</summary>
    private static async Task CompleteAdbOperationAsync(Task operation, TaskCompletionSource ownership)
    {
        try
        {
            await operation;
            ownership.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            ownership.TrySetCanceled();
        }
        catch (Exception exception)
        {
            ownership.TrySetException(exception);
        }
    }

    /// <summary>Runs one owned discovery attempt and ignores superseded completions.</summary>
    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsLiveEnabled || isPairing || isConnecting)
        {
            return;
        }

        CancelRefresh();
        long generation = ++discoveryGeneration;
        CancellationTokenSource ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCancellation = ownedCancellation;
        pairingServicesFresh = false;
        connectionServicesFresh = false;
        pairingDiscoveryState = LiveDiscoveryState.Loading;
        DiscoveryStateChanged(LiveDiscoveryState.Loading, null);
        OnPropertyChanged(nameof(PairingDiscoveryStatusLabel));
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanConnect));

        bool enteredDiscoveryGate = false;

        try
        {
            await discoveryGate.WaitAsync(ownedCancellation.Token);
            enteredDiscoveryGate = true;
            AdbResult<IReadOnlyList<AdbMdnsService>> services = await discovery!.GetServicesAsync(
                ownedCancellation.Token);
            AdbResult<IReadOnlyList<AdbDevice>> devices = await discovery.GetDevicesAsync(ownedCancellation.Token);
            dispatch!(() => PublishDiscovery(generation, devices, services));
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
            if (enteredDiscoveryGate)
            {
                discoveryGate.Release();
            }

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
        pairingDiscoveryState = LiveDiscoveryState.Cancelled;
        DiscoveryStateChanged(LiveDiscoveryState.Cancelled, text.Get("devices.live.cancelled"));
        OnPropertyChanged(nameof(PairingDiscoveryStatusLabel));
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Pairs explicitly and optionally connects a separately chosen endpoint.</summary>
    public Task PairAsync(CancellationToken cancellationToken = default)
    {
        return TrackAdbOperation(() => PairCoreAsync(cancellationToken), direct: true);
    }

    /// <summary>Runs a captured pairing attempt without retaining its visible code.</summary>
    private async Task PairCoreAsync(CancellationToken cancellationToken)
    {
        PairEligibility eligibility = EvaluatePairEligibility();

        if (!eligibility.IsAvailable || eligibility.Endpoint is null)
        {
            PairingMessage = text.Get(eligibility.MessageKey);
            return;
        }

        NetworkEndpoint endpoint = eligibility.Endpoint;
        string code = PairingCode;
        PairingCode = string.Empty;
        long generation = ++pairingGeneration;
        CancellationTokenSource ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pairingCancellation = ownedCancellation;
        isPairing = true;
        PairingOutcome = null;
        PairingMessage = text.Get("devices.live.pairing");
        OnPropertyChanged(nameof(IsPairing));
        OnPropertyChanged(nameof(CanCloseWirelessSetup));
        OnPropertyChanged(nameof(CanRefresh));
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanCancelPair));
        OnPropertyChanged(nameof(CanConnect));

        bool enteredDiscoveryGate = false;

        try
        {
            // Pair waits until a cancelled discovery process has actually settled.
            await discoveryGate.WaitAsync(ownedCancellation.Token);
            enteredDiscoveryGate = true;
            AdbResult<AdbPairingOutcome> result = await pairing!.PairAsync(endpoint, code, null,
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
            if (enteredDiscoveryGate)
            {
                discoveryGate.Release();
            }

            if (ReferenceEquals(pairingCancellation, ownedCancellation))
            {
                pairingCancellation = null;
                OnPropertyChanged(nameof(CanCloseWirelessSetup));
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
        OnPropertyChanged(nameof(CanCloseWirelessSetup));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCancelPair));
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Connects one explicitly chosen endpoint without pairing or saving a profile.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return TrackAdbOperation(() => ConnectCoreAsync(cancellationToken), direct: true);
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
        OnPropertyChanged(nameof(CanCloseWirelessSetup));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanCancelConnect));
        NotifyPairEligibility();

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
                OnPropertyChanged(nameof(CanCloseWirelessSetup));
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
        OnPropertyChanged(nameof(CanCloseWirelessSetup));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanCancelConnect));
        NotifyPairEligibility();
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
        NotifyPairEligibility();
        OnPropertyChanged(nameof(CanConnect));
    }

    /// <summary>Cancels owned ADB work and awaits its settlement before application shutdown.</summary>
    public async Task<bool> ShutdownLiveAsync(CancellationToken cancellationToken = default)
    {
        DisposeLive();

        try
        {
            Task[] operations;
            lock (adbTaskLock)
            {
                operations = activeAdbOperations.ToArray();
            }

            await Task.WhenAll(operations)
                .WaitAsync(ShutdownWait, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            // Generations are invalidated even when an external gateway fails to honor cancellation.
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A faulted owned operation cannot be reported as a successful close.
            return false;
        }
    }

    /// <summary>Publishes a current ADB result without guessing profile or device equivalence.</summary>
    private void PublishDiscovery(long generation, AdbResult<IReadOnlyList<AdbDevice>> devices,
        AdbResult<IReadOnlyList<AdbMdnsService>> services)
    {
        if (liveDisposed || generation != discoveryGeneration)
        {
            return;
        }

        if (devices.IsSuccess && devices.Value is not null)
        {
            string? selectedSerial = selectedObservedDevice?.Device.Serial;
            ObservedDevices.Clear();

            foreach (AdbDevice device in devices.Value)
            {
                ObservedDevices.Add(new ObservedDeviceChoice(device,
                    $"{device.Model ?? device.Serial} · {device.Serial} · {device.State}"));
            }

            SelectedDeviceChoice = ObservedDevices.FirstOrDefault(choice => choice.Device.Serial == selectedSerial);
            DiscoveryStateChanged(ObservedDevices.Count == 0 ? LiveDiscoveryState.Empty : LiveDiscoveryState.Ready,
                null);
        }
        else
        {
            DiscoveryStateChanged(LiveDiscoveryState.Error, text.Get("devices.live.error"));
        }

        bool servicesAvailable = services.IsSuccess && services.Value is not null;
        pairingServicesFresh = servicesAvailable;
        connectionServicesFresh = servicesAvailable;
        pairingDiscoveryState = servicesAvailable
            ? services.Value!.Any(service => service.Kind == AdbServiceKind.Pairing)
                ? LiveDiscoveryState.Ready
                : LiveDiscoveryState.Empty
            : services.Failure == AdbFailureKind.MdnsUnavailable
                ? LiveDiscoveryState.Unavailable
                : LiveDiscoveryState.Error;
        ServiceMessage = servicesAvailable || services.Failure == AdbFailureKind.MdnsUnavailable
            ? null : text.Get("devices.live.serviceFailed");

        if (servicesAvailable)
        {
            UpdatePairingServices(services.Value!);
            UpdateConnectionServices(services.Value!);
        }

        OnPropertyChanged(nameof(HasObservedDevices));
        OnPropertyChanged(nameof(SelectedObservedDevice));
        OnPropertyChanged(nameof(SelectedConnectionEndpoint));
        OnPropertyChanged(nameof(PairingDiscoveryStatusLabel));
        NotifyPairEligibility();
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
        pairingDiscoveryState = LiveDiscoveryState.Error;
        pairingServicesFresh = false;
        connectionServicesFresh = false;
        OnPropertyChanged(nameof(PairingDiscoveryStatusLabel));
        NotifyPairEligibility();
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
        pairingDiscoveryState = LiveDiscoveryState.Cancelled;
        pairingServicesFresh = false;
        connectionServicesFresh = false;
        OnPropertyChanged(nameof(PairingDiscoveryStatusLabel));
        NotifyPairEligibility();
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
            : text.Get(result.Failure switch
            {
                AdbFailureKind.Unavailable => "devices.live.pairUnavailable",
                AdbFailureKind.TimedOut => "devices.live.pairTimedOut",
                AdbFailureKind.PairingRejected => "devices.live.pairRejected",
                AdbFailureKind.MalformedResponse => "devices.live.pairResponseUnknown",
                _ => "devices.live.pairProcessFailed",
            });
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

        PairingMessage = text.Get("devices.live.pairProcessFailed");
        FinishPairing();
    }

    /// <summary>Settles only presentation state for one pairing operation.</summary>
    private void FinishPairing()
    {
        isPairing = false;
        PairingCode = string.Empty;
        OnPropertyChanged(nameof(IsPairing));
        OnPropertyChanged(nameof(CanRefresh));
        NotifyPairEligibility();
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

    /** Preserves an unchanged target and never substitutes a different advertised endpoint. */
    private void UpdatePairingServices(IReadOnlyList<AdbMdnsService> services)
    {
        AdbMdnsService[] candidates = services.Where(service => service.Kind == AdbServiceKind.Pairing).ToArray();
        ObservedServiceChoice? previous = selectedPairingService;
        updatingPairingServices = true;

        try
        {
            foreach (ObservedServiceChoice choice in PairingServices.ToArray())
            {
                if (!candidates.Contains(choice.Service))
                {
                    PairingServices.Remove(choice);
                }
            }

            foreach (AdbMdnsService service in candidates)
            {
                if (!PairingServices.Any(choice => choice.Service == service))
                {
                    PairingServices.Add(new ObservedServiceChoice(service,
                        $"{service.InstanceName} · {service.Endpoint}"));
                }
            }

            if (previous is not null && !PairingServices.Contains(previous))
            {
                selectedPairingService = null;
                pairingSelectionInvalidated = true;
                PairingCode = string.Empty;
            }

            if (previous is null && !pairingSelectionInvalidated && !isManualPairing
                && PairingServices.Count == 1)
            {
                selectedPairingService = PairingServices[0];
                PairingCode = string.Empty;
            }
        }
        finally
        {
            updatingPairingServices = false;
        }

        OnPropertyChanged(nameof(SelectedPairingService));
    }

    /** Keeps an unchanged connection target and offers one unambiguous service. */
    private void UpdateConnectionServices(IReadOnlyList<AdbMdnsService> services)
    {
        AdbMdnsService[] candidates = services.Where(service => service.Kind == AdbServiceKind.Connection).ToArray();
        ObservedServiceChoice? previous = selectedConnectionService;
        updatingConnectionServices = true;

        try
        {
            foreach (ObservedServiceChoice choice in ConnectionServices.ToArray())
            {
                if (!candidates.Contains(choice.Service))
                {
                    ConnectionServices.Remove(choice);
                }
            }

            foreach (AdbMdnsService service in candidates)
            {
                if (!ConnectionServices.Any(choice => choice.Service == service))
                {
                    ConnectionServices.Add(new ObservedServiceChoice(service,
                        $"{service.InstanceName} · {service.Endpoint}"));
                }
            }

            selectedConnectionService = previous is not null && ConnectionServices.Contains(previous)
                ? previous
                : previous is null && ConnectionServices.Count == 1 && !isManualConnection
                    ? ConnectionServices[0]
                    : null;
        }
        finally
        {
            updatingConnectionServices = false;
        }

        OnPropertyChanged(nameof(SelectedConnectionService));
    }

    /** Derives the button state, explanation and captured target from one validation pass. */
    private PairEligibility EvaluatePairEligibility()
    {
        if (!IsLiveEnabled || !isWirelessSetupOpen)
        {
            return new(false, "devices.live.pairOpenSetup", null);
        }

        if (isPairing)
        {
            return new(false, "devices.live.pairing", null);
        }

        if (isConnecting)
        {
            return new(false, "devices.live.pairConnectBusy", null);
        }

        if (IsDiscovering)
        {
            return new(false, "devices.live.pairingSearching", null);
        }

        NetworkEndpoint? endpoint;

        if (isManualPairing)
        {
            if (manualPairingEndpoint.Length == 0)
            {
                return new(false, "devices.live.pairEnterManual", null);
            }

            endpoint = AdbDiscoveryService.ResolveManualEndpoint(manualPairingEndpoint).Value;

            if (endpoint is null)
            {
                return new(false, "devices.live.pairInvalidEndpoint", null);
            }
        }
        else
        {
            if (pairingDiscoveryState == LiveDiscoveryState.Error)
            {
                return new(false, "devices.live.pairDiscoveryFailed", null);
            }

            if (pairingDiscoveryState == LiveDiscoveryState.Unavailable)
            {
                return new(false, "devices.live.pairDiscoveryUnavailable", null);
            }

            if (pairingDiscoveryState == LiveDiscoveryState.Cancelled)
            {
                return new(false, "devices.live.pairDiscoveryCancelled", null);
            }

            if (pairingDiscoveryState == LiveDiscoveryState.Empty)
            {
                return new(false, "devices.live.pairNoCandidate", null);
            }

            if (!pairingServicesFresh)
            {
                return new(false, "devices.live.pairNotSearched", null);
            }

            if (selectedPairingService is null)
            {
                string key = pairingSelectionInvalidated
                    ? "devices.live.pairTargetChanged"
                    : "devices.live.pairChooseCandidate";
                return new(false, key, null);
            }

            bool selectedCandidateIsFresh = PairingServices.Contains(selectedPairingService)
                && selectedPairingService.Service.Kind == AdbServiceKind.Pairing;

            if (!selectedCandidateIsFresh)
            {
                return new(false, "devices.live.pairChooseCandidate", null);
            }

            endpoint = selectedPairingService.Service.Endpoint;
        }

        if (pairingCode.Length != PairingCodeLength || !pairingCode.All(char.IsAsciiDigit))
        {
            return new(false, "devices.live.pairEnterCode", null);
        }

        return new(true, "devices.live.pairReady", endpoint);
    }

    /** Keeps inline guidance and button eligibility synchronized after every state change. */
    private void NotifyPairEligibility()
    {
        OnPropertyChanged(nameof(CanPair));
        OnPropertyChanged(nameof(PairEligibilityMessage));
    }

    /// <summary>Resolves the independent connection endpoint only after explicit entry or choice.</summary>
    private NetworkEndpoint? ResolveConnectionEndpoint() =>
        isManualConnection
            ? AdbDiscoveryService.ResolveManualEndpoint(manualConnectionEndpoint).Value
            : connectionServicesFresh && selectedConnectionService is not null
                && ConnectionServices.Contains(selectedConnectionService)
                && selectedConnectionService.Service.Kind == AdbServiceKind.Connection
                    ? selectedConnectionService.Service.Endpoint
                    : null;
}
