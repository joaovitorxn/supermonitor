using Supermonitor.Core.Dashboard;

namespace Supermonitor.Tests.Dashboard;

public class ChartSamplerTests
{
    [Fact]
    public void Short_series_is_copied_as_is()
    {
        var destination = new float[5];

        var count = ChartSampler.Downsample([1, 2, 3], destination);

        Assert.Equal(3, count);
        Assert.Equal(new float[] { 1, 2, 3 }, destination[..3]);
    }

    [Fact]
    public void Long_series_keeps_the_peak_of_each_bucket()
    {
        var destination = new float[3];

        var count = ChartSampler.Downsample([1, 5, 2, 8, 3, 9], destination);

        Assert.Equal(3, count);
        Assert.Equal(new float[] { 5, 8, 9 }, destination);
    }

    [Fact]
    public void Bucket_without_finite_values_is_nan()
    {
        var destination = new float[2];

        ChartSampler.Downsample([float.NaN, float.NaN, float.NaN, 4], destination);

        Assert.True(float.IsNaN(destination[0]));
        Assert.Equal(4, destination[1]);
    }

    [Fact]
    public void Maximum_uses_fixed_value_headroom_or_one()
    {
        Assert.Equal(100, ChartSampler.Maximum([5, 50], 100f));
        Assert.Equal(55, ChartSampler.Maximum([5, 50, float.NaN], null), 3);
        Assert.Equal(1, ChartSampler.Maximum([0, float.NaN], null));
    }
}
