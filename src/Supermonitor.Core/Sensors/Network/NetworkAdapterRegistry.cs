namespace Supermonitor.Core.Sensors.Network;

// Stable index per LUID: new adapters get the next index; an adapter that disappears and returns keeps its own.
public sealed class NetworkAdapterRegistry
{
    private readonly Dictionary<ulong, int> _indexes = [];

    public IReadOnlyList<(ulong Luid, int Index, string Alias)> AddNew(IEnumerable<(ulong Luid, string Alias)> current)
    {
        var added = new List<(ulong Luid, int Index, string Alias)>();
        foreach (var (luid, alias) in current)
        {
            if (_indexes.ContainsKey(luid))
            {
                continue;
            }

            var index = _indexes.Count;
            _indexes[luid] = index;
            added.Add((luid, index, alias));
        }

        return added;
    }

    public int IndexOf(ulong luid) => _indexes.TryGetValue(luid, out var index) ? index : -1;
}
