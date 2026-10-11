using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Shell;

namespace Supermonitor;

public partial class App : Application
{
    private readonly string[] _args;
    private readonly SingleInstance _instance;
    private DispatcherQueue? _dispatcher;
    private AppHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;

    public App(string[] args, SingleInstance instance)
    {
        _args = args;
        _instance = instance;
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _host = AppHost.Create();
        _host.Start();

        _tray = new TrayIcon(
            "Supermonitor",
            AppPaths.IconPath,
            onOpen: () => _dispatcher.TryEnqueue(ShowDashboard),
            onExit: () => _dispatcher.TryEnqueue(ExitApp));
        _instance.ListenForActivation(() => _dispatcher.TryEnqueue(ShowDashboard));

        var minimized = _args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) || _host.Settings.Current.StartMinimized;
        if (minimized)
        {
            ProcessEfficiency.SetEfficiencyMode(true);
        }
        else
        {
            ShowDashboard();
        }
    }

    private void ShowDashboard()
    {
        if (_exiting || _host is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_host);
            _window.Closed += OnWindowClosed;
        }

        ProcessEfficiency.SetEfficiencyMode(false);
        _window.BringToFront();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _window = null;
        ProcessEfficiency.SetEfficiencyMode(true);
    }

    private async void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        try
        {
            _tray?.Dispose();
            _window?.Close();
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Shutdown failed", ex);
        }
        finally
        {
            Exit();
        }
    }
}
