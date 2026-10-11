using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public static class WindowActivation
{
    public static void ForceForeground(nint hwnd) => User32.SetForegroundWindow(hwnd);
}
