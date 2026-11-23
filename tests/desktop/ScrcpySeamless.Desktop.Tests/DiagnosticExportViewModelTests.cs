using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core.Application.Diagnostics;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using ScrcpySeamless.Infrastructure.Diagnostics;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class DiagnosticExportViewModelTests
{
    [Fact]
    public async Task ConstructionIsInertAndUserActionCapturesOneDetachedAllowlistedSnapshot()
    {
        FakeWriter writer = new();
        int picks = 0, captures = 0;
        const string privatePath = "C:\\PRIVATE_PROFILE_DEVICE\\private-endpoint.jsonl";
        DiagnosticExportViewModel model = new(() => { captures++; return [DiagnosticSyntheticEvidence.Create()]; },
            writer, _ => { picks++; return Task.FromResult<string?>(privatePath); },
            new PresentationText(new Dictionary<string, string> { ["diagnostics.export.title"] = "PRIVATE_LABEL" }));
        Assert.Equal(0, picks + captures + writer.Calls);
        await model.ExportAsync();
        Assert.Equal(1, picks);
        Assert.Equal(1, captures);
        Assert.Equal(1, writer.Calls);
        Assert.Equal(privatePath, writer.Path);
        Assert.DoesNotContain("PRIVATE", Encoding.UTF8.GetString(writer.Document.Span));
        Assert.DoesNotContain(privatePath, model.Status);
        Assert.Equal(DiagnosticExportStatus.Saved, model.Result!.Status);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CancelledPickerDoesNotCaptureOrOpenAFile()
    {
        FakeWriter writer = new();
        int captures = 0;
        DiagnosticExportViewModel model = new(() => { captures++; return []; }, writer,
            _ => Task.FromResult<string?>(null), new PresentationText());
        await model.ExportAsync();
        Assert.Equal(0, captures + writer.Calls);
        Assert.Equal(DiagnosticExportStatus.Cancelled, model.Result!.Status);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CloseDuringPickerJoinsItsActualSettlementAndSuppressesLateWrites()
    {
        FakeWriter writer = new();
        TaskCompletionSource<string?> destination = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken pickerToken = default;
        DiagnosticExportViewModel model = new(() => [], writer, token => { pickerToken = token; return destination.Task; }, new PresentationText());
        Task export = model.ExportAsync();
        Assert.Same(export, model.ExportAsync());
        Task close = model.CloseAsync();
        Assert.True(pickerToken.IsCancellationRequested);
        Assert.False(close.IsCompleted);
        destination.SetResult("C:\\synthetic late choice.jsonl");
        await Task.WhenAll(export, close);
        Assert.Equal(0, writer.Calls);
        Assert.Equal(DiagnosticExportStatus.Cancelled, model.Result!.Status);
        Assert.False(model.CanExport);
    }

    [Fact]
    public async Task CancelAndCloseWaitForWriteReceiptAndPreserveTruthfulPartialOutcome()
    {
        FakeWriter writer = new();
        TaskCompletionSource<DiagnosticWriteResult> written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        writer.Response = written.Task;
        DiagnosticExportViewModel model = Create(writer);
        Task export = model.ExportAsync();
        Assert.Equal(1, writer.Calls);
        model.Cancel();
        Assert.True(writer.Token.IsCancellationRequested);
        Assert.False(export.IsCompleted);
        Assert.True(model.IsExporting);
        Task close = model.CloseAsync();
        Assert.False(close.IsCompleted);
        written.SetResult(new(DiagnosticExportStatus.Cancelled, true, PartialOutputRetained: true));
        await Task.WhenAll(export, close);
        Assert.True(model.HasPartialOutput);
        Assert.Equal(DiagnosticExportStatus.Cancelled, model.Result!.Status);
    }

    [Fact]
    public async Task CompletedSaveIsNotRelabelledCancelledByALateCloseRequest()
    {
        FakeWriter writer = new();
        TaskCompletionSource<DiagnosticWriteResult> written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        writer.Response = written.Task;
        DiagnosticExportViewModel model = Create(writer);
        Task export = model.ExportAsync();
        Task close = model.CloseAsync();
        written.SetResult(new(DiagnosticExportStatus.Saved, true, writer.Document.Length));
        await Task.WhenAll(export, close);
        Assert.Equal(DiagnosticExportStatus.Saved, model.Result!.Status);
        Assert.False(model.HasPartialOutput);
    }

    [Theory]
    [InlineData(DiagnosticExportStatus.AlreadyExists)]
    [InlineData(DiagnosticExportStatus.AccessDenied)]
    [InlineData(DiagnosticExportStatus.IoFailed)]
    [InlineData(DiagnosticExportStatus.InvalidDestination)]
    public async Task TypedIoOutcomeUsesLocalizedCopyWithoutExposingTheDestination(DiagnosticExportStatus status)
    {
        FakeWriter writer = new() { Response = Task.FromResult(new DiagnosticWriteResult(status)) };
        DiagnosticExportViewModel model = new(() => [], writer, _ => Task.FromResult<string?>("C:\\PRIVATE.jsonl"),
            new PresentationText(new Dictionary<string, string> { [$"diagnostics.export.status.{status}"] = "localized" }));
        await model.ExportAsync();
        Assert.Equal("localized", model.Status);
        Assert.DoesNotContain("PRIVATE", model.Status);
        await model.CloseAsync();
    }

    [Fact]
    public async Task InvalidOrOversizedEvidenceNeverReachesWriter()
    {
        FakeWriter writer = new();
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        DiagnosticExportViewModel invalid = new(() => [snapshot with { TotalObserved = 0 }], writer,
            _ => Task.FromResult<string?>("C:\\synthetic.jsonl"), new PresentationText());
        await invalid.ExportAsync();
        Assert.Equal(DiagnosticExportStatus.InvalidEvidence, invalid.Result!.Status);
        Assert.Equal(0, writer.Calls);
        await invalid.CloseAsync();
        DiagnosticExportViewModel oversized = new(() => Enumerable.Repeat(snapshot, 9).ToArray(), writer,
            _ => Task.FromResult<string?>("C:\\synthetic.jsonl"), new PresentationText());
        await oversized.ExportAsync();
        Assert.Equal(DiagnosticExportStatus.LimitExceeded, oversized.Result!.Status);
        Assert.Equal(0, writer.Calls);
        await oversized.CloseAsync();
    }

    [Fact]
    public async Task UnsupportedProviderAndUnexpectedFaultsRemainSanitized()
    {
        FakeWriter writer = new();
        DiagnosticExportViewModel unsupported = new(() => [], writer,
            _ => throw new NotSupportedException("PRIVATE provider"), new PresentationText());
        await unsupported.ExportAsync();
        Assert.Equal(DiagnosticExportStatus.InvalidDestination, unsupported.Result!.Status);
        Assert.DoesNotContain("PRIVATE", unsupported.Status);
        await unsupported.CloseAsync();
        DiagnosticExportViewModel failure = new(() => throw new IOException("PRIVATE"), writer,
            _ => Task.FromResult<string?>("C:\\synthetic.jsonl"), new PresentationText());
        await failure.ExportAsync();
        Assert.Equal(DiagnosticExportStatus.Unavailable, failure.Result!.Status);
        Assert.DoesNotContain("PRIVATE", failure.Status);
        await failure.CloseAsync();
    }

    [Fact]
    public async Task ActualSaveCanBeParsedAndDoesNotConsumeRetainedEvidence()
    {
        using WorkspaceTestDirectory directory = new();
        SessionEvidenceSnapshot snapshot = DiagnosticSyntheticEvidence.Create();
        string before = SessionEvidenceFormatter.Format(snapshot);
        string path = Path.Combine(directory.Path, "user selected.jsonl");
        DiagnosticExportViewModel model = new(() => [snapshot], new LocalDiagnosticWriter(),
            _ => Task.FromResult<string?>(path), new PresentationText());
        await model.ExportAsync();
        Assert.Equal(DiagnosticExportStatus.Saved, model.Result!.Status);
        string[] lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(5, lines.Length);

        foreach (string line in lines)
        {
            using JsonDocument parsed = JsonDocument.Parse(line);
            Assert.True(parsed.RootElement.TryGetProperty("Record", out _));
        }
        Assert.Equal(before, SessionEvidenceFormatter.Format(snapshot));
        await model.CloseAsync();
    }

    [Fact]
    public async Task ReentrantCloseUsesReceiptPublishedBeforeObservableCallbacks()
    {
        FakeWriter writer = new();
        DiagnosticExportViewModel model = Create(writer);
        Task? close = null;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsExporting) && model.IsExporting && close is null)
            {
                close = model.CloseAsync();
            }
        };
        await model.ExportAsync();
        Assert.NotNull(close);
        await close;
        Assert.Equal(0, writer.Calls);
        Assert.False(model.IsExporting);
    }

    [AvaloniaFact]
    public void DiagnosticsExposeAnExplicitAccessibleExportWithoutCreatingAnyOutput()
    {
        FakeWriter writer = new();
        int picks = 0;
        DiagnosticsWorkspaceViewModel diagnostics = new(null, new PresentationText());
        diagnostics.AttachExport(writer, _ => { picks++; return Task.FromResult<string?>(null); });
        diagnostics.Refresh();
        Assert.Empty(diagnostics.CaptureSnapshots());
        Window window = new() { Content = new DiagnosticsView { DataContext = diagnostics }, Width = 740, Height = 540 };
        window.Show();
        window.UpdateLayout();
        Button save = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetAutomationId(button) == "diagnostics.export.save");
        Assert.Equal("Save diagnostic snapshot…", AutomationProperties.GetName(save));
        Assert.True(save.IsEnabled);
        Assert.Equal(0, picks + writer.Calls);
        window.Close();
    }

    private static DiagnosticExportViewModel Create(FakeWriter writer) => new(() => [DiagnosticSyntheticEvidence.Create()],
        writer, _ => Task.FromResult<string?>("C:\\synthetic selected destination.jsonl"), new PresentationText());

    private sealed class FakeWriter : ILocalDiagnosticWriter
    {
        public int Calls { get; private set; }
        public string? Path { get; private set; }
        public CancellationToken Token { get; private set; }
        public ReadOnlyMemory<byte> Document { get; private set; }
        public Task<DiagnosticWriteResult>? Response { get; set; }

        public Task<DiagnosticWriteResult> WriteNewAsync(string destinationPath, ReadOnlyMemory<byte> jsonl,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Path = destinationPath;
            Token = cancellationToken;
            Document = jsonl;
            return Response ?? Task.FromResult(new DiagnosticWriteResult(DiagnosticExportStatus.Saved, true, jsonl.Length));
        }
    }
}
