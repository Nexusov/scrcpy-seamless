using System.Buffers.Binary;
using System.Text;
using ScrcpySeamless.Infrastructure.NativeProtocol;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeProtocol;

/// <summary>Protects the independent C# framing and JSON contract without a native process.</summary>
public sealed class ProtocolContractTests
{
    private static readonly (string Name, int Length, string Prefix)[] Golden =
    [
        ("hello", 182, "b6000000"),
        ("hello-result", 167, "a7000000"),
        ("stop", 128, "80000000"),
        ("stop-result", 154, "9a000000"),
        ("focus", 116, "74000000"),
        ("focus-result", 142, "8e000000"),
        ("lifecycle", 294, "26010000"),
        ("fatal", 308, "34010000"),
        ("unicode-unknown-command", 115, "73000000"),
    ];

    /// <summary>Checks independently authored payload bytes and optional C-produced frames.</summary>
    [Fact]
    public async Task GoldenVectorsDecodeAndEncodeExactBytesAcrossLanguages()
    {
        string? cFrames = Environment.GetEnvironmentVariable("SC_IPC_C_FRAMES");
        string? managedFrames = Environment.GetEnvironmentVariable("SC_IPC_MANAGED_FRAMES");

        if (managedFrames is not null)
        {
            Directory.CreateDirectory(managedFrames);
        }

        foreach ((string name, int length, string prefix) in Golden)
        {
            byte[] payload = await File.ReadAllBytesAsync(GoldenPath(name),
                TestContext.Current.CancellationToken);
            Assert.Equal(length, payload.Length);
            ProtocolMessage message = ProtocolJsonCodec.Decode(payload);
            Assert.Equal(payload, ProtocolJsonCodec.Encode(message));

            using MemoryStream frame = new();
            await ProtocolFrameCodec.WriteAsync(frame, payload, TestContext.Current.CancellationToken);
            byte[] bytes = frame.ToArray();
            Assert.Equal(Convert.FromHexString(prefix), bytes[..4]);
            Assert.Equal((uint)length, BinaryPrimitives.ReadUInt32LittleEndian(bytes));

            if (cFrames is not null)
            {
                byte[] cBytes = await File.ReadAllBytesAsync(
                    Path.Combine(cFrames, name + ".frame"), TestContext.Current.CancellationToken);
                Assert.Equal(bytes, cBytes);
                using MemoryStream cStream = new(cBytes);
                byte[]? cPayload = await ProtocolFrameCodec.ReadAsync(
                    cStream, TestContext.Current.CancellationToken);
                Assert.Equal(payload, ProtocolJsonCodec.Encode(
                    ProtocolJsonCodec.Decode(cPayload!)));
            }

            if (managedFrames is not null)
            {
                await File.WriteAllBytesAsync(Path.Combine(managedFrames, name + ".frame"),
                    bytes, TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>Ensures split headers, split UTF-8 and coalesced frames are independent of reads.</summary>
    [Fact]
    public async Task FramingHandlesEverySplitAndSeveralFrames()
    {
        byte[] payload = await File.ReadAllBytesAsync(GoldenPath("unicode-unknown-command"),
            TestContext.Current.CancellationToken);
        using MemoryStream frame = new();
        await ProtocolFrameCodec.WriteAsync(frame, payload, TestContext.Current.CancellationToken);
        byte[] bytes = frame.ToArray();

        for (int split = 1; split < bytes.Length; ++split)
        {
            using SegmentedStream segmented = new(bytes, split);
            Assert.Equal(payload, await ProtocolFrameCodec.ReadAsync(
                segmented, TestContext.Current.CancellationToken));
            Assert.Null(await ProtocolFrameCodec.ReadAsync(
                segmented, TestContext.Current.CancellationToken));
        }

        using MemoryStream coalesced = new();
        await ProtocolFrameCodec.WriteAsync(coalesced, payload, TestContext.Current.CancellationToken);
        await ProtocolFrameCodec.WriteAsync(coalesced, payload, TestContext.Current.CancellationToken);
        coalesced.Position = 0;
        Assert.Equal(payload, await ProtocolFrameCodec.ReadAsync(
            coalesced, TestContext.Current.CancellationToken));
        Assert.Equal(payload, await ProtocolFrameCodec.ReadAsync(
            coalesced, TestContext.Current.CancellationToken));
        Assert.Null(await ProtocolFrameCodec.ReadAsync(
            coalesced, TestContext.Current.CancellationToken));
    }

    /// <summary>Distinguishes clean EOF, partial header, partial body and invalid lengths.</summary>
    [Fact]
    public async Task FramingRejectsMalformedBoundariesBeforeBodyAllocation()
    {
        Assert.Null(await ProtocolFrameCodec.ReadAsync(new MemoryStream(),
            TestContext.Current.CancellationToken));
        await AssertFailureAsync([1, 0], ProtocolFailure.TruncatedHeader);
        await AssertFailureAsync([2, 0, 0, 0, (byte)'x'],
            ProtocolFailure.TruncatedPayload);
        await AssertFailureAsync([0, 0, 0, 0], ProtocolFailure.InvalidLength);
        await AssertFailureAsync([1, 0, 16, 0], ProtocolFailure.InvalidLength);
        await AssertFailureAsync([255, 255, 255, 255], ProtocolFailure.InvalidLength);

        byte[] maximum = new byte[ProtocolFrameCodec.MaximumPayloadBytes];
        using MemoryStream stream = new();
        await ProtocolFrameCodec.WriteAsync(stream, maximum,
            TestContext.Current.CancellationToken);
        Assert.Equal((uint)ProtocolFrameCodec.MaximumPayloadBytes,
            BinaryPrimitives.ReadUInt32LittleEndian(stream.ToArray()));
        await Assert.ThrowsAsync<ProtocolException>(() => ProtocolFrameCodec.WriteAsync(
            new MemoryStream(), new byte[ProtocolFrameCodec.MaximumPayloadBytes + 1],
            TestContext.Current.CancellationToken));
    }

    /// <summary>Cancels a blocked stream read without inventing EOF or retaining a buffer.</summary>
    [Fact]
    public async Task StreamCancellationRemainsDistinctFromEof()
    {
        using CancellationTokenSource cancellation = new();
        using BlockingStream stream = new();
        Task<byte[]?> read = ProtocolFrameCodec.ReadAsync(stream, cancellation.Token);
        Assert.False(read.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    /// <summary>Rejects malformed structure, types, precision loss and ambiguous keys.</summary>
    [Fact]
    public void JsonBoundaryRejectsMalformedInputs()
    {
        string[] invalid =
        [
            "{}",
            "[]",
            "{\"messageType\":\"unknown\"}",
            "{\"messageType\":\"command\",\"requestId\":null}",
            "{\"messageType\":\"command\",\"requestId\":\"01\"}",
            "{\"messageType\":\"command\",\"requestId\":\"18446744073709551616\"}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":null}",
            "{\"messageType\":\"command\",\"messageType\":\"command\"}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"Stop\",\"nested\":{}}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"Stop\"} trailing",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"Stop\\u0000extra\"}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"\\uD800\"}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"Stop\","
                + "\"future\":\"a\",\"future\":\"b\"}",
            "{\"messageType\":\"command\",\"requestId\":\"1\",\"sessionId\":"
                + "\"11111111-2222-3333-4444-555555555555\",\"command\":\"Stop\","
                + "\"future\":[]}",
            "{\"messageType\":\"hello\",\"product\":\"scrcpy-seamless\","
                + "\"protocolMajor\":1,\"protocolMinor\":0,"
                + "\"requiredCapabilities\":[\"stop\"],\"supportedCapabilities\":[]}",
            "{\"messageType\":\"lifecycle\",\"sequence\":\"1\",\"utc\":"
                + "\"2026-09-29T00:00:00.000Z\",\"monotonicMicroseconds\":\"0\","
                + "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
                + "\"connectionAttemptId\":null,\"subsystem\":\"connection\","
                + "\"eventType\":\"Connecting\",\"reason\":\"none\",\"error\":\"none\"}",
            "{\"messageType\":\"lifecycle\",\"sequence\":\"1\","
                + "\"utc\":\"2026-02-30T00:00:00.000Z\"}",
        ];

        foreach (string input in invalid)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(
                Encoding.UTF8.GetBytes(input)));
        }

        Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Decode(
            [(byte)'{', (byte)'\"', (byte)'x', (byte)'\"', (byte)':',
                (byte)'\"', 0xc3, 0x28, (byte)'\"', (byte)'}']));
    }

    /// <summary>Preserves exact uint64 values and allows only bounded optional scalars.</summary>
    [Fact]
    public async Task SemanticCompatibilityDoesNotExecuteUnknownCommands()
    {
        ProtocolMessage maximum = ProtocolJsonCodec.Decode(
            await File.ReadAllBytesAsync(GoldenPath("stop"),
                TestContext.Current.CancellationToken));
        Assert.Equal(ulong.MaxValue, maximum.RequestId);
        Assert.Equal("Stop", maximum.Command);
        byte[] optional = Encoding.UTF8.GetBytes(
            "{\"messageType\":\"command\",\"requestId\":\"3\","
            + "\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            + "\"command\":\"Фокус\",\"futureNote\":\"supported later\"}");
        ProtocolMessage unknown = ProtocolJsonCodec.Decode(optional);
        Assert.Equal("Фокус", unknown.Command);

        byte[] reordered = Encoding.UTF8.GetBytes(
            "{\"command\":\"Stop\",\"sessionId\":\"11111111-2222-3333-4444-555555555555\","
            + "\"requestId\":\"18446744073709551615\",\"messageType\":\"command\"}");
        Assert.Equal(maximum.RequestId, ProtocolJsonCodec.Decode(reordered).RequestId);

        ProtocolMessage hello = ProtocolJsonCodec.Decode(
            await File.ReadAllBytesAsync(GoldenPath("hello"),
                TestContext.Current.CancellationToken));
        Assert.Equal("accepted", ProtocolCompatibility.Evaluate(hello).Status);
        Assert.Equal("majorMismatch", ProtocolCompatibility.Evaluate(hello with
        {
            ProtocolMajor = 2,
        }).Status);
        Assert.Equal("requiredCapabilityMissing", ProtocolCompatibility.Evaluate(hello with
        {
            RequiredCapabilities = ["future-required"],
            SupportedCapabilities = ["future-required"],
        }).Status);
        Assert.Throws<ProtocolException>(() => ProtocolCompatibility.Evaluate(hello with
        {
            SupportedCapabilities = [],
        }));
    }

    /// <summary>Rejects oversized in-memory fields before allocating serialized JSON.</summary>
    [Fact]
    public void EncoderRejectsUnboundedPublicFields()
    {
        ProtocolMessage command = new()
        {
            MessageType = "command",
            RequestId = 1,
            SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Command = new string('x', ProtocolFrameCodec.MaximumPayloadBytes + 1),
        };
        Assert.Equal(ProtocolFailure.InvalidMessage,
            Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Encode(command)).Failure);

        ProtocolMessage hello = new()
        {
            MessageType = "hello",
            Product = ProtocolCompatibility.Product,
            ProtocolMajor = ProtocolCompatibility.Major,
            ProtocolMinor = ProtocolCompatibility.Minor,
            RequiredCapabilities = [],
            SupportedCapabilities = Enumerable.Repeat("stop", 17).ToArray(),
        };
        Assert.Equal(ProtocolFailure.InvalidMessage,
            Assert.Throws<ProtocolException>(() => ProtocolJsonCodec.Encode(hello)).Failure);
    }

    /// <summary>Locates one human-authored golden payload copied into the test output.</summary>
    private static string GoldenPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "NativeProtocolGolden", name + ".json");

    /// <summary>Asserts a precise framing failure class.</summary>
    private static async Task AssertFailureAsync(byte[] frame, ProtocolFailure expected)
    {
        using MemoryStream stream = new(frame);
        ProtocolException exception = await Assert.ThrowsAsync<ProtocolException>(() =>
            ProtocolFrameCodec.ReadAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal(expected, exception.Failure);
    }

    /// <summary>Returns a controlled first segment, then one-byte reads.</summary>
    private sealed class SegmentedStream(byte[] bytes, int firstSegment) : MemoryStream(bytes)
    {
        private bool first = true;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int maximum = first ? firstSegment : 1;
            first = false;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, maximum)],
                cancellationToken);
        }
    }

    /// <summary>Waits for cancellation without returning a false EOF.</summary>
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
