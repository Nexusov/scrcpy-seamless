using System.Windows.Input;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Infrastructure.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Retains bounded typed observations without adding a lifecycle consumer or an operation.</summary>
public sealed partial class DeviceSessionViewModel
{
    private readonly Func<string, Task>? evidenceClipboard;
    private SessionEvidenceSnapshot? sessionEvidence;
    private long evidenceGeneration;
    private bool copyingEvidence;
    private string evidenceCopyStatus = string.Empty;

    public ICommand CopySessionEvidenceCommand { get; }
    public bool CanCopySessionEvidence => evidenceClipboard is not null;
    public string EvidenceCopyStatus
    {
        get => evidenceCopyStatus;
        private set => SetProperty(ref evidenceCopyStatus, value);
    }

    /// <summary>Captures detached values under the existing owner gate without consuming observations.</summary>
    public SessionEvidenceSnapshot CaptureSessionEvidence()
    {
        lock (gate)
        {
            SessionEvidenceSnapshot retained = sessionEvidence ?? CreateEmptyEvidence();
            return retained with
            {
                CapturedUtc = DateTimeOffset.UtcNow,
                Events = lifecycleProjection?.RecentObservations ?? retained.Events,
                TotalObserved = lifecycleProjection?.TotalObserved ?? retained.TotalObserved,
                NativeReadyObserved = lifecycleProjection?.NativeReadyObserved ?? retained.NativeReadyObserved,
            };
        }
    }

    /// <summary>Serializes and writes only on an explicit DEV action, independently of terminal status.</summary>
    public async Task<bool> CopySessionEvidenceAsync()
    {
        lock (gate)
        {
            if (evidenceClipboard is null || copyingEvidence)
            {
                return false;
            }

            copyingEvidence = true;
        }

        NotifyEvidenceCommand();

        try
        {
            SessionEvidenceSnapshot snapshot = CaptureSessionEvidence();
            string formatted = await Task.Run(() => SessionEvidenceFormatter.Format(snapshot));
            await evidenceClipboard(formatted);
            EvidenceCopyStatus = "Session evidence copied. Capture again before a new launch or Desktop exit.";
            return true;
        }
        catch (Exception)
        {
            // Clipboard and formatting faults are observation failures, never session failures.
            EvidenceCopyStatus = "Session evidence could not be copied. Retry explicitly.";
            return false;
        }
        finally
        {
            lock (gate)
            {
                copyingEvidence = false;
            }

            NotifyEvidenceCommand();
        }
    }

    /// <summary>Refreshes availability without changing native or launch commands.</summary>
    private void NotifyEvidenceCommand() =>
        ((AsyncActionCommand)CopySessionEvidenceCommand).NotifyCanExecuteChanged();

    /// <summary>Describes unavailable observations without inventing a session or handshake.</summary>
    private SessionEvidenceSnapshot CreateEmptyEvidence() => new(
        DateTimeOffset.UtcNow, null, null, SessionObservationStage.NoSession,
        runtime.Bundle?.Manifest.SourceSha, null, false, [], 0, null,
        SessionCompletionState.NotRecorded,
        new SessionCleanupEvidence(SessionCleanupIntent.None, SessionCleanupState.NotStarted,
            SessionStopCallState.NotRequested, null, SessionCleanupFailureCategory.None, null, false));

    /// <summary>Starts retention only for an acquired exact session; rejected launches keep prior evidence.</summary>
    private void InitializeSessionEvidence(INativeSession owned, long generation)
    {
        INativeHandshakeEvidence? machineEvidence = owned as INativeHandshakeEvidence;
        sessionEvidence = CreateEmptyEvidence() with
        {
            SessionId = owned.SessionId.Value,
            ProcessId = machineEvidence?.ProcessId,
            AcceptedHandshake = machineEvidence?.AcceptedHandshake,
            ObservationStage = SessionObservationStage.Active,
            CompletionState = SessionCompletionState.InProgress,
        };
        evidenceGeneration = generation;
    }

    /// <summary>Retains completion before posting presentation or releasing the owning adapter.</summary>
    private void RetainSessionCompletion(INativeSession owned, long generation, NativeExit result)
    {
        lock (gate)
        {
            bool currentEvidence = ReferenceEquals(session, owned) && evidenceGeneration == generation &&
                sessionEvidence?.SessionId == result.SessionId.Value && result.SessionId == owned.SessionId;

            if (!currentEvidence)
            {
                return;
            }

            sessionEvidence = sessionEvidence! with
            {
                TerminalResult = result,
                CompletionState = SessionCompletionState.Completed,
                Cleanup = sessionEvidence.Cleanup with
                {
                    ExactChildExitObserved = owned is INativeHandshakeEvidence ? true : null,
                },
            };
        }
    }

    /// <summary>Records a completion fault as unavailable typed exit evidence, without exception text.</summary>
    private void RetainCompletionFailure(INativeSession owned)
    {
        if (sessionEvidence is null || !ReferenceEquals(session, owned))
        {
            return;
        }

        sessionEvidence = sessionEvidence with
        {
            CompletionState = SessionCompletionState.Failed,
            Cleanup = sessionEvidence.Cleanup with { FailureCategory = SessionCleanupFailureCategory.Completion },
        };
    }

    /// <summary>Preserves the active cleanup initiator while allowing a new route after failed cleanup.</summary>
    private void SetCleanupIntent(INativeSession owned, SessionCleanupIntent intent)
    {
        if (sessionEvidence is null || !ReferenceEquals(session, owned))
        {
            return;
        }

        // Joining an active cleanup does not initiate a new operation; a failed cleanup can be retried.
        bool hasActiveInitiator = sessionEvidence.Cleanup.State == SessionCleanupState.InProgress &&
            sessionEvidence.Cleanup.Intent != SessionCleanupIntent.None;
        SessionCleanupIntent initiatingIntent = hasActiveInitiator ? sessionEvidence.Cleanup.Intent : intent;

        sessionEvidence = sessionEvidence with
        {
            ObservationStage = SessionObservationStage.CleanupInProgress,
            Cleanup = sessionEvidence.Cleanup with { Intent = initiatingIntent, State = SessionCleanupState.InProgress },
        };
    }

    /// <summary>Observes the existing Stop await without changing its exception or settlement contract.</summary>
    private async Task StopWithEvidenceAsync(INativeSession owned, NativeTerminationReason reason,
        SessionCleanupIntent intent)
    {
        lock (gate)
        {
            SetCleanupIntent(owned, intent);
            UpdateStopEvidence(owned, SessionStopCallState.InProgress, reason, null);
        }

        try
        {
            await owned.StopAsync(reason, CancellationToken.None);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                UpdateStopEvidence(owned, SessionStopCallState.Failed, reason,
                    (exception as MachineNativeSession.StopException)?.Failure);
            }

            throw;
        }

        lock (gate)
        {
            UpdateStopEvidence(owned, SessionStopCallState.Succeeded, reason, null);
        }
    }

    /// <summary>Retains the managed call result without claiming any wire command or acknowledgement.</summary>
    private void UpdateStopEvidence(INativeSession owned, SessionStopCallState state,
        NativeTerminationReason reason, MachineNativeSession.StopFailure? failure)
    {
        if (sessionEvidence is null || !ReferenceEquals(session, owned))
        {
            return;
        }

        sessionEvidence = sessionEvidence with
        {
            Cleanup = sessionEvidence.Cleanup with
            {
                StopCallState = state,
                StopRequestedReason = reason,
                StopFailure = failure,
                FailureCategory = state == SessionStopCallState.Failed
                    ? SessionCleanupFailureCategory.StopOperation : SessionCleanupFailureCategory.None,
            },
        };
    }

    /// <summary>Marks disposal and owner-work settlement as pending, not proved by process exit.</summary>
    private void MarkCleanupInProgress(INativeSession owned)
    {
        lock (gate)
        {
            if (sessionEvidence is null || !ReferenceEquals(session, owned))
            {
                return;
            }

            SessionCleanupIntent intent = sessionEvidence.Cleanup.Intent == SessionCleanupIntent.None
                ? SessionCleanupIntent.CompletionCleanup : sessionEvidence.Cleanup.Intent;
            SetCleanupIntent(owned, intent);
        }
    }

    /// <summary>Preserves an actual disposal or owner-work failure while ownership remains retained.</summary>
    private void RetainCleanupFailure(INativeSession owned)
    {
        if (sessionEvidence is null || !ReferenceEquals(session, owned))
        {
            return;
        }

        sessionEvidence = sessionEvidence with
        {
            ObservationStage = SessionObservationStage.OwnershipRetained,
            Cleanup = sessionEvidence.Cleanup with
            {
                State = SessionCleanupState.Failed,
                FailureCategory = SessionCleanupFailureCategory.DisposalOrOwnedWork,
            },
        };
    }

    /// <summary>Captures successful adapter disposal and all owned observer/focus joins before release.</summary>
    private void RetainSuccessfulCleanup(INativeSession owned)
    {
        if (sessionEvidence is null || !ReferenceEquals(session, owned))
        {
            return;
        }

        sessionEvidence = sessionEvidence with
        {
            ObservationStage = SessionObservationStage.Released,
            Events = lifecycleProjection?.RecentObservations ?? sessionEvidence.Events,
            TotalObserved = lifecycleProjection?.TotalObserved ?? sessionEvidence.TotalObserved,
            NativeReadyObserved = lifecycleProjection?.NativeReadyObserved ?? sessionEvidence.NativeReadyObserved,
            Cleanup = sessionEvidence.Cleanup with { State = SessionCleanupState.Succeeded, OwnershipReleased = true },
        };
    }
}
