using System.Globalization;

namespace Supermonitor.Core.Shell;

public static class RestartArguments
{
    public const string WaitForPid = "--wait-for-pid";

    public static string[] Build(int processId) => [WaitForPid, processId.ToString(CultureInfo.InvariantCulture)];

    public static bool TryGetWaitPid(IReadOnlyList<string> args, out int processId)
    {
        processId = 0;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == WaitForPid)
            {
                return int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out processId) && processId > 0;
            }
        }

        return false;
    }
}
