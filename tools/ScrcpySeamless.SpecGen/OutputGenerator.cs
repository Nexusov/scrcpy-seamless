using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace ScrcpySeamless.SpecGen;

/// <summary>Renders every tracked projection from one validated option specification.</summary>
internal static class OutputGenerator
{
    private const string Source = "spec/options/options.yaml";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyDictionary<string, string> Generate(OptionSpecification specification)
    {
        SortedDictionary<string, string> outputs = new(StringComparer.Ordinal)
        {
            ["spec/options/options.schema.json"] = RenderSchema(),
            ["src/scrcpy/app/src/cli_options.generated.inc"] = RenderNative(specification),
            ["src/desktop/ScrcpySeamless.Core/Options/GeneratedOptionCatalog.g.cs"] = RenderCore(specification),
            ["src/desktop/ScrcpySeamless.Desktop/Resources/Options/GeneratedOptionResources.en.json"] = RenderEnglishResources(specification),
            ["launcher/option-catalog.json"] = RenderLegacy(specification),
            ["docs/reference/options.md"] = RenderReference(specification),
        };

        foreach (string path in outputs.Keys.ToArray())
        {
            outputs[path] = outputs[path].Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        return outputs;
    }

    private static string RenderSchema()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var schema = options.GetJsonSchemaAsNode(typeof(OptionSpecification));
        var root = schema.AsObject();
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["$comment"] = $"DO NOT EDIT - generated from {Source} by SpecGen v1.";
        root["type"] = "object";
        root["required"] = new JsonArray("specVersion", "upstream", "options");
        var properties = root["properties"]!.AsObject();
        properties["specVersion"]!["const"] = 1;
        var upstream = properties["upstream"]!.AsObject();
        upstream["required"] = new JsonArray("repository", "tag", "commit");
        var optionList = properties["options"]!.AsObject();
        optionList["minItems"] = 1;
        var item = optionList["items"]!.AsObject();
        item["type"] = "object";
        item["required"] = new JsonArray("id", "argumentShape", "nativeHelp", "classification", "category", "valueKind");
        var itemProperties = item["properties"]!.AsObject();
        itemProperties["id"]!["pattern"] = "^[a-z][a-z0-9-]*$";
        itemProperties["argumentShape"]!["enum"] = SchemaEnum(SpecValidator.ArgumentShapes);
        itemProperties["classification"]!["enum"] = SchemaEnum(SpecValidator.Classifications);
        itemProperties["category"]!["enum"] = SchemaEnum(SpecValidator.Categories);
        itemProperties["valueKind"]!["enum"] = SchemaEnum(SpecValidator.ValueKinds);
        SealObjects(root);
        return JsonSerializer.Serialize(schema, JsonOptions) + "\n";
    }

    private static JsonArray SchemaEnum(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            result.Add(value);
        }

        return result;
    }

    private static void SealObjects(JsonNode? node)
    {
        if (node is JsonObject objectNode)
        {
            if (objectNode["type"]?.ToString() == "object")
            {
                objectNode["additionalProperties"] = false;
            }

            foreach (var child in objectNode.ToArray())
            {
                SealObjects(child.Value);
            }
        }
        else if (node is JsonArray arrayNode)
        {
            foreach (var child in arrayNode)
            {
                SealObjects(child);
            }
        }
    }

    private static string RenderNative(OptionSpecification specification)
    {
        var lines = new List<string>
        {
            $"// DO NOT EDIT - generated from {Source} by SpecGen v{specification.SpecVersion}.",
            "// Native parser/behavior remains in cli.c; this file owns static CLI declarations.",
            "// OPT_* numeric values are private dispatch IDs for this compiled client, never stable serialized OptionIds.",
            "enum {",
        };
        var nativeIds = specification.Options.Where(option => option.NativeNumericId.HasValue)
            .OrderBy(option => option.NativeNumericId!.Value).ToArray();
        foreach (var option in nativeIds)
        {
            lines.Add($"    {option.NativeId} = {option.NativeNumericId!.Value},");
        }

        lines.Add("};");
        lines.Add("");
        lines.Add("static const struct sc_option options[] = {");
        foreach (var option in specification.Options)
        {
            lines.Add("    {");
            if (option.ShortName is { } shortName)
            {
                lines.Add($"        .shortopt = '{shortName}',");
            }
            else
            {
                lines.Add($"        .longopt_id = {option.NativeId},");
            }

            if (option.LongName is { } longName)
            {
                lines.Add($"        .longopt = {CQuote(longName)},");
            }

            if (option.ArgumentHint is { } hint)
            {
                lines.Add($"        .argdesc = {CQuote(hint)},");
            }

            if (option.ArgumentShape == "optional")
            {
                lines.Add("        .optional_arg = true,");
            }

            if (option.NativeHelpDebug is { } debugHelp)
            {
                lines.Add("#ifndef NDEBUG");
                lines.Add($"        .text = {CQuote(debugHelp)},");
                lines.Add("#else");
                lines.Add($"        .text = {CQuote(option.NativeHelp)},");
                lines.Add("#endif");
            }
            else
            {
                lines.Add($"        .text = {CHelp(option.NativeHelp)},");
            }

            lines.Add("    },");
        }

        lines.Add("};");
        return string.Join('\n', lines) + "\n";
    }

    private static string CHelp(string help)
    {
        const string first = "{DEFAULT_LOCAL_PORT_RANGE_FIRST}";
        const string last = "{DEFAULT_LOCAL_PORT_RANGE_LAST}";
        if (!help.Contains(first, StringComparison.Ordinal))
        {
            return CQuote(help);
        }

        var chunks = help.Split([first, last], StringSplitOptions.None);
        if (chunks.Length != 3)
        {
            throw new InvalidDataException("Invalid native port-help macro template.");
        }

        return $"{CQuote(chunks[0])} STR(DEFAULT_LOCAL_PORT_RANGE_FIRST) {CQuote(chunks[1])} STR(DEFAULT_LOCAL_PORT_RANGE_LAST) {CQuote(chunks[2])}";
    }

    private static string CQuote(string value)
    {
        return '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + '"';
    }

    private static string RenderCore(OptionSpecification specification)
    {
        var lines = new List<string>
        {
            $"// DO NOT EDIT - generated from {Source} by SpecGen v{specification.SpecVersion}.",
            "#nullable enable",
            "using System.Collections.ObjectModel;",
            "",
            "namespace ScrcpySeamless.Core.Options;",
            "",
            "public enum OptionClassification { Editable, Managed, Action, Unsupported, Alias, NativeOnly }",
            "public enum OptionArgumentShape { None, Required, Optional }",
            "public enum OptionValueKind { Boolean, String, Integer, UnsignedInteger, Decimal, Enum, Bitrate, Size, Duration, Color, Custom }",
            "",
            "public sealed record OptionDescriptor",
            "{",
            "    public required string Id { get; init; }",
            "    public string? LongName { get; init; }",
            "    public char? ShortName { get; init; }",
            "    public IReadOnlyList<string> Aliases { get; init; } = [];",
            "    public string? AliasFor { get; init; }",
            "    public required OptionClassification Classification { get; init; }",
            "    public required OptionArgumentShape ArgumentShape { get; init; }",
            "    public string? ArgumentHint { get; init; }",
            "    public required OptionValueKind ValueKind { get; init; }",
            "    public IReadOnlyList<string> EnumValues { get; init; } = [];",
            "    public string? DefaultValue { get; init; }",
            "    public decimal? Minimum { get; init; }",
            "    public decimal? Maximum { get; init; }",
            "    public string? Pattern { get; init; }",
            "    public IReadOnlyList<string> Requires { get; init; } = [];",
            "    public IReadOnlyList<string> Conflicts { get; init; } = [];",
            "    public IReadOnlyList<string> RuleIds { get; init; } = [];",
            "    public required string Category { get; init; }",
            "    public required string LabelResourceKey { get; init; }",
            "    public required string DescriptionResourceKey { get; init; }",
            "    public string? BuildCapability { get; init; }",
            "}",
            "",
            "/// <summary>Generated static option descriptors; native runtime capabilities remain authoritative.</summary>",
            "public static class GeneratedOptionCatalog",
            "{",
            "    public static IReadOnlyList<OptionDescriptor> All { get; } = Array.AsReadOnly(new OptionDescriptor[]",
            "    {",
        };
        var aliasesByTarget = specification.Options.Where(option => option.AliasFor is not null)
            .GroupBy(option => option.AliasFor!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(option => "-" + option.ShortName).ToArray(), StringComparer.Ordinal);
        foreach (var option in specification.Options)
        {
            var aliases = new List<string>();
            if (option.ShortName is not null)
            {
                aliases.Add("-" + option.ShortName);
            }

            if (aliasesByTarget.TryGetValue(option.Id, out var extraAliases))
            {
                aliases.AddRange(extraAliases);
            }

            lines.Add("        new OptionDescriptor");
            lines.Add("        {");
            lines.Add($"            Id = {CsQuote(option.Id)},");
            if (option.LongName is not null) lines.Add($"            LongName = {CsQuote(option.LongName)},");
            if (option.ShortName is not null) lines.Add($"            ShortName = '{option.ShortName}',");
            lines.Add($"            Aliases = {CsList(aliases)},");
            if (option.AliasFor is not null) lines.Add($"            AliasFor = {CsQuote(option.AliasFor)},");
            lines.Add($"            Classification = OptionClassification.{Pascal(option.Classification)},");
            lines.Add($"            ArgumentShape = OptionArgumentShape.{Pascal(option.ArgumentShape)},");
            if (option.ArgumentHint is not null) lines.Add($"            ArgumentHint = {CsQuote(option.ArgumentHint)},");
            lines.Add($"            ValueKind = OptionValueKind.{Pascal(option.ValueKind)},");
            lines.Add($"            EnumValues = {CsList(option.EnumValues)},");
            if (option.DefaultValue is not null) lines.Add($"            DefaultValue = {CsQuote(option.DefaultValue)},");
            if (option.Minimum.HasValue) lines.Add($"            Minimum = {option.Minimum.Value.ToString(CultureInfo.InvariantCulture)}m,");
            if (option.Maximum.HasValue) lines.Add($"            Maximum = {option.Maximum.Value.ToString(CultureInfo.InvariantCulture)}m,");
            if (!string.IsNullOrEmpty(option.Pattern)) lines.Add($"            Pattern = {CsQuote(option.Pattern)},");
            lines.Add($"            Requires = {CsList(option.Requires)},");
            lines.Add($"            Conflicts = {CsList(option.Conflicts)},");
            lines.Add($"            RuleIds = {CsList(option.RuleIds)},");
            lines.Add($"            Category = {CsQuote(option.Category)},");
            lines.Add($"            LabelResourceKey = {CsQuote("options." + option.Id + ".label")},");
            lines.Add($"            DescriptionResourceKey = {CsQuote("options." + option.Id + ".description")},");
            if (option.BuildCapability is not null) lines.Add($"            BuildCapability = {CsQuote(option.BuildCapability)},");
            lines.Add("        },");
        }

        lines.Add("    });");
        lines.Add("    private static readonly IReadOnlyDictionary<string, OptionDescriptor> ById =");
        lines.Add("        new ReadOnlyDictionary<string, OptionDescriptor>(All.ToDictionary(option => option.Id, StringComparer.Ordinal));");
        lines.Add("");
        lines.Add("    public static bool TryGet(string optionId, out OptionDescriptor descriptor)");
        lines.Add("    {");
        lines.Add("        return ById.TryGetValue(optionId, out descriptor!);");
        lines.Add("    }");
        lines.Add("}");
        return string.Join('\n', lines) + "\n";
    }

    private static string CsQuote(string value) => JsonSerializer.Serialize(value);
    private static string CsList(IEnumerable<string> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? "[]" : "[" + string.Join(", ", array.Select(CsQuote)) + "]";
    }

    private static string Pascal(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private static string RenderEnglishResources(OptionSpecification specification)
    {
        var resources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in specification.Options)
        {
            resources[$"options.{option.Id}.label"] = option.Label ?? option.NativeHelp;
            resources[$"options.{option.Id}.description"] = option.Description ?? option.NativeHelp;
            if (option.ArgumentHint is { } hint)
            {
                resources[$"options.{option.Id}.argument"] = hint;
            }
        }

        return JsonSerializer.Serialize(resources, JsonOptions) + "\n";
    }

    private static string RenderLegacy(OptionSpecification specification)
    {
        var legacy = specification.Options.Where(option => option.LongName is not null)
            .Select(option => new
            {
                Name = option.LongName,
                Label = option.Label ?? option.NativeHelp,
                Group = option.Category,
                Kind = option.ValueKind == "boolean" ? "switch" : "value",
                Description = option.Description ?? option.NativeHelp,
                Values = option.EnumValues,
                Basic = option.LegacyBasic,
                Availability = option.Classification,
                Reason = option.LegacyReason ?? "",
                SeamlessCompatible = option.LegacySeamlessCompatible,
                ArgumentOptional = option.ArgumentShape == "optional",
                ArgumentHint = option.LegacyArgumentHint ?? option.ArgumentHint ?? "",
                Pattern = option.Pattern ?? "",
            }).ToArray();
        return JsonSerializer.Serialize(legacy, JsonOptions) + "\n";
    }

    private static string RenderReference(OptionSpecification specification)
    {
        var lines = new List<string>
        {
            "# Option metadata reference",
            "",
            $"<!-- DO NOT EDIT - generated from {Source} by SpecGen v{specification.SpecVersion}. -->",
            "",
            "This table lists the current scrcpy Seamless 2.0 static CLI metadata. Runtime and device capabilities remain authoritative.",
            "",
            "| Option ID | Category | Classification | Argument |",
            "| --- | --- | --- | --- |",
        };
        foreach (var option in specification.Options)
        {
            lines.Add($"| <a id=\"option-{option.Id}\"></a>`{option.Id}` | {option.Category} | {option.Classification} | {option.ArgumentShape} |");
        }

        return string.Join('\n', lines) + "\n";
    }
}
