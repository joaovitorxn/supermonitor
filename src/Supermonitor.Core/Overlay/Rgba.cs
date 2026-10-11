using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static bool TryParse(string? hex, out Rgba color)
    {
        if (ColorText.TryParseRgb(hex, out var r, out var g, out var b))
        {
            color = new Rgba(r, g, b);
            return true;
        }

        color = default;
        return false;
    }

    public static Rgba Parse(string? hex, Rgba fallback) => TryParse(hex, out var color) ? color : fallback;
}
