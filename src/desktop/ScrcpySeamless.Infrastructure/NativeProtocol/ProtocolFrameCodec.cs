using System.Buffers.Binary;

namespace ScrcpySeamless.Infrastructure.NativeProtocol;

/// <summary>Length-prefixed binary framing independent of JSON semantics.</summary>
public static class ProtocolFrameCodec
{
    public const int MaximumPayloadBytes = 1_048_576;
    private const int HeaderBytes = sizeof(uint);

    /// <summary>Reads one frame, distinguishing clean EOF from a truncated frame.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] header = new byte[HeaderBytes];
        int headerRead = await ReadPartAsync(stream, header, cancellationToken);

        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead != HeaderBytes)
        {
            throw new ProtocolException(ProtocolFailure.TruncatedHeader, "Truncated frame header.");
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);

        if (length is 0 or > MaximumPayloadBytes)
        {
            throw new ProtocolException(ProtocolFailure.InvalidLength, "Invalid frame length.");
        }

        byte[] payload = new byte[(int)length];

        if (await ReadPartAsync(stream, payload, cancellationToken) != payload.Length)
        {
            throw new ProtocolException(ProtocolFailure.TruncatedPayload, "Truncated frame payload.");
        }

        return payload;
    }

    /// <summary>Writes one validated frame to the supplied stream.</summary>
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (payload.Length is 0 or > MaximumPayloadBytes)
        {
            throw new ProtocolException(ProtocolFailure.InvalidLength, "Invalid frame length.");
        }

        byte[] header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }

    /// <summary>Accumulates a fixed segment without assuming stream read boundaries.</summary>
    private static async Task<int> ReadPartAsync(Stream stream, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
