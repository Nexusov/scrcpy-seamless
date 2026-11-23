using System.Text;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeProtocol;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeProtocol;

public sealed class GenerationMetricsCodecTests
{
    private const string MinimalMetrics = "{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\"}";
    private const string FullMetrics = "{\"nativeGeneration\":\"18446744073709551615\",\"retirementMicroseconds\":\"9007199254740993\","
        + "\"audioPacketsRead\":\"0\",\"audioFramesDecoded\":\"1\",\"audioSamplesSubmitted\":\"2\","
        + "\"audioUnderflowSamples\":\"3\",\"audioOverflowSamplesDropped\":\"4\",\"audioPcmPeakSamples\":\"5\","
        + "\"videoFramesDecoded\":\"6\",\"controlMessagesWritten\":\"7\",\"processWorkingSetBytes\":\"8\",\"processPrivateBytes\":\"18446744073709551615\"}";

    [Fact]
    public void AllTwelveExactFieldsRoundTripInAgreedCanonicalOrder()
    {
        byte[] source = Payload(FullMetrics);
        ProtocolMessage decoded = ProtocolJsonCodec.Decode(source);
        NativeGenerationMetrics expected = new(ulong.MaxValue, 9007199254740993, 0, 1, 2, 3, 4, 5, 6, 7, 8, ulong.MaxValue);
        Assert.Equal(expected, decoded.GenerationMetrics);
        Assert.Equal(source, ProtocolJsonCodec.Encode(decoded));
        Assert.Equal(expected, ProtocolJsonCodec.Decode(ProtocolJsonCodec.Encode(decoded)).GenerationMetrics);
    }

    [Fact]
    public void MissingOptionalMeasurementsStayUnknownAndZeroStaysMeasuredZero()
    {
        ProtocolMessage minimal = ProtocolJsonCodec.Decode(Payload(MinimalMetrics));
        Assert.Equal(new NativeGenerationMetrics(1, 0), minimal.GenerationMetrics);
        Assert.Null(minimal.GenerationMetrics!.AudioPacketsRead);
        Assert.Null(minimal.GenerationMetrics.ProcessPrivateBytes);
        ProtocolMessage zero = ProtocolJsonCodec.Decode(Payload(MinimalMetrics.Replace("}", ",\"audioPacketsRead\":\"0\",\"processPrivateBytes\":\"0\"}")));
        Assert.Equal(0UL, zero.GenerationMetrics!.AudioPacketsRead);
        Assert.Equal(0UL, zero.GenerationMetrics.ProcessPrivateBytes);
        Assert.Null(zero.GenerationMetrics.ProcessWorkingSetBytes);
        Assert.DoesNotContain("audioFramesDecoded", Encoding.UTF8.GetString(ProtocolJsonCodec.Encode(zero)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"nativeGeneration\":\"1\"}")]
    [InlineData("{\"retirementMicroseconds\":\"0\"}")]
    [InlineData("{\"nativeGeneration\":\"0\",\"retirementMicroseconds\":\"0\"}")]
    [InlineData("{\"nativeGeneration\":\"01\",\"retirementMicroseconds\":\"0\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"00\"}")]
    [InlineData("{\"nativeGeneration\":1,\"retirementMicroseconds\":\"0\"}")]
    [InlineData("{\"nativeGeneration\":\"18446744073709551616\",\"retirementMicroseconds\":\"0\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"audioPacketsRead\":null}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"audioPacketsRead\":-1}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"audioFramesDecoded\":\"-1\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"audioFramesDecoded\":\"1e2\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"future\":\"1\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"AudioPacketsRead\":\"1\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"nativeGeneration\":\"2\"}")]
    [InlineData("{\"nativeGeneration\":\"1\",\"retirementMicroseconds\":\"0\",\"\\u006eativeGeneration\":\"2\"}")]
    public void RejectsMalformedUnknownDuplicateOrInexactMetrics(string metrics)
    {
        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(Payload(metrics)));
    }

    [Theory]
    [InlineData("eventType", "GenerationRetired", "Connecting")]
    [InlineData("subsystem", "native", "audio")]
    [InlineData("reason", "none", "unknown")]
    [InlineData("error", "none", "unknown")]
    public void MetricScopeIsOneExplicitRetiredEvent(string field, string original, string invalid)
    {
        string json = Encoding.UTF8.GetString(Payload(MinimalMetrics));
        json = json.Replace($"\"{field}\":\"{original}\"", $"\"{field}\":\"{invalid}\"", StringComparison.Ordinal);
        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void RequiresAttemptAndMetricObjectForRetirement()
    {
        string source = Encoding.UTF8.GetString(Payload(MinimalMetrics));
        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(Encoding.UTF8.GetBytes(source.Replace(
            "\"connectionAttemptId\":\"22222222-2222-4222-8222-222222222222\"", "\"connectionAttemptId\":null", StringComparison.Ordinal))));
        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(Encoding.UTF8.GetBytes(source.Replace(
            ",\"generationMetrics\":" + MinimalMetrics, string.Empty, StringComparison.Ordinal))));
    }

    [Fact]
    public void LegacyLifecycleBytesAndRequiredCapabilitiesAreUnchanged()
    {
        string source = Encoding.UTF8.GetString(Payload(MinimalMetrics))
            .Replace("GenerationRetired", "Connecting", StringComparison.Ordinal)
            .Replace(",\"generationMetrics\":" + MinimalMetrics, string.Empty, StringComparison.Ordinal);
        ProtocolMessage legacy = ProtocolJsonCodec.Decode(Encoding.UTF8.GetBytes(source));
        Assert.Null(legacy.GenerationMetrics);
        Assert.Equal(source, Encoding.UTF8.GetString(ProtocolJsonCodec.Encode(legacy)));
        Assert.Equal(new[] { "focus-window", "lifecycle-v1", "stop" }, ProtocolCompatibility.RequiredCapabilities);
        ProtocolMessage hello = new()
        {
            MessageType = "hello", Product = ProtocolCompatibility.Product,
            ProtocolMajor = 1, ProtocolMinor = 0,
            RequiredCapabilities = ProtocolCompatibility.RequiredCapabilities,
            SupportedCapabilities = ProtocolCompatibility.RequiredCapabilities,
        };
        Assert.DoesNotContain(ProtocolCompatibility.GenerationMetricsCapability, ProtocolCompatibility.Evaluate(hello).Capabilities!);
        Assert.Contains(ProtocolCompatibility.GenerationMetricsCapability,
            ProtocolCompatibility.Evaluate(hello with { SupportedCapabilities = ProtocolCompatibility.SupportedCapabilities }).Capabilities!);
    }

    [Fact]
    public void EncodeCannotSilentlyDropMetricsOnAnUnrelatedMessage()
    {
        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Encode(new ProtocolMessage
        {
            MessageType = "command", RequestId = 1, SessionId = Guid.NewGuid(), Command = "Stop",
            GenerationMetrics = new(1, 0),
        }));
    }

    private static byte[] Payload(string metrics) => Encoding.UTF8.GetBytes(
        "{\"messageType\":\"lifecycle\",\"sequence\":\"1\",\"utc\":\"2026-11-23T00:00:00.000Z\",\"monotonicMicroseconds\":\"0\","
        + "\"sessionId\":\"11111111-1111-4111-8111-111111111111\",\"connectionAttemptId\":\"22222222-2222-4222-8222-222222222222\","
        + "\"subsystem\":\"native\",\"eventType\":\"GenerationRetired\",\"reason\":\"none\",\"error\":\"none\",\"generationMetrics\":" + metrics + "}");
}
