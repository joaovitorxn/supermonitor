namespace Supermonitor.Core.Sensors.Processes;

public enum ProcessSortKey
{
    Cpu,
    Memory,
    Gpu,
    Name,
}

public static class ProcessSorting
{
    public static ProcessSample[] Top(IReadOnlyList<ProcessSample> processes, ProcessSortKey key, string? filter, int count)
    {
        IEnumerable<ProcessSample> query = processes;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var term = filter.Trim();
            query = query.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = key switch
        {
            ProcessSortKey.Memory => query.OrderByDescending(p => p.PrivateBytes),
            ProcessSortKey.Gpu => query.OrderByDescending(p => Known(p.GpuPercent)),
            ProcessSortKey.Name => query.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(p => Known(p.CpuPercent)),
        };

        return ordered.ThenBy(p => p.ProcessId).Take(count).ToArray();
    }

    private static float Known(float value) => float.IsFinite(value) ? value : -1;
}
