using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Core.Sensors.Processes;

public sealed unsafe class ProcessMonitor(ProcessGpuUsage gpuUsage) : ISensorProvider
{
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    private readonly ProcessTracker _tracker = new();
    private readonly List<RawProcessEntry> _raw = [];
    private byte* _buffer;
    private int _bufferSize;
    private ProcessSample[] _snapshot = [];

    public string Name => "Processes";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public Demand? RequiredDemand => Demand.Processes;

    public IReadOnlyList<ProcessSample> Snapshot => Volatile.Read(ref _snapshot);

    public void Initialize(MetricStore store)
    {
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        Query();
        var samples = _tracker.Update(
            _raw,
            elapsed,
            Environment.ProcessorCount,
            e => SystemProcessParser.ReadName(e) ?? $"PID {e.ProcessId}",
            gpuUsage.Get);
        Volatile.Write(ref _snapshot, samples);
    }

    public void Dispose()
    {
        NativeMemory.Free(_buffer);
        _buffer = null;
    }

    private void Query()
    {
        if (_buffer == null)
        {
            Grow(1 << 20);
        }

        while (true)
        {
            var status = Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessInformation, _buffer, (uint)_bufferSize, out var needed);
            if (status == StatusInfoLengthMismatch)
            {
                Grow(Math.Max((int)needed + 65536, _bufferSize * 2));
                continue;
            }

            if (status < 0)
            {
                throw new Win32Exception($"NtQuerySystemInformation failed with 0x{status:X8}");
            }

            SystemProcessParser.Parse(_buffer, _bufferSize, _raw);
            return;
        }
    }

    private void Grow(int size)
    {
        NativeMemory.Free(_buffer);
        _buffer = (byte*)NativeMemory.Alloc((nuint)size);
        _bufferSize = size;
    }
}
