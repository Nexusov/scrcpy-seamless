using System.Globalization;
using System.Text.Json;
using ScrcpySeamless.Core.Application.Diagnostics;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

public enum DiagnosticFormatStatus { Ready, InvalidEvidence, LimitExceeded }
public enum DiagnosticRedactionStatus { NotApplied, AllowlistV1Applied }

public sealed record DiagnosticFormatResult(DiagnosticFormatStatus Status, ReadOnlyMemory<byte> Jsonl = default,
    int SessionCount = 0, int EventCount = 0)
{
    public DiagnosticRedactionStatus Redaction => Status == DiagnosticFormatStatus.Ready
        ? DiagnosticRedactionStatus.AllowlistV1Applied : DiagnosticRedactionStatus.NotApplied;
}

/// <summary>Append-only JSONL v1: allowlisted typed values only, with export-local identity ordinals.</summary>
public static class DiagnosticExportFormatter
{
    public const int FormatVersion = 1;
    public const int MaximumSessions = SessionsWorkspaceViewModel.MaximumEntries;
    public const int MaximumEventsPerSession = SessionEvidenceFormatter.MaximumLifecycleEvents;
    public const int MaximumRecords = 1 + MaximumSessions * (1 + MaximumEventsPerSession);
    public const int MaximumDepth = 6;
    private const int MaximumHandshakeCapabilities = 16;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = MaximumDepth };

    public static DiagnosticFormatResult Format(IReadOnlyList<SessionEvidenceSnapshot> snapshots)
        => FormatWithinBudget(snapshots, DiagnosticExportLimits.MaximumBytes);

    internal static DiagnosticFormatResult FormatWithinBudget(IReadOnlyList<SessionEvidenceSnapshot> snapshots, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        if (snapshots.Count > MaximumSessions || maximumBytes is <= 0 or > DiagnosticExportLimits.MaximumBytes)
        {
            return new(DiagnosticFormatStatus.LimitExceeded);
        }

        try
        {
            HashSet<Guid> sessionIdentities = [];
            int eventCount = 0;

            foreach (SessionEvidenceSnapshot snapshot in snapshots)
            {
                if (snapshot is null || snapshot.Cleanup is null)
                {
                    return new(DiagnosticFormatStatus.InvalidEvidence);
                }

                if (snapshot.Events.Count > MaximumEventsPerSession)
                {
                    return new(DiagnosticFormatStatus.LimitExceeded);
                }

                if (snapshot.AcceptedHandshake is { } handshake &&
                    (handshake.Capabilities.Count > MaximumHandshakeCapabilities ||
                     handshake.OmittedCapabilityCount > MaximumHandshakeCapabilities - handshake.Capabilities.Count))
                {
                    return new(DiagnosticFormatStatus.InvalidEvidence);
                }
                SessionEvidenceFormatter.Validate(snapshot, SessionEvidenceFormatter.MaximumOutputBytes);

                if (snapshot.SessionId is { } identity && !sessionIdentities.Add(identity))
                {
                    return new(DiagnosticFormatStatus.InvalidEvidence);
                }
                eventCount += snapshot.Events.Count;
            }

            using BoundedJsonlStream output = new(maximumBytes);
            WriteLine(output, new
            {
                Record = "Export",
                FormatVersion,
                MaximumBytes = DiagnosticExportLimits.MaximumBytes,
                MaximumRecords,
                MaximumDepth,
                SessionCount = snapshots.Count,
                RetainedEventCount = eventCount,
                IdentityScope = "ExportLocalOrdinalsOnly",
                Redaction = DiagnosticRedactionStatus.AllowlistV1Applied.ToString(),
                Coverage = "ExistingBoundedDesktopProjection",
                Privacy = "NoProfilesIdentifiersPathsDeviceDataCommandsStderrClipboardOrErrorPayloads",
                Rotation = "ExplicitSingleNewFileNoBackgroundLog",
            });

            for (int index = 0; index < snapshots.Count; index++)
            {
                SessionEvidenceSnapshot snapshot = snapshots[index];
                Dictionary<Guid, int> attempts = [];

                foreach (NativeLifecycleObservation observation in snapshot.Events)
                {
                    if (observation.ConnectionAttemptId is { } attempt && !attempts.ContainsKey(attempt.Value))
                    {
                        attempts.Add(attempt.Value, attempts.Count + 1);
                    }
                }
                bool monotonic = true;
                ulong? previous = null;
                List<string> recoveries = [];
                ulong? transportLost = null;

                foreach (NativeLifecycleObservation observation in snapshot.Events)
                {
                    monotonic &= previous is null || observation.MonotonicMicroseconds >= previous.Value;
                    previous = observation.MonotonicMicroseconds;

                    if (observation.EventType == NativeLifecycleEventType.TransportLost && transportLost is null)
                    {
                        transportLost = observation.MonotonicMicroseconds;
                    }

                    if (observation.EventType == NativeLifecycleEventType.StreamResumed && transportLost is { } lost)
                    {
                        if (monotonic && observation.MonotonicMicroseconds >= lost)
                        {
                            recoveries.Add(Number(observation.MonotonicMicroseconds - lost));
                        }
                        transportLost = null;
                    }
                }
                string? span = snapshot.Events.Count > 0 && monotonic
                    ? Number(snapshot.Events[^1].MonotonicMicroseconds - snapshot.Events[0].MonotonicMicroseconds) : null;
                WriteLine(output, new
                {
                    Record = "Session",
                    Session = index + 1,
                    CapturedUtc = snapshot.CapturedUtc.ToUniversalTime(),
                    Stage = snapshot.ObservationStage.ToString(),
                    snapshot.NativeReadyObserved,
                    Handshake = new
                    {
                        Accepted = snapshot.AcceptedHandshake is not null,
                        ProtocolMajor = snapshot.AcceptedHandshake?.ProtocolMajor,
                        ProtocolMinor = snapshot.AcceptedHandshake?.ProtocolMinor,
                    },
                    Observed = Number(snapshot.TotalObserved),
                    Retained = snapshot.Events.Count,
                    Omitted = Number(snapshot.TotalObserved - (ulong)snapshot.Events.Count),
                    RetainedAttemptCount = attempts.Count,
                    TimingScope = monotonic ? "RetainedNativeMonotonicOnly" : "NonMonotonicUnavailable",
                    RetainedNativeSpanMicroseconds = span,
                    RetainedRecoveryDurationsMicroseconds = monotonic ? recoveries.ToArray() : [],
                    RecoveryIntervalDefinition = "FirstRetainedLossToNextRetainedResumeNotCompleteOutage",
                    RetainedChannelEventCounts = new
                    {
                        Video = snapshot.Events.Count(item => item.Subsystem == NativeLifecycleSubsystem.Video),
                        Audio = snapshot.Events.Count(item => item.Subsystem == NativeLifecycleSubsystem.Audio),
                        Control = snapshot.Events.Count(item => item.Subsystem == NativeLifecycleSubsystem.Control),
                    },
                    Completion = snapshot.CompletionState.ToString(),
                    TerminalReason = snapshot.TerminalResult?.Reason.ToString(),
                    Cleanup = new
                    {
                        Intent = snapshot.Cleanup.Intent.ToString(),
                        State = snapshot.Cleanup.State.ToString(),
                        StopCall = snapshot.Cleanup.StopCallState.ToString(),
                        StopFailure = snapshot.Cleanup.StopFailure?.ToString(),
                        FailureCategory = snapshot.Cleanup.FailureCategory.ToString(),
                        snapshot.Cleanup.ExactChildExitObserved,
                        snapshot.Cleanup.OwnershipReleased,
                    },
                    UnavailableMetrics = new
                    {
                        NativeGeneration = (int?)null,
                        AudioPacketsDecoded = (ulong?)null,
                        AudioSamplesSubmitted = (ulong?)null,
                        AudioUnderflows = (ulong?)null,
                        AudibleReadiness = (bool?)null,
                        VideoFramesPresented = (ulong?)null,
                        NativeQueueDepth = (ulong?)null,
                        NativeRetainedBytes = (ulong?)null,
                        ProcessMemoryBytes = (ulong?)null,
                        ProcessExitCode = (int?)null,
                        WireStopAcknowledged = (bool?)null,
                        ForegroundPermissionReturn = (bool?)null,
                        Availability = "NotRecorded",
                    },
                });
                ulong? first = snapshot.Events.FirstOrDefault()?.MonotonicMicroseconds;
                previous = null;

                foreach (NativeLifecycleObservation observation in snapshot.Events)
                {
                    WriteLine(output, new
                    {
                        Record = "Lifecycle",
                        Session = index + 1,
                        Attempt = observation.ConnectionAttemptId is { } attempt ? (int?)attempts[attempt.Value] : null,
                        Sequence = Number(observation.Sequence),
                        NativeUtc = observation.Utc.ToUniversalTime(),
                        NativeMonotonicMicroseconds = Number(observation.MonotonicMicroseconds),
                        SincePreviousMicroseconds = monotonic && previous is { } prior
                            ? Number(observation.MonotonicMicroseconds - prior) : null,
                        SinceFirstRetainedMicroseconds = monotonic && first is { } origin
                            ? Number(observation.MonotonicMicroseconds - origin) : null,
                        Subsystem = observation.Subsystem.ToString(),
                        Event = observation.EventType.ToString(),
                        Reason = observation.Reason.ToString(),
                    });
                    previous = observation.MonotonicMicroseconds;
                }
            }

            return new(DiagnosticFormatStatus.Ready, output.Written, snapshots.Count, eventCount);
        }
        catch (EvidenceFormatException)
        {
            return new(DiagnosticFormatStatus.InvalidEvidence);
        }
        catch (DiagnosticBudgetException)
        {
            return new(DiagnosticFormatStatus.LimitExceeded);
        }
    }

    private static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static void WriteLine<T>(BoundedJsonlStream output, T value)
    {
        using (Utf8JsonWriter writer = new(output, new JsonWriterOptions { MaxDepth = MaximumDepth }))
        {
            JsonSerializer.Serialize(writer, value, JsonOptions);
        }
        output.WriteByte((byte)'\n');
    }

    private sealed class DiagnosticBudgetException : Exception { }

    private sealed class BoundedJsonlStream(int capacity) : Stream
    {
        private readonly byte[] buffer = new byte[capacity];
        private int count;
        public ReadOnlyMemory<byte> Written => buffer.AsMemory(0, count);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => count;
        public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] source, int offset, int length) => Write(source.AsSpan(offset, length));

        public override void Write(ReadOnlySpan<byte> source)
        {
            if (source.Length > buffer.Length - count)
            {
                throw new DiagnosticBudgetException();
            }
            source.CopyTo(buffer.AsSpan(count));
            count += source.Length;
        }

        public override int Read(byte[] source, int offset, int length) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
