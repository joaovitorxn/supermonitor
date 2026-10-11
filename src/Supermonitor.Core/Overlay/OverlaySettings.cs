using System.Text.Json.Serialization;
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

[JsonConverter(typeof(LenientEnumConverter<OverlayAnchor>))]
public enum OverlayAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

[JsonConverter(typeof(LenientEnumConverter<OverlayLayout>))]
public enum OverlayLayout
{
    Horizontal,
    Vertical,
    Compact,
}

[JsonConverter(typeof(LenientEnumConverter<OverlayMetric>))]
public enum OverlayMetric
{
    Fps,
    FrameTime,
    Low1Percent,
    CpuUsage,
    CpuTemperature,
    CpuClock,
    CpuPower,
    GpuUsage,
    GpuTemperature,
    GpuClock,
    GpuPower,
    GpuVram,
    Ram,
    NetworkDown,
    NetworkUp,
    Ping,
    Clock,
}

public sealed class OverlayItemSettings
{
    public OverlayMetric Metric { get; set; }

    public string? LabelColor { get; set; }

    public string? ValueColor { get; set; }

    public MetricThreshold? Threshold { get; set; }

    public OverlayItemSettings Clone() => new()
    {
        Metric = Metric,
        LabelColor = LabelColor,
        ValueColor = ValueColor,
        Threshold = Threshold is null ? null : new MetricThreshold { Warning = Threshold.Warning, Critical = Threshold.Critical },
    };
}

public sealed class OverlayStyle
{
    public const string DefaultFontFamily = "Segoe UI";
    public const string DefaultLabelColor = "#B0B0B0";
    public const string DefaultValueColor = "#FFFFFF";
    public const string DefaultBackgroundColor = "#000000";
    public const int DefaultRefreshHz = 4;
    private const int MaxOffset = 4000;

    public static IReadOnlyList<int> AllowedRefreshHz { get; } = [1, 2, 4, 10, 30];

    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopLeft;

    public int OffsetX { get; set; } = 16;

    public int OffsetY { get; set; } = 16;

    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public float FontSize { get; set; } = 14;

    public string LabelColor { get; set; } = DefaultLabelColor;

    public string ValueColor { get; set; } = DefaultValueColor;

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    public float BackgroundOpacity { get; set; } = 0.55f;

    public int CornerRadius { get; set; } = 8;

    public int Padding { get; set; } = 10;

    public bool ShowFrameGraph { get; set; }

    public int GraphWidth { get; set; } = 160;

    public int GraphHeight { get; set; } = 40;

    public int RefreshHz { get; set; } = DefaultRefreshHz;

    public List<OverlayItemSettings> Items { get; set; } = DefaultItems();

    public static List<OverlayItemSettings> DefaultItems() => ItemsOf(
        OverlayMetric.Fps,
        OverlayMetric.FrameTime,
        OverlayMetric.Low1Percent,
        OverlayMetric.CpuUsage,
        OverlayMetric.CpuTemperature,
        OverlayMetric.GpuUsage,
        OverlayMetric.GpuTemperature,
        OverlayMetric.Ram);

    public static List<OverlayItemSettings> ItemsOf(params OverlayMetric[] metrics) =>
        metrics.Select(m => new OverlayItemSettings { Metric = m }).ToList();

    public OverlayStyle Clone()
    {
        var copy = (OverlayStyle)MemberwiseClone();
        copy.Items = Items.Select(i => i.Clone()).ToList();
        return copy;
    }

    public void Normalize()
    {
        Anchor = Enum.IsDefined(Anchor) ? Anchor : OverlayAnchor.TopLeft;
        Layout = Enum.IsDefined(Layout) ? Layout : OverlayLayout.Vertical;
        OffsetX = Math.Clamp(OffsetX, -MaxOffset, MaxOffset);
        OffsetY = Math.Clamp(OffsetY, -MaxOffset, MaxOffset);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? DefaultFontFamily : FontFamily.Trim();
        FontSize = float.IsFinite(FontSize) ? Math.Clamp(FontSize, 8f, 48f) : 14f;
        LabelColor = ValidColor(LabelColor) ?? DefaultLabelColor;
        ValueColor = ValidColor(ValueColor) ?? DefaultValueColor;
        BackgroundColor = ValidColor(BackgroundColor) ?? DefaultBackgroundColor;
        BackgroundOpacity = float.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0f, 1f) : 0.55f;
        CornerRadius = Math.Clamp(CornerRadius, 0, 32);
        Padding = Math.Clamp(Padding, 0, 48);
        GraphWidth = Math.Clamp(GraphWidth, 40, 600);
        GraphHeight = Math.Clamp(GraphHeight, 16, 200);
        RefreshHz = AllowedRefreshHz.Contains(RefreshHz) ? RefreshHz : DefaultRefreshHz;

        Items = Items is null
            ? DefaultItems()
            : Items.Where(i => i is not null && Enum.IsDefined(i.Metric)).DistinctBy(i => i.Metric).ToList();
        foreach (var item in Items)
        {
            item.LabelColor = ValidColor(item.LabelColor);
            item.ValueColor = ValidColor(item.ValueColor);
            if (item.Threshold is { } threshold)
            {
                if (!float.IsFinite(threshold.Warning) || !float.IsFinite(threshold.Critical))
                {
                    item.Threshold = null;
                }
                else if (OverlayMetricCatalog.LowerIsWorse(item.Metric)
                    ? threshold.Warning < threshold.Critical
                    : threshold.Warning > threshold.Critical)
                {
                    (threshold.Warning, threshold.Critical) = (threshold.Critical, threshold.Warning);
                }
            }
        }
    }

    private static string? ValidColor(string? text) =>
        ColorText.TryParseRgb(text, out _, out _, out _) ? text!.ToUpperInvariant() : null;
}

public sealed class OverlayPreset
{
    public string Name { get; set; } = string.Empty;

    public OverlayStyle Style { get; set; } = new();
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; }

    public string? MonitorId { get; set; }

    public OverlayStyle Style { get; set; } = OverlayPresets.Default();

    public List<OverlayPreset> UserPresets { get; set; } = [];

    public void Normalize()
    {
        MonitorId = string.IsNullOrWhiteSpace(MonitorId) ? null : MonitorId.Trim();
        Style ??= OverlayPresets.Default();
        Style.Normalize();

        var seen = new HashSet<string>(OverlayPresets.BuiltInNames, StringComparer.OrdinalIgnoreCase);
        var kept = new List<OverlayPreset>();
        foreach (var preset in UserPresets ?? [])
        {
            var name = preset?.Name?.Trim();
            if (preset?.Style is null || string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            preset.Name = name;
            preset.Style.Normalize();
            kept.Add(preset);
        }

        UserPresets = kept;
    }
}
