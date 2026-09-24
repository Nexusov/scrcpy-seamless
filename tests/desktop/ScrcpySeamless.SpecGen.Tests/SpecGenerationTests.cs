using ScrcpySeamless.SpecGen;
using System.Text.Json;
using Xunit;

namespace ScrcpySeamless.SpecGen.Tests;

/// <summary>Protects strict option specification validation and generated-output integrity.</summary>
public sealed class SpecGenerationTests
{
    /// <summary>Two generations from identical input have identical bytes in every output.</summary>
    [Fact]
    public void GenerationIsDeterministic()
    {
        var specification = Load();
        var first = OutputGenerator.Generate(specification);
        var second = OutputGenerator.Generate(specification);

        Assert.Equal(first.Keys, second.Keys);
        foreach (var key in first.Keys)
        {
            Assert.Equal(first[key], second[key]);
        }
    }

    /// <summary>Verify detects modified output without writing even one generated file.</summary>
    [Fact]
    public void VerifyDetectsDriftWithoutMutatingOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "scrcpy-specgen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "spec", "options", "options.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(input)!);
            File.Copy(Path.Combine(FindRoot(), "spec", "options", "options.yaml"), input);
            GenerationRunner.Execute(root, "generate");
            var first = OutputGenerator.Generate(Load());
            var firstBytes = first.Keys.ToDictionary(
                key => key,
                key => File.ReadAllBytes(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))));
            GenerationRunner.Execute(root, "generate");
            foreach (var key in first.Keys)
            {
                Assert.Equal(firstBytes[key], File.ReadAllBytes(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))));
            }

            GenerationRunner.Execute(root, "verify");

            var outputs = OutputGenerator.Generate(Load());
            var target = Path.Combine(root, "launcher", "option-catalog.json");
            File.AppendAllText(target, " ");
            var before = outputs.Keys.ToDictionary(
                key => key,
                key => File.ReadAllBytes(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))));

            Assert.Throws<InvalidDataException>(() => GenerationRunner.Execute(root, "verify"));

            foreach (var key in outputs.Keys)
            {
                var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(before[key], File.ReadAllBytes(path));
            }
        }
        finally
        {
            var fullRoot = Path.GetFullPath(root);
            var fullTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var isOwnedFixture = fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullRoot).StartsWith("scrcpy-specgen-", StringComparison.Ordinal);

            if (isOwnedFixture && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    /// <summary>Duplicate stable IDs are rejected before any output is written.</summary>
    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var specification = Load();
        specification.Options[1].Id = specification.Options[0].Id;
        AssertInvalid(specification, "Duplicate option ID");
    }

    /// <summary>A short CLI spelling cannot select two different entries.</summary>
    [Fact]
    public void DuplicateShortAliasesAreRejected()
    {
        var specification = Load();
        var aliases = specification.Options.Where(option => option.ShortName is not null).Take(2).ToArray();
        aliases[1].ShortName = aliases[0].ShortName;
        AssertInvalid(specification, "Duplicate/invalid short alias");
    }

    /// <summary>Relationships must point to an existing canonical option ID.</summary>
    [Fact]
    public void UnknownReferencesAreRejected()
    {
        var specification = Load();
        specification.Options[0].Requires.Add("missing-option");
        AssertInvalid(specification, "Missing required option");
    }

    /// <summary>An enum default must belong to its declared value set.</summary>
    [Fact]
    public void InvalidEnumDefaultIsRejected()
    {
        var specification = Load();
        var option = specification.Options.First(item => item.ValueKind == "enum");
        option.DefaultValue = "not-a-valid-value";
        AssertInvalid(specification, "Enum default is invalid");
    }

    /// <summary>Managed and action entries cannot become legacy editable settings.</summary>
    [Fact]
    public void NonEditableBasicSettingIsRejected()
    {
        var specification = Load();
        var option = specification.Options.First(item => item.Classification == "managed");
        option.LegacyBasic = true;
        AssertInvalid(specification, "Non-editable option is marked basic");
    }

    /// <summary>Every conditional validator ID needs a typed implementation.</summary>
    [Fact]
    public void UnknownRuleIdIsRejected()
    {
        var specification = Load();
        specification.Options[0].RuleIds.Add("unknown-rule");
        AssertInvalid(specification, "Unknown RuleId");
    }

    /// <summary>An unconditional requirement cannot also be a conflict.</summary>
    [Fact]
    public void ContradictoryRelationshipsAreRejected()
    {
        var specification = Load();
        specification.Options[0].Requires.Add(specification.Options[1].Id);
        specification.Options[0].Conflicts.Add(specification.Options[1].Id);
        AssertInvalid(specification, "both requires and conflicts");
    }

    /// <summary>Unsigned defaults must be integral and within the declared range.</summary>
    [Fact]
    public void InvalidNumericDefaultIsRejected()
    {
        var specification = Load();
        var option = specification.Options.First(item => item.Id == "max-size");
        option.DefaultValue = "-1";
        AssertInvalid(specification, "Unsigned default is negative");
    }

    /// <summary>A broken simple value pattern cannot enter generated metadata.</summary>
    [Fact]
    public void InvalidPatternIsRejected()
    {
        var specification = Load();
        specification.Options[0].Pattern = "[";
        AssertInvalid(specification, "Invalid value pattern");
    }

    /// <summary>Generated JSON Schema documents required non-null canonical fields.</summary>
    [Fact]
    public void GeneratedSchemaHasRequiredContract()
    {
        var specification = Load();
        var schemaText = OutputGenerator.Generate(specification)["spec/options/options.schema.json"];
        using var schema = JsonDocument.Parse(schemaText);
        var root = schema.RootElement;
        var required = root.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        var optionItem = root.GetProperty("properties").GetProperty("options").GetProperty("items");
        var optionRequired = optionItem.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.Contains("specVersion", required);
        Assert.Contains("options", required);
        Assert.Equal("object", optionItem.GetProperty("type").GetString());
        Assert.Contains("id", optionRequired);
        Assert.Contains("classification", optionRequired);
        Assert.False(optionItem.GetProperty("additionalProperties").GetBoolean());
    }

    /// <summary>Duplicate YAML keys fail during strict parsing.</summary>
    [Fact]
    public void DuplicateYamlKeysAreRejected()
    {
        var original = File.ReadAllText(Path.Combine(FindRoot(), "spec", "options", "options.yaml"));
        AssertYamlInvalid(original.Replace("specVersion: 1", "specVersion: 1\nspecVersion: 1", StringComparison.Ordinal));
    }

    /// <summary>Unexpected YAML keys cannot silently drift outside the typed model.</summary>
    [Fact]
    public void UnknownYamlFieldsAreRejected()
    {
        var original = File.ReadAllText(Path.Combine(FindRoot(), "spec", "options", "options.yaml"));
        AssertYamlInvalid(original.Replace("specVersion: 1", "specVersion: 1\nunknownField: true", StringComparison.Ordinal));
    }

    private static OptionSpecification Load()
    {
        return OptionSpecification.Read(Path.Combine(FindRoot(), "spec", "options", "options.yaml"));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "spec", "options", "options.yaml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate canonical option specification.");
    }

    private static void AssertInvalid(OptionSpecification specification, string expectedMessage)
    {
        var error = Assert.Throws<InvalidDataException>(() => SpecValidator.Validate(specification));
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    private static void AssertYamlInvalid(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            Assert.ThrowsAny<Exception>(() => OptionSpecification.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
