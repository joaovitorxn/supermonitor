using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Core.Frames;

public interface IPresentSource : IDisposable
{
    // processId of the presenting app and timestamp in ms (monotonic).
    event Action<int, double>? Presented;

    // Throws if capture cannot start.
    void Start();
}

public interface IForegroundProbe
{
    // 0 when there is no foreground window.
    int GetForegroundProcessId();
}

public enum FrameCaptureState
{
    Stopped,
    Running,
    Failed,
}

// Runs the present source only while a Demand.FrameCapture lease exists, follows the foreground process
// and feeds the FrameTimeAggregator. Start failures are reported and retried every 30 s.
public sealed class FrameCaptureService : IDisposable
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DemandTracker _demand;
    private readonly Func<IPresentSource> _sourceFactory;
    private readonly IForegroundProbe _probe;
    private readonly TimeProvider _time;
    private readonly int _ownProcessId;
    private readonly bool _runLoop;
    private readonly FrameTimeAggregator _aggregator = new();
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private IPresentSource? _source;
    private int _state;
    private int _target;
    private long _lastPresent;
    private long _failedAt;
    private int _loopActive;
    private Task? _loop;

    public FrameCaptureService(DemandTracker demand, Func<IPresentSource> sourceFactory, IForegroundProbe probe, TimeProvider time, int ownProcessId, bool runLoop = true)
    {
        _demand = demand;
        _sourceFactory = sourceFactory;
        _probe = probe;
        _time = time;
        _ownProcessId = ownProcessId;
        _runLoop = runLoop;
        _demand.Changed += OnDemandChanged;
    }

    public FrameCaptureState State => (FrameCaptureState)Volatile.Read(ref _state);

    public string? FailureReason { get; private set; }

    public int TargetProcessId => Volatile.Read(ref _target);

    public long FrameVersion => _aggregator.Version;

    // No presents for over IdleAfter (game stopped, alt-tab) returns to "no data".
    public FrameStats Current
    {
        get
        {
            var last = Volatile.Read(ref _lastPresent);
            return last == 0 || _time.GetElapsedTime(last) > IdleAfter ? FrameStats.None : _aggregator.GetStats();
        }
    }

    public int CopyFrameTimes(Span<float> destination) => _aggregator.CopyFrameTimes(destination);

    public void Dispose()
    {
        _demand.Changed -= OnDemandChanged;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        lock (_gate)
        {
            StopSource();
        }

        _cts.Dispose();
    }

    internal void Poll()
    {
        lock (_gate)
        {
            if (!_demand.IsActive(Demand.FrameCapture))
            {
                StopSource();
                SetTarget(0);
                FailureReason = null;
                Volatile.Write(ref _state, (int)FrameCaptureState.Stopped);
                return;
            }

            if (_source is null)
            {
                if (State == FrameCaptureState.Failed && _time.GetElapsedTime(_failedAt) < RetryInterval)
                {
                    return;
                }

                TryStart();
                if (_source is null)
                {
                    return;
                }
            }

            var pid = _probe.GetForegroundProcessId();
            SetTarget(pid == _ownProcessId ? 0 : pid);
        }
    }

    private void TryStart()
    {
        IPresentSource? source = null;
        try
        {
            source = _sourceFactory();
            source.Presented += OnPresented;
            source.Start();
            _source = source;
            FailureReason = null;
            Volatile.Write(ref _state, (int)FrameCaptureState.Running);
            Log.Info("Frame capture started");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            source?.Dispose();
            _failedAt = _time.GetTimestamp();
            FailureReason = ex.Message;
            Volatile.Write(ref _state, (int)FrameCaptureState.Failed);
            Log.Warn("Frame capture could not start; will retry", ex);
        }
    }

    private void StopSource()
    {
        if (_source is { } source)
        {
            _source = null;
            source.Dispose();
            Log.Info("Frame capture stopped");
        }

        _aggregator.Reset();
        Volatile.Write(ref _lastPresent, 0);
    }

    private void SetTarget(int pid)
    {
        if (pid == Volatile.Read(ref _target))
        {
            return;
        }

        _aggregator.Reset();
        Volatile.Write(ref _lastPresent, 0);
        Volatile.Write(ref _target, pid);
    }

    // ETW thread.
    private void OnPresented(int pid, double timestampMs)
    {
        if (pid == 0 || pid != Volatile.Read(ref _target))
        {
            return;
        }

        _aggregator.AddPresent(timestampMs);
        Volatile.Write(ref _lastPresent, _time.GetTimestamp());
    }

    private void OnDemandChanged(Demand demand, bool active)
    {
        if (demand == Demand.FrameCapture && active && _runLoop)
        {
            EnsureLoop();
        }
    }

    private void EnsureLoop()
    {
        if (Interlocked.CompareExchange(ref _loopActive, 1, 0) == 0)
        {
            _loop = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        var completed = false;
        try
        {
            using var timer = new PeriodicTimer(PollInterval, _time);
            do
            {
                Poll();
            }
            while (_demand.IsActive(Demand.FrameCapture) && await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false));

            Poll();
            completed = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Frame capture loop failed", ex);
        }
        finally
        {
            Volatile.Write(ref _loopActive, 0);
            // Demand may have returned between the last check and loop end. After an exception, do not restart, to avoid spinning.
            if (completed && !_cts.IsCancellationRequested && _demand.IsActive(Demand.FrameCapture))
            {
                EnsureLoop();
            }
        }
    }
}
