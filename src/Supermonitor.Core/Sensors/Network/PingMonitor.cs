using System.Net.NetworkInformation;

namespace Supermonitor.Core.Sensors.Network;

public sealed class PingMonitor(Func<string> host, TimeSpan period) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly PingStatistics _statistics = new(10);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _payload = new byte[32];
    private Task? _loop;

    public PingSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _statistics.GetSnapshot();
            }
        }
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        using var timer = new PeriodicTimer(period);
        try
        {
            do
            {
                try
                {
                    var reply = await ping.SendPingAsync(host(), Timeout, _payload, null, cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (reply.Status == IPStatus.Success)
                        {
                            _statistics.AddReply(reply.RoundtripTime);
                        }
                        else
                        {
                            _statistics.AddLoss();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_gate)
                    {
                        _statistics.AddLoss();
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
