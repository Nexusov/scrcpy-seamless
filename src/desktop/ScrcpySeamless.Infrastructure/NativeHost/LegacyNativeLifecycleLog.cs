using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Flushes a bounded, session-scoped lifecycle trace without native log text or device secrets.</summary>
internal sealed class LegacyNativeLifecycleLog : IDisposable
{
    private const int MaximumBytes = 8_192;
    private readonly FileStream stream;
    private readonly object writeLock = new();
    private readonly SessionId sessionId;
    private int sequence;
    private bool disposed;

    internal string Path { get; }
    internal Exception? WriteError { get; private set; }

    /// <summary>Creates a unique JSONL file beneath the explicit DEV data root.</summary>
    internal LegacyNativeLifecycleLog(string directory, SessionId sessionId)
    {
        this.sessionId = sessionId;
        Path = System.IO.Path.Combine(directory, $"native-session-{sessionId}-{Guid.NewGuid():N}.jsonl");
        stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 4_096, FileOptions.WriteThrough);
    }

    /// <summary>Writes only owned-process lifecycle metadata with local receive order and immediate flush.</summary>
    internal void Write(string eventType, int processId, nint windowHandle = default, bool forced = false)
    {
        lock (writeLock)
        {
            if (disposed || WriteError is not null || stream.Length >= MaximumBytes)
            {
                return;
            }

            try
            {
                var entry = new
                {
                    sessionId = sessionId.ToString(),
                    sequence = ++sequence,
                    receivedAtUtc = DateTimeOffset.UtcNow,
                    eventType,
                    processId,
                    windowHandle = windowHandle == nint.Zero ? null : (long?)windowHandle,
                    forced,
                };
                byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");

                if (stream.Length + line.Length > MaximumBytes)
                {
                    return;
                }

                stream.Write(line);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Session ownership and Stop must survive a diagnostic-write failure.
                WriteError = error;
            }
        }
    }

    /// <summary>Closes the single session file after the child has exited.</summary>
    public void Dispose()
    {
        lock (writeLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stream.Dispose();
        }
    }
}
