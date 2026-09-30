using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Verifies the bounded privacy surface without reading settings, processes or a real clipboard.</summary>
public sealed class SessionEvidenceFormatterTests
{
    private static readonly DateTimeOffset CapturedUtc = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly SessionId Session = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
    private static readonly ConnectionAttemptId FirstAttempt = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
    private static readonly ConnectionAttemptId SecondAttempt = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

    /// <summary>Detaching events protects captures from caller mutation and subsequent immutable updates.</summary>
    [Fact]
    public void SnapshotDetachesEventsOnConstructionAndWithUpdates()
    {
        NativeLifecycleObservation first = Observation(1, FirstAttempt);
        NativeLifecycleObservation second = Observation(2, SecondAttempt);
        NativeLifecycleObservation[] callerEvents = [first];
        SessionEvidenceSnapshot original = Snapshot(callerEvents);
        callerEvents[0] = second;
        Assert.Equal(first, Assert.Single(original.Events));
        Assert.Throws<NotSupportedException>(() => ((IList<NativeLifecycleObservation>)original.Events)[0] = second);

        NativeLifecycleObservation[] replacementEvents = [first, second];
        SessionEvidenceSnapshot updated = original with { Events = replacementEvents, TotalObserved = 2 };
        replacementEvents[1] = first;
        Assert.Equal(second, updated.Events[1]);
        Assert.Single(original.Events);
        Assert.Equal(2, updated.Events.Count);
    }

    /// <summary>Retained history preserves exact sequences and distinctly observed attempts across repeated reads.</summary>
    [Fact]
    public void BoundedHistoryReportsOmissionsAndKeepsExactIntegerValues()
    {
        const ulong firstSequence = 9007199254740993;
        NativeLifecycleObservation[] history = Enumerable.Range(0, SessionEvidenceFormatter.MaximumLifecycleEvents)
            .Select(offset => Observation(firstSequence + (ulong)offset, offset % 2 == 0 ? FirstAttempt : SecondAttempt))
            .ToArray();
        const ulong totalObserved = 80;
        SessionEvidenceSnapshot snapshot = Snapshot(history) with { TotalObserved = totalObserved };
        string firstRead = SessionEvidenceFormatter.Format(snapshot);
        Assert.Equal(firstRead, SessionEvidenceFormatter.Format(snapshot));
        Assert.True(Encoding.UTF8.GetByteCount(firstRead) <= SessionEvidenceFormatter.MaximumOutputBytes);
        using JsonDocument document = JsonDocument.Parse(firstRead);
        JsonElement lifecycle = document.RootElement.GetProperty("Lifecycle");
        Assert.Equal("80", lifecycle.GetProperty("TotalObserved").GetString());
        Assert.Equal("16", lifecycle.GetProperty("OmittedCount").GetString());
        Assert.Equal("9007199254740993", lifecycle.GetProperty("FirstRetainedSequence").GetString());
        JsonElement[] events = lifecycle.GetProperty("Events").EnumerateArray().ToArray();
        Assert.Equal(SessionEvidenceFormatter.MaximumLifecycleEvents, events.Length);
        Assert.Equal(FirstAttempt.Value.ToString(), events[0].GetProperty("ConnectionAttemptId").GetString());
        Assert.Equal(SecondAttempt.Value.ToString(), events[1].GetProperty("ConnectionAttemptId").GetString());
        Assert.Equal(history.Length, snapshot.Events.Count);
    }

    /// <summary>Unknown extension capability text and malformed metadata cannot bypass the semantic allowlist.</summary>
    [Fact]
    public void PrivacyAllowlistOmitsSyntheticSecretsAndPreservesSafeCapabilityCounts()
    {
        const string syntheticSecret = "synthetic-private-key_pairing-987654_endpoint-private.invalid:8765";
        string[] sourceCapabilities = ["stop", syntheticSecret, "lifecycle-v1", "synthetic-usb-serial", "focus-window"];
        NativeHandshakeObservation handshake = new("scrcpy-seamless", 1, 0, sourceCapabilities);
        SessionEvidenceSnapshot snapshot = Snapshot([Observation(1, null)]) with
        {
            AcceptedHandshake = handshake,
            BuildMetadataSourceSha = syntheticSecret,
        };
        sourceCapabilities[0] = syntheticSecret;
        string output = SessionEvidenceFormatter.Format(snapshot);
        Assert.DoesNotContain(syntheticSecret, output);
        Assert.DoesNotContain("synthetic-usb-serial", output);
        Assert.DoesNotContain("private.invalid", output);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement negotiation = document.RootElement.GetProperty("Handshake");
        Assert.Equal(2, negotiation.GetProperty("OmittedCapabilityCount").GetInt32());
        Assert.Equal(["stop", "lifecycle-v1", "focus-window"], negotiation.GetProperty("Capabilities")
            .EnumerateArray().Select(capability => Assert.IsType<string>(capability.GetString())).ToArray());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("BuildIdentity").GetProperty("SourceSha").ValueKind);
    }

    /// <summary>Unavailable negotiation, wire details and exit code remain explicit instead of fabricated success.</summary>
    [Fact]
    public void MissingObservationsRemainUnavailable()
    {
        SessionEvidenceSnapshot snapshot = Snapshot([]) with
        {
            SessionId = null,
            ProcessId = null,
            ObservationStage = SessionObservationStage.NoSession,
            CompletionState = SessionCompletionState.NotRecorded,
        };
        using JsonDocument document = JsonDocument.Parse(SessionEvidenceFormatter.Format(snapshot));
        JsonElement evidence = document.RootElement;
        Assert.Equal("NotRecorded", evidence.GetProperty("Handshake").GetProperty("Availability").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("SessionResult").GetProperty("ProcessExitCode").ValueKind);
        Assert.Equal("NotRecorded", evidence.GetProperty("Cleanup").GetProperty("WireStopAcknowledged").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("Cleanup").GetProperty("EscalationObserved").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("Cleanup").GetProperty("ExactChildExitObserved").ValueKind);
        Assert.False(evidence.GetProperty("NativeReadyObserved").GetBoolean());
    }

    /// <summary>Unsuccessful session, successful cleanup and failed Stop stay separately represented.</summary>
    [Fact]
    public void SessionFailureDoesNotBecomeCleanupFailureOrUserIntent()
    {
        SessionEvidenceSnapshot snapshot = Snapshot([Observation(1, null)]) with
        {
            TerminalResult = new NativeExit(Session, NativeTerminationReason.NativeFailure),
            CompletionState = SessionCompletionState.Completed,
            ObservationStage = SessionObservationStage.Released,
            Cleanup = new(SessionCleanupIntent.AutomaticTerminal, SessionCleanupState.Succeeded,
                SessionStopCallState.Failed, MachineNativeSession.StopFailure.TerminalFailure,
                SessionCleanupFailureCategory.StopOperation, true, true, NativeTerminationReason.NativeFailure),
        };
        using JsonDocument document = JsonDocument.Parse(SessionEvidenceFormatter.Format(snapshot));
        JsonElement evidence = document.RootElement;
        Assert.Equal("NativeFailure", evidence.GetProperty("SessionResult").GetProperty("Reason").GetString());
        Assert.Equal("AutomaticTerminal", evidence.GetProperty("Cleanup").GetProperty("Intent").GetString());
        Assert.Equal("Succeeded", evidence.GetProperty("Cleanup").GetProperty("State").GetString());
        Assert.Equal("Failed", evidence.GetProperty("Cleanup").GetProperty("StopCallState").GetString());
        Assert.Equal("TerminalFailure", evidence.GetProperty("Cleanup").GetProperty("StopFailure").GetString());
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("Cleanup").GetProperty("EscalationObserved").ValueKind);
    }

    /// <summary>The hard byte budget refuses export and leaves the detached record available for another explicit attempt.</summary>
    [Fact]
    public void OutputOverflowRefusesWithoutMutatingSource()
    {
        SessionEvidenceSnapshot snapshot = Snapshot([Observation(1, FirstAttempt)]);
        string before = SessionEvidenceFormatter.Format(snapshot);
        const int insufficientBudget = 128;
        EvidenceFormatException exception = Assert.Throws<EvidenceFormatException>(() =>
            SessionEvidenceFormatter.Format(snapshot, insufficientBudget));
        Assert.Equal("EvidenceOutputBudgetExceeded", exception.Message);
        Assert.Equal(before, SessionEvidenceFormatter.Format(snapshot));
        Assert.Throws<EvidenceFormatException>(() =>
            SessionEvidenceFormatter.Format(snapshot, SessionEvidenceFormatter.MaximumOutputBytes + 1));
    }

    /// <summary>Malformed bounds, enum codes and cross-session data cannot become misleading semantic evidence.</summary>
    [Fact]
    public void InvalidSemanticEvidenceIsRefused()
    {
        SessionEvidenceSnapshot snapshot = Snapshot([Observation(1, null)]);
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with { TotalObserved = 0 }));
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with
        {
            ObservationStage = (SessionObservationStage)int.MaxValue,
        }));
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with
        {
            TerminalResult = new NativeExit(SessionId.New(), NativeTerminationReason.UserStop),
        }));
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with
        {
            Events = [Observation(1, null), Observation(1, null)],
            TotalObserved = 2,
        }));
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with
        {
            AcceptedHandshake = new("synthetic-private-product", 1, 0, []),
        }));
        NativeLifecycleObservation[] excess = Enumerable.Range(1, SessionEvidenceFormatter.MaximumLifecycleEvents + 1)
            .Select(sequence => Observation((ulong)sequence, null)).ToArray();
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot with
        {
            Events = excess,
            TotalObserved = (ulong)excess.Length,
        }));
    }

    /// <summary>Build identity is explicitly metadata and typed escalation remains distinct from its absence.</summary>
    [Fact]
    public void MetadataAndEscalationExposeOnlyObservedCategories()
    {
        const string sourceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        SessionEvidenceSnapshot snapshot = Snapshot([]) with
        {
            BuildMetadataSourceSha = sourceSha.ToUpperInvariant(),
            Cleanup = new(SessionCleanupIntent.ExplicitStop, SessionCleanupState.Succeeded,
                SessionStopCallState.Failed, MachineNativeSession.StopFailure.Escalated,
                SessionCleanupFailureCategory.StopOperation, true, true, NativeTerminationReason.UserStop),
        };
        using JsonDocument document = JsonDocument.Parse(SessionEvidenceFormatter.Format(snapshot));
        JsonElement evidence = document.RootElement;
        Assert.Equal(sourceSha, evidence.GetProperty("BuildIdentity").GetProperty("SourceSha").GetString());
        Assert.Equal("ExistingBuildMetadata", evidence.GetProperty("BuildIdentity").GetProperty("Source").GetString());
        Assert.Equal("NotRecorded", evidence.GetProperty("BuildIdentity").GetProperty("IndependentBinaryVerification").GetString());
        Assert.True(evidence.GetProperty("Cleanup").GetProperty("EscalationObserved").GetBoolean());
    }

    /// <summary>Builds only synthetic immutable owner evidence for formatter tests.</summary>
    private static SessionEvidenceSnapshot Snapshot(IReadOnlyList<NativeLifecycleObservation> observations) => new(
        CapturedUtc, Session.Value, 1234, SessionObservationStage.Active, null, null, false,
        observations, (ulong)observations.Count, null, SessionCompletionState.InProgress,
        new(SessionCleanupIntent.None, SessionCleanupState.NotStarted, SessionStopCallState.NotRequested,
            null, SessionCleanupFailureCategory.None, null, false));

    /// <summary>Creates a valid same-session native observation without free-form payload text.</summary>
    private static NativeLifecycleObservation Observation(ulong sequence, ConnectionAttemptId? attempt) => new(
        Session, sequence, CapturedUtc, sequence, attempt, NativeLifecycleSubsystem.Connection,
        NativeLifecycleEventType.Connecting, NativeLifecycleReason.None, NativeLifecycleError.None);
}
