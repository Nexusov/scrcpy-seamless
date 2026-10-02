using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Infrastructure.Activation;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Activation;

/// <summary>Exercises real activation transport with explicitly controlled peer publication.</summary>
public sealed class ActivationPeerSchedulingTests
{
    private const int MaximumTraceEntries = 256;
    private const string InvalidFocusCommand = "FOCUS:00000000-0000-0000-0000-000000000000\n";
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    // Added: the real helper must publish without depending on its caller's continuation queue.
    /// <summary>A complete invalid command is rejected while the caller's context remains held.</summary>
    [Fact]
    public async Task CompleteInvalidPeerDoesNotDependOnCallerSynchronizationContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        ObservationTrace trace = new();
        HeldSynchronizationContext heldContext = new();
        using CancellationTokenSource observationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using CancellationTokenSource peerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using WindowsControlCenterActivation primary = new(root, trace.Record);
        SynchronizationContext? originalContext = SynchronizationContext.Current;
        trace.RecordClient("CallerSynchronizationContext:" + (originalContext?.GetType().Name ?? "None"));
        Task<string> peer;

        try
        {
            SynchronizationContext.SetSynchronizationContext(heldContext);
            peer = WindowsControlCenterActivationTests.SendRawCommandAsync(
                primary.PipeName, InvalidFocusCommand, trace.RecordClient, peerCancellation.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        Task<IReadOnlyList<ActivationRequest>>? collected = null;

        try
        {
            // No server exists yet for this unique synthetic scope: connection must be pending.
            Assert.False(peer.IsCompleted);
            Assert.Contains("ConnectStarted", trace.ClientStages);
            Assert.DoesNotContain("Connected", trace.ClientStages);
            Assert.Equal(ActivationDisposition.PrimaryOwner,
                await primary.AcquireOrForwardAsync(new(ActivationKind.ShowControlCenter),
                    TestContext.Current.CancellationToken));
            collected = CollectRequestsAsync(new(primary, observationCancellation.Token));

            Task<ActivationObservation> expired = trace.RequestExpired.Task;
            Task completed = await Task.WhenAny(peer, expired)
                .WaitAsync(TestDeadline, TestContext.Current.CancellationToken);
            bool publicationExpired = ReferenceEquals(completed, expired);

            if (publicationExpired)
            {
                // Preserve the actual write boundary: release the peer only after the old pipe is closed.
                await trace.PipeDisposed.Task.WaitAsync(TestDeadline, TestContext.Current.CancellationToken);
                Assert.True(heldContext.HasQueuedContinuations);
            }

            Assert.True(ReferenceEquals(completed, peer),
                "The connected peer depended on the held caller context until the real request deadline expired.");
            Assert.Equal("NO", await peer);
            Assert.False(heldContext.HasQueuedContinuations);
            Assert.Contains(trace.Observations, item => item.Stage == ActivationStage.LineComplete);
            Assert.Contains(trace.Observations, item => item.Stage == ActivationStage.Rejected);
            Assert.DoesNotContain(trace.Observations, item => item.Stage == ActivationStage.Delivered);
            await VerifyFocusAndOwnershipAsync(new(root, primary, collected));
        }
        finally
        {
            observationCancellation.Cancel();
            peerCancellation.Cancel();
            heldContext.Open();
            await SettlePeerAsync(new(peer, trace));
            await heldContext.SettleCallbacksAsync();
            await SettleCollectorAsync(collected);
            await primary.DisposeAsync();
            trace.WriteOutput();
        }
    }

    // Added: expiration is a separate peer category, never a substitute for complete-message rejection.
    /// <summary>A deliberately silent peer expires before publication and the same primary remains usable.</summary>
    [Fact]
    public async Task ExpiredSilentPeerDoesNotDeliverAndPrimaryAcceptsFreshFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        ObservationTrace trace = new();
        using CancellationTokenSource observationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using CancellationTokenSource peerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        peerCancellation.CancelAfter(TestDeadline);
        await using WindowsControlCenterActivation primary = new(root, trace.Record);
        await using NamedPipeClientStream peer = new(".", primary.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task<IReadOnlyList<ActivationRequest>>? collected = null;

        try
        {
            Assert.Equal(ActivationDisposition.PrimaryOwner,
                await primary.AcquireOrForwardAsync(new(ActivationKind.ShowControlCenter),
                    TestContext.Current.CancellationToken));
            collected = CollectRequestsAsync(new(primary, observationCancellation.Token));
            await peer.ConnectAsync(peerCancellation.Token);
            ActivationObservation accepted = await trace.Accepted.Task.WaitAsync(TestDeadline, peerCancellation.Token);
            ActivationObservation reading = await trace.ReadStarted.Task.WaitAsync(TestDeadline, peerCancellation.Token);
            ActivationObservation expired = await trace.RequestExpired.Task.WaitAsync(TestDeadline, peerCancellation.Token);
            ActivationObservation closed = await trace.PipeDisposed.Task.WaitAsync(TestDeadline, peerCancellation.Token);

            Assert.Equal(ActivationCancellationCause.RequestDeadline, expired.CancellationCause);
            Assert.True(accepted.Timestamp <= reading.Timestamp);
            Assert.True(reading.Timestamp <= expired.Timestamp);
            Assert.True(expired.Timestamp <= closed.Timestamp);
            Assert.DoesNotContain(trace.Observations, item => item.Stage == ActivationStage.ReadProgress);
            Assert.DoesNotContain(trace.Observations, item => item.Stage == ActivationStage.LineComplete);
            Assert.DoesNotContain(trace.Observations, item => item.Stage == ActivationStage.Delivered);

            trace.RecordClient("WriteStartedAfterObservedDisposal");
            Exception? exception = await Record.ExceptionAsync(async () =>
                await peer.WriteAsync(Encoding.ASCII.GetBytes(InvalidFocusCommand), peerCancellation.Token));
            IOException disconnected = Assert.IsType<IOException>(exception);
            trace.RecordException(disconnected);
            await VerifyFocusAndOwnershipAsync(new(root, primary, collected));
        }
        finally
        {
            peerCancellation.Cancel();
            observationCancellation.Cancel();
            await peer.DisposeAsync();
            await SettleCollectorAsync(collected);
            await primary.DisposeAsync();
            trace.WriteOutput();
        }
    }

    /// <summary>Collects the actual primary stream until ownership disposal completes its writer.</summary>
    private static async Task<IReadOnlyList<ActivationRequest>> CollectRequestsAsync(CollectionOptions options)
    {
        List<ActivationRequest> collected = [];

        await foreach (ActivationRequest request in options.Primary.ObserveRequestsAsync(options.CancellationToken)
            .ConfigureAwait(false))
        {
            collected.Add(request);
        }

        return collected;
    }

    /// <summary>Checks exact observer output, retained primary ownership and reservation reacquisition.</summary>
    private static async Task VerifyFocusAndOwnershipAsync(FocusOptions options)
    {
        ActivationRequest show = new(ActivationKind.ShowControlCenter);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await options.Primary.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        await using WindowsControlCenterActivation repeated = new(options.Root);
        SessionId sessionId = SessionId.New();
        Assert.Equal(ActivationDisposition.ForwardedToPrimary,
            await repeated.AcquireOrForwardAsync(new(ActivationKind.FocusSession, sessionId),
                TestContext.Current.CancellationToken));
        await options.Primary.DisposeAsync();
        IReadOnlyList<ActivationRequest> received = await options.Collected
            .WaitAsync(TestDeadline, TestContext.Current.CancellationToken);
        ActivationRequest only = Assert.Single(received);
        Assert.Equal(ActivationKind.FocusSession, only.Kind);
        Assert.Equal(sessionId, only.SessionId);
        await using WindowsControlCenterActivation successor = new(options.Root);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await successor.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(options.Root));
    }

    /// <summary>Settles a peer after opening its gate, preserving its observed cleanup exception in the trace.</summary>
    private static async Task SettlePeerAsync(PeerOptions options)
    {
        try
        {
            await options.Peer.WaitAsync(TestDeadline);
        }
        catch (IOException exception)
        {
            // The normal assertion path propagates peer failures; cleanup only settles the same task.
            options.Trace.RecordException(exception);
        }
        catch (OperationCanceledException exception)
        {
            options.Trace.RecordException(exception);
        }
    }

    /// <summary>Settles the stream reader before its cancellation source and primary are disposed.</summary>
    private static async Task SettleCollectorAsync(Task<IReadOnlyList<ActivationRequest>>? collected)
    {
        if (collected is null)
        {
            return;
        }

        try
        {
            await collected.WaitAsync(TestDeadline);
        }
        catch (OperationCanceledException)
        {
            // The enclosing finally has explicitly cancelled this test-owned observation.
        }
    }

    /// <summary>Creates an isolated scope without accessing or creating real application data.</summary>
    private static string NewSyntheticRoot() =>
        Path.Combine(Path.GetTempPath(), "scrcpy-activation-scheduling-test-" + Guid.NewGuid().ToString("N"));

    private sealed record CollectionOptions(WindowsControlCenterActivation Primary, CancellationToken CancellationToken);
    private sealed record FocusOptions(string Root, WindowsControlCenterActivation Primary,
        Task<IReadOnlyList<ActivationRequest>> Collected);
    private sealed record PeerOptions(Task<string> Peer, ObservationTrace Trace);
    private sealed record PendingContinuation(SendOrPostCallback Callback, object? State);

    /// <summary>Holds only the raw helper's posted callbacks and owns every callback released during cleanup.</summary>
    private sealed class HeldSynchronizationContext : SynchronizationContext
    {
        private readonly object synchronization = new();
        private readonly Queue<PendingContinuation> pending = new();
        private readonly List<Task> released = [];
        private bool open;

        public bool HasQueuedContinuations
        {
            get
            {
                lock (synchronization)
                {
                    return pending.Count != 0;
                }
            }
        }

        /// <summary>Queues captured continuations without blocking the production listener or timer.</summary>
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (synchronization)
            {
                PendingContinuation continuation = new(callback, state);

                if (!open)
                {
                    pending.Enqueue(continuation);
                    return;
                }

                released.Add(Task.Run(() => continuation.Callback(continuation.State)));
            }
        }

        /// <summary>Releases queued and future continuations on every assertion and cancellation path.</summary>
        public void Open()
        {
            lock (synchronization)
            {
                open = true;

                while (pending.TryDequeue(out PendingContinuation? continuation))
                {
                    PendingContinuation releasedContinuation = continuation;
                    released.Add(Task.Run(() => releasedContinuation.Callback(releasedContinuation.State)));
                }
            }
        }

        /// <summary>Joins released callbacks after the raw peer has reached terminal completion.</summary>
        public Task SettleCallbacksAsync()
        {
            lock (synchronization)
            {
                return Task.WhenAll(released).WaitAsync(TestDeadline);
            }
        }
    }

    /// <summary>Stores a bounded synthetic trace and signals only real production stages.</summary>
    private sealed class ObservationTrace
    {
        private readonly ConcurrentQueue<ActivationObservation> observations = new();
        private readonly ConcurrentQueue<string> clientStages = new();
        private readonly ConcurrentQueue<string> entries = new();
        private int entryCount;

        public TaskCompletionSource<ActivationObservation> Accepted { get; } = NewSignal();
        public TaskCompletionSource<ActivationObservation> ReadStarted { get; } = NewSignal();
        public TaskCompletionSource<ActivationObservation> RequestExpired { get; } = NewSignal();
        public TaskCompletionSource<ActivationObservation> PipeDisposed { get; } = NewSignal();
        public ActivationObservation[] Observations => observations.ToArray();
        public string[] ClientStages => clientStages.ToArray();

        /// <summary>Records server metadata without gating its progress or serializing real endpoint data.</summary>
        public void Record(ActivationObservation observation)
        {
            bool retained = AddEntry($"server {observation.Stage} ticks={observation.Timestamp} bytes={observation.ByteCount} " +
                $"cause={observation.CancellationCause} hresult={observation.ErrorHResult}");

            if (retained)
            {
                observations.Enqueue(observation);
            }

            if (observation.Stage == ActivationStage.Accepted)
            {
                Accepted.TrySetResult(observation);
            }

            if (observation.Stage == ActivationStage.ReadStarted)
            {
                ReadStarted.TrySetResult(observation);
            }

            bool requestExpired = observation.Stage == ActivationStage.DeadlineCancelled &&
                observation.CancellationCause == ActivationCancellationCause.RequestDeadline;

            if (requestExpired)
            {
                RequestExpired.TrySetResult(observation);
            }

            if (observation.Stage == ActivationStage.PipeDisposed)
            {
                PipeDisposed.TrySetResult(observation);
            }
        }

        /// <summary>Records allowlisted helper stages with monotonic ordering and no command contents.</summary>
        public void RecordClient(string stage)
        {
            bool retained = AddEntry($"client {stage} ticks={Stopwatch.GetTimestamp()}");

            if (retained)
            {
                clientStages.Enqueue(stage);
            }
        }

        /// <summary>Retains exception type and HResult without publishing messages or paths.</summary>
        public void RecordException(Exception exception) =>
            AddEntry($"peer exception={exception.GetType().Name} hresult=0x{exception.HResult:X8} ticks={Stopwatch.GetTimestamp()}");

        /// <summary>Publishes synthetic trace entries only after all owned work is settled.</summary>
        public void WriteOutput()
        {
            foreach (string entry in entries)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(entry);
            }
        }

        /// <summary>Caps diagnostic output without blocking its production callback.</summary>
        private bool AddEntry(string entry)
        {
            if (Interlocked.Increment(ref entryCount) > MaximumTraceEntries)
            {
                return false;
            }

            entries.Enqueue(entry);
            return true;
        }

        /// <summary>Uses asynchronous continuations so observation never executes a test assertion inline.</summary>
        private static TaskCompletionSource<ActivationObservation> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
