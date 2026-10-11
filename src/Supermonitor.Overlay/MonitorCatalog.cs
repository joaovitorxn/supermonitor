using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

public sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, bool IsPrimary, float Scale);

public static unsafe class MonitorCatalog
{
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? id) =>
        monitors.FirstOrDefault(m => id is not null && m.Id == id)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();

    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var found = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(found);
        try
        {
            User32.EnumDisplayMonitors(0, null, &OnMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return found
            .Select((m, index) => m with { Name = $"Display {index + 1} ({m.Bounds.Width}×{m.Bounds.Height}){(m.IsPrimary ? ", primary" : string.Empty)}" })
            .ToArray();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnMonitor(nint monitor, nint hdc, User32.RECT* rect, nint data)
    {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
        var info = new User32.MONITORINFOEXW { cbSize = (uint)sizeof(User32.MONITORINFOEXW) };
        if (User32.GetMonitorInfoW(monitor, &info))
        {
            var bounds = info.rcMonitor;
            Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dpi, out _);
            list.Add(new MonitorInfo(
                new string(info.szDevice),
                string.Empty,
                new PixelRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                dpi == 0 ? 1f : dpi / 96f));
        }

        return 1;
    }
}
