using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Current machine evidence without retaining an unbounded event history.</summary>
internal sealed class NativeLifecycleProjection(SessionId sessionId)
{
    private const int MaximumRememberedAttempts = 32;
    private const int MaximumRecentObservations = 64;
    private ConnectionAttemptId? activeAttempt;
    private bool reconnectScheduled;
    private readonly Queue<ConnectionAttemptId> supersededOrder = new();
    private readonly HashSet<ConnectionAttemptId> supersededAttempts = [];
    private readonly Queue<NativeLifecycleObservation> recentObservations = new();

    public ulong LastSequence { get; private set; }
    public ulong TotalObserved { get; private set; }
    public bool NativeReadyObserved { get; private set; }
    public NativeLifecycleEventType? EventType { get; private set; }
    public NativeLifecycleReason Reason { get; private set; }
    public NativeVideoEvidence Video { get; private set; } = NativeVideoEvidence.Unknown;
    public NativeChannelEvidence Audio { get; private set; } = NativeChannelEvidence.Unknown;
    public NativeChannelEvidence Control { get; private set; } = NativeChannelEvidence.Unknown;
    public IReadOnlyList<NativeLifecycleObservation> RecentObservations => recentObservations.ToArray();

    /// <summary>Applies an ordered native observation without promoting an obsolete attempt.</summary>
    public bool Apply(NativeLifecycleObservation observation)
    {
        if (observation.SessionId != sessionId || observation.Sequence <= LastSequence)
        {
            return false;
        }

        LastSequence = observation.Sequence;
        // Counts observed entries, not inferred gaps in native sequence numbers.
        TotalObserved++;
        NativeReadyObserved |= observation.EventType == NativeLifecycleEventType.NativeReady;
        recentObservations.Enqueue(observation);

        if (recentObservations.Count > MaximumRecentObservations)
        {
            recentObservations.Dequeue();
        }

        bool previousAttempt = observation.ConnectionAttemptId is { } attempt &&
            supersededAttempts.Contains(attempt);

        switch (observation.EventType)
        {
            case NativeLifecycleEventType.GenerationRetired:
                // Retain measurements without changing the operational status.
                return true;
            case NativeLifecycleEventType.Connecting:
                if (activeAttempt is not null)
                {
                    return true;
                }

                activeAttempt = observation.ConnectionAttemptId;
                Video = NativeVideoEvidence.NotReady;
                Audio = Control = NativeChannelEvidence.Unknown;
                break;
            case NativeLifecycleEventType.Reconnecting:
                // Native emits one scheduled transition before each replacement attempt.
                if (!reconnectScheduled || previousAttempt)
                {
                    return true;
                }

                if (activeAttempt is { } prior && prior != observation.ConnectionAttemptId)
                {
                    supersededAttempts.Add(prior);
                    supersededOrder.Enqueue(prior);

                    if (supersededOrder.Count > MaximumRememberedAttempts)
                    {
                        supersededAttempts.Remove(supersededOrder.Dequeue());
                    }
                }

                activeAttempt = observation.ConnectionAttemptId;
                reconnectScheduled = false;
                Video = NativeVideoEvidence.NotReady;
                Audio = Control = NativeChannelEvidence.Unknown;
                break;
            case NativeLifecycleEventType.StreamStarted:
            case NativeLifecycleEventType.StreamResumed:
                if (previousAttempt || activeAttempt is { } current &&
                    observation.ConnectionAttemptId != current)
                {
                    return true;
                }

                activeAttempt = observation.ConnectionAttemptId;
                reconnectScheduled = false;
                Video = NativeVideoEvidence.ObservedFrame;
                break;
            case NativeLifecycleEventType.TransportLost:
                if (previousAttempt || activeAttempt is { } currentAttempt &&
                    observation.ConnectionAttemptId != currentAttempt)
                {
                    return true;
                }

                Video = NativeVideoEvidence.NotReady;
                Audio = Control = NativeChannelEvidence.NotReady;
                reconnectScheduled = false;
                break;
            case NativeLifecycleEventType.CapabilityDegraded:
                if (previousAttempt || activeAttempt is { } degradedAttempt &&
                    observation.ConnectionAttemptId is { } reportedAttempt && reportedAttempt != degradedAttempt)
                {
                    return true;
                }

                switch (observation.Subsystem)
                {
                    case NativeLifecycleSubsystem.Video:
                        Video = NativeVideoEvidence.NotReady;
                        break;
                    case NativeLifecycleSubsystem.Audio:
                        Audio = NativeChannelEvidence.NotReady;
                        break;
                    case NativeLifecycleSubsystem.Control:
                        Control = NativeChannelEvidence.NotReady;
                        break;
                }

                break;
            case NativeLifecycleEventType.ReconnectScheduled:
                bool unrelatedSchedule = observation.ConnectionAttemptId is { } scheduledAttempt &&
                    activeAttempt is { } scheduledCurrent && scheduledAttempt != scheduledCurrent;

                if (previousAttempt || unrelatedSchedule || Video == NativeVideoEvidence.ObservedFrame)
                {
                    return true;
                }

                reconnectScheduled = true;
                Video = NativeVideoEvidence.NotReady;
                break;
            case NativeLifecycleEventType.SessionStopped:
            case NativeLifecycleEventType.FatalError:
                Video = NativeVideoEvidence.NotReady;
                Audio = Control = NativeChannelEvidence.NotReady;
                break;
        }

        EventType = observation.EventType;
        Reason = observation.Reason;
        return true;
    }
}

/// <summary>Separates unknown video from a frame observed at the native presentation seam.</summary>
internal enum NativeVideoEvidence { Unknown, NotReady, ObservedFrame }
internal enum NativeChannelEvidence { Unknown, NotReady }
