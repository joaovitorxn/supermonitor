using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Gpu;

public sealed class GpuProvider : ISensorProvider
{
    private Adapter[] _adapters = [];
    private GpuEngineAggregator? _aggregator;
    private PdhQuery? _query;
    private PdhCounter? _engines;
    private PdhCounter? _dedicated;
    private PdhCounter? _shared;

    public string Name => "GPU";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        var infos = DxgiAdapters.Enumerate()
            .Where(a => GpuAdapterFilter.IsMonitored(a.Flags, a.DedicatedVideoMemory))
            .ToArray();
        if (infos.Length == 0)
        {
            throw new InvalidOperationException("No hardware GPU found.");
        }

        _adapters = infos.Select((info, n) => Register(store, info, n, infos.Length)).ToArray();
        _aggregator = new GpuEngineAggregator(infos.Select(i => i.PdhLuid).ToArray());

        _query = new PdhQuery();
        _engines = _query.AddCounter(@"\GPU Engine(*)\Utilization Percentage");
        _dedicated = _query.AddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _shared = _query.AddCounter(@"\GPU Adapter Memory(*)\Shared Usage");
        _query.Collect();
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        _aggregator!.Reset();

        var engines = _engines!.GetArray();
        for (var i = 0; i < engines.Count; i++)
        {
            _aggregator.Add(engines.NameAt(i), engines.ValueAt(i));
        }

        for (var n = 0; n < _adapters.Length; n++)
        {
            var adapter = _adapters[n];
            var usage = _aggregator.GetUsage(n);
            store.Write(adapter.Usage, usage.Total);
            store.Write(adapter.ThreeD, usage.ThreeD);
            store.Write(adapter.Copy, usage.Copy);
            store.Write(adapter.Decode, usage.VideoDecode);
            store.Write(adapter.Encode, usage.VideoEncode);
            store.Write(adapter.Compute, usage.Compute);
            store.Write(adapter.VramTotal, adapter.Info.DedicatedVideoMemory);
        }

        WriteMemory(store, _dedicated!.GetArray(), dedicated: true);
        WriteMemory(store, _shared!.GetArray(), dedicated: false);
    }

    public void Dispose() => _query?.Dispose();

    private void WriteMemory(MetricStore store, PdhArray array, bool dedicated)
    {
        for (var i = 0; i < array.Count; i++)
        {
            var name = array.NameAt(i);
            foreach (var adapter in _adapters)
            {
                if (name.StartsWith(adapter.MemoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    store.Write(dedicated ? adapter.VramUsed : adapter.SharedUsed, (float)array.ValueAt(i));
                }
            }
        }
    }

    private Adapter Register(MetricStore store, GpuAdapterInfo info, int n, int total)
    {
        var prefix = total > 1 ? $"GPU {n}" : "GPU";
        MetricHandle Add(string id, string name, MetricUnit unit) =>
            store.Register(new MetricDescriptor(id, $"{prefix} {name}", MetricCategory.Gpu, unit), Name);

        return new Adapter(info, $"luid_{info.PdhLuid}")
        {
            Usage = Add(MetricIds.GpuUsage(n), "usage", MetricUnit.Percent),
            ThreeD = Add(MetricIds.Gpu3D(n), "3D", MetricUnit.Percent),
            Copy = Add(MetricIds.GpuCopy(n), "copy", MetricUnit.Percent),
            Decode = Add(MetricIds.GpuVideoDecode(n), "video decode", MetricUnit.Percent),
            Encode = Add(MetricIds.GpuVideoEncode(n), "video encode", MetricUnit.Percent),
            Compute = Add(MetricIds.GpuCompute(n), "compute", MetricUnit.Percent),
            VramUsed = Add(MetricIds.GpuVramUsed(n), "VRAM used", MetricUnit.Bytes),
            VramTotal = Add(MetricIds.GpuVramTotal(n), "VRAM total", MetricUnit.Bytes),
            SharedUsed = Add(MetricIds.GpuSharedUsed(n), "shared memory used", MetricUnit.Bytes),
        };
    }

    private sealed class Adapter(GpuAdapterInfo info, string memoryPrefix)
    {
        public GpuAdapterInfo Info { get; } = info;

        public string MemoryPrefix { get; } = memoryPrefix;

        public MetricHandle Usage { get; init; }

        public MetricHandle ThreeD { get; init; }

        public MetricHandle Copy { get; init; }

        public MetricHandle Decode { get; init; }

        public MetricHandle Encode { get; init; }

        public MetricHandle Compute { get; init; }

        public MetricHandle VramUsed { get; init; }

        public MetricHandle VramTotal { get; init; }

        public MetricHandle SharedUsed { get; init; }
    }
}
