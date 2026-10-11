namespace Supermonitor.Core.Dashboard;

public readonly record struct MemoryComposition(double InUse, double Modified, double Standby, double Free)
{
    public static MemoryComposition From(double total, double used, double modified, double standby, double free)
    {
        if (!(total > 0) || !double.IsFinite(used))
        {
            return default;
        }

        if (!double.IsFinite(standby) || !double.IsFinite(free))
        {
            return new MemoryComposition(used, 0, 0, Math.Max(0, total - used));
        }

        var modifiedBytes = double.IsFinite(modified) ? modified : 0;
        return new MemoryComposition(Math.Max(0, used - modifiedBytes), modifiedBytes, standby, free);
    }
}
