using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors;

public sealed class SensorHub : IAsyncDisposable
{
    public const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    private readonly MetricStore _store;
    private readonly TimeProvider _time;
    private readonly Slot[] _slots;
    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _loop;
    private TimeSpan _interval;
    private bool _initialized;

    public SensorHub(MetricStore store, IEnumerable<ISensorProvider> providers, TimeProvider time, TimeSpan interval)
    {
        _store = store;
        _time = time;
        _slots = providers.Select(p => new Slot(p)).ToArray();
        Interval = interval;
    }

    public event Action? SampleReady;

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumInterval);
            _interval = value;
            if (_timer is not null)
            {
                _timer.Period = value;
            }
        }
    }

    public IReadOnlyList<ProviderStatus> GetStatuses() =>
        _slots.Select(s => new ProviderStatus(s.Provider.Name, s.Available, s.Reason)).ToArray();

    public void InitializeProviders()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        foreach (var slot in _slots)
        {
            try
            {
                slot.Provider.Initialize(_store);
            }
            catch (Exception ex)
            {
                slot.Available = false;
                slot.Reason = ex.Message;
                Log.Warn($"Provider '{slot.Provider.Name}' unavailable", ex);
            }
        }
    }

    public void Start()
    {
        InitializeProviders();
        _timer = new PeriodicTimer(_interval, _time);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Tick()
    {
        var now = _time.GetTimestamp();
        foreach (var slot in _slots)
        {
            if (!slot.Available)
            {
                continue;
            }

            var elapsed = slot.HasSampled ? _time.GetElapsedTime(slot.LastSample, now) : TimeSpan.Zero;
            if (slot.HasSampled && elapsed < slot.Provider.MinInterval)
            {
                continue;
            }

            // Métricas que o provedor não reescrever nesta amostra (instância sumiu) viram NaN.
            _store.InvalidateOwner(slot.Provider.Name);
            try
            {
                slot.Provider.Sample(_store, elapsed);
                slot.Failures = 0;
            }
            catch (Exception ex)
            {
                slot.Failures++;
                Log.Warn($"Provider '{slot.Provider.Name}' failed ({slot.Failures}/{MaxConsecutiveFailures})", ex);
                if (slot.Failures >= MaxConsecutiveFailures)
                {
                    slot.Available = false;
                    slot.Reason = ex.Message;
                    _store.InvalidateOwner(slot.Provider.Name);
                }
            }

            slot.LastSample = now;
            slot.HasSampled = true;
        }

        _store.CommitTick();

        try
        {
            SampleReady?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("SampleReady handler failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _timer?.Dispose();
        foreach (var slot in _slots)
        {
            try
            {
                slot.Provider.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"Provider '{slot.Provider.Name}' failed to dispose", ex);
            }
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Tick();
        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class Slot(ISensorProvider provider)
    {
        public ISensorProvider Provider { get; } = provider;

        public bool Available { get; set; } = true;

        public string? Reason { get; set; }

        public int Failures { get; set; }

        public long LastSample { get; set; }

        public bool HasSampled { get; set; }
    }
}
