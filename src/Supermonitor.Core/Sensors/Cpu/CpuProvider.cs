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

        _totalHandle = store.Register(new MetricDescriptor(MetricIds.CpuUsage, "CPU usage", MetricCategory.Cpu, MetricUnit.Percent, "CPU"), Name);

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
                new MetricDescriptor(MetricIds.CpuCoreUsage(index), $"Core {index} usage", MetricCategory.Cpu, MetricUnit.Percent, "CPU cores"),
                Name);
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        store.Write(_totalHandle, Clamp(_total!.GetValue()));

        var array = _perCore!.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            if (CpuInstanceName.TryParse(array.NameAt(i), out var key) && _cores.TryGetValue(key, out var handle))
            {
                store.Write(handle, Clamp(array.ValueAt(i)));
            }
        }
    }

    public void Dispose() => _query?.Dispose();

    // "% Processor Utility" passa de 100 com turbo boost; o Gerenciador de Tarefas limita a 100.
    private static float Clamp(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 100) : float.NaN;
}
