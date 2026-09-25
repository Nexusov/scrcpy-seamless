using System.Reflection;
using System.Text.Json;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Resolves Desktop-owned UI and generated option text by stable resource key.</summary>
public sealed class PresentationText
{
    private const string UiResource = "ScrcpySeamless.Desktop.Resources.Ui.en.json";
    private const string OptionResource = "ScrcpySeamless.Desktop.Resources.Options.GeneratedOptionResources.en.json";
    private readonly IReadOnlyDictionary<string, string> values;

    public PresentationText(IReadOnlyDictionary<string, string>? replacements = null)
    {
        Dictionary<string, string> resolved = new(StringComparer.Ordinal);
        LoadResource(UiResource, resolved);
        LoadResource(OptionResource, resolved);

        if (replacements is not null)
        {
            foreach ((string key, string value) in replacements)
            {
                resolved[key] = value;
            }
        }

        values = resolved;
    }

    /// <summary>Returns the requested English presentation string or fails for a missing key.</summary>
    public string Get(string key)
    {
        return values.TryGetValue(key, out string? value)
            ? value
            : throw new KeyNotFoundException($"Missing Desktop text resource: {key}");
    }

    /// <summary>Loads one embedded flat JSON resource without changing generated files.</summary>
    private static void LoadResource(string name, Dictionary<string, string> destination)
    {
        Assembly assembly = typeof(PresentationText).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
        using JsonDocument document = JsonDocument.Parse(stream);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            destination.Add(property.Name, property.Value.GetString()
                ?? throw new InvalidOperationException($"Resource {property.Name} is not text."));
        }
    }
}
