using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Refuses invalid or oversized evidence without returning a misleading partial export.</summary>
public sealed class EvidenceFormatException : Exception
{
    /// <summary>Reports a fixed diagnostic category without exporting source payloads.</summary>
    public EvidenceFormatException(string category) : base(category) { }
}

/// <summary>Explicit privacy allowlist for the versioned DEV evidence copy action.</summary>
public static class SessionEvidenceFormatter
{
    public const int MaximumOutputBytes = 64 * 1024;
    public const int MaximumLifecycleEvents = 64;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Formats semantic evidence outside the owning lock; a lower budget is useful for bounded consumers.</summary>
    public static string Format(SessionEvidenceSnapshot snapshot, int maximumBytes = MaximumOutputBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot, maximumBytes);
        NativeHandshakeObservation? handshake = snapshot.AcceptedHandshake;
        NativeExit? terminal = snapshot.TerminalResult;
        SessionCleanupEvidence cleanup = snapshot.Cleanup;
        var export = new
        {
            snapshot.FormatVersion,
            CapturedUtc = snapshot.CapturedUtc.ToUniversalTime(),
            snapshot.SessionId,
            snapshot.ProcessId,
            ObservationStage = snapshot.ObservationStage.ToString(),
            BuildIdentity = new
            {
                Source = "ExistingBuildMetadata",
                SourceSha = snapshot.BuildMetadataSourceSha,
                Availability = snapshot.BuildMetadataSourceSha is null ? "NotRecorded" : "Recorded",
                IndependentBinaryVerification = "NotRecorded",
            },
            Handshake = new
            {
                Availability = handshake is null ? "NotRecorded" : "Accepted",
                Product = handshake?.Product,
                ProtocolMajor = handshake?.ProtocolMajor,
                ProtocolMinor = handshake?.ProtocolMinor,
                Capabilities = handshake?.Capabilities,
                OmittedCapabilityCount = handshake?.OmittedCapabilityCount,
            },
            snapshot.NativeReadyObserved,
            Lifecycle = new
            {
                Capacity = MaximumLifecycleEvents,
                TotalObserved = snapshot.TotalObserved.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RetainedCount = snapshot.Events.Count,
                OmittedCount = (snapshot.TotalObserved - (ulong)snapshot.Events.Count)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                FirstRetainedSequence = snapshot.Events.FirstOrDefault()?.Sequence
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                LastRetainedSequence = snapshot.Events.LastOrDefault()?.Sequence
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ReceiptTimestamps = "NotRecorded",
                NativeScope = "NotRecorded",
                Events = snapshot.Events.Select(observation => new
                {
                    SessionId = observation.SessionId.Value,
                    Sequence = observation.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NativeUtc = observation.Utc.ToUniversalTime(),
                    NativeMonotonicMicroseconds = observation.MonotonicMicroseconds
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ConnectionAttemptId = observation.ConnectionAttemptId?.Value,
                    Subsystem = observation.Subsystem.ToString(),
                    EventType = observation.EventType.ToString(),
                    Reason = observation.Reason.ToString(),
                    Error = observation.Error.ToString(),
                }).ToArray(),
            },
            SessionResult = new
            {
                CompletionState = snapshot.CompletionState.ToString(),
                SessionId = terminal?.SessionId.Value,
                Reason = terminal?.Reason.ToString(),
                ErrorCode = terminal?.ErrorCode?.ToString(),
                ProcessExitCode = (int?)null,
                ProcessExitCodeAvailability = "NotRecorded",
            },
            Cleanup = new
            {
                Intent = cleanup.Intent.ToString(),
                State = cleanup.State.ToString(),
                StopCallState = cleanup.StopCallState.ToString(),
                StopRequestedReason = cleanup.StopRequestedReason?.ToString(),
                StopFailure = cleanup.StopFailure?.ToString(),
                FailureCategory = cleanup.FailureCategory.ToString(),
                cleanup.ExactChildExitObserved,
                cleanup.OwnershipReleased,
                EscalationObserved = cleanup.StopFailure == MachineNativeSession.StopFailure.Escalated
                    ? (bool?)true : null,
                Scope = "ManagedAdapterDisposalAndOwnedDesktopWork",
                PhoneSideCleanup = "NotRecorded",
                WireStopQueued = "NotRecorded",
                WireStopWritten = "NotRecorded",
                WireStopAcknowledged = "NotRecorded",
            },
        };
        string formatted = JsonSerializer.Serialize(export, JsonOptions);

        if (Encoding.UTF8.GetByteCount(formatted) > maximumBytes)
        {
            throw new EvidenceFormatException("EvidenceOutputBudgetExceeded");
        }

        return formatted;
    }

    /// <summary>Rejects malformed semantic values and cross-session records before serialization.</summary>
    private static void Validate(SessionEvidenceSnapshot snapshot, int maximumBytes)
    {
        bool invalidBounds = maximumBytes <= 0 || maximumBytes > MaximumOutputBytes ||
            snapshot.Events.Count > MaximumLifecycleEvents || snapshot.TotalObserved < (ulong)snapshot.Events.Count;

        if (invalidBounds)
        {
            throw new EvidenceFormatException("InvalidEvidenceBounds");
        }

        bool invalidIdentity = snapshot.SessionId == Guid.Empty || snapshot.ProcessId is <= 0 ||
            snapshot.TerminalResult is { } terminal && terminal.SessionId.Value != snapshot.SessionId;

        if (invalidIdentity)
        {
            throw new EvidenceFormatException("InvalidEvidenceIdentity");
        }

        RequireDefined(snapshot.ObservationStage);
        RequireDefined(snapshot.CompletionState);
        RequireDefined(snapshot.Cleanup.Intent);
        RequireDefined(snapshot.Cleanup.State);
        RequireDefined(snapshot.Cleanup.StopCallState);
        RequireDefined(snapshot.Cleanup.FailureCategory);

        if (snapshot.Cleanup.StopFailure is { } stopFailure)
        {
            RequireDefined(stopFailure);
        }

        if (snapshot.Cleanup.StopRequestedReason is { } requestedReason)
        {
            RequireDefined(requestedReason);
        }

        if (snapshot.TerminalResult?.ErrorCode is { } errorCode)
        {
            RequireDefined(errorCode);
        }

        if (snapshot.AcceptedHandshake is { } handshake)
        {
            bool invalidHandshake = handshake.Product != "scrcpy-seamless" || handshake.ProtocolMajor < 0 ||
                handshake.ProtocolMinor < 0 || handshake.OmittedCapabilityCount < 0 ||
                handshake.Capabilities.Distinct(StringComparer.Ordinal).Count() != handshake.Capabilities.Count ||
                handshake.Capabilities.Any(capability => capability is not ("focus-window" or "lifecycle-v1" or "stop"));

            if (invalidHandshake)
            {
                throw new EvidenceFormatException("InvalidHandshakeEvidence");
            }
        }

        ulong previousSequence = 0;

        foreach (NativeLifecycleObservation observation in snapshot.Events)
        {
            bool invalidObservation = observation.SessionId.Value != snapshot.SessionId ||
                observation.Sequence <= previousSequence || observation.ConnectionAttemptId?.Value == Guid.Empty;

            if (invalidObservation)
            {
                throw new EvidenceFormatException("InvalidLifecycleEvidence");
            }

            RequireDefined(observation.Subsystem);
            RequireDefined(observation.EventType);
            RequireDefined(observation.Reason);
            RequireDefined(observation.Error);
            previousSequence = observation.Sequence;
        }
    }

    /// <summary>Prevents arbitrary enum numeric values from becoming export text.</summary>
    private static void RequireDefined<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new EvidenceFormatException("InvalidEvidenceClassification");
        }
    }
}
