using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>Authority state of the separate Desktop preferences document.</summary>
public enum DesktopPreferencesReadStatus
{
    Missing,
    Loaded,
    Invalid,
    Unsupported,
}

/// <summary>A validated preferences snapshot and its opaque exact-byte revision.</summary>
public sealed record DesktopPreferencesReadResult(
    DesktopPreferencesReadStatus Status,
    DesktopPreferences? Preferences,
    string? Revision,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>Outcome of an atomic preferences compare-and-swap.</summary>
public enum DesktopPreferencesCommitStatus
{
    Committed,
    Unchanged,
    RevisionConflict,
    Busy,
    InvalidPreferences,
    InvalidExistingDocument,
}

/// <summary>Save result without raw preferences or filesystem contents in diagnostics.</summary>
public sealed record DesktopPreferencesCommitResult(
    DesktopPreferencesCommitStatus Status,
    string? Revision,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>Persists one versioned Desktop preferences document in an explicitly selected root.</summary>
public sealed class VersionedDesktopPreferencesStore
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private readonly string preferencesFile;
    private readonly string backupFile;
    private readonly string mutexName;

    /// <summary>Shows callers the explicitly selected path for actionable read/write diagnostics.</summary>
    public string PreferencesFile => preferencesFile;

    /// <summary>Constructs the adapter without reading or creating the selected path.</summary>
    public VersionedDesktopPreferencesStore(string preferencesFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencesFile);
        this.preferencesFile = Path.GetFullPath(preferencesFile);
        backupFile = this.preferencesFile + ".bak";
        string identity = OperatingSystem.IsWindows() ? this.preferencesFile.ToUpperInvariant() : this.preferencesFile;
        byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
        mutexName = "scrcpy-seamless-preferences-" + Convert.ToHexString(digest);
    }

    /// <summary>Reads a snapshot; absent preferences remain in-memory defaults until explicitly saved.</summary>
    public async Task<DesktopPreferencesReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(preferencesFile, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new DesktopPreferencesReadResult(DesktopPreferencesReadStatus.Missing, new DesktopPreferences(), null, []);
        }
        catch (DirectoryNotFoundException)
        {
            return new DesktopPreferencesReadResult(DesktopPreferencesReadStatus.Missing, new DesktopPreferences(), null, []);
        }

        return Parse(bytes);
    }

    /// <summary>Commits only against the expected revision without overwriting an invalid existing document.</summary>
    public DesktopPreferencesCommitResult Commit(
        string? expectedRevision,
        DesktopPreferences preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ValidationIssue> issues = preferences.Validate();

        if (issues.Count > 0)
        {
            return new DesktopPreferencesCommitResult(DesktopPreferencesCommitStatus.InvalidPreferences, null, issues);
        }

        byte[] newBytes = JsonSerializer.SerializeToUtf8Bytes(preferences, SerializationOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesFile)!);
        using Mutex mutex = new(initiallyOwned: false, mutexName);
        bool acquired;

        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            return new DesktopPreferencesCommitResult(DesktopPreferencesCommitStatus.Busy, null, []);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? currentBytes;

            try
            {
                currentBytes = File.ReadAllBytes(preferencesFile);
            }
            catch (FileNotFoundException)
            {
                currentBytes = null;
            }

            string? currentRevision = currentBytes is null ? null : RevisionOf(currentBytes);

            if (!string.Equals(expectedRevision, currentRevision, StringComparison.Ordinal))
            {
                return new DesktopPreferencesCommitResult(DesktopPreferencesCommitStatus.RevisionConflict, currentRevision, []);
            }

            if (currentBytes is not null)
            {
                DesktopPreferencesReadResult existing = Parse(currentBytes);

                if (existing.Status != DesktopPreferencesReadStatus.Loaded)
                {
                    return new DesktopPreferencesCommitResult(
                        DesktopPreferencesCommitStatus.InvalidExistingDocument, currentRevision, existing.Issues);
                }

                byte[] canonicalCurrentBytes = JsonSerializer.SerializeToUtf8Bytes(existing.Preferences, SerializationOptions);

                if (canonicalCurrentBytes.AsSpan().SequenceEqual(newBytes))
                {
                    return new DesktopPreferencesCommitResult(DesktopPreferencesCommitStatus.Unchanged, currentRevision, []);
                }
            }

            string temporaryFile = preferencesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (FileStream stream = new(temporaryFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(newBytes);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (currentBytes is null)
                {
                    File.Move(temporaryFile, preferencesFile);
                }
                else
                {
                    File.Replace(temporaryFile, preferencesFile, backupFile);
                }

                return new DesktopPreferencesCommitResult(
                    DesktopPreferencesCommitStatus.Committed, RevisionOf(newBytes), []);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>Decodes one exact-byte snapshot without mutating it.</summary>
    private static DesktopPreferencesReadResult Parse(byte[] bytes)
    {
        string revision = RevisionOf(bytes);

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(nameof(DesktopPreferences.SchemaVersion), out JsonElement version) &&
                version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int schemaVersion) &&
                schemaVersion != DesktopPreferences.CurrentSchemaVersion)
            {
                return new DesktopPreferencesReadResult(
                    DesktopPreferencesReadStatus.Unsupported, null, revision,
                    [new ValidationIssue(CoreErrorCode.UnsupportedConfigurationVersion,
                        nameof(DesktopPreferences.SchemaVersion))]);
            }

            DesktopPreferences? preferences = JsonSerializer.Deserialize<DesktopPreferences>(bytes, SerializationOptions);
            IReadOnlyList<ValidationIssue> issues = preferences?.Validate()
                ?? [new ValidationIssue(CoreErrorCode.InvalidConfiguration, "$")];
            bool unsupported = issues.Any(issue => issue.Code == CoreErrorCode.UnsupportedConfigurationVersion);

            return issues.Count == 0
                ? new DesktopPreferencesReadResult(DesktopPreferencesReadStatus.Loaded, preferences, revision, [])
                : new DesktopPreferencesReadResult(
                    unsupported ? DesktopPreferencesReadStatus.Unsupported : DesktopPreferencesReadStatus.Invalid,
                    null, revision, issues);
        }
        catch (JsonException)
        {
            return new DesktopPreferencesReadResult(
                DesktopPreferencesReadStatus.Invalid, null, revision,
                [new ValidationIssue(CoreErrorCode.InvalidConfiguration, "$")]);
        }
    }

    /// <summary>Computes an opaque SHA-256 revision from the persisted bytes.</summary>
    private static string RevisionOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
