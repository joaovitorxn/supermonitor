using System.Globalization;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public static class OverlayFrameBuilder
{
    public static readonly Rgba WarningColor = new(0xFF, 0xB9, 0x00);
    public static readonly Rgba CriticalColor = new(0xE8, 0x11, 0x23);

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static OverlayFrame Build(OverlayStyle style, IOverlayValues values, UnitOptions units, IReadOnlyDictionary<string, MetricThreshold> thresholds)
    {
        var defaultLabel = Rgba.Parse(style.LabelColor, new Rgba(0xB0, 0xB0, 0xB0));
        var defaultValue = Rgba.Parse(style.ValueColor, new Rgba(0xFF, 0xFF, 0xFF));
        var now = values.Now;

        var rows = new OverlayRow[style.Items.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var item = style.Items[i];
            var value = values.Get(item.Metric);
            var valueColor = Rgba.Parse(item.ValueColor, defaultValue);

            var threshold = item.Threshold;
            if (threshold is null && OverlayMetricCatalog.StoreMetricId(item.Metric) is { } id)
            {
                thresholds.TryGetValue(id, out threshold);
            }

            valueColor = ThresholdEvaluator.Evaluate((float)value, threshold, OverlayMetricCatalog.LowerIsWorse(item.Metric)) switch
            {
                ThresholdLevel.Critical => CriticalColor,
                ThresholdLevel.Warning => WarningColor,
                _ => valueColor,
            };

            rows[i] = new OverlayRow(
                item.CustomLabel ?? OverlayMetricCatalog.Label(item.Metric),
                FormatValue(item.Metric, value, now, units),
                Rgba.Parse(item.LabelColor, defaultLabel),
                valueColor);
        }

        var graph = Array.Empty<float>();
        long version = 0;
        if (style.ShowFrameGraph)
        {
            // Two samples per pixel of graph width.
            var buffer = new float[Math.Max(1, style.GraphWidth / 2)];
            var copied = values.CopyFrameTimes(buffer);
            graph = copied == buffer.Length ? buffer : buffer.AsSpan(0, copied).ToArray();
            version = values.FrameVersion;
        }

        return new OverlayFrame(rows, graph, version);
    }

    public static string FormatValue(OverlayMetric metric, double value, DateTime now, UnitOptions units)
    {
        if (metric == OverlayMetric.Clock)
        {
            return now.ToString("HH:mm", Culture);
        }

        if (!double.IsFinite(value))
        {
            return "N/A";
        }

        // The label already says "FPS"; the value shows only the number.
        if (metric is OverlayMetric.Fps or OverlayMetric.Low1Percent)
        {
            return value.ToString("0", Culture);
        }

        return UnitFormatter.Format(value, OverlayMetricCatalog.Unit(metric), units);
    }
}
