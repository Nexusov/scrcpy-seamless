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
            : initialValues.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, JsonElement> Values => values;
    public event Action<string, JsonElement?>? Changed;

    /// <summary>Sets a detached string override, retaining even an empty optional argument.</summary>
    public void SetText(string optionId, string value)
    {
        values[optionId] = JsonSerializer.SerializeToElement(value);
        Changed?.Invoke(optionId, values[optionId]);
    }

    /// <summary>Sets an explicit boolean override, including false.</summary>
    public void SetBoolean(string optionId, bool value)
    {
        values[optionId] = JsonSerializer.SerializeToElement(value);
        Changed?.Invoke(optionId, values[optionId]);
    }

    /// <summary>Returns the current raw override without substituting a descriptor default.</summary>
    public bool TryGet(string optionId, out JsonElement value) => values.TryGetValue(optionId, out value);

    /// <summary>Removes one override and exposes the inherited default again.</summary>
    public void Remove(string optionId)
    {
        if (values.Remove(optionId))
        {
            Changed?.Invoke(optionId, null);
        }
    }

    /// <summary>Discards every in-memory override without any persistence operation.</summary>
    public void Reset()
    {
        if (values.Count == 0)
        {
            return;
        }

        string[] optionIds = values.Keys.ToArray();
        values.Clear();

        foreach (string optionId in optionIds)
        {
            Changed?.Invoke(optionId, null);
        }
    }

    /// <summary>Replaces editor values with detached persisted option values.</summary>
    public void Replace(IReadOnlyDictionary<string, JsonElement> replacements)
    {
        Dictionary<string, JsonElement> snapshot = replacements.ToDictionary(
            entry => entry.Key, entry => entry.Value.Clone(), StringComparer.Ordinal);
        values.Clear();

        foreach ((string optionId, JsonElement value) in snapshot)
        {
            values.Add(optionId, value);
        }

    }

    /// <summary>Applies Core's existing semantic validator to a detached snapshot.</summary>
    public OptionSelectionResult Validate()
    {
        return OptionSelectionValidator.Evaluate(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement>(values, StringComparer.Ordinal),
        });
    }
}
