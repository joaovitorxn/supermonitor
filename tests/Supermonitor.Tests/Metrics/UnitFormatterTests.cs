using Supermonitor.Core.Metrics;

namespace Supermonitor.Tests.Metrics;

public class UnitFormatterTests
{
    [Theory]
    [InlineData(42.4, MetricUnit.Percent, "42%")]
    [InlineData(0, MetricUnit.Bytes, "0 B")]
    [InlineData(1536, MetricUnit.Bytes, "1.5 KB")]
    [InlineData(157286400, MetricUnit.Bytes, "150 MB")]
    [InlineData(33930241638, MetricUnit.Bytes, "31.6 GB")]
    [InlineData(2097152, MetricUnit.BytesPerSecond, "2.0 MB/s")]
    [InlineData(80, MetricUnit.Celsius, "80 °C")]
    [InlineData(5050, MetricUnit.Megahertz, "5.05 GHz")]
    [InlineData(800, MetricUnit.Megahertz, "800 MHz")]
    [InlineData(16.66, MetricUnit.Milliseconds, "16.7 ms")]
    [InlineData(143.6, MetricUnit.FramesPerSecond, "144 FPS")]
    [InlineData(1.2346, MetricUnit.Volts, "1.235 V")]
    [InlineData(120.26, MetricUnit.Watts, "120.3 W")]
    [InlineData(1450, MetricUnit.Rpm, "1450 RPM")]
    public void Formats_values_with_default_options(double value, MetricUnit unit, string expected)
    {
        Assert.Equal(expected, UnitFormatter.Format(value, unit, UnitOptions.Default));
    }

    [Fact]
    public void Network_rate_in_bits_uses_decimal_prefixes()
    {
        Assert.Equal("10.0 Mbps", UnitFormatter.Format(1_250_000, MetricUnit.BytesPerSecond, new UnitOptions(NetworkInBits: true)));
    }

    [Fact]
    public void Fahrenheit_option_converts_celsius()
    {
        Assert.Equal("176 °F", UnitFormatter.Format(80, MetricUnit.Celsius, new UnitOptions(Fahrenheit: true)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_render_as_not_available(double value)
    {
        Assert.Equal("N/A", UnitFormatter.Format(value, MetricUnit.Percent, UnitOptions.Default));
    }
}
