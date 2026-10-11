using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.ViewModels;

public sealed partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private const int MaxRows = 100;

    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private readonly IDisposable _lease;
    private int _refreshQueued;

    public ProcessesViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized, Func<IDisposable> acquireProcesses)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        _lease = acquireProcesses();
        CountText = string.Empty;
        host.Hub.SampleReady += OnSampleReady;
    }

    public ObservableCollection<ProcessRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string CountText { get; set; }

    public ProcessSortKey SortKey { get; private set; } = ProcessSortKey.Cpu;

    public string? Filter { get; private set; }

    public void SortBy(ProcessSortKey key)
    {
        SortKey = key;
        RefreshNow();
    }

    public void SetFilter(string? filter)
    {
        Filter = filter;
        RefreshNow();
    }

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _lease.Dispose();
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

        var snapshot = _host.Processes.Snapshot;
        var top = ProcessSorting.Top(snapshot, SortKey, Filter, MaxRows);
        var options = _host.Settings.Current.ToUnitOptions();
        while (Rows.Count < top.Length)
        {
            Rows.Add(new ProcessRowViewModel());
        }

        while (Rows.Count > top.Length)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        for (var i = 0; i < top.Length; i++)
        {
            Rows[i].Update(top[i], options);
        }

        CountText = $"{snapshot.Count} processes";
    }
}
