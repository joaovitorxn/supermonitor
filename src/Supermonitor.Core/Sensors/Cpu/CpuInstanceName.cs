using System.Globalization;

namespace Supermonitor.Core.Sensors.Cpu;

public static class CpuInstanceName
{
    // "Processor Information" instances: "group,core" (e.g. "0,7"), "0,_Total", "_Total".
    public static bool TryParse(ReadOnlySpan<char> name, out int key)
    {
        key = 0;
        var comma = name.IndexOf(',');
        if (comma <= 0)
        {
            return false;
        }

        if (!int.TryParse(name[..comma], NumberStyles.None, CultureInfo.InvariantCulture, out var group)
            || !int.TryParse(name[(comma + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        key = group * 1024 + number;
        return true;
    }
}
