using System.Globalization;

namespace Supermonitor.Core.Metrics;

public sealed record UnitOptions(bool NetworkInBits = false, bool Fahrenheit = false)
{
    public static UnitOptions Default { get; } = new();
}

public static class UnitFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];
    private static readonly string[] BitUnits = ["bps", "Kbps", "Mbps", "Gbps"];

    public static string Format(double value, MetricUnit unit, UnitOptions options)
    {
        if (!double.IsFinite(value))
        {
            return "N/A";
        }

        return unit switch
        {
            MetricUnit.Percent => value.ToString("0", Culture) + "%",
            MetricUnit.Bytes => FormatBytes(value, string.Empty),
            MetricUnit.BytesPerSecond => options.NetworkInBits ? FormatBits(value * 8) : FormatBytes(value, "/s"),
            MetricUnit.Celsius => options.Fahrenheit
                ? (value * 9 / 5 + 32).ToString("0", Culture) + " °F"
                : value.ToString("0", Culture) + " °C",
            MetricUnit.Megahertz => value >= 1000
                ? (value / 1000).ToString("0.00", Culture) + " GHz"
                : value.ToString("0", Culture) + " MHz",
            MetricUnit.Watts => value.ToString("0.0", Culture) + " W",
            MetricUnit.Volts => value.ToString("0.000", Culture) + " V",
            MetricUnit.Rpm => value.ToString("0", Culture) + " RPM",
            MetricUnit.Milliseconds => value.ToString("0.0", Culture) + " ms",
            MetricUnit.FramesPerSecond => value.ToString("0", Culture) + " FPS",
            _ => value.ToString("0", Culture),
        };
    }

    private static string FormatBytes(double value, string suffix)
    {
        var index = 0;
        while (Math.Abs(value) >= 1024 && index < ByteUnits.Length - 1)
        {
            value /= 1024;
            index++;
        }

        var format = index == 0 || Math.Abs(value) >= 100 ? "0" : "0.0";
        return value.ToString(format, Culture) + " " + ByteUnits[index] + suffix;
    }

    private static string FormatBits(double bits)
    {
        var index = 0;
        while (Math.Abs(bits) >= 1000 && index < BitUnits.Length - 1)
        {
            bits /= 1000;
            index++;
        }

        var format = index == 0 || Math.Abs(bits) >= 100 ? "0" : "0.0";
        return bits.ToString(format, Culture) + " " + BitUnits[index];
    }
}
