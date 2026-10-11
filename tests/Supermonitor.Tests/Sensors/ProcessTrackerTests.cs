using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Sensors;

public class ProcessTrackerTests
{
    private static RawProcessEntry Raw(int pid, long create, long cpu, long privateBytes = 0) => new(pid, create, cpu, privateBytes, 0, 0);

    private static ProcessSample[] Update(ProcessTracker tracker, TimeSpan elapsed, params RawProcessEntry[] raw) =>
        tracker.Update(raw, elapsed, processorCount: 2, resolveName: e => $"p{e.ProcessId}", gpuPercent: pid => pid == 10 ? 12 : 0);

    [Fact]
    public void Cpu_percent_uses_delta_since_previous_sample_across_all_cores()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(10, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(10, 1, 5_000_000, 2048));

        var sample = Assert.Single(samples);
        Assert.Equal(25, sample.CpuPercent, 3);
        Assert.Equal(2048, sample.PrivateBytes);
        Assert.Equal(12, sample.GpuPercent);
        Assert.Equal("p10", sample.Name);
    }

    [Fact]
    public void First_sample_of_a_process_has_unknown_cpu()
    {
        var samples = Update(new ProcessTracker(), TimeSpan.FromSeconds(1), Raw(10, 1, 100));

        Assert.True(float.IsNaN(samples[0].CpuPercent));
    }

    [Fact]
    public void Reused_pid_with_new_create_time_is_a_new_process()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(10, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(10, 2, 9_000_000));

        Assert.True(float.IsNaN(samples[0].CpuPercent));
    }

    [Fact]
    public void Idle_pid_zero_is_skipped_and_exited_processes_disappear()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(0, 0, 0), Raw(10, 1, 0), Raw(20, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(0, 0, 0), Raw(20, 1, 0));

        Assert.Equal(new[] { 20 }, samples.Select(s => s.ProcessId));
    }

    [Fact]
    public void Name_is_resolved_once_per_process()
    {
        var tracker = new ProcessTracker();
        var calls = 0;
        string Resolve(RawProcessEntry e)
        {
            calls++;
            return "x";
        }

        tracker.Update([Raw(10, 1, 0)], TimeSpan.Zero, 1, Resolve, _ => 0);
        tracker.Update([Raw(10, 1, 0)], TimeSpan.FromSeconds(1), 1, Resolve, _ => 0);

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(0, 10_000_000, 1, 1, 100)]
    [InlineData(0, 40_000_000, 1, 4, 100)]
    [InlineData(100, 50, 1, 1, 0)]
    public void Calculator_clamps_to_valid_range(long previous, long current, int seconds, int cores, double expected)
    {
        Assert.Equal(expected, ProcessCpuCalculator.Percent(previous, current, TimeSpan.FromSeconds(seconds), cores), 3);
    }

    [Fact]
    public void Calculator_returns_nan_without_elapsed_time()
    {
        Assert.True(double.IsNaN(ProcessCpuCalculator.Percent(0, 10, TimeSpan.Zero, 1)));
    }
}
