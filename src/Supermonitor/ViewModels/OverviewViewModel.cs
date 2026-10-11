using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Metrics;

namespace Supermonitor.ViewModels;

public sealed class OverviewViewModel : IDisposable
{
    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private int _refreshQueued;

    public OverviewViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        foreach (var card in host.Settings.Current.Cards)
        {
            Cards.Add(new MetricCardViewModel(card.MetricId, host.Metrics));
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public ObservableCollection<MetricCardViewModel> Cards { get; } = [];

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
    }

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, RefreshNow))
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

        var settings = _host.Settings.Current;
        var points = Math.Max(2, 60_000 / settings.SampleIntervalMs);
        var options = settings.ToUnitOptions();
        foreach (var card in Cards)
        {
            card.PointCount = points;
            card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
        }
    }
}
