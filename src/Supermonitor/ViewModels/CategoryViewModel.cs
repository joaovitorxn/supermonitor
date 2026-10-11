using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Processes;
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public sealed class CategoryViewModel : IDisposable
{
    private const int CompactGroupThreshold = 8;
    private const int TopProcessCount = 10;

    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private readonly IDisposable? _processLease;
    private readonly ProcessSortKey _sortKey;
    private int _refreshQueued;
    private int _descriptorCount = -1;

    public CategoryViewModel(AppHost host, MetricCategory category, string title, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        Category = category;
        Title = title;
        IsMemory = category == MetricCategory.Memory;
        ShowsProcesses = category is MetricCategory.Cpu or MetricCategory.Memory or MetricCategory.Gpu;
        _sortKey = category switch
        {
            MetricCategory.Memory => ProcessSortKey.Memory,
            MetricCategory.Gpu => ProcessSortKey.Gpu,
            _ => ProcessSortKey.Cpu,
        };

        if (ShowsProcesses)
        {
            _processLease = host.Demand.Acquire(Demand.Processes);
            for (var i = 0; i < TopProcessCount; i++)
            {
                TopProcesses.Add(new ProcessRowViewModel());
            }
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public event Action<MemoryComposition>? MemoryUpdated;

    public MetricCategory Category { get; }

    public string Title { get; }

    public bool IsMemory { get; }

    public bool ShowsProcesses { get; }

    public ObservableCollection<MetricGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ProcessRowViewModel> TopProcesses { get; } = [];

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
        _processLease?.Dispose();
    }

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, _refreshHandler))
        {
            Volatile.Write(ref _refreshQueued, 0);
        }
    }

    private void RefreshNow()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (_isMinimized())
        {
            return;
        }

        var descriptors = _host.Metrics.GetDescriptors().Where(d => d.Category == Category).ToArray();
        if (descriptors.Length != _descriptorCount)
        {
            Rebuild(descriptors);
        }

        var settings = _host.Settings.Current;
        var options = settings.ToUnitOptions();
        var points = Math.Clamp(settings.HistoryMinutes * 60_000 / settings.SampleIntervalMs, 2, _host.Metrics.HistoryCapacity);
        foreach (var group in Groups)
        {
            foreach (var card in group.Items)
            {
                card.PointCount = points;
                card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
            }
        }

        if (ShowsProcesses)
        {
            var top = ProcessSorting.Top(_host.Processes.Snapshot, _sortKey, null, TopProcessCount);
            for (var i = 0; i < TopProcesses.Count; i++)
            {
                if (i < top.Length)
                {
                    TopProcesses[i].Update(top[i], options);
                }
                else
                {
                    TopProcesses[i].Update(new ProcessSample(0, string.Empty, float.NaN, 0, 0), options);
                }
            }
        }

        if (IsMemory)
        {
            var store = _host.Metrics;
            MemoryUpdated?.Invoke(MemoryComposition.From(
                store.GetLatest(MetricIds.MemoryTotal),
                store.GetLatest(MetricIds.MemoryUsed),
                store.GetLatest(MetricIds.MemoryModified),
                store.GetLatest(MetricIds.MemoryStandby),
                store.GetLatest(MetricIds.MemoryFree)));
        }
    }

    private void Rebuild(IReadOnlyList<MetricDescriptor> descriptors)
    {
        _descriptorCount = descriptors.Count;
        Groups.Clear();
        foreach (var group in descriptors.GroupBy(d => d.Device ?? "General"))
        {
            var items = group.ToArray();
            var size = items.Length > CompactGroupThreshold ? CardSize.Compact : CardSize.Normal;
            var cards = new ObservableCollection<MetricCardViewModel>(
                items.Select(d => new MetricCardViewModel(d.Id, _host.Metrics) { Size = size }));
            Groups.Add(new MetricGroupViewModel(group.Key, cards));
        }
    }
}
