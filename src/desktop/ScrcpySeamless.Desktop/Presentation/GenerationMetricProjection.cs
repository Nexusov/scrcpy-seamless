using System.Globalization;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Only named measured values leave the typed owner; raw native generation becomes a local ordinal.</summary>
internal sealed record GenerationMetricProjection(
    int Generation,
    string RetirementMicroseconds,
    string? AudioPacketsRead,
    string? AudioFramesDecoded,
    string? AudioSamplesSubmitted,
    string? AudioUnderflowSamples,
    string? AudioOverflowSamplesDropped,
    string? AudioPcmPeakSamples,
    string? VideoFramesDecoded,
    string? ControlMessagesWritten,
    string? ProcessWorkingSetBytes,
    string? ProcessPrivateBytes)
{
    public string IdentityScope => "ExportLocalGenerationOrdinal";
    public string CounterScope => "ObservedRetiredGeneration";
    public string ProcessMemoryScope => "PostRetirementSampleNotPeak";

    internal static Dictionary<ulong, int> Ordinals(IReadOnlyList<NativeLifecycleObservation> observations)
    {
        Dictionary<ulong, int> result = [];

        foreach (NativeLifecycleObservation observation in observations)
        {
            if (observation.GenerationMetrics is { } metrics && !result.ContainsKey(metrics.NativeGeneration))
            {
                result.Add(metrics.NativeGeneration, result.Count + 1);
            }
        }

        return result;
    }

    internal static GenerationMetricProjection? Create(NativeGenerationMetrics? metrics,
        IReadOnlyDictionary<ulong, int> ordinals) => metrics is null ? null : new(
        ordinals[metrics.NativeGeneration], Number(metrics.RetirementMicroseconds)!,
        Number(metrics.AudioPacketsRead), Number(metrics.AudioFramesDecoded), Number(metrics.AudioSamplesSubmitted),
        Number(metrics.AudioUnderflowSamples), Number(metrics.AudioOverflowSamplesDropped), Number(metrics.AudioPcmPeakSamples),
        Number(metrics.VideoFramesDecoded), Number(metrics.ControlMessagesWritten),
        Number(metrics.ProcessWorkingSetBytes), Number(metrics.ProcessPrivateBytes));

    private static string? Number(ulong? value) => value?.ToString(CultureInfo.InvariantCulture);
}
