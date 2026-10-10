using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Cpu;

public sealed class CpuProvider : ISensorProvider
{
    private readonly Dictionary<int, MetricHandle> _cores = [];
    private PdhQuery? _query;
    private PdhCounter? _total;
    private PdhCounter? _perCore;
    private MetricHandle _totalHandle;

    public string Name => "CPU";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _query = new PdhQuery();
        _total = _query.AddCounter(@"\Processor Information(_Total)\% Processor Utility");
        _perCore = _query.AddCounter(@"\Processor Information(*)\% Processor Utility");
        _query.Collect();
        _query.Collect();

        _totalHandle = store.Register(new MetricDescriptor(MetricIds.CpuUsage, "CPU usage", MetricCategory.Cpu, MetricUnit.Percent), Name);

        var keys = new List<int>();
        var array = _perCore.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            if (CpuInstanceName.TryParse(array.NameAt(i), out var key))
            {
                keys.Add(key);
            }
        }

        keys.Sort();
        for (var index = 0; index < keys.Count; index++)
        {
            _cores[keys[index]] = store.Register(
                new MetricDescriptor(MetricIds.CpuCoreUsage(index), $"Core {index} usage", MetricCategory.Cpu, MetricUnit.Percent),
                Name);
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        store.Write(_totalHandle, (float)_total!.GetValue());

        var array = _perCore!.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            if (CpuInstanceName.TryParse(array.NameAt(i), out var key) && _cores.TryGetValue(key, out var handle))
            {
                store.Write(handle, (float)array.ValueAt(i));
            }
        }
    }

    public void Dispose() => _query?.Dispose();
}
