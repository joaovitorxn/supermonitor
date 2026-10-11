namespace Supermonitor.Core.Settings;

// Shades used by the WinUI SystemAccentColor{Light,Dark}{1..3} resources.
public readonly record struct AccentShades(
    (byte R, byte G, byte B) Base,
    (byte R, byte G, byte B) Light1,
    (byte R, byte G, byte B) Light2,
    (byte R, byte G, byte B) Light3,
    (byte R, byte G, byte B) Dark1,
    (byte R, byte G, byte B) Dark2,
    (byte R, byte G, byte B) Dark3)
{
    public static AccentShades Compute(byte r, byte g, byte b) => new(
        (r, g, b),
        Lighten(r, g, b, 0.2),
        Lighten(r, g, b, 0.4),
        Lighten(r, g, b, 0.6),
        Darken(r, g, b, 0.25),
        Darken(r, g, b, 0.5),
        Darken(r, g, b, 0.75));

    private static (byte, byte, byte) Lighten(byte r, byte g, byte b, double amount) =>
        (Mix(r, 255, amount), Mix(g, 255, amount), Mix(b, 255, amount));

    private static (byte, byte, byte) Darken(byte r, byte g, byte b, double amount) =>
        (Mix(r, 0, amount), Mix(g, 0, amount), Mix(b, 0, amount));

    private static byte Mix(byte from, byte to, double amount) =>
        (byte)Math.Round(from + (to - from) * amount, MidpointRounding.AwayFromZero);
}
