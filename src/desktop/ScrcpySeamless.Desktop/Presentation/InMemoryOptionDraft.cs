using System.Text.Json;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Options;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Owns detached option edits for a preview session; it has no storage adapter.</summary>
public sealed class InMemoryOptionDraft
{
    private readonly Dictionary<string, JsonElement> values;

    public InMemoryOptionDraft(IReadOnlyDictionary<string, JsonElement>? initialValues = null)
    {
        values = initialValues is null
            ? new(StringComparer.Ordinal)
            : new(initialValues, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, JsonElement> Values => values;

    /// <summary>Sets a detached string override, retaining even an empty optional argument.</summary>
    public void SetText(string optionId, string value) => values[optionId] = JsonSerializer.SerializeToElement(value);

    /// <summary>Sets an explicit boolean override, including false.</summary>
    public void SetBoolean(string optionId, bool value) => values[optionId] = JsonSerializer.SerializeToElement(value);

    /// <summary>Returns the current raw override without substituting a descriptor default.</summary>
    public bool TryGet(string optionId, out JsonElement value) => values.TryGetValue(optionId, out value);

    /// <summary>Removes one override and exposes the inherited default again.</summary>
    public void Remove(string optionId) => values.Remove(optionId);

    /// <summary>Discards every in-memory override without any persistence operation.</summary>
    public void Reset() => values.Clear();

    /// <summary>Applies Core's existing semantic validator to a detached snapshot.</summary>
    public OptionSelectionResult Validate()
    {
        return OptionSelectionValidator.Evaluate(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement>(values, StringComparer.Ordinal),
        });
    }
}
