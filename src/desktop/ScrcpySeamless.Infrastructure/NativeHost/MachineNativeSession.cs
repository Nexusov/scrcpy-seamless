using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeProtocol;

namespace ScrcpySeamless.Infrastructure.NativeHost;

/// <summary>Owns exactly one native process and its bounded binary channel.</summary>
public sealed class MachineNativeSession : INativeInteractiveSession
{
    private const int MaximumQueuedFrames = 32;
    private const int MaximumQueuedBytes = 2_097_152;
    private const int MaximumPendingCommands = 16;
    private const int MaximumRetainedStderrBytes = 65_536;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FrameWriteTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ForcedStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StreamSettleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminalEofExitTimeout = TimeSpan.FromSeconds(2);

    private readonly Process process;
    private readonly Stream standardInput;
    // Test-only pause used to verify that reserved IDs and frame publication are atomic.
    private readonly Action<ulong, string>? beforeRequestPublication;
    private readonly Channel<OutboundFrame> outgoing = Channel.CreateBounded<OutboundFrame>(
        new BoundedChannelOptions(MaximumQueuedFrames)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private readonly Channel<NativeLifecycleObservation> lifecycle = Channel.CreateBounded<NativeLifecycleObservation>(
        new BoundedChannelOptions(128)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly TaskCompletionSource<ProtocolMessage> hello = NewCompletion<ProtocolMessage>();
    private readonly TaskCompletionSource ready = NewCompletion();
    private readonly TaskCompletionSource failed = NewCompletion();
    private readonly Dictionary<ulong, PendingCommand> pending = [];
    private readonly HashSet<PendingCommand> activeDeadlines = [];
    private readonly object gate = new();
    private readonly Task readerTask;
    private readonly Task writerTask;
    private readonly Task stderrTask;
    private readonly Task<NativeExit> completion;
    private Task? stopTask;
    private Task? disposeTask;
    private Task? settleTask;
    private Exception? channelFailure;
    private NativeTerminationReason? observedReason;
    private ulong nextRequestId = 1;
    private ulong lastSequence;
    private int queuedFrames;
    private int queuedBytes;
    private bool handshakeAccepted;
    private bool nativeReady;
    private bool terminalObserved;
    private bool disposed;

    public SessionId SessionId { get; }
    public int ProcessId { get; }
    public Task<NativeExit> Completion => completion;
    internal (int Pending, int Deadlines) OwnedCommandCounts
    {
        get
        {
            lock (gate)
            {
                return (pending.Count, activeDeadlines.Count);
            }
        }
    }
    public IAsyncEnumerable<NativeLifecycleObservation> ObserveLifecycleAsync(CancellationToken cancellationToken) =>
        lifecycle.Reader.ReadAllAsync(cancellationToken);
    public string StandardError { get; private set; } = string.Empty;
    public bool StandardErrorTruncated { get; private set; }

    /// <summary>Classifies a Stop that did not end with an observed clean native lifecycle.</summary>
    public enum StopFailure { Escalated, TerminalFailure, SettlementFailed }

    /// <summary>Reports failed graceful Stop while retaining the exact native exit observation.</summary>
    public sealed class StopException(
        StopFailure failure, NativeExit? exit, Exception? cause = null)
        : Exception($"Native machine Stop failed: {failure}.", cause)
    {
        public StopFailure Failure { get; } = failure;
        public NativeExit? Exit { get; } = exit;
    }

    /// <summary>Identifies a command displaced by an already observed terminal lifecycle.</summary>
    private sealed class TerminalCommandException : IOException
    {
        public TerminalCommandException() : base("Native session stopped before the command returned a result.")
        {
        }
    }

    private MachineNativeSession(SessionId sessionId, Process process,
        Action<ulong, string>? beforeRequestPublication, Func<Stream, Stream>? wrapStandardInput)
    {
        SessionId = sessionId;
        this.process = process;
        standardInput = wrapStandardInput?.Invoke(process.StandardInput.BaseStream) ??
            process.StandardInput.BaseStream;
        this.beforeRequestPublication = beforeRequestPublication;
        ProcessId = process.Id;
        writerTask = WriteLoopAsync();
        readerTask = ReadLoopAsync();
        stderrTask = DrainStderrAsync();
        completion = CompleteAsync();
    }

    /// <summary>Starts a real redirected child and proves the negotiated dispatcher is ready.</summary>
    internal static async Task<MachineNativeSession> StartAsync(
        SessionId sessionId, ProcessStartInfo startInfo, CancellationToken cancellationToken,
        Action<ulong, string>? beforeRequestPublication = null,
        Func<Stream, Stream>? wrapStandardInput = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();

        if (!startInfo.RedirectStandardInput || !startInfo.RedirectStandardOutput ||
            !startInfo.RedirectStandardError || startInfo.UseShellExecute)
        {
            throw new ArgumentException("Machine mode requires three redirected pipes and direct launch.",
                nameof(startInfo));
        }

        Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The native child did not start.");
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }

        MachineNativeSession session;

        try
        {
            session = new(sessionId, process, beforeRequestPublication, wrapStandardInput);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                await process.WaitForExitAsync();
            }
            finally
            {
                process.Dispose();
            }

            throw;
        }

        try
        {
            await session.InitializeAsync(cancellationToken);
            return session;
        }
        catch (Exception primary)
        {
            try
            {
                await session.AbortAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Machine startup and exact-child cleanup both failed.",
                    primary, cleanup);
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    /// <summary>Negotiates all v1 capabilities before accepting native readiness.</summary>
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ProtocolMessage request = new()
        {
            MessageType = "hello",
            Product = ProtocolCompatibility.Product,
            ProtocolMajor = ProtocolCompatibility.Major,
            ProtocolMinor = ProtocolCompatibility.Minor,
            RequiredCapabilities = ["focus-window", "lifecycle-v1", "stop"],
            SupportedCapabilities = ["focus-window", "lifecycle-v1", "stop"],
        };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(HandshakeTimeout);
        await SendAsync(request, deadline.Token).WaitAsync(deadline.Token);
        ProtocolMessage response = await hello.Task.WaitAsync(deadline.Token);

        if (response.Status != "accepted" || response.Product != ProtocolCompatibility.Product ||
            response.ProtocolMajor != ProtocolCompatibility.Major ||
            response.ProtocolMinor != ProtocolCompatibility.Minor ||
            response.Capabilities is null ||
            !request.RequiredCapabilities!.All(response.Capabilities.Contains))
        {
            throw new ProtocolException(ProtocolFailure.InvalidMessage,
                "Native machine capabilities were not accepted.");
        }

        await ready.Task.WaitAsync(deadline.Token);
    }

    /// <summary>Queues FocusWindow and reports native main-thread dispatch truthfully.</summary>
    public async Task<NativeFocusOutcome> FocusWindowAsync(CancellationToken cancellationToken)
    {
        ProtocolMessage result = await RequestAsync("FocusWindow", cancellationToken);
        NativeFocusOutcome? outcome = result.Status switch
        {
            "applied" => NativeFocusOutcome.Applied,
            "noWindow" => NativeFocusOutcome.NoWindow,
            "invalidState" => NativeFocusOutcome.InvalidState,
            "failed" => NativeFocusOutcome.Failed,
            _ => null,
        };

        if (outcome is null)
        {
            ProtocolException failure = new(ProtocolFailure.InvalidMessage,
                "Native returned an invalid FocusWindow result.");
            FailChannel(failure);
            throw failure;
        }

        return outcome.Value;
    }

    /// <summary>Requests idempotent graceful stop, then escalates only the owned child.</summary>
    public Task StopAsync(NativeTerminationReason reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task task;

        lock (gate)
        {
            stopTask ??= StopCoreAsync(reason);
            task = stopTask;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>Completes Stop only after the exact process terminates.</summary>
    private async Task StopCoreAsync(NativeTerminationReason reason)
    {
        Exception? gracefulFailure = null;

        bool alreadyTerminal;

        lock (gate)
        {
            alreadyTerminal = terminalObserved;
        }

        if (!process.HasExited && !alreadyTerminal)
        {
            try
            {
                await StopGracefullyAsync().WaitAsync(GracefulStopTimeout);
            }
            catch (TerminalCommandException)
            {
                // Native window closure won the Stop race; its terminal event owns the result.
            }
            catch (Exception exception)
            {
                gracefulFailure = exception;
                FailChannel(exception);
                KillOwnedProcess();
            }
        }

        NativeExit exit;

        try
        {
            exit = await completion.WaitAsync(ForcedStopTimeout);
        }
        catch (Exception settlementFailure)
        {
            Exception cause = gracefulFailure is null
                ? settlementFailure
                : new AggregateException(gracefulFailure, settlementFailure);
            throw new StopException(StopFailure.SettlementFailed, null, cause);
        }

        if (gracefulFailure is not null)
        {
            throw new StopException(StopFailure.Escalated, exit, gracefulFailure);
        }

        if (exit.Reason == NativeTerminationReason.NativeFailure)
        {
            throw new StopException(StopFailure.TerminalFailure, exit);
        }
    }

    private async Task StopGracefullyAsync()
    {
        ProtocolMessage result = await RequestAsync("Stop", CancellationToken.None);

        if (result.Status is not ("accepted" or "applied"))
        {
            throw new ProtocolException(ProtocolFailure.InvalidMessage,
                "Native Stop was not accepted.");
        }

        await completion;
    }

    /// <summary>Releases process and streams only after all channel tasks settle.</summary>
    public async ValueTask DisposeAsync()
    {
        Task task;

        lock (gate)
        {
            disposeTask ??= DisposeCoreAsync();
            task = disposeTask;
        }

        try
        {
            await task;
        }
        catch
        {
            lock (gate)
            {
                if (ReferenceEquals(disposeTask, task))
                {
                    disposeTask = null;
                }
            }

            throw;
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await StopAsync(NativeTerminationReason.ApplicationShutdown, CancellationToken.None);
        }
        catch (StopException) when (process.HasExited)
        {
            // Stop already reported its failure; disposal can still release the settled exact child.
        }

        await SettleIoAsync();
        await SettleDeadlinesAsync();
        process.Dispose();
        lifetimeCancellation.Dispose();
        disposed = true;
    }

    /// <summary>Correlates one command by its unique in-flight request identity.</summary>
    private async Task<ProtocolMessage> RequestAsync(string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingCommand request;
        ulong requestId;
        OutboundFrame frame;
        Exception? priorFailure;
        bool queued;

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool channelUnavailable = !nativeReady || terminalObserved || channelFailure is not null ||
                pending.Count >= MaximumPendingCommands || nextRequestId == 0;

            if (channelUnavailable)
            {
                if (terminalObserved)
                {
                    throw new TerminalCommandException();
                }

                throw new InvalidOperationException("The native command channel is unavailable.");
            }

            requestId = nextRequestId++;
            ProtocolMessage message = new()
            {
                MessageType = "command",
                RequestId = requestId,
                SessionId = SessionId.Value,
                Command = command,
            };
            frame = new OutboundFrame(ProtocolJsonCodec.Encode(message));
            beforeRequestPublication?.Invoke(requestId, command);
            priorFailure = channelFailure;
            queued = TryQueueFrameLocked(frame);
            request = new PendingCommand(command);

            if (queued)
            {
                pending.Add(requestId, request);
            }
        }

        if (!queued)
        {
            throw RejectOutputQueue(priorFailure);
        }

        await frame.Sent.Task;
        Task<ProtocolMessage> response = request.Completion.Task;

        lock (gate)
        {
            if (pending.TryGetValue(requestId, out PendingCommand? current) &&
                ReferenceEquals(current, request))
            {
                activeDeadlines.Add(request);
                request.DeadlineTask = EnforceCommandDeadlineAsync(requestId, request);
            }
        }

        return await response.WaitAsync(cancellationToken);
    }

    /// <summary>Fails only a command still owned when its finite result budget expires.</summary>
    private async Task EnforceCommandDeadlineAsync(ulong requestId, PendingCommand request)
    {
        try
        {
            await request.Completion.Task.WaitAsync(CommandTimeout);
        }
        catch (TimeoutException exception)
        {
            bool failedPending;

            lock (gate)
            {
                failedPending = !terminalObserved && channelFailure is null &&
                    pending.TryGetValue(requestId, out PendingCommand? current) &&
                    ReferenceEquals(current, request);

                if (failedPending)
                {
                    SetChannelFailureLocked(exception);
                }
            }

            if (failedPending)
            {
                KillOwnedProcess();
            }
        }
        catch (Exception) when (request.Completion.Task.IsCompleted)
        {
            // The completed request already delivered its failure to the caller.
        }
        finally
        {
            lock (gate)
            {
                activeDeadlines.Remove(request);
            }
        }
    }

    /// <summary>Accounts for both frame count and bytes before the single writer accepts work.</summary>
    private Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = ProtocolJsonCodec.Encode(message);
        OutboundFrame frame = new(payload);
        Exception? priorFailure;
        bool queued;

        lock (gate)
        {
            priorFailure = channelFailure;
            queued = TryQueueFrameLocked(frame);
        }

        if (!queued)
        {
            throw RejectOutputQueue(priorFailure);
        }

        return frame.Sent.Task;
    }

    /// <summary>Publishes one bounded frame while the request-order gate is held.</summary>
    private bool TryQueueFrameLocked(OutboundFrame frame)
    {
        int frameBytes = frame.Payload.Length + sizeof(uint);
        bool queueUnavailable = channelFailure is not null || queuedFrames >= MaximumQueuedFrames ||
            queuedBytes > MaximumQueuedBytes - frameBytes || !outgoing.Writer.TryWrite(frame);

        if (queueUnavailable)
        {
            return false;
        }

        queuedFrames++;
        queuedBytes += frameBytes;
        return true;
    }

    /// <summary>Fails the exact channel when bounded frame publication is rejected.</summary>
    private IOException RejectOutputQueue(Exception? priorFailure)
    {
        IOException exception = process.HasExited
            ? new IOException($"The native process exited before the next machine frame was sent " +
                $"(code 0x{process.ExitCode:X8}).", priorFailure)
            : new IOException("The bounded native output queue is unavailable.", priorFailure);
        FailChannel(exception);
        return exception;
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (OutboundFrame frame in outgoing.Reader.ReadAllAsync(lifetimeCancellation.Token))
            {
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
                deadline.CancelAfter(FrameWriteTimeout);

                try
                {
                    await ProtocolFrameCodec.WriteAsync(standardInput, frame.Payload,
                        deadline.Token);
                    await standardInput.FlushAsync(deadline.Token);
                    frame.Sent.TrySetResult();
                }
                catch (Exception exception)
                {
                    frame.Sent.TrySetException(exception);
                    throw;
                }
                finally
                {
                    lock (gate)
                    {
                        queuedFrames--;
                        queuedBytes -= frame.Payload.Length + sizeof(uint);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            bool terminal;

            lock (gate)
            {
                terminal = terminalObserved;
            }

            if (!lifetimeCancellation.IsCancellationRequested && !terminal)
            {
                FailChannel(exception);
            }
        }
        finally
        {
            while (outgoing.Reader.TryRead(out OutboundFrame? frame))
            {
                frame.Sent.TrySetException(new IOException("The native output channel closed."));

                lock (gate)
                {
                    queuedFrames--;
                    queuedBytes -= frame.Payload.Length + sizeof(uint);
                }
            }
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                byte[]? payload = await ProtocolFrameCodec.ReadAsync(process.StandardOutput.BaseStream,
                    lifetimeCancellation.Token);

                if (payload is null)
                {
                    break;
                }

                HandleInbound(ProtocolJsonCodec.Decode(payload));
            }

        }
        catch (Exception exception)
        {
            if (!lifetimeCancellation.IsCancellationRequested)
            {
                FailChannel(exception);
            }
        }
    }

    /// <summary>Rejects wrong-direction, out-of-order and mismatched native messages.</summary>
    private void HandleInbound(ProtocolMessage message)
    {
        lock (gate)
        {
            if (!handshakeAccepted)
            {
                if (message.MessageType != "helloResult" || hello.Task.IsCompleted)
                {
                    throw new ProtocolException(ProtocolFailure.InvalidMessage,
                        "Native sent a non-handshake frame before handshake acceptance.");
                }

                hello.TrySetResult(message);

                if (message.Status == "accepted" && message.Product == ProtocolCompatibility.Product &&
                    message.ProtocolMajor == ProtocolCompatibility.Major &&
                    message.ProtocolMinor == ProtocolCompatibility.Minor && message.Capabilities is not null &&
                    new[] { "focus-window", "lifecycle-v1", "stop" }.All(message.Capabilities.Contains))
                {
                    handshakeAccepted = true;
                }

                return;
            }

            if (message.MessageType == "lifecycle")
            {
                if (terminalObserved || message.SessionId != SessionId.Value ||
                    message.Sequence <= lastSequence)
                {
                    throw new ProtocolException(ProtocolFailure.InvalidMessage,
                        "Native lifecycle identity or ordering is invalid.");
                }

                lastSequence = message.Sequence!.Value;

                if (!nativeReady)
                {
                    if (message.EventType != "NativeReady")
                    {
                        throw new ProtocolException(ProtocolFailure.InvalidMessage,
                            "NativeReady must precede session observations.");
                    }

                    nativeReady = true;
                    ready.TrySetResult();
                }
                else if (message.EventType == "NativeReady")
                {
                    throw new ProtocolException(ProtocolFailure.InvalidMessage,
                        "NativeReady was repeated.");
                }

                if (message.EventType == "SessionStopped")
                {
                    terminalObserved = true;
                    observedReason = message.Reason switch
                    {
                        "userStop" => NativeTerminationReason.UserStop,
                        "windowClosed" => NativeTerminationReason.WindowClosed,
                        "transportLost" => NativeTerminationReason.TransportLost,
                        "protocolError" => NativeTerminationReason.ProtocolError,
                        _ => NativeTerminationReason.NativeFailure,
                    };

                    TerminalCommandException terminalFailure = new();

                    foreach (PendingCommand terminalRequest in pending.Values)
                    {
                        terminalRequest.Completion.TrySetException(terminalFailure);
                    }

                    pending.Clear();
                }
                else if (message.EventType == "FatalError")
                {
                    observedReason = NativeTerminationReason.NativeFailure;
                }

                NativeLifecycleObservation observation = new(SessionId, message.Sequence!.Value,
                    message.Utc!.Value, message.MonotonicMicroseconds!.Value,
                    message.ConnectionAttemptId is { } attempt ? new ConnectionAttemptId(attempt) : null,
                    ParseToken<NativeLifecycleSubsystem>(message.Subsystem!),
                    ParseToken<NativeLifecycleEventType>(message.EventType!),
                    ParseToken<NativeLifecycleReason>(message.Reason!),
                    ParseToken<NativeLifecycleError>(message.Error!));

                if (!lifecycle.Writer.TryWrite(observation))
                {
                    throw new IOException("The bounded native lifecycle queue is full.");
                }

                return;
            }

            if (message.MessageType == "commandResult" && nativeReady && !terminalObserved &&
                message.SessionId == SessionId.Value && message.RequestId is { } requestId &&
                pending.TryGetValue(requestId, out PendingCommand? request) &&
                request.Command == message.Command)
            {
                pending.Remove(requestId);
                request.Completion.TrySetResult(message);
                return;
            }

            throw new ProtocolException(ProtocolFailure.InvalidMessage,
                "Native sent an unexpected or uncorrelated frame.");
        }
    }

    /// <summary>Drains stderr even without line breaks while retaining only a bounded prefix.</summary>
    private async Task DrainStderrAsync()
    {
        byte[] buffer = new byte[4096];
        using MemoryStream retained = new();

        try
        {
            while (true)
            {
                int count = await process.StandardError.BaseStream.ReadAsync(buffer, lifetimeCancellation.Token);

                if (count == 0)
                {
                    break;
                }

                int keep = Math.Min(count, MaximumRetainedStderrBytes - (int)retained.Length);

                if (keep > 0)
                {
                    retained.Write(buffer, 0, keep);
                }

                StandardErrorTruncated |= keep != count;
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            StandardErrorTruncated = true;
        }
        finally
        {
            StandardError = System.Text.Encoding.UTF8.GetString(retained.ToArray());
        }
    }

    /// <summary>Observes child exit separately from SessionStopped and settles held pipe ends.</summary>
    private async Task<NativeExit> CompleteAsync()
    {
        Task exit = process.WaitForExitAsync();
        Task first = await Task.WhenAny(exit, readerTask, failed.Task);

        if (!ReferenceEquals(first, exit) && !process.HasExited)
        {
            bool terminal;

            lock (gate)
            {
                terminal = terminalObserved;
            }

            if (terminal && !ReferenceEquals(first, failed.Task))
            {
                try
                {
                    await exit.WaitAsync(TerminalEofExitTimeout);
                }
                catch (TimeoutException)
                {
                    KillOwnedProcess();
                }
            }
            else
            {
                FailChannel(new IOException("Native stdout closed without terminal lifecycle."));
            }
        }

        await exit.WaitAsync(ForcedStopTimeout);
        await SettleIoAsync();
        bool missingTerminal;

        lock (gate)
        {
            missingTerminal = !terminalObserved && channelFailure is null;
        }

        if (missingTerminal)
        {
            FailChannel(new IOException("Native stdout ended without terminal lifecycle."));
        }

        await SettleDeadlinesAsync();
        NativeTerminationReason reason;

        lock (gate)
        {
            reason = channelFailure is not null || process.ExitCode != 0
                ? NativeTerminationReason.NativeFailure
                : observedReason ?? NativeTerminationReason.NativeFailure;
        }

        lifecycle.Writer.TryComplete();
        return new NativeExit(SessionId, reason);
    }

    private Task SettleIoAsync()
    {
        lock (gate)
        {
            settleTask ??= SettleIoCoreAsync();
            return settleTask;
        }
    }

    /// <summary>Joins command deadlines before releasing the owned Process and cancellation source.</summary>
    private Task SettleDeadlinesAsync()
    {
        Task[] deadlines;

        lock (gate)
        {
            deadlines = activeDeadlines.Select(request => request.DeadlineTask!).ToArray();
        }

        return Task.WhenAll(deadlines).WaitAsync(StreamSettleTimeout);
    }

    private async Task SettleIoCoreAsync()
    {
        outgoing.Writer.TryComplete();

        try
        {
            await writerTask.WaitAsync(StreamSettleTimeout);
        }
        catch (TimeoutException)
        {
            await lifetimeCancellation.CancelAsync();
            process.StandardInput.Dispose();
            standardInput.Dispose();
            await writerTask.WaitAsync(StreamSettleTimeout);
        }

        process.StandardInput.Dispose();
        standardInput.Dispose();

        try
        {
            await Task.WhenAll(readerTask, stderrTask).WaitAsync(StreamSettleTimeout);
        }
        catch (TimeoutException)
        {
            await lifetimeCancellation.CancelAsync();
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            await Task.WhenAll(readerTask, stderrTask).WaitAsync(StreamSettleTimeout);
        }
    }

    private async Task AbortAsync()
    {
        KillOwnedProcess();
        await completion;
        process.Dispose();
        lifetimeCancellation.Dispose();
    }

    /// <summary>Fails every waiter; no later frame can shift or reuse this channel.</summary>
    private void FailChannel(Exception exception)
    {
        bool failedChannel;

        lock (gate)
        {
            failedChannel = channelFailure is null;

            if (failedChannel)
            {
                SetChannelFailureLocked(exception);
            }
        }

        if (failedChannel)
        {
            KillOwnedProcess();
        }
    }

    /// <summary>Publishes channel failure atomically with request admission.</summary>
    private void SetChannelFailureLocked(Exception exception)
    {
        channelFailure = exception;
        hello.TrySetException(exception);
        ready.TrySetException(exception);
        failed.TrySetResult();

        foreach (PendingCommand request in pending.Values)
        {
            request.Completion.TrySetException(exception);
        }

        pending.Clear();
        outgoing.Writer.TryComplete(exception);
    }

    /// <summary>Never terminates a process tree or an unrelated shared daemon.</summary>
    private void KillOwnedProcess()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The exact child exited during escalation.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The exact child exited during escalation.
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static T ParseToken<T>(string token) where T : struct, Enum
    {
        if (!Enum.TryParse(token, ignoreCase: true, out T result) || !Enum.IsDefined(result))
        {
            throw new ProtocolException(ProtocolFailure.InvalidMessage,
                "A native lifecycle token is unsupported.");
        }

        return result;
    }

    private sealed record OutboundFrame(byte[] Payload)
    {
        public TaskCompletionSource Sent { get; } = NewCompletion();
    }

    private sealed class PendingCommand(string command)
    {
        public string Command { get; } = command;
        public TaskCompletionSource<ProtocolMessage> Completion { get; } = NewCompletion<ProtocolMessage>();
        public Task? DeadlineTask { get; set; }
    }
}
