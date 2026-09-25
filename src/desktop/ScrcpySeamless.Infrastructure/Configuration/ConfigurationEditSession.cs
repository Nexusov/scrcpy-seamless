using System.Security;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Infrastructure.Configuration;

/// <summary>State returned when loading the canonical v2 configuration.</summary>
public enum ConfigurationSessionLoadStatus { Ready, Missing, Invalid, Inaccessible, Busy }

/// <summary>State returned when applying a detached configuration draft.</summary>
public enum ConfigurationSessionApplyStatus { Applied, NoChanges, Invalid, RevisionConflict, Busy, IoError, NotLoaded }

/// <summary>A load outcome that never substitutes empty configuration for unreadable state.</summary>
public sealed record ConfigurationSessionLoadResult(
    ConfigurationSessionLoadStatus Status,
    IReadOnlyList<ValidationIssue> Issues,
    string? ErrorKind = null);

/// <summary>An apply outcome with separate structural, semantic, revision, and I/O failures.</summary>
public sealed record ConfigurationSessionApplyResult(
    ConfigurationSessionApplyStatus Status,
    string? Revision,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<OptionDiagnostic> OptionDiagnostics,
    string? ErrorKind = null);

/// <summary>Owns one detached draft and an exact byte-revision baseline for local configuration editing.</summary>
public sealed class ConfigurationEditSession
{
    private readonly VersionedConfigurationStore store;
    private readonly object gate = new();
    private ConfigurationV2? baseline;
    private ConfigurationV2? draft;
    private string? revision;
    private bool busy;
    private bool authorityInvalidated;

    /// <summary>Creates a session without reading or writing the selected data root.</summary>
    public ConfigurationEditSession(VersionedConfigurationStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ConfigurationV2? Baseline { get { lock (gate) { return baseline is null ? null : Clone(baseline); } } }
    public ConfigurationV2? Draft { get { lock (gate) { return draft is null ? null : Clone(draft); } } }
    public string? Revision { get { lock (gate) { return revision; } } }
    public bool IsBusy { get { lock (gate) { return busy; } } }
    public bool RequiresReload { get { lock (gate) { return authorityInvalidated; } } }
    public bool IsDirty { get { lock (gate) { return baseline is not null && draft is not null && !Equivalent(baseline, draft); } } }

    /// <summary>Loads valid v2 state or an in-memory empty draft without creating a file.</summary>
    public async Task<ConfigurationSessionLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return new ConfigurationSessionLoadResult(ConfigurationSessionLoadStatus.Busy, []);
        }

        try
        {
            ConfigurationReadResult read = await store.ReadAsync(cancellationToken).ConfigureAwait(false);

            if (read.Status == ConfigurationReadStatus.Invalid)
            {
                InvalidateAuthority();
                return new ConfigurationSessionLoadResult(ConfigurationSessionLoadStatus.Invalid, read.Issues);
            }

            ConfigurationV2 loaded = read.Status == ConfigurationReadStatus.Loaded
                ? Clone(read.Configuration!)
                : new ConfigurationV2();

            lock (gate)
            {
                baseline = loaded;
                draft = Clone(loaded);
                revision = read.Revision;
                authorityInvalidated = false;
            }

            ConfigurationSessionLoadStatus status = read.Status == ConfigurationReadStatus.Loaded
                ? ConfigurationSessionLoadStatus.Ready
                : ConfigurationSessionLoadStatus.Missing;
            return new ConfigurationSessionLoadResult(status, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            InvalidateAuthority();
            return new ConfigurationSessionLoadResult(ConfigurationSessionLoadStatus.Inaccessible, [], exception.GetType().Name);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Replaces only the detached draft; callers cannot mutate the owned snapshot afterward.</summary>
    public void ReplaceDraft(ConfigurationV2 candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        lock (gate)
        {
            EnsureEditable();
            draft = Clone(candidate);
        }
    }

    /// <summary>Discards unsaved edits and restores the last loaded or successfully applied baseline.</summary>
    public void CancelChanges()
    {
        lock (gate)
        {
            EnsureEditable();
            draft = Clone(baseline!);
        }
    }

    /// <summary>Removes one override from the draft without changing any other option.</summary>
    public void ResetOption(string optionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);

        lock (gate)
        {
            EnsureEditable();
            draft!.Mirroring.Options.Remove(optionId);
        }
    }

    /// <summary>Updates one raw override while leaving all unknown and unrelated values intact.</summary>
    public void SetOption(string optionId, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);

        lock (gate)
        {
            EnsureEditable();
            draft!.Mirroring.Options[optionId] = value.Clone();
        }
    }

    /// <summary>Updates only the global reconnect preference.</summary>
    public void SetReconnect(bool reconnect)
    {
        lock (gate)
        {
            EnsureEditable();
            draft = new ConfigurationV2
            {
                SchemaVersion = draft!.SchemaVersion,
                Profiles = draft.Profiles.Select(Clone).ToList(),
                Mirroring = new MirroringPreferences
                {
                    Reconnect = reconnect,
                    Options = Clone(draft.Mirroring).Options,
                },
            };
        }
    }

    /// <summary>Updates a profile by stable identity without changing another profile.</summary>
    public void UpsertProfile(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (gate)
        {
            EnsureEditable();
            int index = draft!.Profiles.FindIndex(existing => existing.Id == profile.Id);

            if (index < 0)
            {
                draft.Profiles.Add(Clone(profile));
                return;
            }

            draft.Profiles[index] = Clone(profile);
        }
    }

    /// <summary>Removes a saved profile by stable identity as an unsaved edit.</summary>
    public bool DeleteProfile(ProfileId profileId)
    {
        lock (gate)
        {
            EnsureEditable();
            return draft!.Profiles.RemoveAll(profile => profile.Id == profileId) > 0;
        }
    }

    /// <summary>Changes global mirroring preferences without touching saved profiles.</summary>
    public void SetMirroring(MirroringPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        lock (gate)
        {
            EnsureEditable();
            draft = new ConfigurationV2
            {
                SchemaVersion = draft!.SchemaVersion,
                Profiles = draft.Profiles.Select(Clone).ToList(),
                Mirroring = Clone(preferences),
            };
        }
    }

    /// <summary>Validates and atomically applies an immutable candidate on a worker thread.</summary>
    public async Task<ConfigurationSessionApplyResult> ApplyAsync(CancellationToken cancellationToken)
    {
        if (!TryBegin())
        {
            return Outcome(ConfigurationSessionApplyStatus.Busy);
        }

        try
        {
            ConfigurationV2 candidate;
            ConfigurationV2 previous;
            string? expectedRevision;

            lock (gate)
            {
                if (draft is null || baseline is null || authorityInvalidated)
                {
                    return Outcome(ConfigurationSessionApplyStatus.NotLoaded);
                }

                candidate = Clone(draft);
                previous = Clone(baseline);
                expectedRevision = revision;
            }

            if (Equivalent(previous, candidate))
            {
                return Outcome(ConfigurationSessionApplyStatus.NoChanges, expectedRevision);
            }

            IReadOnlyList<ValidationIssue> structuralIssues = candidate.Validate();

            if (structuralIssues.Count != 0)
            {
                return Outcome(ConfigurationSessionApplyStatus.Invalid, expectedRevision, structuralIssues);
            }

            IReadOnlyList<OptionDiagnostic> newOptionIssues = NewOptionDiagnostics(previous.Mirroring, candidate.Mirroring);

            if (newOptionIssues.Count != 0)
            {
                return Outcome(ConfigurationSessionApplyStatus.Invalid, expectedRevision, optionIssues: newOptionIssues);
            }

            ConfigurationCommitResult committed = await Task.Run(
                () => store.Commit(expectedRevision, candidate, cancellationToken), cancellationToken).ConfigureAwait(false);

            if (committed.Status == ConfigurationCommitStatus.Committed)
            {
                lock (gate)
                {
                    baseline = Clone(candidate);
                    draft = Clone(candidate);
                    revision = committed.Revision;
                }

                return Outcome(ConfigurationSessionApplyStatus.Applied, committed.Revision);
            }

            ConfigurationSessionApplyStatus status = committed.Status switch
            {
                ConfigurationCommitStatus.RevisionConflict => ConfigurationSessionApplyStatus.RevisionConflict,
                ConfigurationCommitStatus.Busy => ConfigurationSessionApplyStatus.Busy,
                ConfigurationCommitStatus.InvalidConfiguration => ConfigurationSessionApplyStatus.Invalid,
                _ => throw new InvalidOperationException("Unknown configuration commit status."),
            };
            return Outcome(status, committed.Revision, committed.Issues);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Outcome(ConfigurationSessionApplyStatus.IoError, errorKind: exception.GetType().Name);
        }
        finally
        {
            End();
        }
    }

    /// <summary>Compares new option problems without making historical unknown values a write barrier.</summary>
    private static IReadOnlyList<OptionDiagnostic> NewOptionDiagnostics(MirroringPreferences before, MirroringPreferences after)
    {
        HashSet<OptionDiagnostic> existing = OptionSelectionValidator.Evaluate(before).Diagnostics.ToHashSet();
        HashSet<string> changedOptions = before.Options.Keys.Concat(after.Options.Keys)
            .Where(optionId => !before.Options.TryGetValue(optionId, out JsonElement previous) ||
                !after.Options.TryGetValue(optionId, out JsonElement current) ||
                !JsonElement.DeepEquals(previous, current))
            .ToHashSet(StringComparer.Ordinal);
        return OptionSelectionValidator.Evaluate(after).Diagnostics
            .Where(issue => changedOptions.Contains(issue.OptionId) || !existing.Contains(issue))
            .ToArray();
    }

    /// <summary>Clones all mutable configuration members, including the ownership of JSON option values.</summary>
    private static ConfigurationV2 Clone(ConfigurationV2 source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Profiles = source.Profiles?.Select(Clone).ToList()!,
        Mirroring = source.Mirroring is null ? null! : Clone(source.Mirroring),
    };

    /// <summary>Clones one saved profile's mutable connection preferences.</summary>
    private static DeviceProfile Clone(DeviceProfile? profile) => profile is null ? null! : new DeviceProfile
    {
        Id = profile.Id,
        DeviceIdentity = profile.DeviceIdentity,
        UsbIdentity = profile.UsbIdentity,
        MdnsIdentity = profile.MdnsIdentity,
        PairingEndpoint = profile.PairingEndpoint,
        ConnectionEndpoint = profile.ConnectionEndpoint,
        Alias = profile.Alias,
        Connection = profile.Connection is null ? null! : new ConnectionPreferences
        {
            PreferredTransport = profile.Connection.PreferredTransport,
            AllowFallback = profile.Connection.AllowFallback,
        },
    };

    /// <summary>Clones global preferences while preserving arbitrary unknown option values and raw spelling.</summary>
    private static MirroringPreferences Clone(MirroringPreferences source) => new()
    {
        Reconnect = source.Reconnect,
        Options = source.Options?.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ValueKind == JsonValueKind.Undefined ? default : entry.Value.Clone(),
            StringComparer.Ordinal)!,
    };

    /// <summary>Checks semantic equality without treating JSON dictionary insertion order as an edit.</summary>
    private static bool Equivalent(ConfigurationV2 first, ConfigurationV2 second)
    {
        if (first.SchemaVersion != second.SchemaVersion || first.Profiles is null || second.Profiles is null ||
            first.Mirroring is null || second.Mirroring is null || first.Profiles.Count != second.Profiles.Count ||
            first.Mirroring.Reconnect != second.Mirroring.Reconnect)
        {
            return false;
        }

        for (int index = 0; index < first.Profiles.Count; index++)
        {
            if (!Equivalent(first.Profiles[index], second.Profiles[index]))
            {
                return false;
            }
        }

        Dictionary<string, JsonElement>? firstOptions = first.Mirroring.Options;
        Dictionary<string, JsonElement>? secondOptions = second.Mirroring.Options;

        if (firstOptions is null || secondOptions is null || firstOptions.Count != secondOptions.Count)
        {
            return false;
        }

        return firstOptions.All(entry => secondOptions.TryGetValue(entry.Key, out JsonElement value) &&
            JsonElement.DeepEquals(entry.Value, value));
    }

    /// <summary>Compares every saved profile field, including its transport preference.</summary>
    private static bool Equivalent(DeviceProfile? first, DeviceProfile? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return first.Id == second.Id && first.DeviceIdentity == second.DeviceIdentity &&
            first.UsbIdentity == second.UsbIdentity && first.MdnsIdentity == second.MdnsIdentity &&
            first.PairingEndpoint == second.PairingEndpoint && first.ConnectionEndpoint == second.ConnectionEndpoint &&
            first.Alias == second.Alias && first.Connection?.PreferredTransport == second.Connection?.PreferredTransport &&
            first.Connection?.AllowFallback == second.Connection?.AllowFallback;
    }

    /// <summary>Reserves exclusive edit-session ownership before asynchronous work begins.</summary>
    private bool TryBegin()
    {
        lock (gate)
        {
            if (busy)
            {
                return false;
            }

            busy = true;
            return true;
        }
    }

    /// <summary>Releases edit-session ownership after asynchronous work.</summary>
    private void End()
    {
        lock (gate)
        {
            busy = false;
        }
    }

    /// <summary>Rejects edits during save or before the first successful load.</summary>
    private void EnsureEditable()
    {
        if (busy || authorityInvalidated || draft is null || baseline is null)
        {
            throw new InvalidOperationException("Configuration is not editable during loading or saving.");
        }
    }

    /// <summary>Preserves a previous draft for inspection but blocks writes after an unreadable reload.</summary>
    private void InvalidateAuthority()
    {
        lock (gate)
        {
            authorityInvalidated = true;
        }
    }

    /// <summary>Constructs an apply result without exposing file contents or sensitive exception messages.</summary>
    private static ConfigurationSessionApplyResult Outcome(
        ConfigurationSessionApplyStatus status,
        string? revision = null,
        IReadOnlyList<ValidationIssue>? issues = null,
        IReadOnlyList<OptionDiagnostic>? optionIssues = null,
        string? errorKind = null) =>
        new(status, revision, issues ?? [], optionIssues ?? [], errorKind);
}
