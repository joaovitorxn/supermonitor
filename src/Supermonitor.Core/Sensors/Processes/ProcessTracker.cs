namespace Supermonitor.Core.Sensors.Processes;

public readonly record struct ProcessSample(int ProcessId, string Name, float CpuPercent, long PrivateBytes, float GpuPercent);

public static class ProcessCpuCalculator
{
    // Times in 100 ns units; percent over all logical cores (same as Task Manager).
    public static double Percent(long previous, long current, TimeSpan elapsed, int processorCount)
    {
        if (elapsed <= TimeSpan.Zero || processorCount <= 0)
        {
            return double.NaN;
        }

        var delta = Math.Max(0, current - previous);
        return Math.Clamp(delta * 100.0 / (elapsed.Ticks * (double)processorCount), 0, 100);
    }
}

public sealed class ProcessTracker
{
    private Dictionary<(int Pid, long Create), Tracked> _tracked = [];
    private Dictionary<(int Pid, long Create), Tracked> _next = [];

    public ProcessSample[] Update(
        IReadOnlyList<RawProcessEntry> raw,
        TimeSpan elapsed,
        int processorCount,
        Func<RawProcessEntry, string> resolveName,
        Func<int, float> gpuPercent)
    {
        var samples = new ProcessSample[raw.Count];
        var count = 0;
        _next.Clear();
        foreach (var entry in raw)
        {
            if (entry.ProcessId == 0)
            {
                continue;
            }

            var key = (entry.ProcessId, entry.CreateTime);
            var cpu = float.NaN;
            string name;
            if (_tracked.TryGetValue(key, out var previous))
            {
                cpu = (float)ProcessCpuCalculator.Percent(previous.CpuTime, entry.CpuTime, elapsed, processorCount);
                name = previous.Name;
            }
            else
            {
                name = resolveName(entry);
            }

            _next[key] = new Tracked(entry.CpuTime, name);
            samples[count++] = new ProcessSample(entry.ProcessId, name, cpu, entry.PrivateWorkingSet, gpuPercent(entry.ProcessId));
        }

        (_tracked, _next) = (_next, _tracked);
        Array.Resize(ref samples, count);
        return samples;
    }

    private readonly record struct Tracked(long CpuTime, string Name);
}
