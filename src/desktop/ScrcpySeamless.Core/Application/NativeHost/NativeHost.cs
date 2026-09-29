using System.Text.Json;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Core.Application.NativeHost;

/// <summary>Semantic reason for ending an owned native session.</summary>
public enum NativeTerminationReason
{
    UserStop,
    WindowClosed,
    TransportLost,
    TransientConnectionFailure,
    ServerStartFailure,
    InvalidConfiguration,
    ProtocolError,
    DecoderFatal,
    ControllerFatal,
    TimeLimitReached,
    ApplicationShutdown,
    NativeFailure,
}

/// <summary>Typed launch snapshot for one native child; executable details belong to Infrastructure.</summary>
public sealed class NativeStartRequest
{
    private readonly Dictionary<string, JsonElement> options;
    private readonly bool reconnect;

    public SessionId SessionId { get; }
    public ConnectionPlan Plan { get; }
    public ProfileId ProfileId => Plan.ProfileId;
    public string SelectedAdbSerial { get; }
    public TransportKind SelectedTransport { get; }
    public NetworkEndpoint? ReconnectEndpoint { get; }
    public string? ConfigurationRevision { get; }
    public MirroringPreferences Mirroring => new()
    {
        Reconnect = reconnect,
        Options = new Dictionary<string, JsonElement>(options, StringComparer.Ordinal),
    };

    /// <summary>Captures validated mirroring preferences without retaining a mutable caller dictionary.</summary>
    public NativeStartRequest(SessionId sessionId, ConnectionPlan plan, MirroringPreferences mirroring,
        string selectedAdbSerial, TransportKind selectedTransport,
        NetworkEndpoint? reconnectEndpoint, string? configurationRevision)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Session identity is required.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(mirroring);

        if (mirroring.Validate(nameof(mirroring)).Count != 0)
        {
            throw new ArgumentException("Mirroring preferences are invalid.", nameof(mirroring));
        }

        if (string.IsNullOrWhiteSpace(selectedAdbSerial) || selectedAdbSerial.Any(char.IsControl) ||
            !Enum.IsDefined(selectedTransport))
        {
            throw new ArgumentException("An explicit ADB transport selection is required.", nameof(selectedAdbSerial));
        }

        if (configurationRevision is not null && configurationRevision.Length == 0)
        {
            throw new ArgumentException("A configuration revision cannot be empty.", nameof(configurationRevision));
        }

        SessionId = sessionId;
        Plan = plan;
        SelectedAdbSerial = selectedAdbSerial;
        SelectedTransport = selectedTransport;
        ReconnectEndpoint = reconnectEndpoint;
        ConfigurationRevision = configurationRevision;
        reconnect = mirroring.Reconnect;
        options = mirroring.Options.ToDictionary(
            option => option.Key,
            option => option.Value.Clone(),
            StringComparer.Ordinal);
    }
}

/// <summary>Observed terminal result of one owned native child.</summary>
public sealed record NativeExit
{
    public SessionId SessionId { get; }
    public NativeTerminationReason Reason { get; }
    public CoreErrorCode? ErrorCode { get; }

    /// <summary>Captures a valid terminal identity and semantic reason.</summary>
    public NativeExit(SessionId sessionId, NativeTerminationReason reason, CoreErrorCode? errorCode = null)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Session identity is required.", nameof(sessionId));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        SessionId = sessionId;
        Reason = reason;
        ErrorCode = errorCode;
    }
}

/// <summary>Starts one independently owned native child per requested session.</summary>
public interface INativeHost
{
    /// <summary>Starts and transfers lifetime ownership of exactly one native child.</summary>
    Task<INativeSession> StartAsync(
        NativeStartRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Owned native child lifetime, independent of process IDs and window handles.</summary>
public interface INativeSession : IAsyncDisposable
{
    SessionId SessionId { get; }

    /// <summary>Completes once with the semantic exit result; observing it does not stop the child.</summary>
    Task<NativeExit> Completion { get; }

    /// <summary>Requests an idempotent graceful stop of this child only.</summary>
    Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken);
}

/// <summary>Result of a native-window focus attempt, separate from OS foreground permission.</summary>
public enum NativeFocusOutcome { Applied, NoWindow, InvalidState, Failed }

/// <summary>Semantic native subsystem emitting one ordered lifecycle observation.</summary>
public enum NativeLifecycleSubsystem { Protocol, Native, Connection, Video, Audio, Control }

/// <summary>Lifecycle claims permitted by the negotiated native channel.</summary>
public enum NativeLifecycleEventType
{
    NativeReady, Connecting, StreamStarted, TransportLost, ReconnectScheduled,
    Reconnecting, StreamResumed, CapabilityDegraded, SessionStopped, FatalError,
}

/// <summary>Typed limit of evidence for the observed transition.</summary>
public enum NativeLifecycleReason { None, UserStop, WindowClosed, TransportLost, ProtocolError, NativeFailure, Unknown }

/// <summary>Typed native error classification without raw diagnostic payloads.</summary>
public enum NativeLifecycleError { None, Unknown, InvalidMessage, Unsupported, InternalFailure }

/// <summary>One native-process ordered event; attempt identity is absent only where the protocol permits it.</summary>
public sealed record NativeLifecycleObservation(
    SessionId SessionId,
    ulong Sequence,
    DateTimeOffset Utc,
    ulong MonotonicMicroseconds,
    ConnectionAttemptId? ConnectionAttemptId,
    NativeLifecycleSubsystem Subsystem,
    NativeLifecycleEventType EventType,
    NativeLifecycleReason Reason,
    NativeLifecycleError Error);

/// <summary>Optional machine-session operations; legacy native sessions remain valid INativeSession instances.</summary>
public interface INativeInteractiveSession : INativeSession
{
    /// <summary>Requests focus on the native main thread without claiming OS foreground success.</summary>
    Task<NativeFocusOutcome> FocusWindowAsync(CancellationToken cancellationToken);

    /// <summary>Consumes one bounded stream of ordered native observations.</summary>
    IAsyncEnumerable<NativeLifecycleObservation> ObserveLifecycleAsync(CancellationToken cancellationToken);
}
