using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Core.Metrics;

namespace Supermonitor.ViewModels;

public sealed partial class InfoRowViewModel : ObservableObject
{
    public InfoRowViewModel(string label)
    {
        Label = label;
        Value = "N/A";
    }

    public string Label { get; }

    [ObservableProperty]
    public partial string Value { get; set; }
}

// Card that groups several readings of one device (disk space, temperatures), with an optional bar.
public sealed partial class InfoCardViewModel : ObservableObject
{
    private readonly Action<InfoCardViewModel, UnitOptions> _refresh;

    public InfoCardViewModel(string title, bool hasBar, IEnumerable<string> rowLabels, Action<InfoCardViewModel, UnitOptions> refresh)
    {
        Title = title;
        HasBar = hasBar;
        _refresh = refresh;
        foreach (var label in rowLabels)
        {
            Rows.Add(new InfoRowViewModel(label));
        }
    }

    public string Title { get; }

    public bool HasBar { get; }

    public ObservableCollection<InfoRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial double BarPercent { get; set; }

    public void Refresh(UnitOptions options) => _refresh(this, options);
}
