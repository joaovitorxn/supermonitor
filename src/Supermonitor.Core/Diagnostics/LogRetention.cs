using System.Globalization;

namespace Supermonitor.Core.Diagnostics;

public static class LogRetention
{
    public const string FilePrefix = "supermonitor-";
    public const int KeepDays = 7;

    public static IEnumerable<string> FilesToDelete(IEnumerable<string> fileNames, DateOnly today, int keepDays)
    {
        var cutoff = today.AddDays(-(keepDays - 1));
        foreach (var name in fileNames)
        {
            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var datePart = name.AsSpan(FilePrefix.Length, name.Length - FilePrefix.Length - ".log".Length);
            if (DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date < cutoff)
            {
                yield return name;
            }
        }
    }
}
