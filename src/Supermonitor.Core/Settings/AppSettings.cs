using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultPingHost = "1.1.1.1";
    public const int DefaultSampleIntervalMs = 1000;

    public static IReadOnlyList<int> AllowedIntervalsMs { get; } = [250, 500, 1000, 2000];

    public static IReadOnlyList<string> DefaultOverviewCards { get; } =
    [
        MetricIds.CpuUsage,
        MetricIds.MemoryUsage,
        MetricIds.GpuUsage(0),
        MetricIds.GpuVramUsed(0),
        MetricIds.DiskActive(0),
        MetricIds.NetworkTotalDown,
        MetricIds.NetworkTotalUp,
        MetricIds.Ping,
    ];

    public int Version { get; set; } = SettingsMigrator.CurrentVersion;

    public int SampleIntervalMs { get; set; } = DefaultSampleIntervalMs;

    public string PingHost { get; set; } = DefaultPingHost;

    public bool StartMinimized { get; set; }

    public List<string> OverviewCards { get; set; } = [.. DefaultOverviewCards];

    public void Normalize()
    {
        if (!AllowedIntervalsMs.Contains(SampleIntervalMs))
        {
            SampleIntervalMs = DefaultSampleIntervalMs;
        }

        PingHost = string.IsNullOrWhiteSpace(PingHost) ? DefaultPingHost : PingHost.Trim();
        OverviewCards ??= [.. DefaultOverviewCards];
    }
}
