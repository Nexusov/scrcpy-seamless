namespace ScrcpySeamless.Core.Application.NativeHost;

/// <summary>Observed retired-generation counters; absent measurements remain null, never inferred zero.</summary>
public sealed record NativeGenerationMetrics(
    ulong NativeGeneration,
    ulong RetirementMicroseconds,
    ulong? AudioPacketsRead = null,
    ulong? AudioFramesDecoded = null,
    ulong? AudioSamplesSubmitted = null,
    ulong? AudioUnderflowSamples = null,
    ulong? AudioOverflowSamplesDropped = null,
    ulong? AudioPcmPeakSamples = null,
    ulong? VideoFramesDecoded = null,
    ulong? ControlMessagesWritten = null,
    ulong? ProcessWorkingSetBytes = null,
    ulong? ProcessPrivateBytes = null);
