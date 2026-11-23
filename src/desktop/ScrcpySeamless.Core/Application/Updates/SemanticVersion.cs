namespace ScrcpySeamless.Core.Application.Updates;

/// <summary>Bounded SemVer 2.0 syntax and precedence without fixed-width numeric conversion.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    public const int MaximumLength = 256;
    private readonly string[] core;
    private readonly string[] prerelease;

    private SemanticVersion(string value, string[] core, string[] prerelease)
    {
        Value = value;
        this.core = core;
        this.prerelease = prerelease;
    }

    public string Value { get; }
    public bool IsPrerelease => prerelease.Length != 0;

    /// <summary>Accepts SemVer itself; repository tag prefixes are a separate boundary.</summary>
    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;

        if (value is null || value.Length is 0 or > MaximumLength)
        {
            return false;
        }

        string[] build = value.Split('+');

        if (build.Length > 2 || build.Length == 2 && !ValidIdentifiers(build[1], numericLeadingZero: true))
        {
            return false;
        }

        int separator = build[0].IndexOf('-');
        string coreValue = separator < 0 ? build[0] : build[0][..separator];
        string[] prerelease = separator < 0 ? [] : build[0][(separator + 1)..].Split('.');

        if (separator >= 0 && !ValidIdentifiers(build[0][(separator + 1)..], numericLeadingZero: false))
        {
            return false;
        }

        string[] core = coreValue.Split('.');

        if (core.Length != 3 || core.Any(identifier => !Numeric(identifier) || LeadingZero(identifier)))
        {
            return false;
        }

        version = new SemanticVersion(value, core, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (int index = 0; index < core.Length; index++)
        {
            int order = CompareNumeric(core[index], other.core[index]);

            if (order != 0)
            {
                return order;
            }
        }

        if (!IsPrerelease || !other.IsPrerelease)
        {
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        }

        for (int index = 0; index < Math.Min(prerelease.Length, other.prerelease.Length); index++)
        {
            string left = prerelease[index];
            string right = other.prerelease[index];
            bool leftNumeric = Numeric(left);
            bool rightNumeric = Numeric(right);
            int order = leftNumeric && rightNumeric ? CompareNumeric(left, right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1
                : string.CompareOrdinal(left, right);

            if (order != 0)
            {
                return order;
            }
        }

        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    public override string ToString() => Value;

    private static bool ValidIdentifiers(string value, bool numericLeadingZero)
    {
        foreach (string identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(character =>
                    character is not (>= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-')))
            {
                return false;
            }

            if (!numericLeadingZero && Numeric(identifier) && LeadingZero(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Numeric(string value) => value.Length != 0 &&
        value.All(character => character is >= '0' and <= '9');

    private static bool LeadingZero(string value) => value.Length > 1 && value[0] == '0';

    private static int CompareNumeric(string left, string right) => left.Length != right.Length
        ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
}
