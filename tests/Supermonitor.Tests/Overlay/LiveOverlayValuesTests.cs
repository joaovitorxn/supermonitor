using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Sensors;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public class LiveOverlayValuesTests
{
    private sealed class NoProbe : IForegroundProbe
    {
        public int GetForegroundProcessId() => 0;
    }

    private static FrameCaptureService IdleFrames(FakeTimeProvider time) =>
        new(new DemandTracker(), () => throw new InvalidOperationException("not used"), new NoProbe(), time, 1, runLoop: false);

    [Fact]
    public void Store_backed_metrics_read_the_latest_value()
    {
        var store = new MetricStore(10);
        var expected = new Dictionary<OverlayMetric, double>();
        var number = 1;
        foreach (var metric in Enum.GetValues<OverlayMetric>())
        {
            if (OverlayMetricCatalog.StoreMetricId(metric) is not { } id)
            {
                continue;
            }

            var handle = store.Register(new MetricDescriptor(id, metric.ToString(), MetricCategory.System, OverlayMetricCatalog.Unit(metric)), "test");
            store.Write(handle, number);
            expected[metric] = number++;
        }

        store.CommitTick();
        var time = new FakeTimeProvider();
        using var frames = IdleFrames(time);
        var values = new LiveOverlayValues(store, frames, time);

        Assert.NotEmpty(expected);
        foreach (var (metric, value) in expected)
        {
            Assert.Equal(value, values.Get(metric));
        }
    }

    [Fact]
    public void Frame_metrics_and_missing_store_metrics_are_nan_without_data()
    {
        var time = new FakeTimeProvider();
        using var frames = IdleFrames(time);
        var values = new LiveOverlayValues(new MetricStore(10), frames, time);

        Assert.True(double.IsNaN(values.Get(OverlayMetric.Fps)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.FrameTime)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.Low1Percent)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.CpuUsage)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.Clock)));
        Assert.Equal(0, values.CopyFrameTimes(new float[10]));
    }

    [Fact]
    public void Now_is_the_local_time_of_the_provider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 21, 7, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var frames = IdleFrames(time);

        var values = new LiveOverlayValues(new MetricStore(10), frames, time);

        Assert.Equal(new DateTime(2026, 10, 10, 21, 7, 0), values.Now);
    }
}
