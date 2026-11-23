using System.Windows.Input;
using ScrcpySeamless.Core.Application.Diagnostics;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Explicit new-file export owns and settles picker, immutable snapshot, and write as one operation.</summary>
public sealed class DiagnosticExportViewModel : ObservableViewModel
{
    private readonly Func<IReadOnlyList<SessionEvidenceSnapshot>> capture;
    private readonly ILocalDiagnosticWriter writer;
    private readonly Func<CancellationToken, Task<string?>> chooseNewDestination;
    private readonly PresentationText text;
    private readonly AsyncActionCommand exportCommand;
    private readonly AsyncActionCommand cancelCommand;
    private Task activeOperation = Task.CompletedTask;
    private CancellationTokenSource? cancellation;
    private DiagnosticWriteResult? result;
    private bool exporting;
    private bool cancelling;
    private bool closed;

    public DiagnosticExportViewModel(Func<IReadOnlyList<SessionEvidenceSnapshot>> capture,
        ILocalDiagnosticWriter writer, Func<CancellationToken, Task<string?>> chooseNewDestination,
        PresentationText text)
    {
        this.capture = capture;
        this.writer = writer;
        this.chooseNewDestination = chooseNewDestination;
        this.text = text;
        exportCommand = new AsyncActionCommand(ExportAsync, () => CanExport, UnexpectedFailure);
        cancelCommand = new AsyncActionCommand(() => { Cancel(); return Task.CompletedTask; },
            () => CanCancel, UnexpectedFailure);
    }

    public ICommand ExportCommand => exportCommand;
    public ICommand CancelCommand => cancelCommand;
    public string Title => text.Get("diagnostics.export.title");
    public string Description => text.Get("diagnostics.export.description");
    public string ExportLabel => text.Get("diagnostics.export.save");
    public string CancelLabel => text.Get("diagnostics.export.cancel");
    public string Coverage => text.Get("diagnostics.export.coverage");
    public bool CanExport => !exporting && !closed;
    public bool CanCancel => exporting && !cancelling && !closed;
    public bool IsExporting => exporting;
    public DiagnosticWriteResult? Result => result;
    public bool HasPartialOutput => result?.PartialOutputRetained == true;
    public string PartialOutputMessage => text.Get("diagnostics.export.partial");
    public string Status => text.Get(exporting
        ? cancelling ? "diagnostics.export.cancelling" : "diagnostics.export.saving"
        : result is { } completed ? $"diagnostics.export.status.{completed.Status}" : "diagnostics.export.idle");

    public Task ExportAsync()
    {
        if (exporting)
        {
            return activeOperation;
        }

        if (closed)
        {
            return Task.CompletedTask;
        }
        CancellationTokenSource lifetime = new();
        cancellation = lifetime;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        activeOperation = completion.Task;
        exporting = true;
        cancelling = false;
        result = null;
        NotifyState();
        _ = RunAndSettleAsync(lifetime, completion);
        return completion.Task;
    }

    public void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }
        cancelling = true;
        NotifyState();
        cancellation?.Cancel();
    }

    private async Task RunAndSettleAsync(CancellationTokenSource lifetime, TaskCompletionSource completion)
    {
        Exception? failure = null;

        try
        {
            await RunAsync(lifetime.Token);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            cancellation = null;
            lifetime.Dispose();
            exporting = false;
            cancelling = false;
            NotifyState();
        }

        if (failure is null)
        {
            completion.TrySetResult();
            return;
        }
        completion.TrySetException(failure);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? destination = await chooseNewDestination(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (destination is null)
            {
                result = new(DiagnosticExportStatus.Cancelled);
                return;
            }
            IReadOnlyList<SessionEvidenceSnapshot> snapshots = capture();

            if (snapshots.Count > DiagnosticExportFormatter.MaximumSessions)
            {
                result = new(DiagnosticExportStatus.LimitExceeded);
                return;
            }
            DiagnosticFormatResult document = DiagnosticExportFormatter.Format(snapshots.ToArray());

            if (document.Status != DiagnosticFormatStatus.Ready)
            {
                result = new(document.Status == DiagnosticFormatStatus.LimitExceeded
                    ? DiagnosticExportStatus.LimitExceeded : DiagnosticExportStatus.InvalidEvidence);
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            DiagnosticWriteResult written = await writer.WriteNewAsync(destination, document.Jsonl, cancellationToken);
            result = Enum.IsDefined(written.Status) ? written : new(DiagnosticExportStatus.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(DiagnosticExportStatus.Cancelled);
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            result = new(DiagnosticExportStatus.InvalidDestination);
        }
        catch (Exception)
        {
            result = new(DiagnosticExportStatus.Unavailable);
        }
    }

    /// <summary>Cancellation is not completion; the composition must await this before releasing its adapters.</summary>
    public async Task CloseAsync()
    {
        if (!closed)
        {
            closed = true;
            cancellation?.Cancel();
            NotifyState();
        }

        await activeOperation;
    }

    private void UnexpectedFailure(Exception exception)
    {
        _ = exception;
        result = new(DiagnosticExportStatus.Unavailable);
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsExporting));
        OnPropertyChanged(nameof(HasPartialOutput));
        exportCommand.NotifyCanExecuteChanged();
        cancelCommand.NotifyCanExecuteChanged();
    }
}
