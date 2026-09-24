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
    public MirroringPreferences Mirroring => new()
    {
        Reconnect = reconnect,
        Options = new Dictionary<string, JsonElement>(options, StringComparer.Ordinal),
    };

    /// <summary>Captures validated mirroring preferences without retaining a mutable caller dictionary.</summary>
    public NativeStartRequest(SessionId sessionId, ConnectionPlan plan, MirroringPreferences mirroring)
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

        SessionId = sessionId;
        Plan = plan;
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
