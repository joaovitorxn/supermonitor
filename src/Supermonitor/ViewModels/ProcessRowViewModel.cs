using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.ViewModels;

public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel()
    {
        Name = string.Empty;
        Pid = string.Empty;
        CpuText = string.Empty;
        MemoryText = string.Empty;
        GpuText = string.Empty;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Pid { get; set; }

    [ObservableProperty]
    public partial string CpuText { get; set; }

    [ObservableProperty]
    public partial string MemoryText { get; set; }

    [ObservableProperty]
    public partial string GpuText { get; set; }

    public void Update(ProcessSample sample, UnitOptions options)
    {
        Name = sample.Name;
        Pid = sample.ProcessId.ToString(CultureInfo.InvariantCulture);
        CpuText = float.IsFinite(sample.CpuPercent) ? sample.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
        MemoryText = UnitFormatter.Format(sample.PrivateBytes, MetricUnit.Bytes, options);
        GpuText = sample.GpuPercent > 0 ? sample.GpuPercent.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
    }
}
