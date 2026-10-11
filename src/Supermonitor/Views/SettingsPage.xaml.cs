using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Appearance;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Sensors.Hardware;
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

    private AppSettings Settings => _host!.Settings.Current;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        _loading = true;
        ThemeButtons.SelectedIndex = (int)Settings.Theme;
        BackdropBox.SelectedIndex = (int)Settings.Backdrop;
        SystemAccentSwitch.IsOn = Settings.AccentColor is null;
        AccentPicker.Visibility = SystemAccentSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        AccentPicker.Color = AppearanceService.ResolveAccent(Settings.AccentColor);
        BitsSwitch.IsOn = Settings.NetworkInBits;
        FahrenheitSwitch.IsOn = Settings.Fahrenheit;
        IntervalBox.SelectedIndex = Math.Max(0, AppSettings.AllowedIntervalsMs.ToList().IndexOf(Settings.SampleIntervalMs));
        PingHostBox.Text = Settings.PingHost;
        StartMinimizedSwitch.IsOn = Settings.StartMinimized;
        PawnIoStatus.Text = HardwareProvider.IsPawnIoInstalled
            ? "PawnIO driver: installed. CPU temperature, clock and power are available."
            : "PawnIO driver: not installed. CPU temperature, clock and power are unavailable.";
        ProviderStatus.Text = string.Join(
            Environment.NewLine,
            _host.Hub.GetStatuses().Select(s => s.IsAvailable ? $"{s.Name}: available" : $"{s.Name}: unavailable ({s.UnavailableReason})"));

        try
        {
            StartWithWindowsSwitch.IsOn = await Task.Run(AutostartManager.IsEnabled);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("Could not read the startup task", ex);
            StartWithWindowsSwitch.IsEnabled = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveAppearance()
    {
        _host!.Settings.RequestSave();
        AppearanceService.NotifyChanged();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || ThemeButtons.SelectedIndex < 0)
        {
            return;
        }

        Settings.Theme = (AppTheme)ThemeButtons.SelectedIndex;
        SaveAppearance();
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || BackdropBox.SelectedIndex < 0)
        {
            return;
        }

        Settings.Backdrop = (BackdropKind)BackdropBox.SelectedIndex;
        SaveAppearance();
    }

    private void OnSystemAccentToggled(object sender, RoutedEventArgs e)
    {
        AccentPicker.Visibility = SystemAccentSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        if (_loading || _host is null)
        {
            return;
        }

        var color = AccentPicker.Color;
        Settings.AccentColor = SystemAccentSwitch.IsOn ? null : ColorText.ToHex(color.R, color.G, color.B);
        SaveAppearance();
    }

    private void OnAccentChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading || _host is null || SystemAccentSwitch.IsOn)
        {
            return;
        }

        Settings.AccentColor = ColorText.ToHex(args.NewColor.R, args.NewColor.G, args.NewColor.B);
        SaveAppearance();
    }

    private void OnUnitsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        Settings.NetworkInBits = BitsSwitch.IsOn;
        Settings.Fahrenheit = FahrenheitSwitch.IsOn;
        _host.Settings.RequestSave();
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || IntervalBox.SelectedIndex < 0)
        {
            return;
        }

        var milliseconds = AppSettings.AllowedIntervalsMs[IntervalBox.SelectedIndex];
        Settings.SampleIntervalMs = milliseconds;
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
            PingHostBox.Text = Settings.PingHost;
            return;
        }

        PingHostError.Visibility = Visibility.Collapsed;
        Settings.PingHost = host;
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

        Settings.StartMinimized = StartMinimizedSwitch.IsOn;
        _host.Settings.RequestSave();
    }
}
