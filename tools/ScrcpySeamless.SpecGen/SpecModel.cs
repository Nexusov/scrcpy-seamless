using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ScrcpySeamless.SpecGen;

/// <summary>Typed, strictly parsed canonical static option metadata.</summary>
internal sealed class OptionSpecification
{
    public int SpecVersion { get; set; }
    public UpstreamSource Upstream { get; set; } = new();
    public List<OptionEntry> Options { get; set; } = [];

    public static OptionSpecification Read(string path)
    {
        var parser = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();
        return parser.Deserialize<OptionSpecification>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Option specification is empty.");
    }
}

/// <summary>Provenance of the imported native option help baseline.</summary>
internal sealed class UpstreamSource
{
    public string Repository { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Commit { get; set; } = "";
}

/// <summary>One native CLI table row and its canonical presentation projection.</summary>
internal sealed class OptionEntry
{
    public string Id { get; set; } = "";
    public string? LongName { get; set; }
    public string? ShortName { get; set; }
    public string? NativeId { get; set; }
    public int? NativeNumericId { get; set; }
    public string ArgumentShape { get; set; } = "";
    public string? ArgumentHint { get; set; }
    public string NativeHelp { get; set; } = "";
    public string? NativeHelpDebug { get; set; }
    public string Classification { get; set; } = "";
    public string? AliasFor { get; set; }
    public string Category { get; set; } = "";
    public string ValueKind { get; set; } = "";
    public string? Label { get; set; }
    public string? Description { get; set; }
    public List<string> EnumValues { get; set; } = [];
    public string? DefaultValue { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public string? Pattern { get; set; }
    public List<string> Requires { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
    public List<string> RuleIds { get; set; } = [];
    public string? BuildCapability { get; set; }
    public bool LegacyBasic { get; set; }
    public string? LegacyReason { get; set; }
    public bool LegacySeamlessCompatible { get; set; } = true;
    public string? LegacyArgumentHint { get; set; }
}
