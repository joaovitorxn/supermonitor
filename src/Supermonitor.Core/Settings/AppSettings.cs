using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultPingHost = "1.1.1.1";
    public const int DefaultSampleIntervalMs = 1000;
    public const int DefaultHistoryMinutes = 1;

    public static IReadOnlyList<int> AllowedIntervalsMs { get; } = [250, 500, 1000, 2000];

    public static IReadOnlyList<int> AllowedHistoryMinutes { get; } = [1, 5, 10];

    public int Version { get; set; } = SettingsMigrator.CurrentVersion;

    public int SampleIntervalMs { get; set; } = DefaultSampleIntervalMs;

    public string PingHost { get; set; } = DefaultPingHost;

    public bool StartMinimized { get; set; }

    public List<CardSettings> Cards { get; set; } = DefaultCards();

    public int HistoryMinutes { get; set; } = DefaultHistoryMinutes;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public string? AccentColor { get; set; }

    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    public bool NetworkInBits { get; set; }

    public bool Fahrenheit { get; set; }

    public Dictionary<string, MetricThreshold> Thresholds { get; set; } = DefaultThresholds();

    public static List<CardSettings> DefaultCards() =>
    [
        new() { MetricId = MetricIds.CpuUsage },
        new() { MetricId = MetricIds.CpuTemperature },
        new() { MetricId = MetricIds.MemoryUsage },
        new() { MetricId = MetricIds.GpuUsage(0) },
        new() { MetricId = MetricIds.GpuTemperature(0) },
        new() { MetricId = MetricIds.GpuVramUsed(0) },
        new() { MetricId = MetricIds.DiskActive(0) },
        new() { MetricId = MetricIds.NetworkTotalDown },
        new() { MetricId = MetricIds.NetworkTotalUp },
        new() { MetricId = MetricIds.Ping },
    ];

    public static Dictionary<string, MetricThreshold> DefaultThresholds() => new()
    {
        [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 },
        [MetricIds.GpuTemperature(0)] = new() { Warning = 80, Critical = 90 },
        [MetricIds.GpuHotspot(0)] = new() { Warning = 90, Critical = 100 },
        [MetricIds.GpuMemoryTemperature(0)] = new() { Warning = 90, Critical = 100 },
    };

    public UnitOptions ToUnitOptions() => new(NetworkInBits, Fahrenheit);

    public void Normalize()
    {
        if (!AllowedIntervalsMs.Contains(SampleIntervalMs))
        {
            SampleIntervalMs = DefaultSampleIntervalMs;
        }

        if (!AllowedHistoryMinutes.Contains(HistoryMinutes))
        {
            HistoryMinutes = DefaultHistoryMinutes;
        }

        PingHost = string.IsNullOrWhiteSpace(PingHost) ? DefaultPingHost : PingHost.Trim();
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System;
        Backdrop = Enum.IsDefined(Backdrop) ? Backdrop : BackdropKind.Mica;
        AccentColor = ColorText.TryParseRgb(AccentColor, out _, out _, out _) ? AccentColor!.ToUpperInvariant() : null;

        Cards = Cards is null
            ? DefaultCards()
            : Cards
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.MetricId))
                .DistinctBy(c => c.MetricId)
                .ToList();
        foreach (var card in Cards)
        {
            card.Size = Enum.IsDefined(card.Size) ? card.Size : CardSize.Normal;
            card.Chart = Enum.IsDefined(card.Chart) ? card.Chart : ChartKind.Line;
        }

        Thresholds ??= DefaultThresholds();
        foreach (var (id, threshold) in Thresholds.ToArray())
        {
            if (threshold is null || !float.IsFinite(threshold.Warning) || !float.IsFinite(threshold.Critical))
            {
                Thresholds.Remove(id);
            }
            else if (threshold.Warning > threshold.Critical)
            {
                (threshold.Warning, threshold.Critical) = (threshold.Critical, threshold.Warning);
            }
        }
    }
}
