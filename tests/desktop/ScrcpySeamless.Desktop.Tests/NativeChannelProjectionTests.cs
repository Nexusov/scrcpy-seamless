using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class NativeChannelProjectionTests
{
    [Fact]
    public void RetiredMetricsRemainEvidenceWithoutChangingReconnectStatus()
    {
        SessionId session = SessionId.New();
        ConnectionAttemptId attempt = ConnectionAttemptId.New();
        NativeLifecycleProjection projection = new(session);
        projection.Apply(Observation(session, 1, NativeLifecycleEventType.Connecting, NativeLifecycleSubsystem.Connection, attempt));
        projection.Apply(Observation(session, 2, NativeLifecycleEventType.TransportLost, NativeLifecycleSubsystem.Connection, attempt));
        projection.Apply(Observation(session, 3, NativeLifecycleEventType.ReconnectScheduled, NativeLifecycleSubsystem.Connection) with
        {
            Reason = NativeLifecycleReason.TransportLost,
        });
        NativeLifecycleObservation metrics = Observation(session, 4, NativeLifecycleEventType.GenerationRetired,
            NativeLifecycleSubsystem.Native, attempt) with { GenerationMetrics = new(1, 20) };

        Assert.True(projection.Apply(metrics));

        Assert.Equal(metrics, projection.RecentObservations[^1]);
        Assert.Equal(4UL, projection.LastSequence);
        Assert.Equal(4UL, projection.TotalObserved);
        Assert.Equal(NativeLifecycleEventType.ReconnectScheduled, projection.EventType);
        Assert.Equal(NativeLifecycleReason.TransportLost, projection.Reason);
        Assert.Equal(NativeVideoEvidence.NotReady, projection.Video);
    }

    [Fact]
    public void PresentedVideoDoesNotPromoteAudioOrControlAndDegradationIsIndependent()
    {
        SessionId session = SessionId.New();
        ConnectionAttemptId attempt = ConnectionAttemptId.New();
        NativeLifecycleProjection projection = new(session);
        projection.Apply(Observation(session, 1, NativeLifecycleEventType.Connecting, NativeLifecycleSubsystem.Connection, attempt));
        projection.Apply(Observation(session, 2, NativeLifecycleEventType.StreamStarted, NativeLifecycleSubsystem.Video, attempt));
        Assert.Equal(NativeVideoEvidence.ObservedFrame, projection.Video);
        Assert.Equal(NativeChannelEvidence.Unknown, projection.Audio);
        Assert.Equal(NativeChannelEvidence.Unknown, projection.Control);
        projection.Apply(Observation(session, 3, NativeLifecycleEventType.CapabilityDegraded, NativeLifecycleSubsystem.Audio));
        Assert.Equal(NativeVideoEvidence.ObservedFrame, projection.Video);
        Assert.Equal(NativeChannelEvidence.NotReady, projection.Audio);
        Assert.Equal(NativeChannelEvidence.Unknown, projection.Control);
        projection.Apply(Observation(session, 4, NativeLifecycleEventType.CapabilityDegraded, NativeLifecycleSubsystem.Control));
        Assert.Equal(NativeChannelEvidence.NotReady, projection.Control);
        Assert.Equal(NativeVideoEvidence.ObservedFrame, projection.Video);
    }

    [Fact]
    public void NewAttemptPreservesUnknownAndOldDegradationCannotChangeIt()
    {
        SessionId session = SessionId.New();
        ConnectionAttemptId first = ConnectionAttemptId.New();
        ConnectionAttemptId second = ConnectionAttemptId.New();
        NativeLifecycleProjection projection = new(session);
        projection.Apply(Observation(session, 1, NativeLifecycleEventType.Connecting, NativeLifecycleSubsystem.Connection, first));
        projection.Apply(Observation(session, 2, NativeLifecycleEventType.TransportLost, NativeLifecycleSubsystem.Connection, first));
        Assert.Equal(NativeChannelEvidence.NotReady, projection.Audio);
        projection.Apply(Observation(session, 3, NativeLifecycleEventType.ReconnectScheduled, NativeLifecycleSubsystem.Connection));
        projection.Apply(Observation(session, 4, NativeLifecycleEventType.Reconnecting, NativeLifecycleSubsystem.Connection, second));
        projection.Apply(Observation(session, 5, NativeLifecycleEventType.StreamResumed, NativeLifecycleSubsystem.Video, second));
        projection.Apply(Observation(session, 6, NativeLifecycleEventType.CapabilityDegraded, NativeLifecycleSubsystem.Audio, first));
        Assert.Equal(NativeChannelEvidence.Unknown, projection.Audio);
        Assert.Equal(NativeChannelEvidence.Unknown, projection.Control);
        Assert.Equal(NativeVideoEvidence.ObservedFrame, projection.Video);
        Assert.Equal(6ul, projection.LastSequence);
    }

    private static NativeLifecycleObservation Observation(SessionId session, ulong sequence,
        NativeLifecycleEventType type, NativeLifecycleSubsystem subsystem, ConnectionAttemptId? attempt = null) =>
        new(session, sequence, DateTimeOffset.UnixEpoch, sequence, attempt, subsystem, type,
            NativeLifecycleReason.None, NativeLifecycleError.None);
}
