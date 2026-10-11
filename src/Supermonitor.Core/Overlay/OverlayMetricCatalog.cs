using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Core.Overlay;

public static class OverlayMetricCatalog
{
    public static string Label(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.FrameTime => "Frametime",
        OverlayMetric.Low1Percent => "1% low",
        OverlayMetric.CpuUsage => "CPU",
        OverlayMetric.CpuTemperature => "CPU temp",
        OverlayMetric.CpuClock => "CPU clock",
        OverlayMetric.CpuPower => "CPU power",
        OverlayMetric.GpuUsage => "GPU",
        OverlayMetric.GpuTemperature => "GPU temp",
        OverlayMetric.GpuClock => "GPU clock",
        OverlayMetric.GpuPower => "GPU power",
        OverlayMetric.GpuVram => "VRAM",
        OverlayMetric.Ram => "RAM",
        OverlayMetric.NetworkDown => "Down",
        OverlayMetric.NetworkUp => "Up",
        OverlayMetric.Ping => "Ping",
        OverlayMetric.Clock => "Time",
        _ => metric.ToString(),
    };

    public static MetricUnit Unit(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps or OverlayMetric.Low1Percent => MetricUnit.FramesPerSecond,
        OverlayMetric.FrameTime or OverlayMetric.Ping => MetricUnit.Milliseconds,
        OverlayMetric.CpuUsage or OverlayMetric.GpuUsage or OverlayMetric.Ram => MetricUnit.Percent,
        OverlayMetric.CpuTemperature or OverlayMetric.GpuTemperature => MetricUnit.Celsius,
        OverlayMetric.CpuClock or OverlayMetric.GpuClock => MetricUnit.Megahertz,
        OverlayMetric.CpuPower or OverlayMetric.GpuPower => MetricUnit.Watts,
        OverlayMetric.GpuVram => MetricUnit.Bytes,
        OverlayMetric.NetworkDown or OverlayMetric.NetworkUp => MetricUnit.BytesPerSecond,
        _ => MetricUnit.Count,
    };

    // Id da MetricStore; null para FPS/frametime/1% low (vêm do FrameCaptureService) e para o relógio.
    public static string? StoreMetricId(OverlayMetric metric) => metric switch
    {
        OverlayMetric.CpuUsage => MetricIds.CpuUsage,
        OverlayMetric.CpuTemperature => MetricIds.CpuTemperature,
        OverlayMetric.CpuClock => MetricIds.CpuClock,
        OverlayMetric.CpuPower => MetricIds.CpuPower,
        OverlayMetric.GpuUsage => MetricIds.GpuUsage(0),
        OverlayMetric.GpuTemperature => MetricIds.GpuTemperature(0),
        OverlayMetric.GpuClock => MetricIds.GpuClock(0),
        OverlayMetric.GpuPower => MetricIds.GpuPower(0),
        OverlayMetric.GpuVram => MetricIds.GpuVramUsed(0),
        OverlayMetric.Ram => MetricIds.MemoryUsage,
        OverlayMetric.NetworkDown => MetricIds.NetworkTotalDown,
        OverlayMetric.NetworkUp => MetricIds.NetworkTotalUp,
        OverlayMetric.Ping => MetricIds.Ping,
        _ => null,
    };

    // FPS e 1% low pioram quando caem; os demais pioram quando sobem.
    public static bool LowerIsWorse(OverlayMetric metric) => metric is OverlayMetric.Fps or OverlayMetric.Low1Percent;

    public static Demand? Demand(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps or OverlayMetric.FrameTime or OverlayMetric.Low1Percent => Sensors.Demand.FrameCapture,
        OverlayMetric.CpuTemperature or OverlayMetric.CpuClock or OverlayMetric.CpuPower
            or OverlayMetric.GpuTemperature or OverlayMetric.GpuClock or OverlayMetric.GpuPower => Sensors.Demand.HardwareSensors,
        _ => null,
    };
}

public static class OverlayDemand
{
    public static (bool Hardware, bool Frames) For(OverlayStyle style)
    {
        var demands = style.Items.Select(i => OverlayMetricCatalog.Demand(i.Metric)).ToList();
        return (demands.Contains(Demand.HardwareSensors), demands.Contains(Demand.FrameCapture));
    }
}

public static class OverlayRefresh
{
    // O mini-gráfico de frametime redesenha a até 30 Hz; sem ele vale a taxa configurada.
    public static int EffectiveHz(OverlayStyle style) => style.ShowFrameGraph ? 30 : style.RefreshHz;
}
