namespace Supermonitor.Core.Frames;

public static class Percentile
{
    // Nearest-rank: o menor valor com pelo menos `percent`% dos dados menores ou iguais a ele. Ordena `values` no lugar.
    public static float NearestRank(Span<float> values, double percent)
    {
        if (values.IsEmpty)
        {
            return float.NaN;
        }

        values.Sort();
        // O epsilon evita que 99,9 * 1000 / 100 = 999,0000000000001 vire o posto 1000.
        var rank = (int)Math.Ceiling(percent * values.Length / 100d - 1e-9);
        return values[Math.Clamp(rank, 1, values.Length) - 1];
    }
}
