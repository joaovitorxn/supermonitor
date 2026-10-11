using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class CategoryPage : Page
{
    private AppHost? _host;

    public CategoryPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.MemoryUpdated -= OnMemoryUpdated;
                ViewModel.Dispose();
                ViewModel = null;
            }
        };
    }

    public CategoryViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (CategoryContext)e.Parameter;
        _host = context.Page.Host;
        ViewModel = new CategoryViewModel(_host, context.Category, context.Title, DispatcherQueue, context.Page.IsMinimized, context.Page.AcquireProcesses);
        MemoryBar.Visibility = ViewModel.IsMemory ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPanel.Visibility = ViewModel.ShowsProcesses ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.MemoryUpdated += OnMemoryUpdated;
        Bindings.Update();
    }

    private void OnMemoryUpdated(MemoryComposition composition) =>
        MemoryBar.Update(composition, _host!.Settings.Current.ToUnitOptions());
}
