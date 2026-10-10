using System.Runtime.InteropServices;

namespace Supermonitor.Core.Sensors.Pdh;

public sealed class PdhException(string operation, uint status)
    : Exception($"{operation} failed with 0x{status:X8}")
{
    public uint Status { get; } = status;
}

public sealed class PdhQuery : IDisposable
{
    private readonly List<PdhCounter> _counters = [];
    private nint _handle;

    public PdhQuery()
    {
        Check(Interop.Pdh.PdhOpenQuery(null, 0, out _handle), "PdhOpenQuery");
    }

    public PdhCounter AddCounter(string englishPath)
    {
        Check(Interop.Pdh.PdhAddEnglishCounter(_handle, englishPath, 0, out var counter), $"PdhAddEnglishCounter({englishPath})");
        var wrapper = new PdhCounter(counter);
        _counters.Add(wrapper);
        return wrapper;
    }

    public void Collect() => Check(Interop.Pdh.PdhCollectQueryData(_handle), "PdhCollectQueryData");

    public void Dispose()
    {
        foreach (var counter in _counters)
        {
            counter.FreeBuffer();
        }

        if (_handle != 0)
        {
            Interop.Pdh.PdhCloseQuery(_handle);
            _handle = 0;
        }
    }

    internal static void Check(uint status, string operation)
    {
        if (status != 0)
        {
            throw new PdhException(operation, status);
        }
    }
}

public sealed unsafe class PdhCounter
{
    private readonly nint _handle;
    private byte* _buffer;
    private uint _bufferSize;

    internal PdhCounter(nint handle) => _handle = handle;

    public double GetValue()
    {
        var status = Interop.Pdh.PdhGetFormattedCounterValue(_handle, Interop.Pdh.PDH_FMT_DOUBLE, out _, out var value);
        return status == 0 && value.CStatus <= Interop.Pdh.PDH_CSTATUS_NEW_DATA ? value.DoubleValue : double.NaN;
    }

    public PdhArray GetArray()
    {
        while (true)
        {
            var size = _bufferSize;
            var status = Interop.Pdh.PdhGetFormattedCounterArray(_handle, Interop.Pdh.PDH_FMT_DOUBLE, ref size, out var count, _buffer);
            if (status == Interop.Pdh.PDH_MORE_DATA)
            {
                Grow(size);
                continue;
            }

            return status == 0
                ? new PdhArray((Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W*)_buffer, (int)count)
                : default;
        }
    }

    internal void FreeBuffer()
    {
        NativeMemory.Free(_buffer);
        _buffer = null;
        _bufferSize = 0;
    }

    private void Grow(uint required)
    {
        NativeMemory.Free(_buffer);
        _bufferSize = Math.Max(required, _bufferSize * 2);
        _buffer = (byte*)NativeMemory.Alloc(_bufferSize);
    }
}

public readonly unsafe ref struct PdhArray
{
    private readonly Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W* _items;

    internal PdhArray(Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W* items, int count)
    {
        _items = items;
        Count = count;
    }

    public int Count { get; }

    public ReadOnlySpan<char> NameAt(int index) => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_items[index].szName);

    public double ValueAt(int index) =>
        _items[index].FmtValue.CStatus <= Interop.Pdh.PDH_CSTATUS_NEW_DATA ? _items[index].FmtValue.DoubleValue : double.NaN;
}
