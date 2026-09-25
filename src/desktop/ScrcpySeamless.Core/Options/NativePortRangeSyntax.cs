namespace ScrcpySeamless.Core.Options;

/// <summary>Interprets the native CLI's one- or two-component base-zero port range.</summary>
internal static class NativePortRangeSyntax
{
    private const int MaximumPort = ushort.MaxValue;

    /// <summary>Parses and orders the effective range without changing its stored spelling.</summary>
    public static bool TryParse(string text, out int first, out int last)
    {
        first = 0;
        last = 0;

        int delimiter = text.IndexOf(':');
        string firstText = delimiter < 0 ? text : text[..delimiter];
        string lastText = delimiter < 0 ? firstText : text[(delimiter + 1)..];

        if (!NativeIntegerSyntax.TryParse(firstText, out int firstPort) ||
            !NativeIntegerSyntax.TryParse(lastText, out int lastPort) ||
            firstPort is < 0 or > MaximumPort ||
            lastPort is < 0 or > MaximumPort)
        {
            return false;
        }

        first = Math.Min(firstPort, lastPort);
        last = Math.Max(firstPort, lastPort);
        return true;
    }
}
