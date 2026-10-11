using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Disk;

public sealed class DriveSpaceProvider : ISensorProvider
{
    private readonly Dictionary<char, (MetricHandle Free, MetricHandle Total, MetricHandle Usage)> _drives = [];

    public string Name => "Drive space";

    public TimeSpan MinInterval => TimeSpan.FromSeconds(5);

    public void Initialize(MetricStore store) => Sample(store, TimeSpan.Zero);

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            var letter = char.ToUpperInvariant(drive.Name[0]);
            if (!_drives.TryGetValue(letter, out var handles))
            {
                handles = (
                    Register(store, MetricIds.DriveFree(letter), $"{letter}: free", MetricUnit.Bytes),
                    Register(store, MetricIds.DriveTotal(letter), $"{letter}: size", MetricUnit.Bytes),
                    Register(store, MetricIds.DriveUsage(letter), $"{letter}: used", MetricUnit.Percent));
                _drives[letter] = handles;
            }

            store.Write(handles.Free, drive.TotalFreeSpace);
            store.Write(handles.Total, drive.TotalSize);
            store.Write(handles.Usage, (float)((drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize));
        }
    }

    public void Dispose()
    {
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Disk, unit), Name);
}
