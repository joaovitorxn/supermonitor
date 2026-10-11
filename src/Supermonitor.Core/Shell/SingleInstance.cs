namespace Supermonitor.Core.Shell;

public sealed record SingleInstanceNames(string Mutex, string ActivateEvent)
{
    public static SingleInstanceNames Default { get; } =
        new(@"Local\Supermonitor.SingleInstance", @"Local\Supermonitor.Activate");
}

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, SingleInstanceNames names)
    {
        _mutex = mutex;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, names.ActivateEvent);
    }

    public static SingleInstance? TryAcquire(SingleInstanceNames names)
    {
        var mutex = new Mutex(initiallyOwned: true, names.Mutex, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, names);
    }

    public static void SignalExisting(SingleInstanceNames names)
    {
        if (EventWaitHandle.TryOpenExisting(names.ActivateEvent, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public void ListenForActivation(Action onActivate)
    {
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
