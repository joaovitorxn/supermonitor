namespace Supermonitor.Core.Sensors.Gpu;

public sealed class ProcessGpuUsage
{
    private readonly Lock _gate = new();
    private Dictionary<int, float> _current = [];
    private Dictionary<int, float> _spare = [];

    public void Publish(GpuEngineAggregator aggregator)
    {
        _spare.Clear();
        aggregator.CopyProcessUsage(_spare);
        lock (_gate)
        {
            (_current, _spare) = (_spare, _current);
        }
    }

    public float Get(int processId)
    {
        lock (_gate)
        {
            return _current.GetValueOrDefault(processId);
        }
    }
}
