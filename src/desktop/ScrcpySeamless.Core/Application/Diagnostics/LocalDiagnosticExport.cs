namespace ScrcpySeamless.Core.Application.Diagnostics;

public static class DiagnosticExportLimits
{
    public const int MaximumBytes = 512 * 1024;
}

public enum DiagnosticExportStatus
{
    Saved,
    Cancelled,
    InvalidDestination,
    AlreadyExists,
    AccessDenied,
    InvalidEvidence,
    LimitExceeded,
    IoFailed,
    Unavailable,
}

/// <summary>No path or exception text leaves the writer; a failed new file may need manual removal.</summary>
public sealed record DiagnosticWriteResult(DiagnosticExportStatus Status,
    bool OutputCreated = false, int? BytesWritten = null, bool PartialOutputRetained = false);

/// <summary>Creates only a new user-selected local file. It never replaces an existing destination.</summary>
public interface ILocalDiagnosticWriter
{
    Task<DiagnosticWriteResult> WriteNewAsync(string destinationPath, ReadOnlyMemory<byte> jsonl,
        CancellationToken cancellationToken = default);
}
