using System.Globalization;

namespace Supermonitor.Core.Sensors.Disk;

public static class DiskInstanceName
{
    // Instâncias de "PhysicalDisk": "0 C:", "1 D: E:", "_Total".
    public static bool TryParse(ReadOnlySpan<char> name, out int index, out ReadOnlySpan<char> label)
    {
        label = default;
        var space = name.IndexOf(' ');
        var number = space < 0 ? name : name[..space];
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        label = space < 0 ? ReadOnlySpan<char>.Empty : name[(space + 1)..].Trim();
        return true;
    }
}
