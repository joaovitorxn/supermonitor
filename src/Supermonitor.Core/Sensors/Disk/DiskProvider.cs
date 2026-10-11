using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Disk;

public sealed class DiskProvider : ISensorProvider
{
    private enum Field
    {
        Read,
        Write,
        Active,
        Queue,
    }

    private readonly Dictionary<string, DiskHandles> _disks = new(StringComparer.Ordinal);
    private PdhQuery? _query;
    private PdhCounter? _read;
    private PdhCounter? _write;
    private PdhCounter? _idle;
    private PdhCounter? _queue;

    public string Name => "Disks";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _query = new PdhQuery();
        _read = _query.AddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _write = _query.AddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        _idle = _query.AddCounter(@"\PhysicalDisk(*)\% Idle Time");
        _queue = _query.AddCounter(@"\PhysicalDisk(*)\Current Disk Queue Length");
        _query.Collect();
        _query.Collect();

        var array = _queue.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            var name = array.NameAt(i);
            if (!DiskInstanceName.TryParse(name, out var index, out var label))
            {
                continue;
            }

            var title = label.IsEmpty ? $"Disk {index}" : $"Disk {index} ({label.ToString()})";
            _disks[name.ToString()] = new DiskHandles(
                Register(store, MetricIds.DiskRead(index), $"{title} read", MetricUnit.BytesPerSecond),
                Register(store, MetricIds.DiskWrite(index), $"{title} write", MetricUnit.BytesPerSecond),
                Register(store, MetricIds.DiskActive(index), $"{title} active time", MetricUnit.Percent),
                Register(store, MetricIds.DiskQueue(index), $"{title} queue", MetricUnit.Count));
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        WriteArray(store, _read!.GetArray(), Field.Read);
        WriteArray(store, _write!.GetArray(), Field.Write);
        WriteArray(store, _idle!.GetArray(), Field.Active);
        WriteArray(store, _queue!.GetArray(), Field.Queue);
    }

    public void Dispose() => _query?.Dispose();

    private void WriteArray(MetricStore store, PdhArray array, Field field)
    {
        var lookup = _disks.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = 0; i < array.Count; i++)
        {
            if (!lookup.TryGetValue(array.NameAt(i), out var handles))
            {
                continue;
            }

            var value = array.ValueAt(i);
            switch (field)
            {
                case Field.Read:
                    store.Write(handles.Read, (float)value);
                    break;
                case Field.Write:
                    store.Write(handles.Write, (float)value);
                    break;
                case Field.Active:
                    store.Write(handles.Active, (float)Math.Clamp(100 - value, 0, 100));
                    break;
                case Field.Queue:
                    store.Write(handles.Queue, (float)value);
                    break;
            }
        }
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Disk, unit), Name);

    private sealed record DiskHandles(MetricHandle Read, MetricHandle Write, MetricHandle Active, MetricHandle Queue);
}
