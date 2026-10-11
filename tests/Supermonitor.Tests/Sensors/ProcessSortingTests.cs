using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Sensors;

public class ProcessSortingTests
{
    private static readonly ProcessSample[] Processes =
    [
        new(1, "game.exe", 40, 4_000, 90),
        new(2, "chrome.exe", float.NaN, 9_000, 5),
        new(3, "Discord.exe", 10, 2_000, 0),
        new(4, "audio.exe", 10, 1_000, 0),
    ];

    [Fact]
    public void Sorts_by_cpu_descending_with_unknown_last_and_pid_tiebreak()
    {
        var top = ProcessSorting.Top(Processes, ProcessSortKey.Cpu, null, 10);

        Assert.Equal(new[] { 1, 3, 4, 2 }, top.Select(p => p.ProcessId));
    }

    [Fact]
    public void Sorts_by_memory_and_gpu_descending()
    {
        Assert.Equal(new[] { 2, 1 }, ProcessSorting.Top(Processes, ProcessSortKey.Memory, null, 2).Select(p => p.ProcessId));
        Assert.Equal(1, ProcessSorting.Top(Processes, ProcessSortKey.Gpu, null, 1)[0].ProcessId);
    }

    [Fact]
    public void Sorts_by_name_ignoring_case()
    {
        var names = ProcessSorting.Top(Processes, ProcessSortKey.Name, null, 10).Select(p => p.Name);

        Assert.Equal(new[] { "audio.exe", "chrome.exe", "Discord.exe", "game.exe" }, names);
    }

    [Fact]
    public void Filters_by_name_substring()
    {
        var top = ProcessSorting.Top(Processes, ProcessSortKey.Cpu, " DISC ", 10);

        Assert.Equal(new[] { 3 }, top.Select(p => p.ProcessId));
    }
}
