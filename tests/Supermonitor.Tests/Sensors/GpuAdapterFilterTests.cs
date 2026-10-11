using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Tests.Sensors;

public class GpuAdapterFilterTests
{
    [Fact]
    public void Real_gpu_with_dedicated_memory_is_monitored()
    {
        Assert.True(GpuAdapterFilter.IsMonitored(0, 16UL * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Software_adapter_is_ignored()
    {
        Assert.False(GpuAdapterFilter.IsMonitored(0x2, 0));
    }

    [Fact]
    public void Virtual_display_adapter_without_dedicated_memory_is_ignored()
    {
        Assert.False(GpuAdapterFilter.IsMonitored(0, 0));
    }

    [Fact]
    public void PdhLuid_matches_performance_counter_format()
    {
        var info = new GpuAdapterInfo("AMD Radeon RX 9070 XT", 0xD1F2, 0, 1, 0);

        Assert.Equal("0x00000000_0x0000D1F2", info.PdhLuid);
    }
}
