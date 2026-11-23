namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>Bounds retained bytes for both current and imported configuration documents.</summary>
internal static class ConfigurationDocument
{
    internal const int MaximumBytes = 4 * 1024 * 1024;

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] bytes = Allocate(stream.Length);
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        byte[] extra = new byte[1];
        RejectGrowth(await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0);
        return bytes;
    }

    internal static byte[] Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] bytes = Allocate(stream.Length);
        stream.ReadExactly(bytes);
        RejectGrowth(stream.ReadByte() != -1);
        return bytes;
    }

    private static byte[] Allocate(long length)
    {
        if (length > MaximumBytes)
        {
            throw new InvalidDataException("Configuration document exceeds its size limit.");
        }

        return new byte[(int)length];
    }

    private static void RejectGrowth(bool grew)
    {
        if (grew)
        {
            throw new InvalidDataException("Configuration document grew while being read.");
        }
    }
}
