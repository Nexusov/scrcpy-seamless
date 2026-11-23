using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Diagnostics;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Diagnostics;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class DiagnosticExportFormatterTests
{
    [Fact]
    public void RedactsIdentitiesAndExportsOnlyOrdinalCorrelationsAndActualTiming()
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        DiagnosticFormatResult result = DiagnosticExportFormatter.Format([snapshot]);
        Assert.Equal(DiagnosticFormatStatus.Ready, result.Status);
        Assert.Equal(DiagnosticRedactionStatus.AllowlistV1Applied, result.Redaction);
        string jsonl = Encoding.UTF8.GetString(result.Jsonl.Span);
        Assert.DoesNotContain(snapshot.SessionId!.Value.ToString(), jsonl);
        Assert.DoesNotContain(DiagnosticSyntheticEvidence.Attempt.Value.ToString(), jsonl);
        Assert.DoesNotContain(snapshot.ProcessId!.Value.ToString(), jsonl);
        Assert.DoesNotContain(snapshot.BuildMetadataSourceSha!, jsonl);
        Assert.DoesNotContain("PRIVATE", jsonl);
        Assert.DoesNotContain("ErrorCode", jsonl);
        Assert.DoesNotContain("\"Error\"", jsonl);
        JsonElement[] lines = ParseLines(result.Jsonl);
        Assert.Equal(5, lines.Length);
        Assert.Equal("ExportLocalOrdinalsOnly", lines[0].GetProperty("IdentityScope").GetString());
        JsonElement session = lines[1];
        Assert.Equal(1, session.GetProperty("Session").GetInt32());
        Assert.Equal("10", session.GetProperty("Observed").GetString());
        Assert.Equal(3, session.GetProperty("Retained").GetInt32());
        Assert.Equal("7", session.GetProperty("Omitted").GetString());
        Assert.Equal("500", session.GetProperty("RetainedNativeSpanMicroseconds").GetString());
        Assert.Equal("500", Assert.Single(session.GetProperty("RetainedRecoveryDurationsMicroseconds").EnumerateArray()).GetString());
        Assert.Equal("NativeFailure", session.GetProperty("TerminalReason").GetString());
        Assert.Equal("Succeeded", session.GetProperty("Cleanup").GetProperty("State").GetString());
        Assert.Equal("AutomaticTerminal", session.GetProperty("Cleanup").GetProperty("Intent").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("UnavailableMetrics").GetProperty("AudibleReadiness").ValueKind);
        Assert.Equal(JsonValueKind.Null, session.GetProperty("UnavailableMetrics").GetProperty("NativeGeneration").ValueKind);
        Assert.Equal(1, lines[2].GetProperty("Attempt").GetInt32());
        Assert.Equal(1, lines[3].GetProperty("Attempt").GetInt32());
        Assert.Equal(2, lines[4].GetProperty("Attempt").GetInt32());
        Assert.Equal("250", lines[3].GetProperty("SincePreviousMicroseconds").GetString());
        Assert.Equal(snapshot.Events[1].Utc.ToUniversalTime(), lines[3].GetProperty("NativeUtc").GetDateTimeOffset());
        Assert.Equal(snapshot.Events[1].Sequence.ToString(), lines[3].GetProperty("Sequence").GetString());
    }

    [Fact]
    public void MaximumHistoryHasBoundedCountBytesAndDepth()
    {
        SessionEvidenceSnapshot[] snapshots = Enumerable.Range(0, DiagnosticExportFormatter.MaximumSessions)
            .Select(index =>
            {
                SessionId session = new(Guid.NewGuid());
                NativeLifecycleObservation[] events = Enumerable.Range(1, DiagnosticExportFormatter.MaximumEventsPerSession)
                    .Select(sequence => new NativeLifecycleObservation(session, (ulong)sequence,
                        DiagnosticSyntheticEvidence.Instant, ulong.MaxValue - 100 + (ulong)sequence,
                        DiagnosticSyntheticEvidence.Attempt, NativeLifecycleSubsystem.Audio,
                        NativeLifecycleEventType.CapabilityDegraded, NativeLifecycleReason.Unknown, NativeLifecycleError.Unknown)).ToArray();
                return DiagnosticSyntheticEvidence.Create() with { SessionId = session.Value, Events = events, TotalObserved = 1000, TerminalResult = null };
            }).ToArray();
        DiagnosticFormatResult result = DiagnosticExportFormatter.Format(snapshots);
        Assert.Equal(DiagnosticFormatStatus.Ready, result.Status);
        Assert.Equal(8, result.SessionCount);
        Assert.Equal(512, result.EventCount);
        Assert.InRange(result.Jsonl.Length, 1, DiagnosticExportLimits.MaximumBytes);
        Assert.Equal(DiagnosticExportFormatter.MaximumRecords, ParseLines(result.Jsonl).Length);
        Assert.All(ParseLines(result.Jsonl), line => Assert.True(Depth(line) <= DiagnosticExportFormatter.MaximumDepth));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("sequence")]
    [InlineData("classification")]
    [InlineData("handshake")]
    [InlineData("total")]
    [InlineData("duplicate")]
    public void InvalidEvidenceReturnsNoPartiallyRedactedOutput(string defect)
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        SessionEvidenceSnapshot[] snapshots = [snapshot];

        switch (defect)
        {
            case "session": snapshot = snapshot with { SessionId = Guid.NewGuid() }; break;
            case "sequence": snapshot = snapshot with { Events = [snapshot.Events[0], snapshot.Events[0]] }; break;
            case "classification": snapshot = snapshot with { ObservationStage = (SessionObservationStage) int.MaxValue }; break;
            case "handshake": snapshot = snapshot with { AcceptedHandshake = new("PRIVATE endpoint path", 1, 0, ["stop"]) }; break;
            case "total": snapshot = snapshot with { TotalObserved = 0 }; break;
            case "duplicate": snapshots = [snapshot, snapshot]; break;
        }

        if (defect != "duplicate")
        {
            snapshots = [snapshot];
        }
        DiagnosticFormatResult result = DiagnosticExportFormatter.Format(snapshots);
        Assert.Equal(DiagnosticFormatStatus.InvalidEvidence, result.Status);
        Assert.Equal(DiagnosticRedactionStatus.NotApplied, result.Redaction);
        Assert.True(result.Jsonl.IsEmpty);
    }

    [Fact]
    public void CountLimitsRejectWithoutTruncationAndFormattingDoesNotMutateSnapshots()
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        string original = SessionEvidenceFormatter.Format(snapshot);
        Assert.Equal(DiagnosticFormatStatus.LimitExceeded,
            DiagnosticExportFormatter.Format(Enumerable.Repeat(snapshot, 9).ToArray()).Status);
        Assert.Equal(DiagnosticFormatStatus.LimitExceeded, DiagnosticExportFormatter.Format([snapshot with
        {
            Events = Enumerable.Repeat(snapshot.Events[0], 65).ToArray(), TotalObserved = 65,
        }]).Status);
        DiagnosticExportFormatter.Format([snapshot]);
        Assert.Equal(original, SessionEvidenceFormatter.Format(snapshot));
        DiagnosticFormatResult overBudget = DiagnosticExportFormatter.FormatWithinBudget([snapshot], 256);
        Assert.Equal(DiagnosticFormatStatus.LimitExceeded, overBudget.Status);
        Assert.True(overBudget.Jsonl.IsEmpty);
    }

    [Fact]
    public void NonMonotonicEvidenceKeepsActualTimestampsButDoesNotManufactureDurations()
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        NativeLifecycleObservation[] events = snapshot.Events.ToArray();
        events[1] = events[1] with { MonotonicMicroseconds = 0 };
        DiagnosticFormatResult result = DiagnosticExportFormatter.Format([snapshot with { Events = events }]);
        JsonElement[] lines = ParseLines(result.Jsonl);
        Assert.Equal("NonMonotonicUnavailable", lines[1].GetProperty("TimingScope").GetString());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("RetainedNativeSpanMicroseconds").ValueKind);
        Assert.Empty(lines[1].GetProperty("RetainedRecoveryDurationsMicroseconds").EnumerateArray());
        Assert.Equal("0", lines[3].GetProperty("NativeMonotonicMicroseconds").GetString());
        Assert.Equal(JsonValueKind.Null, lines[3].GetProperty("SincePreviousMicroseconds").ValueKind);
    }

    [Fact]
    public void RepeatedLossBeforeAnyResumeKeepsTheFirstRetainedStart()
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        NativeLifecycleObservation repeatedLoss = snapshot.Events[2] with
        {
            EventType = NativeLifecycleEventType.TransportLost,
            Reason = NativeLifecycleReason.TransportLost,
            MonotonicMicroseconds = 1400,
        };
        NativeLifecycleObservation resumed = snapshot.Events[2] with { Sequence = 13 };
        DiagnosticFormatResult result = DiagnosticExportFormatter.Format([snapshot with
        {
            Events = [snapshot.Events[0], snapshot.Events[1], repeatedLoss, resumed],
        }]);
        JsonElement session = ParseLines(result.Jsonl)[1];
        string? duration = Assert.Single(session.GetProperty("RetainedRecoveryDurationsMicroseconds").EnumerateArray()).GetString();
        Console.WriteLine($"retained loss1000 -> loss1400 -> resume1500 interval={duration}");
        Assert.Equal("500", duration);
    }

    [Fact]
    public async Task ActualAllowlistFormatWritesThroughTheRealNewFileBoundary()
    {
        using WorkspaceTestDirectory directory = new();
        DiagnosticFormatResult document = DiagnosticExportFormatter.Format([DiagnosticSyntheticEvidence.Create()]);
        string path = Path.Combine(directory.Path, "owner selected new file.jsonl");
        DiagnosticWriteResult saved = await new LocalDiagnosticWriter().WriteNewAsync(path, document.Jsonl, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.Saved, saved.Status);
        Assert.Equal(document.Jsonl.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(5, ParseLines(document.Jsonl).Length);
    }

    private static JsonElement[] ParseLines(ReadOnlyMemory<byte> document) =>
        Encoding.UTF8.GetString(document.Span).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using JsonDocument parsed = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = DiagnosticExportFormatter.MaximumDepth });
                return parsed.RootElement.Clone();
            }).ToArray();

    private static int Depth(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => 1 + value.EnumerateObject().Select(property => Depth(property.Value)).DefaultIfEmpty(0).Max(),
        JsonValueKind.Array => 1 + value.EnumerateArray().Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };
}

internal static class DiagnosticSyntheticEvidence
{
    public static readonly DateTimeOffset Instant = DateTimeOffset.Parse("2026-11-23T03:00:00.1234567+00:00");
    public static readonly ConnectionAttemptId Attempt = new(Guid.Parse("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff"));

    public static SessionEvidenceSnapshot Create()
    {
        SessionId session = new(Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"));
        ConnectionAttemptId replacement = new(Guid.Parse("cccccccc-dddd-4eee-8fff-aaaaaaaaaaaa"));
        NativeLifecycleObservation[] observations =
        [
            new(session, 10, Instant, 1000, Attempt, NativeLifecycleSubsystem.Connection, NativeLifecycleEventType.TransportLost, NativeLifecycleReason.TransportLost, NativeLifecycleError.Unknown),
            new(session, 11, Instant.AddTicks(7), 1250, Attempt, NativeLifecycleSubsystem.Audio, NativeLifecycleEventType.Reconnecting, NativeLifecycleReason.None, NativeLifecycleError.None),
            new(session, 12, Instant.AddTicks(21), 1500, replacement, NativeLifecycleSubsystem.Video, NativeLifecycleEventType.StreamResumed, NativeLifecycleReason.None, NativeLifecycleError.None),
        ];
        return new(Instant, session.Value, 987654321, SessionObservationStage.Released,
            "abcdefabcdefabcdefabcdefabcdefabcdefabcd", new("scrcpy-seamless", 1, 0, ["stop", "PRIVATE_CAPABILITY_ENDPOINT"]), true,
            observations, 10, new NativeExit(session, NativeTerminationReason.NativeFailure, CoreErrorCode.InvalidConfiguration), SessionCompletionState.Completed,
            new(SessionCleanupIntent.AutomaticTerminal, SessionCleanupState.Succeeded, SessionStopCallState.Failed,
                MachineNativeSession.StopFailure.TerminalFailure, SessionCleanupFailureCategory.StopOperation, true, true));
    }
}
