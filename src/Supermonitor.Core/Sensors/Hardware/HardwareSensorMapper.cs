using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Hardware;

public readonly record struct WellKnownMetric(string Id, string DisplayName);

public static class HardwareSensorMapper
{
    public static MetricCategory? CategoryOf(HardwareType type) => type switch
    {
        HardwareType.Cpu => MetricCategory.Cpu,
        HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel => MetricCategory.Gpu,
        HardwareType.Storage => MetricCategory.Disk,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController or HardwareType.Cooler or HardwareType.Psu => MetricCategory.System,
        _ => null,
    };

    // Load, memory and traffic already come from PDH/Win32; only what Windows doesn't expose is here.
    public static MetricUnit? UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => MetricUnit.Celsius,
        SensorType.Clock => MetricUnit.Megahertz,
        SensorType.Power => MetricUnit.Watts,
        SensorType.Voltage => MetricUnit.Volts,
        SensorType.Fan => MetricUnit.Rpm,
        SensorType.Control => MetricUnit.Percent,
        _ => null,
    };

    public static string MetricId(string identifier) => "hw" + identifier;

    public static WellKnownMetric? WellKnown(HardwareType hardware, SensorType sensor, string name, int gpuIndex, int gpuCount)
    {
        var gpu = gpuCount > 1 ? $"GPU {gpuIndex}" : "GPU";
        return (hardware, sensor, name) switch
        {
            (HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)" or "CPU Package") => new(MetricIds.CpuTemperature, "CPU temperature"),
            (HardwareType.Cpu, SensorType.Power, "Package" or "CPU Package") => new(MetricIds.CpuPower, "CPU power"),
            (HardwareType.Cpu, SensorType.Clock, "Cores (Average)") => new(MetricIds.CpuClock, "CPU clock"),
            _ when !IsGpu(hardware) => null,
            (_, SensorType.Temperature, "GPU Core") => new(MetricIds.GpuTemperature(gpuIndex), $"{gpu} temperature"),
            (_, SensorType.Temperature, "GPU Hot Spot") => new(MetricIds.GpuHotspot(gpuIndex), $"{gpu} hot spot"),
            (_, SensorType.Temperature, "GPU Memory") => new(MetricIds.GpuMemoryTemperature(gpuIndex), $"{gpu} memory temperature"),
            (_, SensorType.Clock, "GPU Core") => new(MetricIds.GpuClock(gpuIndex), $"{gpu} clock"),
            (_, SensorType.Clock, "GPU Memory") => new(MetricIds.GpuMemoryClock(gpuIndex), $"{gpu} memory clock"),
            (_, SensorType.Power, "GPU Package" or "GPU Power") => new(MetricIds.GpuPower(gpuIndex), $"{gpu} power"),
            (_, SensorType.Fan, "GPU Fan") => new(MetricIds.GpuFan(gpuIndex), $"{gpu} fan"),
            (_, SensorType.Control, "GPU Fan") => new(MetricIds.GpuFanPercent(gpuIndex), $"{gpu} fan speed"),
            _ => null,
        };
    }

    public static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel;
}

public static class GpuIndexMatcher
{
    // Aligns the LHM GPU with the DXGI index used by GpuProvider (gpu{n}).
    public static int Match(IReadOnlyList<string> adapterNames, string name, ICollection<int> used)
    {
        for (var i = 0; i < adapterNames.Count; i++)
        {
            if (!used.Contains(i) && string.Equals(adapterNames[i].Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < adapterNames.Count; i++)
        {
            if (!used.Contains(i))
            {
                return i;
            }
        }

        var next = adapterNames.Count;
        while (used.Contains(next))
        {
            next++;
        }

        return next;
    }
}
