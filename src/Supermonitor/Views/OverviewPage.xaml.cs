using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Dialogs;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            ViewModel?.Dispose();
            ViewModel = null;
        };
    }

    public OverviewViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (PageContext)e.Parameter;
        ViewModel = new OverviewViewModel(context.Host, DispatcherQueue, context.IsMinimized);
        HistoryBox.SelectedIndex = ViewModel.HistoryIndex;
        Bindings.Update();
    }

    private void OnHistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null && HistoryBox.SelectedIndex >= 0)
        {
            ViewModel.HistoryIndex = HistoryBox.SelectedIndex;
        }
    }

    private void OnEditToggled(object sender, RoutedEventArgs e)
    {
        var editing = EditButton.IsChecked == true;
        if (ViewModel is not null)
        {
            ViewModel.IsEditing = editing;
        }

        AddButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        EditHint.Visibility = AddButton.Visibility;
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new AddCardDialog(ViewModel.AvailableMetrics()) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedMetricId is { } id)
        {
            ViewModel.AddCard(id);
        }
    }
}
