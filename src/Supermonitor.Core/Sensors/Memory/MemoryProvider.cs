using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Memory;

public sealed unsafe class MemoryProvider : ISensorProvider
{
    private MetricHandle _total;
    private MetricHandle _used;
    private MetricHandle _available;
    private MetricHandle _usage;
    private MetricHandle _standby;
    private MetricHandle _free;
    private MetricHandle _modified;
    private MetricHandle _commit;
    private MetricHandle _commitLimit;
    private MetricHandle _cache;

    public string Name => "Memory";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _total = Register(store, MetricIds.MemoryTotal, "Memory total", MetricUnit.Bytes);
        _used = Register(store, MetricIds.MemoryUsed, "Memory used", MetricUnit.Bytes);
        _available = Register(store, MetricIds.MemoryAvailable, "Memory available", MetricUnit.Bytes);
        _usage = Register(store, MetricIds.MemoryUsage, "Memory usage", MetricUnit.Percent);
        _standby = Register(store, MetricIds.MemoryStandby, "Standby", MetricUnit.Bytes);
        _free = Register(store, MetricIds.MemoryFree, "Free", MetricUnit.Bytes);
        _modified = Register(store, MetricIds.MemoryModified, "Modified", MetricUnit.Bytes);
        _commit = Register(store, MetricIds.MemoryCommitted, "Committed", MetricUnit.Bytes);
        _commitLimit = Register(store, MetricIds.MemoryCommitLimit, "Commit limit", MetricUnit.Bytes);
        _cache = Register(store, MetricIds.MemoryCache, "Cached", MetricUnit.Bytes);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)sizeof(Kernel32.MEMORYSTATUSEX) };
        if (!Kernel32.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var used = status.ullTotalPhys - status.ullAvailPhys;
        store.Write(_total, status.ullTotalPhys);
        store.Write(_used, used);
        store.Write(_available, status.ullAvailPhys);
        store.Write(_usage, (float)(used * 100.0 / status.ullTotalPhys));

        var performance = new Psapi.PERFORMANCE_INFORMATION { cb = (uint)sizeof(Psapi.PERFORMANCE_INFORMATION) };
        if (Psapi.GetPerformanceInfo(ref performance, performance.cb))
        {
            ulong page = performance.PageSize;
            store.Write(_commit, performance.CommitTotal * page);
            store.Write(_commitLimit, performance.CommitLimit * page);
            store.Write(_cache, performance.SystemCache * page);
        }
        else
        {
            store.Write(_commit, float.NaN);
            store.Write(_commitLimit, float.NaN);
            store.Write(_cache, float.NaN);
        }

        Ntdll.SYSTEM_MEMORY_LIST_INFORMATION lists;
        if (Ntdll.NtQuerySystemInformation(Ntdll.SystemMemoryListInformation, &lists, (uint)sizeof(Ntdll.SYSTEM_MEMORY_LIST_INFORMATION), out _) >= 0)
        {
            var pageSize = (ulong)Environment.SystemPageSize;
            ulong standbyPages = 0;
            for (var i = 0; i < 8; i++)
            {
                standbyPages += lists.PageCountByPriority[i];
            }

            store.Write(_standby, standbyPages * pageSize);
            store.Write(_free, (lists.ZeroPageCount + lists.FreePageCount) * pageSize);
            store.Write(_modified, lists.ModifiedPageCount * pageSize);
        }
        else
        {
            store.Write(_standby, float.NaN);
            store.Write(_free, float.NaN);
            store.Write(_modified, float.NaN);
        }
    }

    public void Dispose()
    {
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Memory, unit), Name);
}
