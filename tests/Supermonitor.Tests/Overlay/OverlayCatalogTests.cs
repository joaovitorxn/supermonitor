using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Overlay;

public class OverlayCatalogTests
{
    [Fact]
    public void Every_metric_has_a_label_and_a_unit()
    {
        foreach (var metric in Enum.GetValues<OverlayMetric>())
        {
            Assert.False(string.IsNullOrWhiteSpace(OverlayMetricCatalog.Label(metric)), metric.ToString());
            Assert.True(Enum.IsDefined(OverlayMetricCatalog.Unit(metric)), metric.ToString());
        }
    }

    [Fact]
    public void Store_backed_metrics_use_known_ids_and_frame_metrics_have_none()
    {
        Assert.Equal(MetricIds.CpuUsage, OverlayMetricCatalog.StoreMetricId(OverlayMetric.CpuUsage));
        Assert.Equal(MetricIds.GpuTemperature(0), OverlayMetricCatalog.StoreMetricId(OverlayMetric.GpuTemperature));
        Assert.Equal(MetricIds.GpuVramUsed(0), OverlayMetricCatalog.StoreMetricId(OverlayMetric.GpuVram));
        Assert.Equal(MetricIds.MemoryUsage, OverlayMetricCatalog.StoreMetricId(OverlayMetric.Ram));
        Assert.Equal(MetricIds.NetworkTotalDown, OverlayMetricCatalog.StoreMetricId(OverlayMetric.NetworkDown));
        Assert.Equal(MetricIds.Ping, OverlayMetricCatalog.StoreMetricId(OverlayMetric.Ping));
        Assert.Null(OverlayMetricCatalog.StoreMetricId(OverlayMetric.Fps));
        Assert.Null(OverlayMetricCatalog.StoreMetricId(OverlayMetric.Clock));
    }

    [Fact]
    public void Demand_follows_the_sensor_behind_each_metric()
    {
        Assert.Equal(Demand.FrameCapture, OverlayMetricCatalog.Demand(OverlayMetric.Fps));
        Assert.Equal(Demand.FrameCapture, OverlayMetricCatalog.Demand(OverlayMetric.Low1Percent));
        Assert.Equal(Demand.HardwareSensors, OverlayMetricCatalog.Demand(OverlayMetric.CpuTemperature));
        Assert.Equal(Demand.HardwareSensors, OverlayMetricCatalog.Demand(OverlayMetric.GpuPower));
        Assert.Null(OverlayMetricCatalog.Demand(OverlayMetric.CpuUsage));
        Assert.Null(OverlayMetricCatalog.Demand(OverlayMetric.Ram));
    }

    [Fact]
    public void Demand_for_a_style_unions_its_items()
    {
        var minimal = OverlayDemand.For(OverlayPresets.Minimal());
        var full = OverlayDemand.For(OverlayPresets.Full());
        var network = OverlayDemand.For(new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Ping, OverlayMetric.Clock) });

        Assert.Equal((false, true), minimal);
        Assert.Equal((true, true), full);
        Assert.Equal((false, false), network);
    }

    [Fact]
    public void Refresh_is_thirty_hertz_with_the_graph_and_the_configured_rate_without()
    {
        Assert.Equal(4, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 4, ShowFrameGraph = false }));
        Assert.Equal(1, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 1 }));
        Assert.Equal(30, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 4, ShowFrameGraph = true }));
    }
}
