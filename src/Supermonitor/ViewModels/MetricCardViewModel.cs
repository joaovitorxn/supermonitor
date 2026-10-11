using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Controls;
using Supermonitor.Core.Metrics;

namespace Supermonitor.ViewModels;

public sealed partial class MetricCardViewModel : ObservableObject, ISeriesSource
{
    private readonly MetricStore _store;

    public MetricCardViewModel(string metricId, MetricStore store)
    {
        MetricId = metricId;
        _store = store;
        Title = metricId;
        ValueText = "N/A";
    }

    public event Action? Refreshed;

    public string MetricId { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ValueText { get; set; }

    public int PointCount { get; set; } = 60;

    public float? FixedMaximum { get; private set; }

    public int CopyHistory(Span<float> destination) => _store.CopyHistory(MetricId, destination);

    public void Refresh(UnitOptions options)
    {
        if (_store.TryGetDescriptor(MetricId, out var descriptor))
        {
            Title = descriptor.DisplayName;
            FixedMaximum = descriptor.Unit == MetricUnit.Percent ? 100f : null;
            ValueText = UnitFormatter.Format(_store.GetLatest(MetricId), descriptor.Unit, options);
        }
        else
        {
            ValueText = "N/A";
        }

        Refreshed?.Invoke();
    }
}
