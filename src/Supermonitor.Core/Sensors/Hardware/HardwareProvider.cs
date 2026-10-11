using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Hardware;

public sealed class HardwareProvider(Func<IReadOnlyList<string>> gpuNames) : ISensorProvider
{
    private Computer? _computer;
    private IHardware[] _hardware = [];
    private Entry[] _entries = [];

    public static bool IsPawnIoInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    public string Name => "Hardware sensors";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public Demand? RequiredDemand => Demand.HardwareSensors;

    public void Initialize(MetricStore store)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
        };
        _computer.Open();

        var adapterNames = gpuNames();
        var usedGpus = new List<int>();
        var usedAliases = new HashSet<string>(StringComparer.Ordinal);
        var hardware = new List<IHardware>();
        var entries = new List<Entry>();

        foreach (var top in _computer.Hardware)
        {
            var category = HardwareSensorMapper.CategoryOf(top.HardwareType);
            var all = new List<IHardware> { top };
            all.AddRange(top.SubHardware);
            hardware.AddRange(all);
            foreach (var item in all)
            {
                item.Update();
            }

            if (category is null)
            {
                continue;
            }

            var gpuIndex = -1;
            if (HardwareSensorMapper.IsGpu(top.HardwareType))
            {
                gpuIndex = GpuIndexMatcher.Match(adapterNames, top.Name, usedGpus);
                usedGpus.Add(gpuIndex);
            }

            foreach (var sensor in all.SelectMany(h => h.Sensors))
            {
                var unit = HardwareSensorMapper.UnitOf(sensor.SensorType);
                if (sensor.IsDefaultHidden || unit is null)
                {
                    continue;
                }

                var wellKnown = HardwareSensorMapper.WellKnown(top.HardwareType, sensor.SensorType, sensor.Name, gpuIndex, Math.Max(adapterNames.Count, usedGpus.Count));
                var descriptor = wellKnown is { } known && usedAliases.Add(known.Id)
                    ? new MetricDescriptor(known.Id, known.DisplayName, category.Value, unit.Value, top.Name)
                    : new MetricDescriptor(HardwareSensorMapper.MetricId(sensor.Identifier.ToString()), sensor.Name, category.Value, unit.Value, top.Name);
                entries.Add(new Entry(sensor, store.Register(descriptor, Name)));
            }
        }

        _hardware = hardware.ToArray();
        _entries = entries.ToArray();
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        foreach (var item in _hardware)
        {
            item.Update();
        }

        foreach (var entry in _entries)
        {
            store.Write(entry.Handle, entry.Sensor.Value ?? float.NaN);
        }
    }

    public void Dispose() => _computer?.Close();

    private sealed record Entry(ISensor Sensor, MetricHandle Handle);
}
