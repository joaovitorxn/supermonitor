namespace Supermonitor.Core.Overlay;

public interface IOverlayValues
{
    DateTime Now { get; }

    // Changes on every new frame; the overlay uses it to redraw the mini-graph.
    long FrameVersion { get; }

    // Value in the metric's base unit; NaN when unavailable.
    double Get(OverlayMetric metric);

    int CopyFrameTimes(Span<float> destination);
}

// Fixed, plausible values for the editor preview.
public sealed class DemoOverlayValues : IOverlayValues
{
    private const int FrameCount = 300;

    public DateTime Now => new(2026, 10, 10, 21, 7, 0);

    public long FrameVersion => 1;

    public double Get(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => 143,
        OverlayMetric.FrameTime => 6.9,
        OverlayMetric.Low1Percent => 98,
        OverlayMetric.CpuUsage => 23,
        OverlayMetric.CpuTemperature => 62,
        OverlayMetric.CpuClock => 4950,
        OverlayMetric.CpuPower => 71.5,
        OverlayMetric.GpuUsage => 97,
        OverlayMetric.GpuTemperature => 71,
        OverlayMetric.GpuClock => 2850,
        OverlayMetric.GpuPower => 245,
        OverlayMetric.GpuVram => 9.4 * 1024 * 1024 * 1024,
        OverlayMetric.Ram => 48,
        OverlayMetric.NetworkDown => 1_250_000,
        OverlayMetric.NetworkUp => 85_000,
        OverlayMetric.Ping => 12,
        _ => double.NaN,
    };

    public int CopyFrameTimes(Span<float> destination)
    {
        var count = Math.Min(destination.Length, FrameCount);
        for (var i = 0; i < count; i++)
        {
            var phase = (FrameCount - count + i) / 14d;
            destination[i] = 6.9f + (float)(Math.Sin(phase) * 1.2) + (i % 47 == 0 ? 9f : 0f);
        }

        return count;
    }
}
