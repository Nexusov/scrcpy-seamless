using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Stage of the exact managed owner, independent of stream success.</summary>
public enum SessionObservationStage { NoSession, Active, CleanupInProgress, Released, OwnershipRetained }

/// <summary>Availability of the adapter's terminal completion result.</summary>
public enum SessionCompletionState { NotRecorded, InProgress, Completed, Failed }

/// <summary>Owner intent that initiated cleanup, independent of the native result.</summary>
public enum SessionCleanupIntent
{
    None, AutomaticTerminal, CompletionCleanup, ExplicitStop, ApplicationShutdown, CancelledLaunch,
}

/// <summary>Settlement of disposal and the Desktop owner's observer and focus work.</summary>
public enum SessionCleanupState { NotStarted, InProgress, Succeeded, Failed }

/// <summary>Outcome of the managed Stop call, without claiming wire acknowledgement.</summary>
public enum SessionStopCallState { NotRequested, InProgress, Succeeded, Failed }

/// <summary>Allowlisted failure category without retaining an exception or its text.</summary>
public enum SessionCleanupFailureCategory { None, StopOperation, DisposalOrOwnedWork, Completion }

/// <summary>Typed cleanup evidence captured by the owner before releasing its adapter.</summary>
public sealed record SessionCleanupEvidence(
    SessionCleanupIntent Intent,
    SessionCleanupState State,
    SessionStopCallState StopCallState,
    MachineNativeSession.StopFailure? StopFailure,
    SessionCleanupFailureCategory FailureCategory,
    bool? ExactChildExitObserved,
    bool OwnershipReleased,
    NativeTerminationReason? StopRequestedReason = null);

/// <summary>Detached evidence values; copying never consumes lifecycle observations.</summary>
public sealed record SessionEvidenceSnapshot
{
    private IReadOnlyList<NativeLifecycleObservation> events = Array.Empty<NativeLifecycleObservation>();
    private string? buildMetadataSourceSha;

    public int FormatVersion => 1;
    public DateTimeOffset CapturedUtc { get; init; }
    public Guid? SessionId { get; init; }
    public int? ProcessId { get; init; }
    public SessionObservationStage ObservationStage { get; init; }
    public string? BuildMetadataSourceSha
    {
        get => buildMetadataSourceSha;
        init => buildMetadataSourceSha = IsSourceSha(value) ? value!.ToLowerInvariant() : null;
    }
    public NativeHandshakeObservation? AcceptedHandshake { get; init; }
    public bool NativeReadyObserved { get; init; }
    public IReadOnlyList<NativeLifecycleObservation> Events
    {
        get => events;
        init => events = Array.AsReadOnly(value.ToArray());
    }
    public ulong TotalObserved { get; init; }
    public NativeExit? TerminalResult { get; init; }
    public SessionCompletionState CompletionState { get; init; }
    public SessionCleanupEvidence Cleanup { get; init; }

    /// <summary>Copies bounded history and keeps immutable typed observations detached from their owner.</summary>
    public SessionEvidenceSnapshot(DateTimeOffset CapturedUtc, Guid? SessionId, int? ProcessId,
        SessionObservationStage ObservationStage, string? BuildMetadataSourceSha,
        NativeHandshakeObservation? AcceptedHandshake, bool NativeReadyObserved,
        IReadOnlyList<NativeLifecycleObservation> Events, ulong TotalObserved,
        NativeExit? TerminalResult, SessionCompletionState CompletionState, SessionCleanupEvidence Cleanup)
    {
        this.CapturedUtc = CapturedUtc;
        this.SessionId = SessionId;
        this.ProcessId = ProcessId;
        this.ObservationStage = ObservationStage;
        this.BuildMetadataSourceSha = BuildMetadataSourceSha;
        this.AcceptedHandshake = AcceptedHandshake;
        this.NativeReadyObserved = NativeReadyObserved;
        this.Events = Events;
        this.TotalObserved = TotalObserved;
        this.TerminalResult = TerminalResult;
        this.CompletionState = CompletionState;
        this.Cleanup = Cleanup;
    }

    /// <summary>Allows only an existing full hexadecimal Git source identity.</summary>
    private static bool IsSourceSha(string? value) =>
        value is { Length: 40 } && value.All(char.IsAsciiHexDigit);
}
