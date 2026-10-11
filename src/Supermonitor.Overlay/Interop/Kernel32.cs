using System.Runtime.InteropServices;

namespace Supermonitor.Overlay.Interop;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);
}
