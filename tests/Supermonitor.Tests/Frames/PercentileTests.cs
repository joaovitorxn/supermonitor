using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Frames;

public class PercentileTests
{
    private static float[] Range(int count) => Enumerable.Range(1, count).Select(i => (float)i).ToArray();

    [Fact]
    public void Ninety_ninth_percentile_of_one_to_hundred_is_ninety_nine()
    {
        Assert.Equal(99f, Percentile.NearestRank(Range(100), 99));
    }

    [Fact]
    public void Median_of_one_to_hundred_is_fifty()
    {
        Assert.Equal(50f, Percentile.NearestRank(Range(100), 50));
    }

    [Fact]
    public void Ninety_nine_point_nine_of_one_to_thousand_is_nine_ninety_nine()
    {
        // 99,9 * 1000 / 100 em ponto flutuante passa de 999 por erro de arredondamento; o resultado correto é o posto 999.
        Assert.Equal(999f, Percentile.NearestRank(Range(1000), 99.9));
    }

    [Fact]
    public void Hundredth_percentile_is_the_maximum()
    {
        Assert.Equal(100f, Percentile.NearestRank(Range(100), 100));
    }

    [Fact]
    public void Single_value_is_every_percentile()
    {
        Assert.Equal(7f, Percentile.NearestRank(new[] { 7f }, 99));
    }

    [Fact]
    public void Empty_input_gives_nan()
    {
        Assert.True(float.IsNaN(Percentile.NearestRank(Span<float>.Empty, 99)));
    }

    [Fact]
    public void Unsorted_input_is_sorted_first()
    {
        Assert.Equal(5f, Percentile.NearestRank(new[] { 5f, 1f, 4f, 2f, 3f }, 100));
    }
}
