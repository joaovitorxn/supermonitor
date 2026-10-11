using System.Globalization;

namespace Supermonitor.Core.Settings;

public static class ColorText
{
    public static bool TryParseRgb(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (text is not { Length: 7 } || text[0] != '#')
        {
            return false;
        }

        return byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
