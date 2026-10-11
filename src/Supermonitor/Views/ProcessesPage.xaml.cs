using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core.Sensors.Processes;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class ProcessesPage : Page
{
    public ProcessesPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            ViewModel?.Dispose();
            ViewModel = null;
        };
    }

    public ProcessesViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (PageContext)e.Parameter;
        ViewModel = new ProcessesViewModel(context.Host, DispatcherQueue, context.IsMinimized, context.AcquireProcesses);
        Bindings.Update();
    }

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            ViewModel?.SortBy(Enum.Parse<ProcessSortKey>(tag));
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) =>
        ViewModel?.SetFilter(((TextBox)sender).Text);
}
