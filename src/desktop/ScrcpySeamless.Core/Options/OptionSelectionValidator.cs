using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Core.Options;

/// <summary>Machine-readable outcomes for a stored option selection.</summary>
public enum OptionDiagnosticCode
{
    UnknownOption,
    NotEditable,
    InvalidValueType,
    MissingArgument,
    InvalidValue,
    MissingRequirement,
    Conflict,
    RuleViolation,
}

/// <summary>A semantic option failure, independent of UI language.</summary>
public sealed record OptionDiagnostic(OptionDiagnosticCode Code, string OptionId, string? RuleId = null);

/// <summary>Pure validation and discrete CLI arguments for one option selection.</summary>
public sealed record OptionSelectionResult(
    IReadOnlyList<OptionDiagnostic> Diagnostics,
    IReadOnlyList<string> Arguments)
{
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Validates persisted bool/string options without changing their stored representation.</summary>
public static class OptionSelectionValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Validates stored options and returns arguments only when every selection is safe to use.</summary>
    public static OptionSelectionResult Evaluate(MirroringPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(preferences.Options);

        List<OptionDiagnostic> diagnostics = [];
        List<string> arguments = [];
        HashSet<string> activeOptions = new(StringComparer.Ordinal);
        Dictionary<string, int> numericValues = new(StringComparer.Ordinal);

        foreach ((string optionId, JsonElement value) in preferences.Options)
        {
            if (!GeneratedOptionCatalog.TryGet(optionId, out OptionDescriptor descriptor))
            {
                diagnostics.Add(new OptionDiagnostic(OptionDiagnosticCode.UnknownOption, optionId));
                continue;
            }

            if (descriptor.Classification != OptionClassification.Editable)
            {
                diagnostics.Add(new OptionDiagnostic(OptionDiagnosticCode.NotEditable, optionId));
                continue;
            }

            OptionDiagnostic? valueIssue = ValidateValue(descriptor, value, out int? numericValue);

            if (valueIssue is not null)
            {
                diagnostics.Add(valueIssue);
                continue;
            }

            if (numericValue is int parsedNumber)
            {
                numericValues.Add(optionId, parsedNumber);
            }

            if (IsActive(descriptor, value))
            {
                activeOptions.Add(optionId);
            }
        }

        foreach (OptionDescriptor descriptor in GeneratedOptionCatalog.All)
        {
            if (!activeOptions.Contains(descriptor.Id))
            {
                continue;
            }

            foreach (string requirement in descriptor.Requires)
            {
                if (!activeOptions.Contains(requirement))
                {
                    diagnostics.Add(new OptionDiagnostic(OptionDiagnosticCode.MissingRequirement, descriptor.Id));
                }
            }

            foreach (string conflict in descriptor.Conflicts)
            {
                if (activeOptions.Contains(conflict))
                {
                    diagnostics.Add(new OptionDiagnostic(OptionDiagnosticCode.Conflict, descriptor.Id));
                }
            }
        }

        diagnostics.AddRange(OptionRuleRegistry.Validate(preferences.Options, activeOptions, numericValues));

        if (diagnostics.Count != 0)
        {
            return new OptionSelectionResult(diagnostics, []);
        }

        foreach (OptionDescriptor descriptor in GeneratedOptionCatalog.All)
        {
            if (!activeOptions.Contains(descriptor.Id))
            {
                continue;
            }

            JsonElement value = preferences.Options[descriptor.Id];
            string argument = descriptor.ArgumentShape == OptionArgumentShape.None || value.GetString()!.Length == 0
                ? $"--{descriptor.LongName}"
                : $"--{descriptor.LongName}={value.GetString()}";
            arguments.Add(argument);
        }

        return new OptionSelectionResult(diagnostics, arguments);
    }

    /// <summary>Checks one descriptor's simple static value contract.</summary>
    private static OptionDiagnostic? ValidateValue(OptionDescriptor descriptor, JsonElement value, out int? numericValue)
    {
        numericValue = null;

        if (descriptor.ArgumentShape == OptionArgumentShape.None)
        {
            return value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? null
                : new OptionDiagnostic(OptionDiagnosticCode.InvalidValueType, descriptor.Id);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return new OptionDiagnostic(OptionDiagnosticCode.InvalidValueType, descriptor.Id);
        }

        string text = value.GetString()!;

        if (text.Any(char.IsControl))
        {
            return new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
        }

        if (text.Length == 0)
        {
            return descriptor.ArgumentShape == OptionArgumentShape.Optional
                ? null
                : new OptionDiagnostic(OptionDiagnosticCode.MissingArgument, descriptor.Id);
        }

        if (descriptor.EnumValues.Count != 0 && !descriptor.EnumValues.Contains(text, StringComparer.Ordinal))
        {
            return new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
        }

        // The native port option accepts base-zero components, not the legacy decimal regex.
        if (descriptor.Id == "port")
        {
            return NativePortRangeSyntax.TryParse(text, out _, out _)
                ? null
                : new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
        }

        bool nativeScalar = descriptor.ValueKind == OptionValueKind.UnsignedInteger ||
            descriptor.Id is "tunnel-port" or "window-x" or "window-y";
        bool nativeBitrate = descriptor.ValueKind == OptionValueKind.Bitrate;
        bool automaticWindowPosition = (descriptor.Id is "window-x" or "window-y") && text == "auto";
        bool nativeNumber = (nativeScalar && !automaticWindowPosition) || nativeBitrate;

        // Legacy regex metadata remains unchanged; modern Core validates native number syntax here.
        if (!nativeNumber && descriptor.Pattern is not null &&
            !Regex.IsMatch(text, descriptor.Pattern, RegexOptions.CultureInvariant, PatternTimeout))
        {
            return new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
        }

        if (nativeNumber)
        {
            bool parsed = nativeBitrate
                ? NativeIntegerSyntax.TryParseBitrate(text, out int parsedBitrate)
                : NativeIntegerSyntax.TryParse(text, out parsedBitrate);
            bool outsideMinimum = descriptor.Minimum is decimal minimum && parsedBitrate < minimum;
            bool outsideMaximum = descriptor.Maximum is decimal maximum && parsedBitrate > maximum;
            bool negativeUnsigned = (descriptor.ValueKind is OptionValueKind.UnsignedInteger or OptionValueKind.Bitrate) &&
                parsedBitrate < 0;

            if (!parsed || outsideMinimum || outsideMaximum || negativeUnsigned)
            {
                return new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
            }

            numericValue = parsedBitrate;
            return null;
        }

        if (descriptor.ValueKind is OptionValueKind.Integer or OptionValueKind.Decimal)
        {
            bool parsed = decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal number);
            bool invalidUnsigned = descriptor.ValueKind == OptionValueKind.UnsignedInteger && number < 0;
            bool invalidIntegral = descriptor.ValueKind != OptionValueKind.Decimal && number != decimal.Truncate(number);
            bool outsideMinimum = descriptor.Minimum is decimal minimum && number < minimum;
            bool outsideMaximum = descriptor.Maximum is decimal maximum && number > maximum;

            if (!parsed || invalidUnsigned || invalidIntegral || outsideMinimum || outsideMaximum)
            {
                return new OptionDiagnostic(OptionDiagnosticCode.InvalidValue, descriptor.Id);
            }
        }

        return null;
    }

    /// <summary>Determines whether a valid stored option affects the native invocation.</summary>
    private static bool IsActive(OptionDescriptor descriptor, JsonElement value)
    {
        return descriptor.ArgumentShape == OptionArgumentShape.None
            ? value.ValueKind == JsonValueKind.True
            : true;
    }
}
