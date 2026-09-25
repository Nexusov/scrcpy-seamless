using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Owns exactly one legacy native child, stop event and concurrently drained pipes.</summary>
public sealed class LegacyNativeSession : INativeSession
{
    private const int MaximumRetainedCharactersPerStream = 32_768;
    private const int StreamBufferCharacters = 4_096;
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ForcedStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReaderDrainTimeout = TimeSpan.FromSeconds(5);
    private readonly Process process;
    private readonly EventWaitHandle stopEvent;
    private readonly LegacyNativeLifecycleLog? lifecycleLog;
    private readonly CancellationTokenSource readerCancellation = new();
    private readonly Task<(string Text, bool Truncated)> outputTask;
    private readonly Task<(string Text, bool Truncated)> errorTask;
    private readonly object stopLock = new();
    private Task? stopTask;
    private NativeTerminationReason? requestedReason;
    private bool forced;
    private bool disposed;

    public SessionId SessionId { get; }
    public Task<NativeExit> Completion { get; }
    public int ProcessId { get; }
    public string StandardOutput { get; private set; } = string.Empty;
    public string StandardError { get; private set; } = string.Empty;
    public bool OutputTruncated { get; private set; }
    public bool WasForced => forced;
    public string? LifecycleLogPath => lifecycleLog?.Path;
    public Exception? LifecycleLogError => lifecycleLog?.WriteError;
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>Reports only a window actually owned by the child, or zero when none is observed.</summary>
    public nint MainWindowHandle
    {
        get
        {
            process.Refresh();
            return process.HasExited ? nint.Zero : process.MainWindowHandle;
        }
    }

    private LegacyNativeSession(
        SessionId sessionId,
        Process process,
        EventWaitHandle stopEvent,
        LegacyNativeLifecycleLog? lifecycleLog)
    {
        SessionId = sessionId;
        this.process = process;
        this.stopEvent = stopEvent;
        this.lifecycleLog = lifecycleLog;
        ProcessId = process.Id;
        lifecycleLog?.Write("started", ProcessId);
        outputTask = ReadBoundedAsync(process.StandardOutput, readerCancellation.Token);
        errorTask = ReadBoundedAsync(process.StandardError, readerCancellation.Token);
        Completion = CompleteAsync();
    }

    /// <summary>Starts a child from an already prepared invocation and transfers event ownership.</summary>
    internal static LegacyNativeSession Start(
        SessionId sessionId,
        ProcessStartInfo startInfo,
        EventWaitHandle stopEvent,
        LegacyNativeLifecycleLog? lifecycleLog = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(stopEvent);
        Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The native child did not start.");
            }

            return new LegacyNativeSession(sessionId, process, stopEvent, lifecycleLog);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>Requests graceful shutdown, then escalates only against the owned child.</summary>
    public async Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (stopLock)
        {
            stopTask ??= StopCoreAsync(reason);
        }

        await stopTask.WaitAsync(cancellationToken);
    }

    /// <summary>Stops and releases owned handles after process and stream completion.</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopAsync(NativeTerminationReason.ApplicationShutdown, CancellationToken.None);
        disposed = true;
        await readerCancellation.CancelAsync();
        readerCancellation.Dispose();
        stopEvent.Dispose();
        lifecycleLog?.Dispose();
        process.Dispose();
    }

    /// <summary>Signals native SDL quit before any forceful child-only termination.</summary>
    private async Task StopCoreAsync(NativeTerminationReason reason)
    {
        if (process.HasExited)
        {
            await Completion;
            return;
        }

        lock (stopLock)
        {
            requestedReason = reason;
        }

        lifecycleLog?.Write("stop_requested", ProcessId, MainWindowHandle);
        stopEvent.Set();

        try
        {
            await process.WaitForExitAsync().WaitAsync(GracefulStopTimeout);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                try
                {
                    // The native child may have launched the shared ADB daemon; never kill a process tree.
                    forced = true;
                    process.Kill();
                    lifecycleLog?.Write("forced_stop", ProcessId, forced: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The child exited between the timeout and escalation.
                    forced = false;
                }
                catch (Win32Exception) when (process.HasExited)
                {
                    // The child exited between the timeout and escalation.
                    forced = false;
                }
            }

            try
            {
                await process.WaitForExitAsync().WaitAsync(ForcedStopTimeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("The owned native child did not exit after forced termination.");
            }
        }

        await Completion;
    }

    /// <summary>Settles one terminal result without inferring stream readiness from process lifetime.</summary>
    private async Task<NativeExit> CompleteAsync()
    {
        await process.WaitForExitAsync();

        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(ReaderDrainTimeout);
        }
        catch (TimeoutException)
        {
            await readerCancellation.CancelAsync();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();

            try
            {
                await Task.WhenAll(outputTask, errorTask).WaitAsync(ForcedStopTimeout);
            }
            catch (TimeoutException)
            {
                OutputTruncated = true;
            }
        }

        if (outputTask.IsCompletedSuccessfully && errorTask.IsCompletedSuccessfully)
        {
            (string output, bool outputTruncated) = await outputTask;
            (string error, bool errorTruncated) = await errorTask;
            StandardOutput = output;
            StandardError = error;
            OutputTruncated |= outputTruncated || errorTruncated;
        }

        EndedAtUtc = DateTimeOffset.UtcNow;
        NativeTerminationReason? stopReason;

        lock (stopLock)
        {
            stopReason = requestedReason;
        }

        NativeTerminationReason reason = forced || (process.ExitCode != 0 && stopReason is null)
            ? NativeTerminationReason.NativeFailure
            : stopReason ?? NativeTerminationReason.WindowClosed;
        lifecycleLog?.Write("exited", ProcessId, forced: forced);
        return new NativeExit(SessionId, reason);
    }

    /// <summary>Retains a bounded prefix while continuing to drain an arbitrarily verbose child.</summary>
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[StreamBufferCharacters];
        StringBuilder output = new();
        bool truncated = false;

        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);

                if (read == 0)
                {
                    break;
                }

                int retained = Math.Min(read, MaximumRetainedCharactersPerStream - output.Length);

                if (retained > 0)
                {
                    output.Append(buffer, 0, retained);
                }

                truncated |= retained != read;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            truncated = true;
        }
        catch (ObjectDisposedException)
        {
            truncated = true;
        }
        catch (IOException)
        {
            truncated = true;
        }

        return (output.ToString(), truncated);
    }
}
