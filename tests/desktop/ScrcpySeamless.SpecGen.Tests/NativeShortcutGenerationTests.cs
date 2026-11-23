using System.Text.Json.Nodes;
using ScrcpySeamless.SpecGen;
using Xunit;

namespace ScrcpySeamless.SpecGen.Tests;

public sealed class NativeShortcutGenerationTests
{
    [Fact]
    public void CanonicalCatalogGeneratesBothLanguagesDeterministically()
    {
        string root = FindRoot();
        IReadOnlyDictionary<string, string> first = NativeShortcutGenerator.Generate(root);
        IReadOnlyDictionary<string, string> second = NativeShortcutGenerator.Generate(root);
        Assert.Equal(2, first.Count);

        foreach ((string path, string content) in first)
        {
            Assert.Equal(content, second[path]);
            Assert.Equal(content, File.ReadAllText(Path.Combine(root, path)));
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("gesture")]
    [InlineData("condition")]
    [InlineData("version")]
    public void RejectsInvalidCatalogBeforeOutput(string defect)
    {
        string root = Path.Combine(Path.GetTempPath(), $"native-shortcut-spec-{Guid.NewGuid():N}");
        string directory = Path.Combine(root, "spec", "options");
        Directory.CreateDirectory(directory);

        try
        {
            JsonNode source = JsonNode.Parse(File.ReadAllText(Path.Combine(FindRoot(), "spec/options/native-shortcuts.json")))!;
            JsonNode first = source["actions"]![0]!;

            switch (defect)
            {
                case "unknown": first["unknown"] = true; break;
                case "duplicate": source["actions"]![1]!["id"] = first["id"]!.GetValue<string>(); break;
                case "gesture": first["gestures"]![0] = "Ctrl+Alt+Delete"; break;
                case "condition": first["condition"] = "unknown"; break;
                case "version": source["version"] = 2; break;
            }
            File.WriteAllText(Path.Combine(directory, "native-shortcuts.json"), source.ToJsonString());
            Assert.ThrowsAny<Exception>(() => NativeShortcutGenerator.Generate(root));
            Assert.False(Directory.Exists(Path.Combine(root, "src")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "spec/options/native-shortcuts.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
