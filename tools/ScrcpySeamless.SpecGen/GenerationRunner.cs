using System.Text;

namespace ScrcpySeamless.SpecGen;

/// <summary>Runs deterministic generation or read-only verification from a repository root.</summary>
internal static class GenerationRunner
{
    public static void Execute(string root, string mode)
    {
        if (mode is not ("generate" or "verify"))
        {
            throw new ArgumentException("Mode must be generate or verify.", nameof(mode));
        }

        var specPath = Path.Combine(root, "spec", "options", "options.yaml");
        if (!File.Exists(specPath))
        {
            throw new FileNotFoundException("Run SpecGen from the repository root.", specPath);
        }

        var specification = OptionSpecification.Read(specPath);
        SpecValidator.Validate(specification);
        var outputs = OutputGenerator.Generate(specification);
        foreach (var (relativePath, content) in outputs)
        {
            var target = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var expected = new UTF8Encoding(false).GetBytes(content);
            if (mode == "verify")
            {
                if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException($"Generated output differs: {relativePath}");
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, expected);
        }

        Console.WriteLine($"SpecGen {mode}: {specification.Options.Count} native entries, {outputs.Count} outputs are current.");
    }
}
