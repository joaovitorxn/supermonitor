using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Ntdll
{
    public const int SystemProcessInformation = 5;
    public const int SystemMemoryListInformation = 80;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_MEMORY_LIST_INFORMATION
    {
        public ulong ZeroPageCount;
        public ulong FreePageCount;
        public ulong ModifiedPageCount;
        public ulong ModifiedNoWritePageCount;
        public ulong BadPageCount;
        public fixed ulong PageCountByPriority[8];
        public fixed ulong RepurposedPagesByPriority[8];
        public ulong ModifiedPageCountPageFile;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, void* info, uint length, out uint returnLength);
}
