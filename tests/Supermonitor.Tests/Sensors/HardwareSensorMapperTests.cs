using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Hardware;

namespace Supermonitor.Tests.Sensors;

public class HardwareSensorMapperTests
{
    [Theory]
    [InlineData(HardwareType.Cpu, MetricCategory.Cpu)]
    [InlineData(HardwareType.GpuAmd, MetricCategory.Gpu)]
    [InlineData(HardwareType.GpuNvidia, MetricCategory.Gpu)]
    [InlineData(HardwareType.GpuIntel, MetricCategory.Gpu)]
    [InlineData(HardwareType.Storage, MetricCategory.Disk)]
    [InlineData(HardwareType.Motherboard, MetricCategory.System)]
    [InlineData(HardwareType.SuperIO, MetricCategory.System)]
    public void Maps_hardware_types_to_categories(HardwareType type, MetricCategory expected)
    {
        Assert.Equal(expected, HardwareSensorMapper.CategoryOf(type));
    }

    [Theory]
    [InlineData(HardwareType.Memory)]
    [InlineData(HardwareType.Network)]
    [InlineData(HardwareType.Battery)]
    public void Ignores_hardware_already_covered_or_out_of_scope(HardwareType type)
    {
        Assert.Null(HardwareSensorMapper.CategoryOf(type));
    }

    [Theory]
    [InlineData(SensorType.Temperature, MetricUnit.Celsius)]
    [InlineData(SensorType.Clock, MetricUnit.Megahertz)]
    [InlineData(SensorType.Power, MetricUnit.Watts)]
    [InlineData(SensorType.Voltage, MetricUnit.Volts)]
    [InlineData(SensorType.Fan, MetricUnit.Rpm)]
    [InlineData(SensorType.Control, MetricUnit.Percent)]
    public void Maps_sensor_types_to_units(SensorType type, MetricUnit expected)
    {
        Assert.Equal(expected, HardwareSensorMapper.UnitOf(type));
    }

    [Theory]
    [InlineData(SensorType.Load)]
    [InlineData(SensorType.Data)]
    [InlineData(SensorType.SmallData)]
    [InlineData(SensorType.Factor)]
    [InlineData(SensorType.Throughput)]
    public void Ignores_sensor_types_covered_elsewhere(SensorType type)
    {
        Assert.Null(HardwareSensorMapper.UnitOf(type));
    }

    [Fact]
    public void Metric_id_prefixes_lhm_identifier()
    {
        Assert.Equal("hw/amdcpu/0/temperature/2", HardwareSensorMapper.MetricId("/amdcpu/0/temperature/2"));
    }

    [Theory]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "cpu.temp", "CPU temperature")]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CPU Package", "cpu.temp", "CPU temperature")]
    [InlineData(HardwareType.Cpu, SensorType.Power, "Package", "cpu.power", "CPU power")]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "Cores (Average)", "cpu.clock", "CPU clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Core", "gpu0.temp", "GPU temperature")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Hot Spot", "gpu0.hotspot", "GPU hot spot")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Memory", "gpu0.memtemp", "GPU memory temperature")]
    [InlineData(HardwareType.GpuAmd, SensorType.Clock, "GPU Core", "gpu0.clock", "GPU clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Clock, "GPU Memory", "gpu0.memclock", "GPU memory clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Power, "GPU Package", "gpu0.power", "GPU power")]
    [InlineData(HardwareType.GpuIntel, SensorType.Power, "GPU Power", "gpu0.power", "GPU power")]
    [InlineData(HardwareType.GpuNvidia, SensorType.Fan, "GPU Fan", "gpu0.fan", "GPU fan")]
    [InlineData(HardwareType.GpuNvidia, SensorType.Control, "GPU Fan", "gpu0.fanpct", "GPU fan speed")]
    public void Recognizes_well_known_sensors(HardwareType hardware, SensorType sensor, string name, string expectedId, string expectedName)
    {
        var metric = HardwareSensorMapper.WellKnown(hardware, sensor, name, gpuIndex: 0, gpuCount: 1);

        Assert.NotNull(metric);
        Assert.Equal(expectedId, metric.Value.Id);
        Assert.Equal(expectedName, metric.Value.DisplayName);
    }

    [Fact]
    public void Well_known_gpu_names_carry_index_when_there_are_several_gpus()
    {
        var metric = HardwareSensorMapper.WellKnown(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Core", gpuIndex: 1, gpuCount: 2);

        Assert.Equal(new WellKnownMetric("gpu1.temp", "GPU 1 temperature"), metric);
    }

    [Theory]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CCD1 (Tdie)")]
    [InlineData(HardwareType.Motherboard, SensorType.Temperature, "GPU Core")]
    [InlineData(HardwareType.GpuAmd, SensorType.Voltage, "GPU Core")]
    public void Other_sensors_are_not_well_known(HardwareType hardware, SensorType sensor, string name)
    {
        Assert.Null(HardwareSensorMapper.WellKnown(hardware, sensor, name, 0, 1));
    }

    [Fact]
    public void Gpu_matcher_prefers_same_name_then_first_unused()
    {
        string[] adapters = ["AMD Radeon RX 9070 XT", "Intel UHD"];
        var used = new List<int>();

        Assert.Equal(1, GpuIndexMatcher.Match(adapters, "intel uhd", used));
        used.Add(1);
        Assert.Equal(0, GpuIndexMatcher.Match(adapters, "Unknown GPU", used));
        used.Add(0);
        Assert.Equal(2, GpuIndexMatcher.Match(adapters, "Extra GPU", used));
    }

    [Fact]
    public void Gpu_matcher_assigns_identical_cards_in_order()
    {
        string[] adapters = ["RTX 4090", "RTX 4090"];
        var used = new List<int>();

        var first = GpuIndexMatcher.Match(adapters, "RTX 4090", used);
        used.Add(first);
        var second = GpuIndexMatcher.Match(adapters, "RTX 4090", used);

        Assert.Equal((0, 1), (first, second));
    }
}
