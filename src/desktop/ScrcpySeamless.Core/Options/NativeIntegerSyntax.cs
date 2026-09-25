namespace ScrcpySeamless.Core.Options;

/// <summary>Interprets the native Windows CLI's signed 32-bit, base-zero integer syntax.</summary>
internal static class NativeIntegerSyntax
{
    private const int Kilo = 1_000;
    private const int Mega = 1_000_000;

    /// <summary>Parses a complete scalar token without accepting C library whitespace quirks.</summary>
    public static bool TryParse(string text, out int value)
    {
        value = 0;

        if (text.Length == 0)
        {
            return false;
        }

        int index = text[0] is '+' or '-' ? 1 : 0;
        bool negative = text[0] == '-';

        if (index == text.Length)
        {
            return false;
        }

        int numberBase = 10;

        if (text[index] == '0')
        {
            numberBase = 8;

            if (index + 1 < text.Length && text[index + 1] is 'x' or 'X')
            {
                numberBase = 16;
                index += 2;
            }
        }

        long magnitude = 0;
        long limit = negative ? -(long)int.MinValue : int.MaxValue;
        bool hasDigit = false;

        for (; index < text.Length; index++)
        {
            int digit = DigitValue(text[index]);

            if (digit < 0 || digit >= numberBase || magnitude > (limit - digit) / numberBase)
            {
                return false;
            }

            magnitude = magnitude * numberBase + digit;
            hasDigit = true;
        }

        if (!hasDigit)
        {
            return false;
        }

        value = (int)(negative ? -magnitude : magnitude);
        return true;
    }

    /// <summary>Applies the native K/M bitrate multiplier after base-zero parsing.</summary>
    public static bool TryParseBitrate(string text, out int value)
    {
        value = 0;

        if (text.Length == 0)
        {
            return false;
        }

        int multiplier = text[^1] switch
        {
            'k' or 'K' => Kilo,
            'm' or 'M' => Mega,
            _ => 1,
        };
        string numberText = multiplier == 1 ? text : text[..^1];

        if (!TryParse(numberText, out int parsed))
        {
            return false;
        }

        long scaled = (long)parsed * multiplier;

        if (scaled < int.MinValue || scaled > int.MaxValue)
        {
            return false;
        }

        value = (int)scaled;
        return true;
    }

    /// <summary>Maps only ASCII base-zero digits to their numeric values.</summary>
    private static int DigitValue(char character)
    {
        return character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };
    }
}
