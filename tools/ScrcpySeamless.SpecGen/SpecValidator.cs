using System.Globalization;
using System.Text.RegularExpressions;

namespace ScrcpySeamless.SpecGen;

/// <summary>Rejects incomplete or contradictory static metadata before emission.</summary>
internal static class SpecValidator
{
    internal static readonly HashSet<string> Classifications =
        ["editable", "managed", "action", "unsupported", "alias", "nativeOnly"];
    internal static readonly HashSet<string> ArgumentShapes = ["none", "required", "optional"];
    internal static readonly HashSet<string> ValueKinds =
        ["boolean", "string", "integer", "unsignedInteger", "decimal", "enum", "bitrate", "size", "duration", "color", "custom"];
    internal static readonly HashSet<string> Categories =
        ["Audio", "Camera", "Control", "Device", "Recording", "Technical", "Video", "Window", "Input & Control"];
    private static readonly HashSet<string> KnownRuleIds =
        ["audio-dup-requires-playback", "camera-source-rules", "recording-output-semantics", "virtual-display-rules", "session-output-required"];

    public static void Validate(OptionSpecification specification)
    {
        Require(specification.SpecVersion == 1, "Unsupported option spec version.");
        Require(specification.Options.Count > 0, "The option list is empty.");
        Require(!string.IsNullOrWhiteSpace(specification.Upstream.Commit), "Upstream commit is missing.");

        var byId = new HashSet<string>(StringComparer.Ordinal);
        var byLong = new HashSet<string>(StringComparer.Ordinal);
        var byShort = new HashSet<char>();
        var byNativeId = new HashSet<string>(StringComparer.Ordinal);
        var byNumericId = new HashSet<int>();

        foreach (var option in specification.Options)
        {
            Require(Regex.IsMatch(option.Id, "^[a-z][a-z0-9-]*$"), $"Invalid option ID: {option.Id}");
            Require(byId.Add(option.Id), $"Duplicate option ID: {option.Id}");
            Require(Classifications.Contains(option.Classification), $"Invalid classification: {option.Id}");
            Require(ArgumentShapes.Contains(option.ArgumentShape), $"Invalid argument shape: {option.Id}");
            Require(ValueKinds.Contains(option.ValueKind), $"Invalid value kind: {option.Id}");
            Require(Categories.Contains(option.Category), $"Invalid category: {option.Id}");
            Require(!string.IsNullOrEmpty(option.NativeHelp), $"Missing native help: {option.Id}");
            Require(option.ArgumentShape == "none" ? option.ArgumentHint is null : !string.IsNullOrEmpty(option.ArgumentHint),
                $"Argument hint/shape mismatch: {option.Id}");
            Require(option.ValueKind != "boolean" || option.ArgumentShape == "none" || option.Classification == "alias",
                $"Boolean option takes an argument: {option.Id}");

            if (option.LongName is { } longName)
            {
                Require(longName == option.Id, $"Stable ID must equal the long CLI name: {option.Id}");
                Require(byLong.Add(longName), $"Duplicate long CLI name: {longName}");
            }

            if (option.ShortName is { } shortName)
            {
                Require(shortName.Length == 1 && byShort.Add(shortName[0]), $"Duplicate/invalid short alias: {option.Id}");
            }

            Require(option.NativeId is not null ^ option.ShortName is not null, $"Native ID/short alias mismatch: {option.Id}");
            Require(option.NativeId is not null == option.NativeNumericId.HasValue, $"Native numeric ID mismatch: {option.Id}");
            if (option.NativeId is { } nativeId)
            {
                Require(Regex.IsMatch(nativeId, "^OPT_[A-Z0-9_]+$") && byNativeId.Add(nativeId), $"Duplicate/invalid native ID: {option.Id}");
                Require(byNumericId.Add(option.NativeNumericId!.Value), $"Duplicate native numeric ID: {option.Id}");
            }

            Require(option.LongName is not null || option.Classification == "alias", $"Short-only entry must be an alias: {option.Id}");
            Require(option.Classification != "alias" || option.AliasFor is not null, $"Missing alias target: {option.Id}");
            Require(option.Classification == "alias" || option.AliasFor is null, $"Unexpected alias target: {option.Id}");
            Require(option.Classification == "alias" || !string.IsNullOrWhiteSpace(option.Label), $"Missing label: {option.Id}");
            Require(option.ValueKind != "enum" || option.EnumValues.Count > 0, $"Enum has no values: {option.Id}");
            Require(option.EnumValues.Distinct(StringComparer.Ordinal).Count() == option.EnumValues.Count, $"Duplicate enum value: {option.Id}");
            if (!string.IsNullOrEmpty(option.Pattern))
            {
                try
                {
                    _ = new Regex(option.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException($"Invalid value pattern: {option.Id}", error);
                }
            }

            if (option.DefaultValue is { } defaultValue)
            {
                Require(option.ValueKind != "enum" || option.EnumValues.Contains(defaultValue, StringComparer.Ordinal), $"Enum default is invalid: {option.Id}");
                if (option.ValueKind is "integer" or "unsignedInteger")
                {
                    Require(long.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer), $"Numeric default is invalid: {option.Id}");
                    Require(option.ValueKind != "unsignedInteger" || integer >= 0, $"Unsigned default is negative: {option.Id}");
                    Require(option.Minimum is null || integer >= option.Minimum, $"Default below minimum: {option.Id}");
                    Require(option.Maximum is null || integer <= option.Maximum, $"Default above maximum: {option.Id}");
                }
                else if (option.ValueKind == "decimal")
                {
                    Require(decimal.TryParse(defaultValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var number), $"Numeric default is invalid: {option.Id}");
                    Require(option.Minimum is null || number >= option.Minimum, $"Default below minimum: {option.Id}");
                    Require(option.Maximum is null || number <= option.Maximum, $"Default above maximum: {option.Id}");
                }
                else if (option.ValueKind == "bitrate")
                {
                    Require(Regex.IsMatch(defaultValue, "^[0-9]+[KMkm]?$"), $"Bitrate default is invalid: {option.Id}");
                }
                else if (option.ValueKind == "boolean")
                {
                    Require(bool.TryParse(defaultValue, out _), $"Boolean default is invalid: {option.Id}");
                }

                if (!string.IsNullOrEmpty(option.Pattern))
                {
                    Require(Regex.IsMatch(defaultValue, option.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)),
                        $"Default does not match pattern: {option.Id}");
                }
            }

            Require(option.Minimum is null || option.Maximum is null || option.Minimum <= option.Maximum, $"Invalid range: {option.Id}");
            Require(option.Classification == "editable" || !option.LegacyBasic, $"Non-editable option is marked basic: {option.Id}");
            Require(option.Classification != "alias" || !option.LegacyBasic, $"Alias cannot be a saved setting: {option.Id}");
            foreach (var ruleId in option.RuleIds)
            {
                Require(KnownRuleIds.Contains(ruleId), $"Unknown RuleId {ruleId}: {option.Id}");
            }
        }

        var byIdMap = specification.Options.ToDictionary(option => option.Id, StringComparer.Ordinal);
        foreach (var option in specification.Options)
        {
            if (option.AliasFor is { } target)
            {
                Require(byIdMap.ContainsKey(target), $"Missing alias target {target}: {option.Id}");
            }

            foreach (var required in option.Requires)
            {
                Require(byIdMap.ContainsKey(required), $"Missing required option {required}: {option.Id}");
                Require(required != option.Id, $"Option cannot require itself: {option.Id}");
                Require(byIdMap[required].Classification is not ("action" or "alias" or "nativeOnly"),
                    $"Option cannot require an action/alias/internal entry: {option.Id}");
                Require(!option.Conflicts.Contains(required, StringComparer.Ordinal), $"Option both requires and conflicts with {required}: {option.Id}");
            }

            foreach (var conflict in option.Conflicts)
            {
                Require(byIdMap.ContainsKey(conflict), $"Missing conflicting option {conflict}: {option.Id}");
                Require(conflict != option.Id, $"Option cannot conflict with itself: {option.Id}");
            }
        }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidDataException(message);
        }
    }
}
