using System.Text.Json;

namespace ScrcpySeamless.Core.Configuration;

/// <summary>Global mirroring settings; option metadata is owned by Phase 4.</summary>
public sealed class MirroringPreferences
{
    private const int MaximumOptionNameLength = 128;

    public bool Reconnect { get; init; } = true;
    public Dictionary<string, JsonElement> Options { get; init; } = [];

    /// <summary>Accepts legacy switch and text values while preserving unknown option names.</summary>
    public IReadOnlyList<ValidationIssue> Validate(string path)
    {
        List<ValidationIssue> issues = [];

        if (Options is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(Options)}"));
            return issues;
        }

        foreach ((string name, JsonElement value) in Options)
        {
            string optionPath = $"{path}.{nameof(Options)}";

            if (name.Length is < 1 or > MaximumOptionNameLength || name.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            {
                issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, optionPath));
                continue;
            }

            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.String) ||
                (value.ValueKind == JsonValueKind.String && value.GetString()!.Any(char.IsControl)))
            {
                issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, optionPath));
            }
        }

        return issues;
    }
}
