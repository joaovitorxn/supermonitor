using System.Collections.ObjectModel;

namespace Supermonitor.ViewModels;

public sealed record MetricGroupViewModel(string Name, ObservableCollection<MetricCardViewModel> Items);

public sealed record CategoryContext(PageContext Page, Core.Metrics.MetricCategory Category, string Title);
