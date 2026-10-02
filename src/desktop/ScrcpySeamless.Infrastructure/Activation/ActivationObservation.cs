namespace ScrcpySeamless.Infrastructure.Activation;

/// <summary>Names allowlisted stages for controlled activation tests, without command or scope data.</summary>
internal enum ActivationStage
{
    OwnershipAcquired,
    ListenerCreated,
    AcceptStarted,
    Accepted,
    DeadlineArmed,
    DeadlineCancelled,
    PeerDisconnected,
    ReadStarted,
    ReadProgress,
    LineComplete,
    Rejected,
    Delivered,
    ResponseWriteStarted,
    ResponseWritten,
    ResponseFlushed,
    PipeDisposed,
    DisposeStarted,
    DisposeCompleted,
    OwnershipReleased,
}

/// <summary>Separates actual deadline expiration from owner lifetime cancellation.</summary>
internal enum ActivationCancellationCause
{
    None,
    RequestDeadline,
    Lifetime,
}

/// <summary>
/// Retains observation-time monotonic ticks for an explicit nonblocking test observer.
/// ReadProgress counts this read; LineComplete counts the full line including newline.
/// DeadlineCancelled observes settled cancellation, not the timer's exact firing instant.
/// </summary>
internal sealed record ActivationObservation(
    ActivationStage Stage,
    long Timestamp,
    int ByteCount = 0,
    ActivationCancellationCause CancellationCause = ActivationCancellationCause.None,
    int? ErrorHResult = null);
