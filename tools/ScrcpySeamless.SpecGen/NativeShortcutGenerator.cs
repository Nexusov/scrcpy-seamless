using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ScrcpySeamless.SpecGen;

/// <summary>Projects one bounded semantic action/default catalog into native and Core values.</summary>
internal static class NativeShortcutGenerator
{
    private const string Source = "spec/options/native-shortcuts.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyDictionary<string, string> Generate(string root)
    {
        string json = File.ReadAllText(Path.Combine(root, Source));
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("actions", out JsonElement actions) || actions.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Invalid native shortcut catalog object.");
        }

        RequireUniqueProperties(document.RootElement);

        foreach (JsonElement action in actions.EnumerateArray())
        {
            if (action.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Invalid native shortcut catalog action object.");
            }

            RequireUniqueProperties(action);
        }

        Catalog catalog = JsonSerializer.Deserialize<Catalog>(json, Options)
            ?? throw new InvalidDataException("Native shortcut catalog is empty.");
        HashSet<string> identifiers = new(StringComparer.Ordinal);
        HashSet<string> nativeNames = new(StringComparer.Ordinal);

        if (catalog.Version != 1 || catalog.Actions is null || catalog.Actions.Count is < 1 or > 64)
        {
            throw new InvalidDataException("Invalid native shortcut catalog version/count.");
        }

        foreach (Entry entry in catalog.Actions)
        {
            if (entry is null || entry.Id is null || !Regex.IsMatch(entry.Id, "^[a-z][a-zA-Z0-9]*$") ||
                !identifiers.Add(entry.Id) || !nativeNames.Add(NativeName(entry.Id)) ||
                entry.Group is not ("navigation" or "window" or "deviceScreen" or "clipboard" or "special") ||
                entry.Context is not ("all" or "display" or "camera") || entry.Tap is < 0 or > 2 ||
                entry.Condition is not ("" or "device" or "deviceNonCamera" or "video" or "stdout" or "android7" or "paste" or "hid" or "camera") ||
                entry.Gestures is null || entry.Gestures.Count is < 1 or > 2 ||
                entry.Gestures.Any(gesture => gesture is null || gesture.Length is 0 or > 32 ||
                    !Regex.IsMatch(gesture, "^(MOD\\+(Shift\\+)?)?([A-Z0-9]|Left|Right|Up|Down|Backspace|F([1-9]|1[0-9]|2[0-4]))$") ||
                    (!gesture.StartsWith("MOD+", StringComparison.Ordinal) &&
                     !Regex.IsMatch(gesture, "^F([1-9]|1[0-9]|2[0-4])$"))))
            {
                throw new InvalidDataException($"Invalid native shortcut action: {entry?.Id}");
            }
        }

        StringBuilder native = new($"/* DO NOT EDIT - generated from {Source}. */\n");
        StringBuilder core = new($"// DO NOT EDIT - generated from {Source}.\nnamespace ScrcpySeamless.Core.Options;\n\n" +
            "public static class GeneratedNativeShortcutCatalog\n{\n    public static IReadOnlyList<NativeShortcutDefinition> Actions { get; } = Array.AsReadOnly(new NativeShortcutDefinition[]\n    {\n");

        foreach (Entry entry in catalog.Actions)
        {
            string name = NativeName(entry.Id);
            string context = entry.Context == "all" ? "ANY" : entry.Context.ToUpperInvariant();
            string first = JsonSerializer.Serialize(entry.Gestures[0]);
            string second = entry.Gestures.Count == 2 ? JsonSerializer.Serialize(entry.Gestures[1]) : "NULL";
            native.AppendLine($"SC_SHORTCUT_ACTION({name}, \"{entry.Id}\", SC_SHORTCUT_CONTEXT_{context}, {entry.Tap}, {first}, {second})");
            string managedContext = entry.Context == "all" ? "Any" : char.ToUpperInvariant(entry.Context[0]) + entry.Context[1..];
            string gestures = string.Join(", ", entry.Gestures.Select(gesture => JsonSerializer.Serialize(gesture)));
            core.AppendLine($"        new(\"{entry.Id}\", \"{entry.Group}\", NativeShortcutContext.{managedContext}, {entry.Tap}, " + JsonSerializer.Serialize(entry.Condition) + $", Array.AsReadOnly(new[] {{ {gestures} }})),");
        }

        core.Append("    });\n}\n");
        return new Dictionary<string, string>
        {
            ["src/scrcpy/app/src/shortcut_actions.generated.inc"] = native.ToString().Replace("\r\n", "\n"),
            ["src/desktop/ScrcpySeamless.Core/Options/GeneratedNativeShortcutCatalog.g.cs"] = core.ToString().Replace("\r\n", "\n"),
        };
    }

    private static string NativeName(string id) => Regex.Replace(id, "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();

    private static void RequireUniqueProperties(JsonElement value)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException("Duplicate native shortcut catalog property.");
            }
        }
    }

    private sealed class Catalog
    {
        [JsonRequired] public int Version { get; init; }
        [JsonRequired] public List<Entry> Actions { get; init; } = [];
    }

    private sealed class Entry
    {
        [JsonRequired] public string Id { get; init; } = string.Empty;
        [JsonRequired] public string Group { get; init; } = string.Empty;
        [JsonRequired] public string Context { get; init; } = string.Empty;
        [JsonRequired] public string Condition { get; init; } = string.Empty;
        [JsonRequired] public int Tap { get; init; }
        [JsonRequired] public List<string> Gestures { get; init; } = [];
    }
}
