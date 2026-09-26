namespace ScrcpySeamless.Core.Adb;

public enum AdbDeviceState
{
    Device,
    Offline,
    Unauthorized,
    Other,
}

public enum AdbServiceKind
{
    Pairing,
    Connection,
}

public enum AdbFailureKind
{
    None,
    InvalidInput,
    Unavailable,
    MdnsUnavailable,
    TimedOut,
    ProcessFailed,
    MalformedResponse,
    AmbiguousDiscovery,
    PairingRejected,
    ConnectionRejected,
    DeviceSerialPropertyMismatch,
}

public sealed record AdbDevice(string Serial, AdbDeviceState State, string? Model);

public sealed record AdbMdnsService(string InstanceName, AdbServiceKind Kind, NetworkEndpoint Endpoint);

public sealed record AdbPairingOutcome(bool Paired, NetworkEndpoint? ConnectedEndpoint, string? ObservedDeviceSerialProperty);

public sealed record AdbResult<T>(T? Value, AdbFailureKind Failure)
{
    public bool IsSuccess => Failure == AdbFailureKind.None;

    public static AdbResult<T> Success(T value) => new(value, AdbFailureKind.None);

    public static AdbResult<T> Error(AdbFailureKind failure) => new(default, failure);

    public static AdbResult<T> Partial(T value, AdbFailureKind failure) => new(value, failure);
}

/** Semantic ADB operations without process or platform dependencies. */
public interface IAdbGateway
{
    Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken);

    Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken);

    Task<AdbResult<bool>> PairAsync(NetworkEndpoint pairingEndpoint, string pairingCode, CancellationToken cancellationToken);

    Task<AdbResult<bool>> ConnectAsync(NetworkEndpoint connectionEndpoint, CancellationToken cancellationToken);

    Task<AdbResult<string>> GetDeviceSerialPropertyAsync(NetworkEndpoint connectionEndpoint, CancellationToken cancellationToken);
}
