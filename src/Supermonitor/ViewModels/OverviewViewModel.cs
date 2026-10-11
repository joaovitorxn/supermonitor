using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, ICardActions, IDisposable
{
    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private int _refreshQueued;

    public OverviewViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        foreach (var card in Settings.Cards)
        {
            Cards.Add(CreateCard(card));
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public ObservableCollection<MetricCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public int HistoryIndex
    {
        get => Math.Max(0, AppSettings.AllowedHistoryMinutes.ToList().IndexOf(Settings.HistoryMinutes));
        set
        {
            if (value < 0 || value >= AppSettings.AllowedHistoryMinutes.Count)
            {
                return;
            }

            Settings.HistoryMinutes = AppSettings.AllowedHistoryMinutes[value];
            Save();
            RefreshNow();
        }
    }

    private AppSettings Settings => _host.Settings.Current;

    public IReadOnlyList<MetricDescriptor> AvailableMetrics() =>
        _host.Metrics.GetDescriptors()
            .Where(d => DashboardEditor.IndexOf(Settings.Cards, d.Id) < 0)
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void AddCard(string metricId)
    {
        if (DashboardEditor.Add(Settings.Cards, metricId))
        {
            var card = CreateCard(Settings.Cards[^1]);
            card.IsEditing = IsEditing;
            Cards.Add(card);
            Save();
            RefreshNow();
        }
    }

    public void Remove(MetricCardViewModel card)
    {
        if (DashboardEditor.Remove(Settings.Cards, card.MetricId))
        {
            Cards.Remove(card);
            Save();
        }
    }

    public void Move(MetricCardViewModel card, int delta)
    {
        var from = Cards.IndexOf(card);
        var to = DashboardEditor.MoveBy(Settings.Cards, card.MetricId, delta);
        if (from >= 0 && to >= 0 && from != to)
        {
            Cards.Move(from, to);
            Save();
        }
    }

    public void MoveTo(string metricId, string targetMetricId)
    {
        var from = IndexOfCard(metricId);
        var target = DashboardEditor.IndexOf(Settings.Cards, targetMetricId);
        if (from < 0 || target < 0)
        {
            return;
        }

        var to = DashboardEditor.MoveTo(Settings.Cards, metricId, target);
        if (to != from)
        {
            Cards.Move(from, to);
            Save();
        }
    }

    public void SetSize(MetricCardViewModel card, CardSize size)
    {
        if (FindSettings(card.MetricId) is { } settings)
        {
            settings.Size = size;
            card.Size = size;
            Save();
        }
    }

    public void SetChart(MetricCardViewModel card, ChartKind chart)
    {
        if (FindSettings(card.MetricId) is { } settings)
        {
            settings.Chart = chart;
            card.Chart = chart;
            Save();
        }
    }

    public MetricThreshold? GetThreshold(string metricId) => Settings.Thresholds.GetValueOrDefault(metricId);

    public void SetThreshold(string metricId, MetricThreshold? threshold)
    {
        if (threshold is null)
        {
            Settings.Thresholds.Remove(metricId);
        }
        else
        {
            Settings.Thresholds[metricId] = threshold;
        }

        Save();
        RefreshNow();
    }

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
    }

    partial void OnIsEditingChanged(bool value)
    {
        foreach (var card in Cards)
        {
            card.IsEditing = value;
        }
    }

    private MetricCardViewModel CreateCard(CardSettings settings) =>
        new(settings.MetricId, _host.Metrics) { Size = settings.Size, Chart = settings.Chart, Actions = this };

    private CardSettings? FindSettings(string metricId)
    {
        var index = DashboardEditor.IndexOf(Settings.Cards, metricId);
        return index < 0 ? null : Settings.Cards[index];
    }

    private int IndexOfCard(string metricId)
    {
        for (var i = 0; i < Cards.Count; i++)
        {
            if (Cards[i].MetricId == metricId)
            {
                return i;
            }
        }

        return -1;
    }

    private void Save() => _host.Settings.RequestSave();

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

        var settings = Settings;
        var points = Math.Clamp(settings.HistoryMinutes * 60_000 / settings.SampleIntervalMs, 2, _host.Metrics.HistoryCapacity);
        var options = settings.ToUnitOptions();
        foreach (var card in Cards)
        {
            card.PointCount = points;
            card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
        }
    }
}
