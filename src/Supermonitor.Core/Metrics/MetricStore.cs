using System.Diagnostics.CodeAnalysis;
using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Metrics;

public sealed class MetricStore
{
    private readonly Lock _gate = new();
    private readonly List<Series> _series = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public MetricStore(int historyCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(historyCapacity, 1);
        HistoryCapacity = historyCapacity;
    }

    public event Action? MetricsChanged;

    public int HistoryCapacity { get; }

    public MetricHandle Register(MetricDescriptor descriptor, string owner)
    {
        MetricHandle handle;
        var added = false;
        lock (_gate)
        {
            if (_index.TryGetValue(descriptor.Id, out var existing))
            {
                handle = new MetricHandle(existing);
            }
            else
            {
                _series.Add(new Series(descriptor, owner, HistoryCapacity));
                handle = new MetricHandle(_series.Count - 1);
                _index[descriptor.Id] = handle.Index;
                added = true;
            }
        }

        if (added)
        {
            MetricsChanged?.Invoke();
        }

        return handle;
    }

    public void Write(MetricHandle handle, float value)
    {
        lock (_gate)
        {
            _series[handle.Index].Current = value;
        }
    }

    public void InvalidateOwner(string owner)
    {
        lock (_gate)
        {
            foreach (var series in _series)
            {
                if (series.Owner == owner)
                {
                    series.Current = float.NaN;
                }
            }
        }
    }

    public void CommitTick()
    {
        lock (_gate)
        {
            foreach (var series in _series)
            {
                series.History.Add(series.Current);
            }
        }
    }

    public IReadOnlyList<MetricDescriptor> GetDescriptors()
    {
        lock (_gate)
        {
            return _series.Select(s => s.Descriptor).ToArray();
        }
    }

    public bool TryGetDescriptor(string id, [NotNullWhen(true)] out MetricDescriptor? descriptor)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(id, out var index))
            {
                descriptor = _series[index].Descriptor;
                return true;
            }
        }

        descriptor = null;
        return false;
    }

    public float GetLatest(string id)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(id, out var index) || _series[index].History.Count == 0)
            {
                return float.NaN;
            }

            return _series[index].History.Latest;
        }
    }

    public int CopyHistory(string id, Span<float> destination)
    {
        lock (_gate)
        {
            return _index.TryGetValue(id, out var index) ? _series[index].History.CopyLast(destination) : 0;
        }
    }

    private sealed class Series(MetricDescriptor descriptor, string owner, int capacity)
    {
        public MetricDescriptor Descriptor { get; } = descriptor;

        public string Owner { get; } = owner;

        public RingBuffer<float> History { get; } = new(capacity);

        public float Current { get; set; } = float.NaN;
    }
}
