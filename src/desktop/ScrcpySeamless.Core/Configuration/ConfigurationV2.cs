using System.Text.Json.Serialization;

namespace ScrcpySeamless.Core.Configuration;

/// <summary>Versioned saved profiles and global mirroring preferences.</summary>
public sealed class ConfigurationV2
{
    public const int CurrentSchemaVersion = 2;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public List<DeviceProfile> Profiles { get; init; } = [];

    [JsonRequired]
    public MirroringPreferences Mirroring { get; init; } = new();

    /// <summary>Reports schema and profile errors before configuration is committed.</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        List<ValidationIssue> issues = [];

        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.UnsupportedConfigurationVersion, nameof(SchemaVersion)));
        }

        if (Profiles is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, nameof(Profiles)));
            return issues;
        }

        HashSet<ProfileId> identifiers = [];

        for (int index = 0; index < Profiles.Count; index++)
        {
            DeviceProfile? profile = Profiles[index];
            string path = $"{nameof(Profiles)}[{index}]";

            if (profile is null)
            {
                issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, path));
                continue;
            }

            issues.AddRange(profile.Validate(path));

            if (profile.Id.Value != Guid.Empty && !identifiers.Add(profile.Id))
            {
                issues.Add(new ValidationIssue(CoreErrorCode.DuplicateProfile, $"{path}.{nameof(DeviceProfile.Id)}"));
            }
        }

        if (Mirroring is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, nameof(Mirroring)));
            return issues;
        }

        issues.AddRange(Mirroring.Validate(nameof(Mirroring)));
        return issues;
    }
}
