using System.Security;
using ScrcpySeamless.Core.Application.Diagnostics;

namespace ScrcpySeamless.Infrastructure.Diagnostics;

/// <summary>Atomic CreateNew admission; failures never delete or replace a path that another owner could reuse.</summary>
public sealed class LocalDiagnosticWriter : ILocalDiagnosticWriter
{
    private const int BufferSize = 4096;
    private readonly Func<string, Stream> createNew;

    public LocalDiagnosticWriter() : this(path => new FileStream(path, FileMode.CreateNew,
        FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
    {
    }

    internal LocalDiagnosticWriter(Func<string, Stream> createNew)
    {
        this.createNew = createNew;
    }

    public async Task<DiagnosticWriteResult> WriteNewAsync(string destinationPath,
        ReadOnlyMemory<byte> jsonl, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(DiagnosticExportStatus.Cancelled);
        }

        if (jsonl.Length is 0 or > DiagnosticExportLimits.MaximumBytes)
        {
            return new(jsonl.Length == 0 ? DiagnosticExportStatus.InvalidEvidence : DiagnosticExportStatus.LimitExceeded);
        }
        bool created = false;

        try
        {
            if (string.IsNullOrWhiteSpace(destinationPath) || !Path.IsPathFullyQualified(destinationPath) ||
                destinationPath.IndexOf('\0') >= 0 || Path.GetFileName(destinationPath).Length == 0)
            {
                return new(DiagnosticExportStatus.InvalidDestination);
            }
            string path = Path.GetFullPath(destinationPath);

            if (OperatingSystem.IsWindows() &&
                (!IsRegularWindowsDestination(destinationPath) || !IsRegularWindowsDestination(path)))
            {
                return new(DiagnosticExportStatus.InvalidDestination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await using (Stream stream = createNew(path))
            {
                created = true;
                await stream.WriteAsync(jsonl, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            return new(DiagnosticExportStatus.Saved, true, jsonl.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(DiagnosticExportStatus.Cancelled, created, PartialOutputRetained: created);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            return new(DiagnosticExportStatus.AccessDenied, created, PartialOutputRetained: created);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(DiagnosticExportStatus.InvalidDestination, created, PartialOutputRetained: created);
        }
        catch (IOException exception)
        {
            bool exists = !created && (exception.HResult & ushort.MaxValue) is 80 or 183;
            return new(exists ? DiagnosticExportStatus.AlreadyExists : DiagnosticExportStatus.IoFailed,
                created, PartialOutputRetained: created);
        }
    }

    private static bool IsRegularWindowsDestination(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            // Non-local providers and Win32 device namespaces cannot prove CreateNew file semantics here.
            return false;
        }
        string relative = path[Path.GetPathRoot(path)!.Length..];

        foreach (string component in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            // Colons here are stream selectors, never the already removed drive separator.
            if (component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                component.EndsWith(' ') || component.EndsWith('.'))
            {
                return false;
            }
            string device = component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            bool serialDevice = device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) ||
                device.StartsWith("LPT", StringComparison.Ordinal)) && char.IsNumber(device[3]);

            if (serialDevice || device is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            {
                return false;
            }
        }

        return true;
    }
}
