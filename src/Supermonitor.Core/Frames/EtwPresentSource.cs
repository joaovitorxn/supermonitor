using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Core.Frames;

// One event per present (DxgKrnl, id 184) from any process; the header ProcessID is the presenting app's.
public sealed class EtwPresentSource : IPresentSource
{
    public const string SessionName = "Supermonitor-FPS";

    private static readonly Guid DxgKrnl = new("802ec45a-1e99-4b83-9920-87c98277ba9d");
    private const int PresentEventId = 184;
    private const ulong PresentKeyword = 0x8000000;

    private TraceEventSession? _session;
    private Task? _processing;

    public event Action<int, double>? Presented;

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true)
        {
            throw new UnauthorizedAccessException("FPS capture needs administrator rights.");
        }

        // The default constructor (Create) stops and recreates an orphan session of the same name left by an earlier crash.
        var session = new TraceEventSession(SessionName);
        try
        {
            session.EnableProvider(DxgKrnl, TraceEventLevel.Informational, PresentKeyword);
            session.Source.AllEvents += OnEvent;
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _session = session;
        _processing = Task.Run(() =>
        {
            try
            {
                session.Source.Process();
            }
            catch (Exception ex)
            {
                Log.Warn("ETW processing stopped", ex);
            }
        });
    }

    public void Dispose()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
        {
            return;
        }

        // Stopping the session makes Process() return.
        session.Dispose();
        try
        {
            _processing?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if ((int)data.ID == PresentEventId && data.ProviderGuid == DxgKrnl)
        {
            Presented?.Invoke(data.ProcessID, data.TimeStampRelativeMSec);
        }
    }
}
