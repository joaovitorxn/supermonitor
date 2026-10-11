using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Appearance;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Hardware;
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
    private IDisposable? _sensorLease;
    private bool _pawnIoReady;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(AppPaths.IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Changed += OnAppWindowChanged;
        AppearanceService.Apply(this, RootGrid, host.Settings.Current);
        AppearanceService.Changed += OnAppearanceChanged;
        RootGrid.ActualThemeChanged += (_, _) => AppearanceService.Apply(this, RootGrid, _host.Settings.Current);
        Closed += (_, _) => AppearanceService.Changed -= OnAppearanceChanged;

        SettingsInfoBar.IsOpen = App.Current.ConsumeSettingsWarning();
        PawnIoInfoBar.IsOpen = !HardwareProvider.IsPawnIoInstalled && !App.Current.PawnIoBannerDismissed;
        Nav.SelectedItem = Nav.MenuItems[0];

        Closed += (_, _) => ReleaseSensorLease();
        UpdateSensorLease();
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
            UpdateSensorLease();
        }
    }

    private void OnAppearanceChanged() => AppearanceService.Apply(this, RootGrid, _host.Settings.Current);

    private void OnPawnIoInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => App.Current.PawnIoBannerDismissed = true;

    private async void OnPawnIoButtonClick(object sender, RoutedEventArgs e)
    {
        if (_pawnIoReady)
        {
            App.Current.Restart();
            return;
        }

        PawnIoButton.IsEnabled = false;
        PawnIoInfoBar.Message = "Installing PawnIO…";
        try
        {
            var exitCode = await PawnIoInstaller.InstallAsync();
            if (exitCode == 0 || HardwareProvider.IsPawnIoInstalled)
            {
                _pawnIoReady = true;
                PawnIoInfoBar.Severity = InfoBarSeverity.Success;
                PawnIoInfoBar.Title = "PawnIO installed";
                PawnIoInfoBar.Message = "Restart Supermonitor to load the driver.";
                PawnIoButton.Content = "Restart now";
            }
            else
            {
                PawnIoInfoBar.Severity = InfoBarSeverity.Error;
                PawnIoInfoBar.Message = $"Installation failed (winget exit code {exitCode}). You can install PawnIO manually from pawnio.eu.";
                PawnIoButton.Content = "Try again";
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn("winget could not be started", ex);
            PawnIoInfoBar.Severity = InfoBarSeverity.Error;
            PawnIoInfoBar.Message = "winget is not available. Install PawnIO manually from pawnio.eu.";
        }
        finally
        {
            PawnIoButton.IsEnabled = true;
        }
    }

    private void UpdateSensorLease()
    {
        if (IsMinimized)
        {
            ReleaseSensorLease();
        }
        else
        {
            _sensorLease ??= _host.Demand.Acquire(Demand.HardwareSensors);
        }
    }

    private void ReleaseSensorLease()
    {
        _sensorLease?.Dispose();
        _sensorLease = null;
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
            case "cpu":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Cpu, "CPU"));
                break;
            case "memory":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Memory, "Memory"));
                break;
            case "gpu":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Gpu, "GPU"));
                break;
            case "disk":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Disk, "Disks"));
                break;
            case "network":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Network, "Network"));
                break;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage), context);
                break;
        }
    }
}
