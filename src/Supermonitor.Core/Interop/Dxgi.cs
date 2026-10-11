using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Dxgi
{
    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 0x2;

    public static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(Guid* riid, void** factory);

    // Índices da vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), IDXGIFactory1.EnumAdapters1 = 12.
    public static int EnumAdapters1(void* factory, uint index, void** adapter) =>
        ((delegate* unmanaged<void*, uint, void**, int>)(*(void***)factory)[12])(factory, index, adapter);

    // IUnknown (0-2), IDXGIObject (3-6), IDXGIAdapter (7-9), IDXGIAdapter1.GetDesc1 = 10.
    public static int GetDesc1(void* adapter, DXGI_ADAPTER_DESC1* description) =>
        ((delegate* unmanaged<void*, DXGI_ADAPTER_DESC1*, int>)(*(void***)adapter)[10])(adapter, description);

    public static uint Release(void* unknown) =>
        ((delegate* unmanaged<void*, uint>)(*(void***)unknown)[2])(unknown);
}
