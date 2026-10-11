using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
        Bindings.Update();
    }
}
