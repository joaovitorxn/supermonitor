using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Sensors.Gpu;

public sealed record GpuAdapterInfo(string Name, uint LuidLow, int LuidHigh, ulong DedicatedVideoMemory, uint Flags)
{
    public string PdhLuid => $"0x{(uint)LuidHigh:X8}_0x{LuidLow:X8}";
}

public static class GpuAdapterFilter
{
    public static bool IsMonitored(uint flags, ulong dedicatedVideoMemory) =>
        (flags & Dxgi.DXGI_ADAPTER_FLAG_SOFTWARE) == 0 && dedicatedVideoMemory > 0;
}

public static unsafe class DxgiAdapters
{
    public static IReadOnlyList<GpuAdapterInfo> Enumerate()
    {
        var iid = Dxgi.IID_IDXGIFactory1;
        void* factory;
        Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory1(&iid, &factory));

        var adapters = new List<GpuAdapterInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                void* adapter;
                var hr = Dxgi.EnumAdapters1(factory, index, &adapter);
                if (hr == Dxgi.DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Dxgi.DXGI_ADAPTER_DESC1 description;
                    Marshal.ThrowExceptionForHR(Dxgi.GetDesc1(adapter, &description));
                    adapters.Add(new GpuAdapterInfo(
                        new string(description.Description),
                        description.LuidLow,
                        description.LuidHigh,
                        description.DedicatedVideoMemory,
                        description.Flags));
                }
                finally
                {
                    Dxgi.Release(adapter);
                }
            }
        }
        finally
        {
            Dxgi.Release(factory);
        }

        return adapters;
    }
}
