using System.Security.Cryptography;
using System.Text;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>Describes an inspected but uncommitted legacy migration.</summary>
public sealed record LegacyMigrationProposal(
    ConfigurationV2 Configuration,
    string? PhoneRevision,
    string? SettingsRevision);

/// <summary>Machine-readable migration outcome without disclosing legacy values.</summary>
public enum LegacyMigrationStatus
{
    Ready,
    NoLegacyData,
    AlreadyV2,
    InvalidV2,
    InvalidLegacy,
    LegacyChanged,
    Busy,
    RevisionConflict,
    Migrated,
}

/// <summary>A preparation or commit result with only safe structured problems.</summary>
public sealed record LegacyMigrationOperation(
    LegacyMigrationStatus Status,
    LegacyMigrationProposal? Proposal,
    IReadOnlyList<LegacyMigrationProblem> Problems);

/// <summary>Stages v1 conversion and commits v2 only after both v1 snapshots are revalidated.</summary>
public sealed class LegacyMigrationCoordinator
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string legacyDirectory;
    private readonly string phoneFile;
    private readonly string settingsFile;
    private readonly string legacyMutexName;
    private readonly VersionedConfigurationStore store;

    /// <summary>Accepts an explicit legacy app directory and a distinct v2 store.</summary>
    public LegacyMigrationCoordinator(string legacyDirectory, VersionedConfigurationStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectory);
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.legacyDirectory = Path.GetFullPath(legacyDirectory);
        phoneFile = Path.Combine(this.legacyDirectory, "phone.json");
        settingsFile = Path.Combine(this.legacyDirectory, "scrcpy-settings.json");
        string normalizedDirectory = this.legacyDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory));
        string scope = OperatingSystem.IsWindows() ? "Local\\" : string.Empty;
        legacyMutexName = scope + "scrcpy-seamless-configuration-" + Convert.ToHexString(digest);
    }

    /// <summary>Reads exact v1 bytes and computes a validated, immutable migration proposal.</summary>
    public async Task<LegacyMigrationOperation> PrepareAsync(CancellationToken cancellationToken)
    {
        ConfigurationReadResult existing = await store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (existing.Status == ConfigurationReadStatus.Loaded)
        {
            return new LegacyMigrationOperation(LegacyMigrationStatus.AlreadyV2, null, []);
        }

        if (existing.Status == ConfigurationReadStatus.Invalid)
        {
            return new LegacyMigrationOperation(LegacyMigrationStatus.InvalidV2, null, []);
        }

        byte[]? phoneBytes = await ReadOptionalAsync(phoneFile, cancellationToken).ConfigureAwait(false);
        byte[]? settingsBytes = await ReadOptionalAsync(settingsFile, cancellationToken).ConfigureAwait(false);

        if (phoneBytes is null && settingsBytes is null)
        {
            return new LegacyMigrationOperation(LegacyMigrationStatus.NoLegacyData, null, []);
        }

        string? phoneJson;
        string? settingsJson;

        try
        {
            phoneJson = phoneBytes is null ? null : StrictUtf8.GetString(phoneBytes);
            settingsJson = settingsBytes is null ? null : StrictUtf8.GetString(settingsBytes);
        }
        catch (DecoderFallbackException)
        {
            return new LegacyMigrationOperation(
                LegacyMigrationStatus.InvalidLegacy,
                null,
                [new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidJson, "legacy", "$")]);
        }

        LegacyMigrationResult migration = LegacyConfigurationMigrator.Migrate(phoneJson, settingsJson);

        if (!migration.Succeeded)
        {
            return new LegacyMigrationOperation(LegacyMigrationStatus.InvalidLegacy, null, migration.Problems);
        }

        LegacyMigrationProposal proposal = new(
            migration.Configuration!,
            RevisionOf(phoneBytes),
            RevisionOf(settingsBytes));

        return new LegacyMigrationOperation(LegacyMigrationStatus.Ready, proposal, []);
    }

    /// <summary>Rechecks both v1 revisions under the legacy writer lock and atomically creates v2.</summary>
    public LegacyMigrationOperation CommitPrepared(LegacyMigrationProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        using Mutex mutex = new(initiallyOwned: false, legacyMutexName);
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
            return new LegacyMigrationOperation(LegacyMigrationStatus.Busy, null, []);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? currentPhone = ReadOptional(phoneFile);
            byte[]? currentSettings = ReadOptional(settingsFile);
            string? currentPhoneRevision = RevisionOf(currentPhone);
            string? currentSettingsRevision = RevisionOf(currentSettings);
            bool phoneChanged = !string.Equals(proposal.PhoneRevision, currentPhoneRevision, StringComparison.Ordinal);
            bool settingsChanged = !string.Equals(proposal.SettingsRevision, currentSettingsRevision, StringComparison.Ordinal);

            if (phoneChanged || settingsChanged)
            {
                return new LegacyMigrationOperation(LegacyMigrationStatus.LegacyChanged, null, []);
            }

            LegacyMigrationResult refreshed;

            try
            {
                refreshed = LegacyConfigurationMigrator.Migrate(
                    currentPhone is null ? null : StrictUtf8.GetString(currentPhone),
                    currentSettings is null ? null : StrictUtf8.GetString(currentSettings));
            }
            catch (DecoderFallbackException)
            {
                return new LegacyMigrationOperation(
                    LegacyMigrationStatus.InvalidLegacy,
                    null,
                    [new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidJson, "legacy", "$")]);
            }

            if (!refreshed.Succeeded)
            {
                return new LegacyMigrationOperation(LegacyMigrationStatus.InvalidLegacy, null, refreshed.Problems);
            }

            ConfigurationCommitResult commit = store.Commit(null, refreshed.Configuration!, cancellationToken);
            LegacyMigrationStatus status = commit.Status switch
            {
                ConfigurationCommitStatus.Committed => LegacyMigrationStatus.Migrated,
                ConfigurationCommitStatus.Busy => LegacyMigrationStatus.Busy,
                _ => LegacyMigrationStatus.RevisionConflict,
            };

            return new LegacyMigrationOperation(status, null, []);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>Runs the two-stage migration; an existing valid v2 file is always authoritative.</summary>
    public async Task<LegacyMigrationOperation> MigrateAsync(CancellationToken cancellationToken)
    {
        LegacyMigrationOperation prepared = await PrepareAsync(cancellationToken).ConfigureAwait(false);

        if (prepared.Proposal is null)
        {
            return prepared;
        }

        return CommitPrepared(prepared.Proposal, cancellationToken);
    }

    /// <summary>Reads a document only when it exists, preserving absence as its own revision.</summary>
    private static async Task<byte[]?> ReadOptionalAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads a document while the legacy writer lock is held.</summary>
    private static byte[]? ReadOptional(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Hashes exact input bytes; a missing file has no revision.</summary>
    private static string? RevisionOf(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
}
