using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;
using Supermonitor.ViewModels;
using Supermonitor.Views;
using WinRT.Interop;

namespace Supermonitor;

public sealed partial class MainWindow : Window
{
    private readonly AppHost _host;
    private bool _lastMinimized;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(AppPaths.IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Changed += OnAppWindowChanged;

        SettingsInfoBar.IsOpen = host.SettingsLoadResult == SettingsLoadResult.RecoveredFromCorrupt;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public bool IsMinimized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        WindowActivation.ForceForeground(WindowNative.GetWindowHandle(this));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var minimized = IsMinimized;
        if (minimized != _lastMinimized)
        {
            _lastMinimized = minimized;
            ProcessEfficiency.SetEfficiencyMode(minimized);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var context = new PageContext(_host, () => IsMinimized);
        Navigate(args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string, context);
    }

    private void Navigate(string? tag, PageContext context)
    {
        switch (tag)
        {
            case "overview":
                ContentFrame.Navigate(typeof(OverviewPage), context);
                break;
        }
    }
}
