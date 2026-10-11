using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = User32.WM_APP + 1;
    private const int MenuOpen = 1;
    private const int MenuExit = 2;
    private const nuint RetryTimerId = 1;
    private const uint RetryIntervalMs = 2000;

    private readonly string _tooltip;
    private readonly Action _onOpen;
    private readonly Action _onExit;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private readonly HiddenWindow _window;

    public TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit)
    {
        _tooltip = tooltip;
        _onOpen = onOpen;
        _onExit = onExit;
        _taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        _icon = User32.LoadImageW(0, iconPath, User32.IMAGE_ICON, User32.GetSystemMetrics(User32.SM_CXSMICON), User32.GetSystemMetrics(User32.SM_CYSMICON), User32.LR_LOADFROMFILE);
        _window = new HiddenWindow("Supermonitor.Tray", HandleMessage);

        // Processo elevado: o UIPI bloqueia o broadcast TaskbarCreated vindo do Explorer sem esta exceção.
        User32.ChangeWindowMessageFilterEx(_window.Handle, _taskbarCreated, User32.MSGFLT_ALLOW, 0);
        Add();
    }

    public void Dispose()
    {
        var data = CreateData(0);
        Shell32.Shell_NotifyIconW(Shell32.NIM_DELETE, &data);
        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
        }

        _window.Dispose();
    }

    private void Add()
    {
        var data = CreateData(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        if (!Shell32.Shell_NotifyIconW(Shell32.NIM_ADD, &data))
        {
            // Barra de tarefas ainda não existe (autostart no logon): tenta de novo até conseguir.
            User32.SetTimer(_window.Handle, RetryTimerId, RetryIntervalMs, 0);
            return;
        }

        User32.KillTimer(_window.Handle, RetryTimerId);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Shell32.NIM_SETVERSION, &data);
    }

    private Shell32.NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new Shell32.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Shell32.NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127)).CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private nint? HandleMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == _taskbarCreated || (msg == User32.WM_TIMER && (nuint)wParam == RetryTimerId))
        {
            Add();
            return 0;
        }

        if (msg != CallbackMessage)
        {
            return null;
        }

        var notification = (uint)(lParam & 0xFFFF);
        if (notification is Shell32.NIN_SELECT or Shell32.NIN_KEYSELECT or User32.WM_LBUTTONDBLCLK)
        {
            _onOpen();
        }
        else if (notification == User32.WM_CONTEXTMENU)
        {
            ShowMenu();
        }

        return 0;
    }

    private void ShowMenu()
    {
        var menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, User32.MF_STRING, MenuOpen, "Open Supermonitor");
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING, MenuExit, "Exit");

        User32.GetCursorPos(out var point);
        User32.SetForegroundWindow(_window.Handle);
        var command = User32.TrackPopupMenuEx(menu, User32.TPM_RETURNCMD | User32.TPM_RIGHTBUTTON | User32.TPM_NONOTIFY, point.X, point.Y, _window.Handle, 0);
        User32.PostMessageW(_window.Handle, User32.WM_NULL, 0, 0);
        User32.DestroyMenu(menu);

        if (command == MenuOpen)
        {
            _onOpen();
        }
        else if (command == MenuExit)
        {
            _onExit();
        }
    }
}
