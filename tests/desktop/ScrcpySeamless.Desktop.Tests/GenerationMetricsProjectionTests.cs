using System.Globalization;
using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class GenerationMetricsProjectionTests
{
    private const ulong PrivateGeneration = 8888888888888888888;

    [Fact]
    public void OptionalMetricsUseLocalGenerationOrdinalAndExactMeasuredValuesInBothFormats()
    {
        NativeGenerationMetrics metrics = new(PrivateGeneration, 9007199254740993,
            AudioSamplesSubmitted: 0, AudioUnderflowSamples: 5, ProcessWorkingSetBytes: 120, ProcessPrivateBytes: 90);
        SessionEvidenceSnapshot snapshot = Snapshot(metrics);
        string copied = SessionEvidenceFormatter.Format(snapshot);
        Assert.DoesNotContain(PrivateGeneration.ToString(CultureInfo.InvariantCulture), copied);
        using JsonDocument copy = JsonDocument.Parse(copied);
        JsonElement copiedMetrics = Assert.Single(copy.RootElement.GetProperty("Lifecycle").GetProperty("Events").EnumerateArray()).GetProperty("GenerationMetrics");
        Assert.Equal(1, copiedMetrics.GetProperty("Generation").GetInt32());
        Assert.Equal("9007199254740993", copiedMetrics.GetProperty("RetirementMicroseconds").GetString());
        Assert.Equal("0", copiedMetrics.GetProperty("AudioSamplesSubmitted").GetString());
        Assert.Equal(JsonValueKind.Null, copiedMetrics.GetProperty("AudioFramesDecoded").ValueKind);
        DiagnosticFormatResult formatted = DiagnosticExportFormatter.Format([snapshot]);
        Assert.Equal(DiagnosticFormatStatus.Ready, formatted.Status);
        string jsonl = Encoding.UTF8.GetString(formatted.Jsonl.Span);
        Assert.DoesNotContain(PrivateGeneration.ToString(CultureInfo.InvariantCulture), jsonl);
        JsonElement[] rows = Rows(jsonl);
        JsonElement exported = rows[2].GetProperty("GenerationMetrics");
        Assert.Equal("ExportLocalGenerationOrdinal", exported.GetProperty("IdentityScope").GetString());
        Assert.Equal("PostRetirementSampleNotPeak", exported.GetProperty("ProcessMemoryScope").GetString());
        Assert.Equal("120", exported.GetProperty("ProcessWorkingSetBytes").GetString());
        JsonElement summary = rows[1].GetProperty("UnavailableMetrics");
        Assert.Equal(0UL, summary.GetProperty("AudioSamplesSubmitted").GetUInt64());
        Assert.Equal("Recorded", summary.GetProperty("FieldAvailability").GetProperty("AudioSamplesSubmitted").GetString());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("AudioUnderflows").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("ProcessMemoryBytes").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("AudibleReadiness").ValueKind);
    }

    [Fact]
    public void GenerationCorrelationIsDistinctFromAttemptAndResetsForAnotherExport()
    {
        SessionEvidenceSnapshot snapshot = Snapshot(new(PrivateGeneration, 1));
        NativeLifecycleObservation first = snapshot.Events[0];
        snapshot = snapshot with { Events = [first, first with { Sequence = 11, GenerationMetrics = new(PrivateGeneration - 1, 2) }] };
        DiagnosticFormatResult formatted = DiagnosticExportFormatter.Format([snapshot]);
        JsonElement[] rows = Rows(Encoding.UTF8.GetString(formatted.Jsonl.Span));
        Assert.Equal(1, rows[2].GetProperty("Attempt").GetInt32());
        Assert.Equal(1, rows[3].GetProperty("Attempt").GetInt32());
        Assert.Equal(1, rows[2].GetProperty("GenerationMetrics").GetProperty("Generation").GetInt32());
        Assert.Equal(2, rows[3].GetProperty("GenerationMetrics").GetProperty("Generation").GetInt32());
        DiagnosticFormatResult second = DiagnosticExportFormatter.Format([Snapshot(new(PrivateGeneration - 1, 2))]);
        Assert.Equal(1, Rows(Encoding.UTF8.GetString(second.Jsonl.Span))[2].GetProperty("GenerationMetrics").GetProperty("Generation").GetInt32());
    }

    [Fact]
    public void OlderSenderHasNoManufacturedCountersOrGeneration()
    {
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        JsonElement summary = Rows(Encoding.UTF8.GetString(DiagnosticExportFormatter.Format([snapshot]).Jsonl.Span))[1];
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("UnavailableMetrics").GetProperty("AudioSamplesSubmitted").ValueKind);
        Assert.Equal("NotRecorded", summary.GetProperty("UnavailableMetrics").GetProperty("FieldAvailability").GetProperty("AudioSamplesSubmitted").GetString());
        Assert.All(Rows(Encoding.UTF8.GetString(DiagnosticExportFormatter.Format([snapshot]).Jsonl.Span)).Skip(2),
            row => Assert.Equal(JsonValueKind.Null, row.GetProperty("GenerationMetrics").ValueKind));
    }

    [Theory]
    [InlineData("unnegotiated")]
    [InlineData("zero")]
    [InlineData("missing")]
    [InlineData("wrongEvent")]
    public void MalformedTypedSnapshotCannotBypassTheProductionMetricScope(string defect)
    {
        SessionEvidenceSnapshot snapshot = Snapshot(new(PrivateGeneration, 0));
        NativeLifecycleObservation observation = snapshot.Events[0];

        switch (defect)
        {
            case "unnegotiated": snapshot = snapshot with { AcceptedHandshake = new("scrcpy-seamless", 1, 0, ["stop"]) }; break;
            case "zero": observation = observation with { GenerationMetrics = new(0, 0) }; break;
            case "missing": observation = observation with { GenerationMetrics = null }; break;
            case "wrongEvent": observation = observation with { EventType = NativeLifecycleEventType.Connecting }; break;
        }
        snapshot = snapshot with { Events = [observation] };
        Assert.Throws<EvidenceFormatException>(() => SessionEvidenceFormatter.Format(snapshot));
        Assert.Equal(DiagnosticFormatStatus.InvalidEvidence, DiagnosticExportFormatter.Format([snapshot]).Status);
    }

    private static SessionEvidenceSnapshot Snapshot(NativeGenerationMetrics metrics)
    {
        SessionEvidenceSnapshot baseline = DiagnosticSyntheticEvidence.Create();
        NativeLifecycleObservation retired = baseline.Events[0] with
        {
            Subsystem = NativeLifecycleSubsystem.Native,
            EventType = NativeLifecycleEventType.GenerationRetired,
            Reason = NativeLifecycleReason.None,
            Error = NativeLifecycleError.None,
            GenerationMetrics = metrics,
        };
        return baseline with
        {
            Events = [retired],
            AcceptedHandshake = new("scrcpy-seamless", 1, 0, ["focus-window", "generation-metrics-v1", "lifecycle-v1", "stop"]),
        };
    }

    private static JsonElement[] Rows(string jsonl) => jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
    {
        using JsonDocument parsed = JsonDocument.Parse(line);
        return parsed.RootElement.Clone();
    }).ToArray();
}
