namespace Supermonitor.Core.Sensors.Network;

public static class RateCalculator
{
    public static double PerSecond(ulong previous, ulong current, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return double.NaN;
        }

        return current < previous ? 0 : (current - previous) / elapsed.TotalSeconds;
    }
}
