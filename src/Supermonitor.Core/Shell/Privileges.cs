using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public static unsafe partial class Privileges
{
    public const string ProfileSingleProcess = "SeProfileSingleProcessPrivilege";

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    public static bool TryEnable(string name)
    {
        if (!OpenProcessToken(Kernel32.GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid))
            {
                return false;
            }

            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, &privileges, 0, 0, 0))
            {
                return false;
            }

            // ERROR_NOT_ALL_ASSIGNED (1300): the token lacks the privilege (app not elevated).
            return Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        nint token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        TOKEN_PRIVILEGES* newState,
        uint bufferLength,
        nint previousState,
        nint returnLength);
}
