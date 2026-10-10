using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Cpu;
using Supermonitor.Core.Sensors.Memory;

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
}
