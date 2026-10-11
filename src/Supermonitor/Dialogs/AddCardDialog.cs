using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Dialogs;

public sealed partial class AddCardDialog : ContentDialog
{
    private readonly MetricOption[] _all;
    private readonly ListView _list;

    public AddCardDialog(IReadOnlyList<MetricDescriptor> available)
    {
        Title = "Add card";
        PrimaryButtonText = "Add";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        _all = available.Select(d => new MetricOption(d.Id, $"{d.DisplayName} — {d.Device ?? d.Category.ToString()}")).ToArray();
        var search = new TextBox { PlaceholderText = "Search metrics" };
        _list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single, ItemsSource = _all };
        search.TextChanged += (_, _) =>
        {
            var term = search.Text.Trim();
            _list.ItemsSource = term.Length == 0
                ? _all
                : _all.Where(o => o.Label.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        _list.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = _list.SelectedItem is MetricOption;
        Content = new StackPanel { Spacing = 8, Width = 420, Children = { search, _list } };
    }

    public string? SelectedMetricId => (_list.SelectedItem as MetricOption)?.Id;

    private sealed record MetricOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
