namespace Supermonitor.Core.Metrics;

public enum MetricUnit
{
    Percent,
    Bytes,
    BytesPerSecond,
    Celsius,
    Megahertz,
    Watts,
    Volts,
    Rpm,
    Milliseconds,
    FramesPerSecond,
    Count,
}

public enum MetricCategory
{
    Cpu,
    Memory,
    Gpu,
    Disk,
    Network,
    Frame,
    System,
}

public sealed record MetricDescriptor(string Id, string DisplayName, MetricCategory Category, MetricUnit Unit);

public readonly record struct MetricHandle(int Index);
