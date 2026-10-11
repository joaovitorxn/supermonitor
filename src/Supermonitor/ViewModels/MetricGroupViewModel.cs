using System.Collections.ObjectModel;

namespace Supermonitor.ViewModels;

public sealed record MetricGroupViewModel(string Name, ObservableCollection<MetricCardViewModel> Items)
{
    // Cards that group several readings (space, temperatures); shown before the charts.
    public ObservableCollection<InfoCardViewModel> Summary { get; init; } = [];
}

public sealed record CategoryContext(PageContext Page, Core.Metrics.MetricCategory Category, string Title);
