using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Frames;

public sealed class ForegroundProbe : IForegroundProbe
{
    public int GetForegroundProcessId()
    {
        var window = User32.GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }

        User32.GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }
}
