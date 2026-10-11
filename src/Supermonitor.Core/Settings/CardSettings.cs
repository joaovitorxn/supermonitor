namespace Supermonitor.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum BackdropKind
{
    Mica,
    MicaAlt,
    Acrylic,
    Solid,
}

public enum CardSize
{
    Compact,
    Normal,
    Large,
}

public enum ChartKind
{
    Line,
    Area,
    Bar,
}

public sealed class CardSettings
{
    public string MetricId { get; set; } = string.Empty;

    public CardSize Size { get; set; } = CardSize.Normal;

    public ChartKind Chart { get; set; } = ChartKind.Line;
}

public sealed class MetricThreshold
{
    public float Warning { get; set; }

    public float Critical { get; set; }
}
