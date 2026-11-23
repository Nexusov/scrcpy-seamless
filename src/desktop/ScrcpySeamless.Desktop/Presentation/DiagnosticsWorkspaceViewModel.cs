using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ScrcpySeamless.Desktop.Presentation;

public sealed record SessionDiagnosticRow(string Label, IReadOnlyList<StatusMetric> Facts);

/// <summary>Explicit bounded snapshots over existing owners, never another protocol consumer.</summary>
public sealed class DiagnosticsWorkspaceViewModel : ObservableViewModel
{
    private readonly SessionsWorkspaceViewModel? sessions;
    private readonly PresentationText text;
    private readonly ObservableCollection<SessionDiagnosticRow> rows = [];

    public DiagnosticsWorkspaceViewModel(SessionsWorkspaceViewModel? sessions, PresentationText text,
        ReleaseCheckViewModel? releases = null)
    {
        this.sessions = sessions;
        this.text = text;
        Releases = releases;
        Rows = new ReadOnlyObservableCollection<SessionDiagnosticRow>(rows);
        RefreshCommand = new ActionCommand(Refresh);
    }

    public ReadOnlyObservableCollection<SessionDiagnosticRow> Rows { get; }
    public ReleaseCheckViewModel? Releases { get; }
    public bool HasReleaseChecks => Releases is not null;
    public ICommand RefreshCommand { get; }
    public string Title => text.Get("diagnostics.title");
    public string Description => text.Get("diagnostics.description");
    public string RefreshLabel => text.Get("diagnostics.refresh");

    public void Refresh()
    {
        rows.Clear();

        if (sessions is null)
        {
            return;
        }

        foreach (DeviceSessionViewModel entry in sessions.Entries)
        {
            SessionEvidenceSnapshot evidence = entry.CaptureSessionEvidence();
            rows.Add(new SessionDiagnosticRow(entry.SessionLabel,
            [
                new("diagnostics.owner", text.Get("diagnostics.owner"), evidence.ObservationStage.ToString()),
                new("diagnostics.completion", text.Get("diagnostics.completion"),
                    evidence.TerminalResult?.Reason.ToString() ?? evidence.CompletionState.ToString()),
                new("diagnostics.cleanup", text.Get("diagnostics.cleanup"), evidence.Cleanup.State.ToString()),
                new("diagnostics.video", text.Get("devices.video"), entry.VideoEvidence),
                new("diagnostics.audio", text.Get("devices.audio"), entry.AudioEvidence),
                new("diagnostics.control", text.Get("devices.control"), entry.ControlEvidence),
            ]));
        }
    }
}
