using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Tests.Sensors;

public class GpuEngineAggregatorTests
{
    private const string Luid = "0x00000000_0x0000D1F2";

    private static string Instance(int pid, int engine, string type, string luid = Luid) =>
        $"pid_{pid}_luid_{luid}_phys_0_eng_{engine}_engtype_{type}";

    [Fact]
    public void Sums_processes_per_engine_and_takes_max_per_type()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Add(Instance(200, 0, "3D"), 25);
        aggregator.Add(Instance(100, 1, "Copy"), 10);
        aggregator.Add(Instance(100, 2, "VideoDecode"), 5);
        aggregator.Add(Instance(100, 3, "VideoEncode"), 4);
        aggregator.Add(Instance(100, 4, "Compute_0"), 3);

        var usage = aggregator.GetUsage(0);

        Assert.Equal(55, usage.ThreeD);
        Assert.Equal(10, usage.Copy);
        Assert.Equal(5, usage.VideoDecode);
        Assert.Equal(4, usage.VideoEncode);
        Assert.Equal(3, usage.Compute);
        Assert.Equal(55, usage.Total);
    }

    [Fact]
    public void Ignores_other_adapters_malformed_names_and_non_finite_values()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D", "0x00000000_0x0000AAAA"), 90);
        aggregator.Add("garbage", 90);
        aggregator.Add("pid_1_luid_0x00000000_0x0000D1F2_phys_0_eng_x_engtype_3D", 90);
        aggregator.Add(Instance(1, 0, "3D"), double.NaN);

        Assert.True(float.IsNaN(aggregator.GetUsage(0).Total));
    }

    [Fact]
    public void Luid_match_is_case_insensitive_and_usage_is_capped_at_100()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D", "0x00000000_0x0000d1f2"), 80);
        aggregator.Add(Instance(2, 0, "3D"), 70);

        Assert.Equal(100, aggregator.GetUsage(0).Total);
    }

    [Fact]
    public void Adapter_without_any_engine_instance_reports_nan()
    {
        var aggregator = new GpuEngineAggregator([Luid, "0x00000000_0x0000AAAA"]);
        aggregator.Add(Instance(1, 0, "3D"), 40);

        Assert.Equal(40, aggregator.GetUsage(0).Total);
        Assert.True(float.IsNaN(aggregator.GetUsage(1).Total));
        Assert.True(float.IsNaN(aggregator.GetUsage(1).ThreeD));
    }

    [Fact]
    public void Per_process_usage_takes_the_busiest_engine_of_each_pid()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Add(Instance(100, 1, "Copy"), 10);
        aggregator.Add(Instance(200, 0, "3D"), 25);
        aggregator.Add(Instance(300, 0, "3D", "0x00000000_0x0000AAAA"), 90);
        var usage = new Dictionary<int, float>();

        aggregator.CopyProcessUsage(usage);

        Assert.Equal(30, usage[100]);
        Assert.Equal(25, usage[200]);
        Assert.False(usage.ContainsKey(300));
    }

    [Fact]
    public void Reset_clears_per_process_usage()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Reset();
        var usage = new Dictionary<int, float>();

        aggregator.CopyProcessUsage(usage);

        Assert.Empty(usage);
    }

    [Fact]
    public void Reset_clears_previous_tick()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D"), 40);
        aggregator.Reset();

        Assert.True(float.IsNaN(aggregator.GetUsage(0).Total));
    }
}
