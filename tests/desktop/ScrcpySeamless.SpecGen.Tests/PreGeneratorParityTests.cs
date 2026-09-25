using System.Text.Json;
using ScrcpySeamless.SpecGen;
using Xunit;

namespace ScrcpySeamless.SpecGen.Tests;

/// <summary>Compares the migration against frozen Phase 3 data, independently of SpecGen output.</summary>
public sealed class PreGeneratorParityTests
{
    private const string Phase3Merge = "3207892ddd37cdd6b20bafc7c770a8470b6cb81f";

    /// <summary>Every native spelling, argument shape, help string and table position matches Phase 3.</summary>
    [Fact]
    public void NativeOptionDataMatchesAcceptedPhase3Table()
    {
        var root = FindRoot();
        var specification = OptionSpecification.Read(Path.Combine(root, "spec", "options", "options.yaml"));
        SpecValidator.Validate(specification);
        var fixturePath = Path.Combine(root, "tests", "fixtures", "options", "scrcpy-v4.0-cli-baseline.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var baseline = fixture.RootElement;

        Assert.Equal(Phase3Merge, baseline.GetProperty("sourceCommit").GetString());
        Assert.Equal("src/scrcpy/app/src/cli.c", baseline.GetProperty("sourcePath").GetString());

        var expectedOptions = baseline.GetProperty("options").EnumerateArray().ToArray();
        Assert.Equal(109, expectedOptions.Length);
        Assert.Equal(expectedOptions.Length, specification.Options.Count);

        for (var index = 0; index < expectedOptions.Length; index++)
        {
            var expected = expectedOptions[index];
            var actual = specification.Options[index];
            Assert.Equal(expected.GetProperty("longName").GetString(), actual.LongName);
            Assert.Equal(expected.GetProperty("shortName").GetString(), actual.ShortName);
            Assert.Equal(expected.GetProperty("argumentShape").GetString(), actual.ArgumentShape);
            Assert.Equal(expected.GetProperty("argumentHint").GetString(), actual.ArgumentHint);
            Assert.Equal(expected.GetProperty("help").GetString(), actual.NativeHelp);
            Assert.Equal(expected.GetProperty("helpDebug").GetString(), actual.NativeHelpDebug);
        }

        var generatedNative = OutputGenerator.Generate(specification)["src/scrcpy/app/src/cli_options.generated.inc"];
        var trackedNative = File.ReadAllText(Path.Combine(root, "src", "scrcpy", "app", "src", "cli_options.generated.inc"));
        Assert.Equal(generatedNative, trackedNative.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>All 106 ordered legacy records retain each of the 13 previous field values.</summary>
    [Fact]
    public void GeneratedLegacyCatalogueMatchesAcceptedPhase3Catalogue()
    {
        var root = FindRoot();
        var fixturePath = Path.Combine(root, "tests", "fixtures", "options", "legacy-option-catalog-baseline.json");
        var currentPath = Path.Combine(root, "launcher", "option-catalog.json");
        var specification = OptionSpecification.Read(Path.Combine(root, "spec", "options", "options.yaml"));
        SpecValidator.Validate(specification);
        var generatedCatalogue = OutputGenerator.Generate(specification)["launcher/option-catalog.json"];
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        using var current = JsonDocument.Parse(File.ReadAllText(currentPath));
        using var generated = JsonDocument.Parse(generatedCatalogue);
        var expectedOptions = fixture.RootElement.EnumerateArray().ToArray();
        var actualOptions = current.RootElement.EnumerateArray().ToArray();

        Assert.Equal(106, expectedOptions.Length);
        Assert.Equal(expectedOptions.Length, actualOptions.Length);
        Assert.True(JsonElement.DeepEquals(current.RootElement, generated.RootElement),
            "Tracked legacy catalogue differs from the generated projection.");

        for (var index = 0; index < expectedOptions.Length; index++)
        {
            var expectedFields = expectedOptions[index].EnumerateObject().ToArray();
            var actual = actualOptions[index];
            Assert.Equal(13, expectedFields.Length);
            Assert.Equal(expectedFields.Length, actual.EnumerateObject().Count());

            foreach (var field in expectedFields)
            {
                Assert.True(actual.TryGetProperty(field.Name, out var actualValue),
                    $"Legacy option {index} is missing {field.Name}.");
                Assert.True(JsonElement.DeepEquals(field.Value, actualValue),
                    $"Legacy option {index} changed {field.Name}.");
            }
        }
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

        throw new DirectoryNotFoundException("Cannot locate the repository root.");
    }
}
