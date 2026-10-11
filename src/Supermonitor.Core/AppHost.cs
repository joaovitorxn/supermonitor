using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Cpu;
using Supermonitor.Core.Sensors.Disk;
using Supermonitor.Core.Sensors.Gpu;
using Supermonitor.Core.Sensors.Hardware;
using Supermonitor.Core.Sensors.Memory;
using Supermonitor.Core.Sensors.Network;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;

namespace Supermonitor.Core;

public sealed class AppHost : IAsyncDisposable
{
    public const int HistoryCapacity = 2400;

    private readonly PingMonitor _ping;

    private AppHost(SettingsStore settings, SettingsLoadResult loadResult, MetricStore metrics, SensorHub hub, PingMonitor ping, DemandTracker demand)
    {
        Demand = demand;
        Settings = settings;
        SettingsLoadResult = loadResult;
        Metrics = metrics;
        Hub = hub;
        _ping = ping;
    }

    public SettingsStore Settings { get; }

    public SettingsLoadResult SettingsLoadResult { get; }

    public MetricStore Metrics { get; }

    public SensorHub Hub { get; }

    public DemandTracker Demand { get; }

    public static IReadOnlyList<string> MonitoredGpuNames() =>
        DxgiAdapters.Enumerate()
            .Where(a => GpuAdapterFilter.IsMonitored(a.Flags, a.DedicatedVideoMemory))
            .Select(a => a.Name)
            .ToArray();

    public static AppHost Create()
    {
        Log.Initialize(AppPaths.LogDirectory);
        Log.Info("Supermonitor starting");
        if (!Privileges.TryEnable(Privileges.ProfileSingleProcess))
        {
            Log.Warn("Could not enable SeProfileSingleProcessPrivilege; memory lists will be unavailable");
        }

        var settings = new SettingsStore(AppPaths.SettingsFile, TimeProvider.System, TimeSpan.FromMilliseconds(500));
        var loadResult = settings.Load();

        var metrics = new MetricStore(HistoryCapacity);
        var demand = new DemandTracker();
        var ping = new PingMonitor(() => settings.Current.PingHost, TimeSpan.FromSeconds(2));
        ISensorProvider[] providers =
        [
            new CpuProvider(),
            new MemoryProvider(),
            new GpuProvider(),
            new HardwareProvider(MonitoredGpuNames),
            new DiskProvider(),
            new DriveSpaceProvider(),
            new NetworkProvider(),
            new PingProvider(ping),
        ];
        var hub = new SensorHub(metrics, providers, TimeProvider.System, TimeSpan.FromMilliseconds(settings.Current.SampleIntervalMs), demand);
        return new AppHost(settings, loadResult, metrics, hub, ping, demand);
    }

    public void Start()
    {
        _ping.Start();
        Hub.Start();
    }

    public async ValueTask DisposeAsync()
    {
        _ping.Dispose();
        await Hub.DisposeAsync().ConfigureAwait(false);
        Settings.Dispose();
        Log.Info("Supermonitor stopped");
    }
}
