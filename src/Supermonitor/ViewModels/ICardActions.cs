using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public interface ICardActions
{
    void Remove(MetricCardViewModel card);

    void Move(MetricCardViewModel card, int delta);

    void MoveTo(string metricId, string targetMetricId);

    void SetSize(MetricCardViewModel card, CardSize size);

    void SetChart(MetricCardViewModel card, ChartKind chart);

    MetricThreshold? GetThreshold(string metricId);

    void SetThreshold(string metricId, MetricThreshold? threshold);
}
