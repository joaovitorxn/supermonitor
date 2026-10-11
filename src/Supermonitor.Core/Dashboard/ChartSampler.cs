namespace Supermonitor.Core.Dashboard;

public static class ChartSampler
{
    // Downsamples the series to the chart width, keeping each bucket's peak (peaks matter to gamers).
    public static int Downsample(ReadOnlySpan<float> source, Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        if (source.Length <= destination.Length)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        for (var bucket = 0; bucket < destination.Length; bucket++)
        {
            var start = (int)((long)bucket * source.Length / destination.Length);
            var end = (int)((long)(bucket + 1) * source.Length / destination.Length);
            var max = float.NaN;
            for (var i = start; i < end; i++)
            {
                var value = source[i];
                if (float.IsFinite(value) && !(value <= max))
                {
                    max = value;
                }
            }

            destination[bucket] = max;
        }

        return destination.Length;
    }

    public static float Maximum(ReadOnlySpan<float> values, float? fixedMaximum)
    {
        if (fixedMaximum is { } fixedValue)
        {
            return fixedValue;
        }

        var max = 0f;
        foreach (var value in values)
        {
            if (float.IsFinite(value) && value > max)
            {
                max = value;
            }
        }

        return max > 0 ? max * 1.1f : 1f;
    }
}
