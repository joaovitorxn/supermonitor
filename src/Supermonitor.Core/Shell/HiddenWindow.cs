using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public sealed unsafe class HiddenWindow : IDisposable
{
    private readonly Func<uint, nint, nint, nint?> _handler;
    private readonly User32.WndProc _wndProc;
    private readonly nint _className;
    private readonly nint _instance;

    public HiddenWindow(string className, Func<uint, nint, nint, nint?> handler)
    {
        _handler = handler;
        _wndProc = WndProc;
        _instance = Kernel32.GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni(className);

        var windowClass = new User32.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(User32.WNDCLASSEXW),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };
        if (User32.RegisterClassExW(&windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Handle = User32.CreateWindowExW(User32.WS_EX_TOOLWINDOW, _className, _className, User32.WS_POPUP, 0, 0, 0, 0, 0, 0, _instance, 0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle != 0)
        {
            User32.DestroyWindow(Handle);
            Handle = 0;
            User32.UnregisterClassW(_className, _instance);
            Marshal.FreeHGlobal(_className);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var result = _handler(msg, wParam, lParam);
            if (result.HasValue)
            {
                return result.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Hidden window handler failed", ex);
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
