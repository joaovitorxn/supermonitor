using System.ComponentModel;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Network;

public sealed unsafe class NetworkProvider : ISensorProvider
{
    private Adapter[] _adapters = [];
    private MetricHandle _totalDown;
    private MetricHandle _totalUp;

    public string Name => "Network";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        Iphlpapi.MIB_IF_TABLE2* table;
        var error = Iphlpapi.GetIfTable2(&table);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        var adapters = new List<Adapter>();
        try
        {
            var rows = &table->Table;
            for (var i = 0; i < table->NumEntries; i++)
            {
                var row = &rows[i];
                if (!NetworkAdapterFilter.IsMonitored(row->Type, row->OperStatus, row->InterfaceAndOperStatusFlags))
                {
                    continue;
                }

                var alias = new string(row->Alias);
                var n = adapters.Count;
                adapters.Add(new Adapter
                {
                    Luid = row->InterfaceLuid,
                    Down = Register(store, MetricIds.NetworkDown(n), $"{alias} download"),
                    Up = Register(store, MetricIds.NetworkUp(n), $"{alias} upload"),
                });
            }
        }
        finally
        {
            Iphlpapi.FreeMibTable(table);
        }

        _adapters = adapters.ToArray();
        _totalDown = Register(store, MetricIds.NetworkTotalDown, "Download");
        _totalUp = Register(store, MetricIds.NetworkTotalUp, "Upload");
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
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

    private MetricHandle Register(MetricStore store, string id, string name) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Network, MetricUnit.BytesPerSecond), Name);

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
