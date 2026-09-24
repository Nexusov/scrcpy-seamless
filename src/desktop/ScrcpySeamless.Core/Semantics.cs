namespace ScrcpySeamless.Core;

/// <summary>Transport reported by discovery or selected by application policy.</summary>
public enum TransportKind
{
    Usb,
    Network,
}

/// <summary>Semantic availability reported for a discovered device.</summary>
public enum DeviceAvailability
{
    Available,
    Offline,
    Unauthorized,
    Unavailable,
}

/// <summary>Semantic connection state, independent of presentation text.</summary>
public enum ConnectionStatus
{
    Idle,
    Connecting,
    Connected,
    Recovering,
    Failed,
    Cancelled,
}

/// <summary>Stable semantic failure categories for Core callers.</summary>
public enum CoreErrorCode
{
    InvalidIdentity,
    InvalidEndpoint,
    InvalidConfiguration,
    UnsupportedConfigurationVersion,
    DuplicateProfile,
    DeviceUnavailable,
    DeviceUnauthorized,
    PairingFailed,
    TransportUnavailable,
    ConnectionFailed,
    NativeHostStartFailed,
    Cancelled,
}

/// <summary>A machine-readable validation issue at a schema path.</summary>
public sealed record ValidationIssue(CoreErrorCode Code, string Path);
