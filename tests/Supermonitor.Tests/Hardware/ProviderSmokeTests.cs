using Supermonitor.Core;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Cpu;
using Supermonitor.Core.Sensors.Disk;
using Supermonitor.Core.Sensors.Gpu;
using Supermonitor.Core.Sensors.Hardware;
using Supermonitor.Core.Sensors.Memory;
using Supermonitor.Core.Sensors.Network;
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class ProviderSmokeTests
{
    internal static MetricStore SampleTwice(ISensorProvider provider)
    {
        var store = new MetricStore(10);
        provider.Initialize(store);
        provider.Sample(store, TimeSpan.Zero);
        store.CommitTick();
        Thread.Sleep(1000);
        provider.Sample(store, TimeSpan.FromSeconds(1));
        store.CommitTick();
        return store;
    }

    [Fact]
    public void Cpu_provider_reports_total_and_per_core_usage()
    {
        using var provider = new CpuProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.CpuUsage), 0, 100);
        for (var core = 0; core < Environment.ProcessorCount; core++)
        {
            Assert.InRange(store.GetLatest(MetricIds.CpuCoreUsage(core)), 0, 100);
        }
        Assert.True(store.TryGetDescriptor(MetricIds.CpuUsage, out var descriptor));
        Assert.Equal("CPU", descriptor!.Device);
    }

    [Fact]
    public void Memory_provider_reports_total_and_used()
    {
        using var provider = new MemoryProvider();
        var store = SampleTwice(provider);

        var total = store.GetLatest(MetricIds.MemoryTotal);
        Assert.True(total > 1024f * 1024 * 1024);
        Assert.InRange(store.GetLatest(MetricIds.MemoryUsed), 1, total);
        Assert.InRange(store.GetLatest(MetricIds.MemoryUsage), 0, 100);
        Assert.True(store.GetLatest(MetricIds.MemoryCommitted) > 0);
    }

    [Fact]
    public void Disk_provider_reports_activity_for_disk_zero()
    {
        using var provider = new DiskProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.DiskActive(0)), 0, 100);
        Assert.True(store.GetLatest(MetricIds.DiskRead(0)) >= 0);
    }

    [Fact]
    public void Drive_space_provider_reports_system_drive()
    {
        using var provider = new DriveSpaceProvider();
        var store = SampleTwice(provider);
        var letter = Environment.GetFolderPath(Environment.SpecialFolder.Windows)[0];

        Assert.True(store.GetLatest(MetricIds.DriveTotal(letter)) > 0);
        Assert.InRange(store.GetLatest(MetricIds.DriveUsage(letter)), 0, 100);
    }

    [Fact]
    public void Network_provider_reports_totals()
    {
        using var provider = new NetworkProvider();
        var store = SampleTwice(provider);

        Assert.True(store.GetLatest(MetricIds.NetworkTotalDown) >= 0);
        Assert.True(store.GetLatest(MetricIds.NetworkTotalUp) >= 0);
    }

    [Fact]
    public void Gpu_provider_reports_usage_and_vram_for_first_gpu()
    {
        using var provider = new GpuProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.GpuUsage(0)), 0, 100);
        Assert.True(store.GetLatest(MetricIds.GpuVramTotal(0)) > 0);
        Assert.True(store.GetLatest(MetricIds.GpuVramUsed(0)) > 0);
        Assert.True(store.TryGetDescriptor(MetricIds.GpuUsage(0), out var descriptor));
        Assert.False(string.IsNullOrWhiteSpace(descriptor!.Device));
    }

    [Fact]
    public void Hardware_provider_reports_gpu_temperature()
    {
        using var provider = new HardwareProvider(AppHost.MonitoredGpuNames);
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.GpuTemperature(0)), 1, 120);
        Assert.True(store.TryGetDescriptor(MetricIds.GpuTemperature(0), out var descriptor));
        Assert.False(string.IsNullOrWhiteSpace(descriptor!.Device));
    }

    [Fact]
    public void Process_monitor_lists_this_process_with_cpu_usage()
    {
        using var provider = new ProcessMonitor(new ProcessGpuUsage());
        SampleTwice(provider);

        var self = Assert.Single(provider.Snapshot, p => p.ProcessId == Environment.ProcessId);
        Assert.True(float.IsFinite(self.CpuPercent));
        Assert.True(self.PrivateBytes > 0);
    }
}
