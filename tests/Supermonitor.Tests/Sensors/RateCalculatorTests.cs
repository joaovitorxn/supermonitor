using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class RateCalculatorTests
{
    [Fact]
    public void Computes_bytes_per_second()
    {
        Assert.Equal(500, RateCalculator.PerSecond(1000, 2000, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Counter_reset_yields_zero()
    {
        Assert.Equal(0, RateCalculator.PerSecond(5000, 100, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Zero_elapsed_yields_nan()
    {
        Assert.True(double.IsNaN(RateCalculator.PerSecond(0, 100, TimeSpan.Zero)));
    }
}
