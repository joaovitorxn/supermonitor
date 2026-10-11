using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

// Janela Win32 layered, click-through, sempre no topo, sem foco e fora do Alt+Tab.
// Precisa ser criada e usada na thread que roda o laço de mensagens.
internal sealed unsafe class OverlayWindow : IDisposable
{
    private const string ClassName = "Supermonitor.Overlay";

    private readonly Func<uint, nuint, nint, bool> _handler;
    private readonly User32.WndProc _wndProc;
    private readonly nint _className;
    private readonly nint _instance;

    public OverlayWindow(Func<uint, nuint, nint, bool> handler)
    {
        _handler = handler;
        _wndProc = WndProc;
        _instance = Kernel32.GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni(ClassName);

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

        const uint exStyle = User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW;
        Handle = User32.CreateWindowExW(exStyle, _className, _className, User32.WS_POPUP, 0, 0, 1, 1, 0, 0, _instance, 0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public nint Handle { get; private set; }

    // Troca o conteúdo e a posição de uma vez; o DIB já vem premultiplicado.
    public void Present(OverlayBitmap bitmap, int x, int y)
    {
        var position = new User32.POINT { X = x, Y = y };
        var size = new User32.SIZE { cx = bitmap.Width, cy = bitmap.Height };
        var origin = new User32.POINT();
        var blend = new User32.BLENDFUNCTION { BlendOp = User32.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = User32.AC_SRC_ALPHA };
        if (!User32.UpdateLayeredWindow(Handle, 0, &position, &size, bitmap.Hdc, &origin, 0, &blend, User32.ULW_ALPHA))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Show() => User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);

    public void Hide() => User32.ShowWindow(Handle, User32.SW_HIDE);

    // Jogos borderless também são topmost; reafirmar a ordem de tempos em tempos mantém o overlay por cima.
    public void KeepOnTop() =>
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

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

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (_handler(msg, wParam, lParam))
        {
            return 0;
        }

        if (msg == User32.WM_DESTROY)
        {
            User32.PostQuitMessage(0);
            return 0;
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
