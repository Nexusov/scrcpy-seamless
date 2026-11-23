using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace ScrcpySeamless.Core.Options;

public enum NativeShortcutContext
{
    Any,
    Display,
    Camera,
}

/// <summary>Generated identity/default data; execution conditions remain native-owned.</summary>
public sealed record NativeShortcutDefinition(string Id, string Group,
    NativeShortcutContext Context, int Tap, string Condition, IReadOnlyList<string> DefaultGestures);

public sealed record NativeShortcutGesture(string Key, bool Modifier, bool Shift, int Tap = 0);

public enum NativeShortcutBindingError { InvalidJson, TooLarge, UnknownAction, DuplicateAction, InvalidGesture, Conflict }
public sealed record NativeShortcutBindingIssue(NativeShortcutBindingError Error, string ActionId, string? OtherActionId = null);

/// <summary>One validated, detached override map for an immutable native launch.</summary>
public sealed class NativeShortcutBindings
{
    public const string OptionId = "shortcut-bindings";
    public const int MaximumBytes = 4096;
    public const int MaximumGestureLength = 32;
    private readonly ReadOnlyDictionary<string, string> overrides;

    private NativeShortcutBindings(Dictionary<string, string> overrides) => this.overrides = new(overrides);

    public IReadOnlyDictionary<string, string> Overrides => overrides;

    public static bool TryParse(string json, out NativeShortcutBindings? bindings) => TryParse(json, out bindings, out _);

    public static bool TryParse(string json, out NativeShortcutBindings? bindings,
        out IReadOnlyList<NativeShortcutBindingIssue> issues)
    {
        bindings = null;
        List<NativeShortcutBindingIssue> errors = [];
        issues = errors;

        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            errors.Add(new(NativeShortcutBindingError.TooLarge, string.Empty));
            return false;
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Count() > GeneratedNativeShortcutCatalog.Actions.Count)
            {
                errors.Add(new(NativeShortcutBindingError.InvalidJson, string.Empty));
                return false;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!GeneratedNativeShortcutCatalog.Actions.Any(action => action.Id == property.Name))
                {
                    errors.Add(new(NativeShortcutBindingError.UnknownAction, property.Name));
                    continue;
                }

                if (values.ContainsKey(property.Name))
                {
                    errors.Add(new(NativeShortcutBindingError.DuplicateAction, property.Name));
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String ||
                    property.Value.GetString() is not string gesture ||
                    gesture.Length != 0 && !TryParseGesture(gesture, out _))
                {
                    errors.Add(new(NativeShortcutBindingError.InvalidGesture, property.Name));
                    continue;
                }

                values.Add(property.Name, gesture);
            }
        }
        catch (JsonException)
        {
            errors.Add(new(NativeShortcutBindingError.InvalidJson, string.Empty));
            return false;
        }
        catch (InvalidOperationException)
        {
            // JsonDocument accepts lone escaped surrogates; string decoding rejects them.
            errors.Add(new(NativeShortcutBindingError.InvalidJson, string.Empty));
            return false;
        }

        if (errors.Count != 0)
        {
            return false;
        }

        NativeShortcutBindings candidate = new(values);
        IReadOnlyList<NativeShortcutDefinition> actions = GeneratedNativeShortcutCatalog.Actions;
        IReadOnlyList<NativeShortcutGesture>[] effective = actions.Select(candidate.Effective).ToArray();

        for (int index = 0; index < actions.Count; index++)
        {
            for (int other = index + 1; other < actions.Count; other++)
            {
                NativeShortcutDefinition first = actions[index];
                NativeShortcutDefinition second = actions[other];

                if (!ContextsOverlap(first.Context, second.Context))
                {
                    continue;
                }

                if (effective[index].Any(left => effective[other].Any(right => Overlap(left, right))))
                {
                    errors.Add(new(NativeShortcutBindingError.Conflict, first.Id, second.Id));
                }
            }
        }

        if (errors.Count != 0)
        {
            return false;
        }

        bindings = candidate;
        return true;
    }

    public IReadOnlyList<NativeShortcutGesture> Effective(NativeShortcutDefinition action)
    {
        if (!overrides.TryGetValue(action.Id, out string? gesture))
        {
            return action.DefaultGestures.Select(value => ParseDefault(value, action.Tap)).ToArray();
        }

        if (gesture.Length == 0)
        {
            return [];
        }

        int tap = action.DefaultGestures.Contains(gesture, StringComparer.Ordinal) ? action.Tap : 0;
        return [ParseDefault(gesture, tap)];
    }

    public static bool TryParseGesture(string value, out NativeShortcutGesture? gesture)
    {
        gesture = null;

        if (value.Length is 0 or > MaximumGestureLength)
        {
            return false;
        }

        bool modifier = value.StartsWith("MOD+", StringComparison.Ordinal);
        string key = modifier ? value[4..] : value;
        bool shift = modifier && key.StartsWith("Shift+", StringComparison.Ordinal);

        if (shift)
        {
            key = key[6..];
        }

        bool function = key.Length is >= 2 and <= 3 && key[0] == 'F' &&
            int.TryParse(key.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int number) &&
            number is >= 1 and <= 24 && key[1] != '0';
        bool ordinary = key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' ||
            key is "Left" or "Right" or "Up" or "Down" or "Backspace";

        if (!function && (!modifier || !ordinary))
        {
            return false;
        }

        gesture = new(key, modifier, shift);
        return true;
    }

    private static NativeShortcutGesture ParseDefault(string value, int tap)
    {
        if (!TryParseGesture(value, out NativeShortcutGesture? gesture))
        {
            throw new InvalidOperationException("Invalid generated native shortcut gesture.");
        }

        return gesture! with { Tap = tap };
    }

    private static bool ContextsOverlap(NativeShortcutContext left, NativeShortcutContext right) =>
        left == NativeShortcutContext.Any || right == NativeShortcutContext.Any || left == right;

    private static bool Overlap(NativeShortcutGesture left, NativeShortcutGesture right) =>
        left.Key == right.Key && left.Modifier == right.Modifier && left.Shift == right.Shift &&
        (left.Tap == 0 || right.Tap == 0 || left.Tap == right.Tap);
}
