namespace Supermonitor.Core.Overlay;

public static class OverlayPresets
{
    public const string MinimalName = "Minimal";
    public const string DefaultName = "Default";
    public const string FullName = "Full";

    public static IReadOnlyList<string> BuiltInNames { get; } = [MinimalName, DefaultName, FullName];

    // Só FPS, numa linha.
    public static OverlayStyle Minimal() => new()
    {
        Layout = OverlayLayout.Compact,
        Padding = 6,
        BackgroundOpacity = 0.45f,
        Items = OverlayStyle.ItemsOf(OverlayMetric.Fps),
    };

    public static OverlayStyle Default() => new() { ShowFrameGraph = true };

    public static OverlayStyle Full() => new()
    {
        FontSize = 13,
        GraphWidth = 200,
        ShowFrameGraph = true,
        Items = OverlayStyle.ItemsOf(Enum.GetValues<OverlayMetric>()),
    };

    public static IReadOnlyList<OverlayPreset> BuiltIn() =>
    [
        new OverlayPreset { Name = MinimalName, Style = Minimal() },
        new OverlayPreset { Name = DefaultName, Style = Default() },
        new OverlayPreset { Name = FullName, Style = Full() },
    ];
}
