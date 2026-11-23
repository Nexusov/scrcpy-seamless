using System.Text;
using ScrcpySeamless.Core.Application.Diagnostics;
using ScrcpySeamless.Infrastructure.Diagnostics;
using ScrcpySeamless.Infrastructure.Tests.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Diagnostics;

public sealed class LocalDiagnosticWriterTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("{\"Record\":\"synthetic\"}\n");

    [Fact]
    public async Task CreatesOneNewFileAndNeverReplacesIt()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "diagnostic with spaces.jsonl");
        LocalDiagnosticWriter writer = new();
        DiagnosticWriteResult first = await writer.WriteNewAsync(path, Content, TestContext.Current.CancellationToken);
        Assert.Equal(new(DiagnosticExportStatus.Saved, true, Content.Length), first);
        byte[] retained = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        DiagnosticWriteResult second = await writer.WriteNewAsync(path, "different"u8.ToArray(), TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.AlreadyExists, second.Status);
        Assert.False(second.OutputCreated);
        Assert.Equal(retained, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentCreateNewHasExactlyOneWinner()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "one.jsonl");
        LocalDiagnosticWriter writer = new();
        DiagnosticWriteResult[] results = await Task.WhenAll(
            writer.WriteNewAsync(path, Content, TestContext.Current.CancellationToken),
            writer.WriteNewAsync(path, Content, TestContext.Current.CancellationToken));
        Assert.Single(results, result => result.Status == DiagnosticExportStatus.Saved);
        Assert.Single(results, result => result.Status == DiagnosticExportStatus.AlreadyExists);
        Assert.Equal(Content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InputBoundsAndPreCancellationDoNotOpenADestination()
    {
        int opens = 0;
        LocalDiagnosticWriter writer = new(_ => { opens++; return new MemoryStream(); });
        string path = Path.Combine(Path.GetTempPath(), "not-created.jsonl");
        Assert.Equal(DiagnosticExportStatus.InvalidEvidence,
            (await writer.WriteNewAsync(path, ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(DiagnosticExportStatus.LimitExceeded,
            (await writer.WriteNewAsync(path, new byte[DiagnosticExportLimits.MaximumBytes + 1], TestContext.Current.CancellationToken)).Status);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Equal(DiagnosticExportStatus.Cancelled, (await writer.WriteNewAsync(path, Content, cancellation.Token)).Status);
        Assert.Equal(0, opens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.jsonl")]
    [InlineData("\0invalid")]
    public async Task RejectsInvalidDestinationBeforeOpening(string path)
    {
        int opens = 0;
        LocalDiagnosticWriter writer = new(_ => { opens++; return new MemoryStream(); });
        Assert.Equal(DiagnosticExportStatus.InvalidDestination,
            (await writer.WriteNewAsync(path, Content, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, opens);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("dispose")]
    public async Task FailureRetainsTypedPartialOutcomeAndSettlesTheOwnedStream(string stage)
    {
        FaultStream stream = new(stage);
        LocalDiagnosticWriter writer = new(_ => stream);
        DiagnosticWriteResult result = await writer.WriteNewAsync(
            Path.Combine(Path.GetTempPath(), "synthetic-only.jsonl"), Content, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.IoFailed, result.Status);
        Assert.True(result.OutputCreated);
        Assert.True(result.PartialOutputRetained);
        Assert.Null(result.BytesWritten);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task MidWriteCancellationIsSettledBeforeReturningPartialReceipt()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingStream stream = new(entered);
        LocalDiagnosticWriter writer = new(_ => stream);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<DiagnosticWriteResult> pending = writer.WriteNewAsync(
            Path.Combine(Path.GetTempPath(), "synthetic-only.jsonl"), Content, cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        DiagnosticWriteResult result = await pending;
        Assert.Equal(DiagnosticExportStatus.Cancelled, result.Status);
        Assert.True(result.PartialOutputRetained);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task AccessAndMissingDirectoryFailuresRemainTypedWithoutPathOrExceptionData()
    {
        using TemporaryDirectory directory = new();
        LocalDiagnosticWriter unavailable = new(_ => throw new UnauthorizedAccessException("PRIVATE"));
        DiagnosticWriteResult denied = await unavailable.WriteNewAsync(
            Path.Combine(directory.Path, "denied.jsonl"), Content, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.AccessDenied, denied.Status);
        Assert.False(denied.OutputCreated);
        DiagnosticWriteResult missing = await new LocalDiagnosticWriter().WriteNewAsync(
            Path.Combine(directory.Path, "absent", "file.jsonl"), Content, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.IoFailed, missing.Status);
        Assert.False(missing.OutputCreated);
        Assert.DoesNotContain("PRIVATE", denied.ToString());
    }

    [Fact]
    public async Task ExactMaximumBytesCanBeSaved()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "bounded.jsonl");
        DiagnosticWriteResult result = await new LocalDiagnosticWriter().WriteNewAsync(
            path, new byte[DiagnosticExportLimits.MaximumBytes], TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.Saved, result.Status);
        Assert.Equal(DiagnosticExportLimits.MaximumBytes, new FileInfo(path).Length);
    }

    [Fact]
    public async Task RejectsAlternateStreamDestinationWithoutModifyingTheExistingFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using TemporaryDirectory directory = new();
        string existing = Path.Combine(directory.Path, "existing.bin");
        byte[] sentinel = "owned synthetic existing data"u8.ToArray();
        await File.WriteAllBytesAsync(existing, sentinel, TestContext.Current.CancellationToken);
        DiagnosticWriteResult result = await new LocalDiagnosticWriter().WriteNewAsync(
            existing + ":diagnostics.jsonl", Content, TestContext.Current.CancellationToken);
        Console.WriteLine($"alternate-stream admission: {result.Status}, created={result.OutputCreated}");
        Assert.Equal(sentinel, await File.ReadAllBytesAsync(existing, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticExportStatus.InvalidDestination, result.Status);
        Assert.False(result.OutputCreated);
    }

    [Theory]
    [InlineData("existing.bin:diagnostic.jsonl")]
    [InlineData("component:stream/file.jsonl")]
    [InlineData("NUL.jsonl")]
    [InlineData("COM¹.jsonl")]
    [InlineData("trailing.jsonl.")]
    public async Task WindowsNonRegularFileSelectorsNeverReachTheOpenBoundary(string suffix)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        int opens = 0;
        LocalDiagnosticWriter writer = new(_ => { opens++; return new MemoryStream(); });
        string path = Path.Combine(Path.GetTempPath(), suffix);
        DiagnosticWriteResult result = await writer.WriteNewAsync(path, Content, TestContext.Current.CancellationToken);
        Assert.Equal(DiagnosticExportStatus.InvalidDestination, result.Status);
        Assert.Equal(0, opens);
    }

    private sealed class FaultStream(string stage) : MemoryStream
    {
        public bool Disposed { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stage == "write")
            {
                base.Write(buffer.Span[..1]);
                throw new IOException("PRIVATE write error");
            }

            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (stage == "flush")
            {
                throw new IOException("PRIVATE flush error");
            }

            return base.FlushAsync(cancellationToken);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            base.Dispose();

            if (stage == "dispose")
            {
                throw new IOException("PRIVATE disposal error");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingStream(TaskCompletionSource entered) : MemoryStream
    {
        public bool Disposed { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            base.Write(buffer.Span[..1]);
            entered.TrySetResult();
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }
}
