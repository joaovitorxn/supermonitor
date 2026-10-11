using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Dashboard;

public class ThresholdEvaluatorTests
{
    private static readonly MetricThreshold Temps = new() { Warning = 80, Critical = 90 };

    [Theory]
    [InlineData(79.9f, ThresholdLevel.Normal)]
    [InlineData(80f, ThresholdLevel.Warning)]
    [InlineData(89.9f, ThresholdLevel.Warning)]
    [InlineData(90f, ThresholdLevel.Critical)]
    [InlineData(float.NaN, ThresholdLevel.Normal)]
    public void Evaluates_levels(float value, ThresholdLevel expected)
    {
        Assert.Equal(expected, ThresholdEvaluator.Evaluate(value, Temps));
    }

    [Fact]
    public void No_threshold_is_always_normal()
    {
        Assert.Equal(ThresholdLevel.Normal, ThresholdEvaluator.Evaluate(1000, null));
    }
}
