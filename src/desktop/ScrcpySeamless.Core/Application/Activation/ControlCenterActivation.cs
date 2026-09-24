namespace ScrcpySeamless.Core.Application.Activation;

/// <summary>The action requested by a repeated application launch.</summary>
public enum ActivationKind
{
    ShowControlCenter,
    FocusSession,
}

/// <summary>A semantic activation request, independent of any platform channel.</summary>
public sealed record ActivationRequest
{
    public ActivationKind Kind { get; }
    public SessionId? SessionId { get; }

    /// <summary>Creates a request with the identity required by its action.</summary>
    public ActivationRequest(ActivationKind kind, SessionId? sessionId = null)
    {
        if (kind == ActivationKind.FocusSession && sessionId is null)
        {
            throw new ArgumentException("A session must be specified for focus activation.", nameof(sessionId));
        }

        if (kind == ActivationKind.ShowControlCenter && sessionId is not null)
        {
            throw new ArgumentException("Control-center activation cannot target a session.", nameof(sessionId));
        }

        if (sessionId.HasValue && sessionId.Value.Value == Guid.Empty)
        {
            throw new ArgumentException("The target session identity is invalid.", nameof(sessionId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        SessionId = sessionId;
    }
}

/// <summary>Whether this launch owns the control center or reached its owner.</summary>
public enum ActivationDisposition
{
    PrimaryOwner,
    ForwardedToPrimary,
}

/// <summary>Same-user single-instance boundary implemented by Infrastructure.</summary>
public interface IControlCenterActivation : IAsyncDisposable
{
    /// <summary>Acquires the sole same-user owner or forwards to that owner's restricted channel.</summary>
    Task<ActivationDisposition> AcquireOrForwardAsync(
        ActivationRequest request,
        CancellationToken cancellationToken);

    /// <summary>Observes subsequent requests only in the primary owner.</summary>
    IAsyncEnumerable<ActivationRequest> ObserveRequestsAsync(CancellationToken cancellationToken);
}
