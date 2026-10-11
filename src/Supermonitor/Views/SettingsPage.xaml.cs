using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class SettingsPage : Page
{
    private AppHost? _host;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        var settings = _host.Settings.Current;

        _loading = true;
        IntervalBox.SelectedIndex = Math.Max(0, AppSettings.AllowedIntervalsMs.ToList().IndexOf(settings.SampleIntervalMs));
        PingHostBox.Text = settings.PingHost;
        StartMinimizedSwitch.IsOn = settings.StartMinimized;
        StartWithWindowsSwitch.IsOn = await Task.Run(AutostartManager.IsEnabled);
        _loading = false;
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || IntervalBox.SelectedIndex < 0)
        {
            return;
        }

        var milliseconds = AppSettings.AllowedIntervalsMs[IntervalBox.SelectedIndex];
        _host.Settings.Current.SampleIntervalMs = milliseconds;
        _host.Hub.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _host.Settings.RequestSave();
    }

    private void OnPingHostLostFocus(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        var host = PingHostBox.Text.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            PingHostError.Text = "Enter a valid host name or IP address.";
            PingHostError.Visibility = Visibility.Visible;
            PingHostBox.Text = _host.Settings.Current.PingHost;
            return;
        }

        PingHostError.Visibility = Visibility.Collapsed;
        _host.Settings.Current.PingHost = host;
        _host.Settings.RequestSave();
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var enable = StartWithWindowsSwitch.IsOn;
        try
        {
            await Task.Run(() => AutostartManager.SetEnabled(enable, Environment.ProcessPath!));
            AutostartError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Warn("Changing the startup task failed", ex);
            AutostartError.Text = $"Could not change the startup task: {ex.Message}";
            AutostartError.Visibility = Visibility.Visible;
            _loading = true;
            StartWithWindowsSwitch.IsOn = !enable;
            _loading = false;
        }
    }

    private void OnStartMinimizedToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.Settings.Current.StartMinimized = StartMinimizedSwitch.IsOn;
        _host.Settings.RequestSave();
    }
}
