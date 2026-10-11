using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Controls;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

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
        Size = CardSize.Normal;
        Chart = ChartKind.Line;
        Level = ThresholdLevel.Normal;
    }

    public event Action? Refreshed;

    public string MetricId { get; }

    public ICardActions? Actions { get; init; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ValueText { get; set; }

    [ObservableProperty]
    public partial CardSize Size { get; set; }

    [ObservableProperty]
    public partial ChartKind Chart { get; set; }

    [ObservableProperty]
    public partial ThresholdLevel Level { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public int PointCount { get; set; } = 60;

    public float? FixedMaximum { get; private set; }

    public int CopyHistory(Span<float> destination) => _store.CopyHistory(MetricId, destination);

    public void Refresh(UnitOptions options, MetricThreshold? threshold)
    {
        var latest = _store.GetLatest(MetricId);
        if (_store.TryGetDescriptor(MetricId, out var descriptor))
        {
            Title = descriptor.DisplayName;
            FixedMaximum = descriptor.Unit == MetricUnit.Percent ? 100f : null;
            ValueText = UnitFormatter.Format(latest, descriptor.Unit, options);
        }
        else
        {
            ValueText = "N/A";
        }

        Level = ThresholdEvaluator.Evaluate(latest, threshold);
        Refreshed?.Invoke();
    }
}
