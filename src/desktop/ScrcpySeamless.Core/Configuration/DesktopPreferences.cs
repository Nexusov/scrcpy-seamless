using System.Text.Json.Serialization;

namespace ScrcpySeamless.Core.Configuration;

/// <summary>Requested Desktop theme independent of the effective operating-system theme.</summary>
public enum DesktopTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Stable IDs for shortcuts implemented by the Desktop shell.</summary>
public static class DesktopCommandIds
{
    public const string FocusSettingsSearch = "settings.focus-search";
    public const string ShowDevices = "navigation.devices";
    public const string ShowSettings = "navigation.settings";

    public static IReadOnlyList<string> Supported { get; } =
        [FocusSettingsSearch, ShowDevices, ShowSettings];
}

/// <summary>Requested appearance; null color and font mean the semantic system defaults.</summary>
public sealed class DesktopAppearancePreferences
{
    private const int MaximumFontFamilyLength = 128;

    [JsonRequired]
    public DesktopTheme Theme { get; init; } = DesktopTheme.System;

    [JsonRequired]
    public string? AccentColor { get; init; }

    [JsonRequired]
    public string? FontFamily { get; init; }

    [JsonRequired]
    public int ScalePercent { get; init; } = 100;

    /// <summary>Checks the saved request without probing fonts or the active display.</summary>
    public IReadOnlyList<ValidationIssue> Validate(string path)
    {
        List<ValidationIssue> issues = [];

        if (!Enum.IsDefined(Theme))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(Theme)}"));
        }

        if (AccentColor is not null && !IsRgbHexColor(AccentColor))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(AccentColor)}"));
        }

        if (FontFamily is not null &&
            (FontFamily.Length is < 1 or > MaximumFontFamilyLength || FontFamily.Any(char.IsControl)))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(FontFamily)}"));
        }

        if (ScalePercent is not (100 or 110 or 125 or 150))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(ScalePercent)}"));
        }

        return issues;
    }

    /// <summary>Accepts a bounded RGB color without relying on presentation-library parsers.</summary>
    private static bool IsRgbHexColor(string value)
    {
        return value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    }
}

/// <summary>Local shell bindings; an explicit empty gesture disables a command.</summary>
public sealed class DesktopShortcutPreferences
{
    private static readonly HashSet<string> ReservedGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Control+A", "Control+C", "Control+V", "Control+X", "Control+Z", "Control+Y",
        "Control+Shift+Z", "Control+F4", "Alt+F4",
    };

    [JsonRequired]
    public Dictionary<string, string> Bindings { get; init; } = new(StringComparer.Ordinal)
    {
        [DesktopCommandIds.FocusSettingsSearch] = "Control+F",
        [DesktopCommandIds.ShowDevices] = "Control+1",
        [DesktopCommandIds.ShowSettings] = "Control+2",
    };

    /// <summary>Returns the saved gesture, including an explicit empty disabled binding.</summary>
    public string EffectiveBinding(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        if (Bindings.TryGetValue(commandId, out string? gesture))
        {
            return gesture;
        }

        return commandId switch
        {
            DesktopCommandIds.FocusSettingsSearch => "Control+F",
            DesktopCommandIds.ShowDevices => "Control+1",
            DesktopCommandIds.ShowSettings => "Control+2",
            _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
        };
    }

    /// <summary>Rejects unknown commands, unsafe gestures, and collisions in the shell scope.</summary>
    public IReadOnlyList<ValidationIssue> Validate(string path)
    {
        List<ValidationIssue> issues = [];

        if (Bindings is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(Bindings)}"));
            return issues;
        }

        foreach ((string commandId, string gesture) in Bindings)
        {
            string bindingPath = $"{path}.{nameof(Bindings)}.{commandId}";

            if (!DesktopCommandIds.Supported.Contains(commandId, StringComparer.Ordinal) ||
                gesture is null || !IsSafeGesture(gesture))
            {
                issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, bindingPath));
            }
        }

        HashSet<string> usedGestures = new(StringComparer.OrdinalIgnoreCase);

        foreach (string commandId in DesktopCommandIds.Supported)
        {
            if (Bindings.TryGetValue(commandId, out string? storedGesture) && storedGesture is null)
            {
                continue;
            }

            string gesture = EffectiveBinding(commandId);

            if (gesture.Length > 0 && !usedGestures.Add(gesture))
            {
                issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration,
                    $"{path}.{nameof(Bindings)}.{commandId}"));
            }
        }

        return issues;
    }

    /// <summary>Restricts bindings to a single local modifier chord and a known key.</summary>
    private static bool IsSafeGesture(string gesture)
    {
        if (gesture.Length == 0)
        {
            return true;
        }

        if (ReservedGestures.Contains(gesture))
        {
            return false;
        }

        string[] parts = gesture.Split('+');

        if (parts.Length is not (2 or 3) ||
            parts[0] is not ("Control" or "Alt") ||
            (parts.Length == 3 && parts[1] != "Shift"))
        {
            return false;
        }

        string key = parts[^1];
        bool isLetterOrDigit = key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]);
        bool isFunctionKey = key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int functionNumber) &&
            functionNumber is >= 1 and <= 12;

        return isLetterOrDigit || isFunctionKey || key is "Comma" or "Period";
    }
}

/// <summary>Versioned application preferences, deliberately separate from device configuration.</summary>
public sealed class DesktopPreferences
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public DesktopAppearancePreferences Appearance { get; init; } = new();

    [JsonRequired]
    public DesktopShortcutPreferences Shortcuts { get; init; } = new();

    /// <summary>Checks a persisted request before it becomes active or overwrites another revision.</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        List<ValidationIssue> issues = [];

        if (SchemaVersion != CurrentSchemaVersion)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.UnsupportedConfigurationVersion, nameof(SchemaVersion)));
        }

        if (Appearance is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, nameof(Appearance)));
        }
        else
        {
            issues.AddRange(Appearance.Validate(nameof(Appearance)));
        }

        if (Shortcuts is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, nameof(Shortcuts)));
        }
        else
        {
            issues.AddRange(Shortcuts.Validate(nameof(Shortcuts)));
        }

        return issues;
    }
}
