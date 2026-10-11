using System.Runtime.CompilerServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Tests.Interop;

public class StructLayoutTests
{
    [Fact]
    public void Native_memory_structs_match_x64_sizes()
    {
        Assert.Equal(64, Unsafe.SizeOf<Kernel32.MEMORYSTATUSEX>());
        Assert.Equal(104, Unsafe.SizeOf<Psapi.PERFORMANCE_INFORMATION>());
        Assert.Equal(176, Unsafe.SizeOf<Ntdll.SYSTEM_MEMORY_LIST_INFORMATION>());
        Assert.Equal(1352, Unsafe.SizeOf<Iphlpapi.MIB_IF_ROW2>());
        Assert.Equal(312, Unsafe.SizeOf<Dxgi.DXGI_ADAPTER_DESC1>());
    }
}
