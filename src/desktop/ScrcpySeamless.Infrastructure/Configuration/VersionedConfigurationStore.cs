using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>Result category for a versioned configuration snapshot.</summary>
public enum ConfigurationReadStatus
{
    Missing,
    Loaded,
    Invalid,
}

/// <summary>A validated snapshot and opaque byte revision for compare-and-swap updates.</summary>
public sealed record ConfigurationReadResult(
    ConfigurationReadStatus Status,
    ConfigurationV2? Configuration,
    string? Revision,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>Result category for an atomic compare-and-swap commit.</summary>
public enum ConfigurationCommitStatus
{
    Committed,
    RevisionConflict,
    Busy,
    InvalidConfiguration,
}

/// <summary>A commit result without raw file contents or secrets in diagnostics.</summary>
public sealed record ConfigurationCommitResult(
    ConfigurationCommitStatus Status,
    string? Revision,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>Reads and atomically commits one v2 document with byte-revision checks.</summary>
public sealed class VersionedConfigurationStore
{
    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    private readonly string configurationFile;
    private readonly string backupFile;
    private readonly string mutexName;

    /// <summary>Uses an explicit path so no test or caller reads the owner installation implicitly.</summary>
    public VersionedConfigurationStore(string configurationFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFile);
        this.configurationFile = Path.GetFullPath(configurationFile);
        backupFile = this.configurationFile + ".bak";
        string mutexIdentity = OperatingSystem.IsWindows() ? this.configurationFile.ToUpperInvariant() : this.configurationFile;
        byte[] mutexDigest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(mutexIdentity));
        mutexName = "scrcpy-seamless-config-" + Convert.ToHexString(mutexDigest);
    }

    /// <summary>Reads a validated snapshot without taking the writer mutex.</summary>
    public async Task<ConfigurationReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(configurationFile))
        {
            return new ConfigurationReadResult(ConfigurationReadStatus.Missing, null, null, []);
        }

        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(configurationFile, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new ConfigurationReadResult(ConfigurationReadStatus.Missing, null, null, []);
        }

        string revision = RevisionOf(bytes);

        try
        {
            ConfigurationV2? configuration = JsonSerializer.Deserialize<ConfigurationV2>(bytes, SerializationOptions);
            IReadOnlyList<ValidationIssue> issues = configuration?.Validate()
                ?? [new ValidationIssue(CoreErrorCode.InvalidConfiguration, "$")];

            return issues.Count == 0
                ? new ConfigurationReadResult(ConfigurationReadStatus.Loaded, configuration, revision, [])
                : new ConfigurationReadResult(ConfigurationReadStatus.Invalid, null, revision, issues);
        }
        catch (JsonException)
        {
            return new ConfigurationReadResult(
                ConfigurationReadStatus.Invalid,
                null,
                revision,
                [new ValidationIssue(CoreErrorCode.InvalidConfiguration, "$")]);
        }
    }

    /// <summary>Commits only if the expected byte revision still matches under a short cross-process lock.</summary>
    public ConfigurationCommitResult Commit(
        string? expectedRevision,
        ConfigurationV2 configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ValidationIssue> issues = configuration.Validate();

        if (issues.Count > 0)
        {
            return new ConfigurationCommitResult(ConfigurationCommitStatus.InvalidConfiguration, null, issues);
        }

        byte[] newBytes = JsonSerializer.SerializeToUtf8Bytes(configuration, SerializationOptions);
        string newRevision = RevisionOf(newBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(configurationFile)!);
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
            return new ConfigurationCommitResult(ConfigurationCommitStatus.Busy, null, []);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? currentRevision = File.Exists(configurationFile)
                ? RevisionOf(File.ReadAllBytes(configurationFile))
                : null;

            if (!string.Equals(expectedRevision, currentRevision, StringComparison.Ordinal))
            {
                return new ConfigurationCommitResult(ConfigurationCommitStatus.RevisionConflict, currentRevision, []);
            }

            string temporaryFile = configurationFile + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (FileStream stream = new(temporaryFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(newBytes);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (currentRevision is null)
                {
                    File.Move(temporaryFile, configurationFile);
                }
                else
                {
                    File.Replace(temporaryFile, configurationFile, backupFile);
                }

                return new ConfigurationCommitResult(ConfigurationCommitStatus.Committed, newRevision, []);
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

    /// <summary>Computes an opaque SHA-256 revision over exact persisted bytes.</summary>
    private static string RevisionOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
