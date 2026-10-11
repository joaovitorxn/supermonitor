namespace Supermonitor.Core.Frames;

public static class Percentile
{
    // Nearest-rank: the smallest value with at least `percent`% of the data less than or equal to it. Sorts `values` in place.
    public static float NearestRank(Span<float> values, double percent)
    {
        if (values.IsEmpty)
        {
            return float.NaN;
        }

        values.Sort();
        // The epsilon keeps 99.9 * 1000 / 100 = 999.0000000000001 from becoming rank 1000.
        var rank = (int)Math.Ceiling(percent * values.Length / 100d - 1e-9);
        return values[Math.Clamp(rank, 1, values.Length) - 1];
    }
}
