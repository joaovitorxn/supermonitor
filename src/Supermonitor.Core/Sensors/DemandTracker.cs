namespace Supermonitor.Core.Sensors;

public enum Demand
{
    HardwareSensors,
    Processes,
    FrameCapture,
}

public sealed class DemandTracker
{
    private readonly int[] _counts = new int[Enum.GetValues<Demand>().Length];

    // Raised on the acquiring/releasing thread when a Demand count goes 0 to 1 or 1 to 0.
    public event Action<Demand, bool>? Changed;

    public IDisposable Acquire(Demand demand)
    {
        if (Interlocked.Increment(ref _counts[(int)demand]) == 1)
        {
            Changed?.Invoke(demand, true);
        }

        return new Lease(this, demand);
    }

    public bool IsActive(Demand demand) => Volatile.Read(ref _counts[(int)demand]) > 0;

    private sealed class Lease(DemandTracker owner, Demand demand) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0
                && Interlocked.Decrement(ref owner._counts[(int)demand]) == 0)
            {
                owner.Changed?.Invoke(demand, false);
            }
        }
    }
}
