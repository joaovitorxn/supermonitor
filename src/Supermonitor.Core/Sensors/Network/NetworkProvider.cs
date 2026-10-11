using System.ComponentModel;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Network;

public sealed unsafe class NetworkProvider : ISensorProvider
{
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(5);

    private readonly NetworkAdapterRegistry _registry = new();
    private readonly List<Adapter> _adapters = [];
    private MetricHandle _totalDown;
    private MetricHandle _totalUp;
    private TimeSpan _sinceScan;

    public string Name => "Network";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _totalDown = Register(store, MetricIds.NetworkTotalDown, "Download", "Network");
        _totalUp = Register(store, MetricIds.NetworkTotalUp, "Upload", "Network");
        var error = Scan(store);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        // Adapters that come up after startup (Wi-Fi, VPN, USB) join on the next scan.
        _sinceScan += elapsed;
        if (_sinceScan >= RescanInterval)
        {
            _sinceScan = TimeSpan.Zero;
            Scan(store);
        }

        double down = 0;
        double up = 0;
        var any = false;

        foreach (var adapter in _adapters)
        {
            var row = new Iphlpapi.MIB_IF_ROW2 { InterfaceLuid = adapter.Luid };
            if (Iphlpapi.GetIfEntry2(&row) != 0)
            {
                store.Write(adapter.Down, float.NaN);
                store.Write(adapter.Up, float.NaN);
                adapter.HasLast = false;
                continue;
            }

            if (adapter.HasLast)
            {
                var d = RateCalculator.PerSecond(adapter.LastIn, row.InOctets, elapsed);
                var u = RateCalculator.PerSecond(adapter.LastOut, row.OutOctets, elapsed);
                store.Write(adapter.Down, (float)d);
                store.Write(adapter.Up, (float)u);
                if (double.IsFinite(d) && double.IsFinite(u))
                {
                    down += d;
                    up += u;
                    any = true;
                }
            }

            adapter.LastIn = row.InOctets;
            adapter.LastOut = row.OutOctets;
            adapter.HasLast = true;
        }

        store.Write(_totalDown, any ? (float)down : float.NaN);
        store.Write(_totalUp, any ? (float)up : float.NaN);
    }

    public void Dispose()
    {
    }

    private uint Scan(MetricStore store)
    {
        Iphlpapi.MIB_IF_TABLE2* table;
        var error = Iphlpapi.GetIfTable2(&table);
        if (error != 0)
        {
            return error;
        }

        var monitored = new List<(ulong Luid, string Alias)>();
        try
        {
            var rows = &table->Table;
            for (var i = 0; i < table->NumEntries; i++)
            {
                var row = &rows[i];
                if (NetworkAdapterFilter.IsMonitored(row->Type, row->OperStatus, row->InterfaceAndOperStatusFlags))
                {
                    monitored.Add((row->InterfaceLuid, new string(row->Alias)));
                }
            }
        }
        finally
        {
            Iphlpapi.FreeMibTable(table);
        }

        foreach (var (luid, index, alias) in _registry.AddNew(monitored))
        {
            _adapters.Add(new Adapter
            {
                Luid = luid,
                Down = Register(store, MetricIds.NetworkDown(index), $"{alias} download", alias),
                Up = Register(store, MetricIds.NetworkUp(index), $"{alias} upload", alias),
            });
        }

        return 0;
    }

    private MetricHandle Register(MetricStore store, string id, string name, string device) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Network, MetricUnit.BytesPerSecond, device), Name);

    private sealed class Adapter
    {
        public ulong Luid { get; init; }

        public MetricHandle Down { get; init; }

        public MetricHandle Up { get; init; }

        public ulong LastIn { get; set; }

        public ulong LastOut { get; set; }

        public bool HasLast { get; set; }
    }
}
