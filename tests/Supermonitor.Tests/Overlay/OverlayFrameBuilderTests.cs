using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Overlay;

public class OverlayFrameBuilderTests
{
    private sealed class FakeValues : IOverlayValues
    {
        public Dictionary<OverlayMetric, double> Values { get; } = [];

        public DateTime Now { get; set; } = new(2026, 10, 10, 9, 5, 0);

        public long FrameVersion { get; set; }

        public float[] FrameTimes { get; set; } = [];

        public double Get(OverlayMetric metric) => Values.GetValueOrDefault(metric, double.NaN);

        public int CopyFrameTimes(Span<float> destination)
        {
            var count = Math.Min(destination.Length, FrameTimes.Length);
            FrameTimes.AsSpan(FrameTimes.Length - count).CopyTo(destination);
            return count;
        }
    }

    private static readonly IReadOnlyDictionary<string, MetricThreshold> NoThresholds = new Dictionary<string, MetricThreshold>();

    private static OverlayFrame Build(OverlayStyle style, FakeValues values, UnitOptions? units = null, IReadOnlyDictionary<string, MetricThreshold>? thresholds = null) =>
        OverlayFrameBuilder.Build(style, values, units ?? UnitOptions.Default, thresholds ?? NoThresholds);

    [Fact]
    public void Rows_follow_the_item_order_with_labels_and_formatted_values()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 143.6;
        values.Values[OverlayMetric.FrameTime] = 6.94;
        values.Values[OverlayMetric.CpuUsage] = 23.4;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuUsage, OverlayMetric.Fps, OverlayMetric.FrameTime) };

        var frame = Build(style, values);

        Assert.Equal(new[] { "CPU", "FPS", "Frametime" }, frame.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "23%", "144", "6.9 ms" }, frame.Rows.Select(r => r.Value));
    }

    [Fact]
    public void Missing_values_show_na()
    {
        var frame = Build(new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Fps, OverlayMetric.GpuTemperature) }, new FakeValues());

        Assert.All(frame.Rows, r => Assert.Equal("N/A", r.Value));
    }

    [Fact]
    public void Units_follow_the_user_options()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 50;
        values.Values[OverlayMetric.NetworkDown] = 1_000_000;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuTemperature, OverlayMetric.NetworkDown) };

        var celsius = Build(style, values);
        var imperial = Build(style, values, new UnitOptions(NetworkInBits: true, Fahrenheit: true));

        Assert.Equal("50 °C", celsius.Rows[0].Value);
        Assert.Equal("122 °F", imperial.Rows[0].Value);
        Assert.Equal("8.0 Mbps", imperial.Rows[1].Value);
    }

    [Fact]
    public void Clock_shows_hours_and_minutes()
    {
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Clock) };

        var frame = Build(style, new FakeValues { Now = new DateTime(2026, 10, 10, 21, 7, 33) });

        Assert.Equal("21:07", frame.Rows[0].Value);
    }

    [Fact]
    public void Colors_come_from_the_style_unless_the_item_overrides_them()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 100;
        values.Values[OverlayMetric.Ping] = 20;
        var style = new OverlayStyle
        {
            LabelColor = "#111111",
            ValueColor = "#222222",
            Items =
            [
                new() { Metric = OverlayMetric.Fps },
                new() { Metric = OverlayMetric.Ping, LabelColor = "#AA0000", ValueColor = "#00BB00" },
            ],
        };

        var frame = Build(style, values);

        Assert.Equal(new Rgba(0x11, 0x11, 0x11), frame.Rows[0].LabelColor);
        Assert.Equal(new Rgba(0x22, 0x22, 0x22), frame.Rows[0].ValueColor);
        Assert.Equal(new Rgba(0xAA, 0, 0), frame.Rows[1].LabelColor);
        Assert.Equal(new Rgba(0, 0xBB, 0), frame.Rows[1].ValueColor);
    }

    [Fact]
    public void Thresholds_color_the_value_using_the_base_unit()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 85;
        values.Values[OverlayMetric.GpuTemperature] = 95;
        values.Values[OverlayMetric.CpuUsage] = 10;
        var thresholds = new Dictionary<string, MetricThreshold>
        {
            [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 },
            [MetricIds.GpuTemperature(0)] = new() { Warning = 80, Critical = 90 },
        };
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuTemperature, OverlayMetric.GpuTemperature, OverlayMetric.CpuUsage) };

        var frame = Build(style, values, new UnitOptions(Fahrenheit: true), thresholds);

        Assert.Equal(OverlayFrameBuilder.WarningColor, frame.Rows[0].ValueColor);
        Assert.Equal(OverlayFrameBuilder.CriticalColor, frame.Rows[1].ValueColor);
        Assert.Equal(new Rgba(0xFF, 0xFF, 0xFF), frame.Rows[2].ValueColor);
    }

    [Fact]
    public void An_item_threshold_wins_over_the_global_one()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 85;
        var thresholds = new Dictionary<string, MetricThreshold> { [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 } };
        var style = new OverlayStyle
        {
            Items = [new() { Metric = OverlayMetric.CpuTemperature, Threshold = new MetricThreshold { Warning = 60, Critical = 80 } }],
        };

        var frame = Build(style, values, thresholds: thresholds);

        Assert.Equal(OverlayFrameBuilder.CriticalColor, frame.Rows[0].ValueColor);
    }

    [Fact]
    public void Graph_is_filled_only_when_enabled_and_sized_by_the_graph_width()
    {
        var values = new FakeValues { FrameVersion = 7, FrameTimes = Enumerable.Range(1, 500).Select(i => (float)i).ToArray() };

        var off = Build(new OverlayStyle { ShowFrameGraph = false }, values);
        var on = Build(new OverlayStyle { ShowFrameGraph = true, GraphWidth = 160 }, values);

        Assert.Empty(off.Graph);
        Assert.Equal(80, on.Graph.Length);
        Assert.Equal(500f, on.Graph[^1]);
        Assert.Equal(7, on.GraphVersion);
    }

    [Fact]
    public void Same_content_ignores_the_graph_version_when_the_graph_is_off()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 60;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Fps) };

        var first = Build(style, values);
        values.FrameVersion = 99;
        var second = Build(style, values);
        values.Values[OverlayMetric.Fps] = 61;
        var third = Build(style, values);

        Assert.True(first.SameContentAs(second));
        Assert.False(first.SameContentAs(third));
        Assert.False(first.SameContentAs(null));
    }

    [Fact]
    public void Same_content_notices_a_new_frame_when_the_graph_is_on()
    {
        var values = new FakeValues { FrameTimes = [10f, 11f] };
        var style = new OverlayStyle { ShowFrameGraph = true, Items = OverlayStyle.ItemsOf(OverlayMetric.Fps) };

        values.FrameVersion = 1;
        var first = Build(style, values);
        values.FrameVersion = 2;
        var second = Build(style, values);

        Assert.False(first.SameContentAs(second));
    }

    [Fact]
    public void Demo_values_build_a_full_frame_for_every_metric()
    {
        var demo = new DemoOverlayValues();

        var frame = Build(OverlayPresets.Full(), demo);

        Assert.Equal(Enum.GetValues<OverlayMetric>().Length, frame.Rows.Length);
        Assert.All(frame.Rows, r => Assert.NotEqual("N/A", r.Value));
        Assert.NotEmpty(frame.Graph);
    }

    private static OverlayFrame Build(OverlayStyle style, IOverlayValues values) =>
        OverlayFrameBuilder.Build(style, values, UnitOptions.Default, NoThresholds);

    [Fact]
    public void Frame_rate_thresholds_turn_the_value_warning_or_critical_when_it_drops_below_them()
    {
        var style = new OverlayStyle
        {
            Items = [new() { Metric = OverlayMetric.Fps, Threshold = new MetricThreshold { Warning = 60, Critical = 30 } }],
        };
        var values = new FakeValues();

        values.Values[OverlayMetric.Fps] = 144;
        var healthy = Build(style, values);
        values.Values[OverlayMetric.Fps] = 50;
        var low = Build(style, values);
        values.Values[OverlayMetric.Fps] = 20;
        var bad = Build(style, values);

        Assert.Equal(new Rgba(0xFF, 0xFF, 0xFF), healthy.Rows[0].ValueColor);
        Assert.Equal(OverlayFrameBuilder.WarningColor, low.Rows[0].ValueColor);
        Assert.Equal(OverlayFrameBuilder.CriticalColor, bad.Rows[0].ValueColor);
    }

    [Fact]
    public void Frame_time_thresholds_still_grow_worse_with_the_value()
    {
        var style = new OverlayStyle
        {
            Items = [new() { Metric = OverlayMetric.FrameTime, Threshold = new MetricThreshold { Warning = 20, Critical = 33 } }],
        };
        var values = new FakeValues();
        values.Values[OverlayMetric.FrameTime] = 40;

        var frame = Build(style, values);

        Assert.Equal(OverlayFrameBuilder.CriticalColor, frame.Rows[0].ValueColor);
    }
}
