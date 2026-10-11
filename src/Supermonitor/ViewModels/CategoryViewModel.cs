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

    public CategoryViewModel(AppHost host, MetricCategory category, string title, DispatcherQueue dispatcher, Func<bool> isMinimized, Func<IDisposable> acquireProcesses)
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
            _processLease = acquireProcesses();
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

            foreach (var info in group.Summary)
            {
                info.Refresh(options);
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
        if (Category == MetricCategory.Disk)
        {
            RebuildDisks(descriptors);
            return;
        }

        foreach (var group in descriptors.GroupBy(d => d.Device ?? "General"))
        {
            var items = group.ToArray();
            var size = items.Length > CompactGroupThreshold ? CardSize.Compact : CardSize.Normal;
            var cards = new ObservableCollection<MetricCardViewModel>(
                items.Select(d => new MetricCardViewModel(d.Id, _host.Metrics) { Size = size }));
            Groups.Add(new MetricGroupViewModel(group.Key, cards));
        }
    }

    // Each physical disk becomes a group named after it: space card (bar + free/size/used),
    // a single temperatures card and the read/write/activity/queue charts.
    private void RebuildDisks(IReadOnlyList<MetricDescriptor> descriptors)
    {
        var drives = descriptors.Where(d => d.Id.StartsWith("drive.", StringComparison.Ordinal)).ToArray();
        var hardware = descriptors.Where(d => d.Id.StartsWith("hw", StringComparison.Ordinal)).GroupBy(d => d.Device ?? "Storage").ToArray();
        var physical = descriptors
            .Where(d => d.Id.StartsWith("disk", StringComparison.Ordinal))
            .GroupBy(d => d.Device ?? "Disk")
            .ToArray();

        // LibreHardwareMonitor lists disks in the same order as Windows; match by position only when the counts agree.
        var pairByOrder = hardware.Length == physical.Length;
        var usedDrives = new HashSet<char>();

        for (var i = 0; i < physical.Length; i++)
        {
            var group = new MetricGroupViewModel(physical[i].Key, new ObservableCollection<MetricCardViewModel>(
                physical[i].Select(d => new MetricCardViewModel(d.Id, _host.Metrics) { Size = CardSize.Normal })));
            foreach (var letter in DriveLetters(physical[i].Key))
            {
                if (drives.Any(d => d.Id == MetricIds.DriveTotal(letter)) && usedDrives.Add(letter))
                {
                    group.Summary.Add(SpaceCard(letter));
                }
            }

            if (pairByOrder)
            {
                AddTemperatureCard(group, hardware[i]);
            }

            Groups.Add(group);
        }

        var loose = new MetricGroupViewModel("Drives", []);
        foreach (var letter in drives.Select(d => d.Id.Split('.')[1][0]).Distinct().Where(l => !usedDrives.Contains(l)))
        {
            loose.Summary.Add(SpaceCard(letter));
        }

        if (loose.Summary.Count > 0)
        {
            Groups.Add(loose);
        }

        if (!pairByOrder)
        {
            foreach (var device in hardware)
            {
                var group = new MetricGroupViewModel(device.Key, []);
                AddTemperatureCard(group, device);
                if (group.Summary.Count > 0)
                {
                    Groups.Add(group);
                }
            }
        }
    }

    private static IEnumerable<char> DriveLetters(string title)
    {
        var open = title.IndexOf('(');
        if (open < 0)
        {
            yield break;
        }

        for (var i = open; i + 1 < title.Length; i++)
        {
            if (title[i + 1] == ':' && char.IsLetter(title[i]))
            {
                yield return char.ToUpperInvariant(title[i]);
            }
        }
    }

    private InfoCardViewModel SpaceCard(char letter)
    {
        var store = _host.Metrics;
        return new InfoCardViewModel($"{letter}:", true, ["Used", "Free", "Size"], (card, options) =>
        {
            var usage = store.GetLatest(MetricIds.DriveUsage(letter));
            card.BarPercent = double.IsFinite(usage) ? usage : 0;
            card.Rows[0].Value = UnitFormatter.Format(usage, MetricUnit.Percent, options);
            card.Rows[1].Value = UnitFormatter.Format(store.GetLatest(MetricIds.DriveFree(letter)), MetricUnit.Bytes, options);
            card.Rows[2].Value = UnitFormatter.Format(store.GetLatest(MetricIds.DriveTotal(letter)), MetricUnit.Bytes, options);
        });
    }

    private void AddTemperatureCard(MetricGroupViewModel group, IGrouping<string, MetricDescriptor> sensors)
    {
        var store = _host.Metrics;
        var temperatures = sensors.Where(d => d.Unit == MetricUnit.Celsius).ToArray();
        if (temperatures.Length == 0)
        {
            return;
        }

        group.Summary.Add(new InfoCardViewModel($"Temperature · {sensors.Key}", false, temperatures.Select(d => d.DisplayName), (card, options) =>
        {
            for (var i = 0; i < temperatures.Length; i++)
            {
                card.Rows[i].Value = UnitFormatter.Format(store.GetLatest(temperatures[i].Id), MetricUnit.Celsius, options);
            }
        }));
    }
}
