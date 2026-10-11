using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class FrameCaptureSmokeTests
{
    private static bool IsElevated => TraceEventSession.IsElevated() == true;

    [Fact]
    public void Foreground_probe_returns_an_existing_process()
    {
        var pid = new ForegroundProbe().GetForegroundProcessId();

        Assert.True(pid > 0, "No foreground window; run the test with a desktop session.");
        using var process = Process.GetProcessById(pid);
        Assert.False(process.HasExited);
    }

    [Fact]
    public void Etw_source_starts_and_removes_its_session_on_dispose()
    {
        if (!IsElevated)
        {
            Assert.Skip("ETW needs an administrator process; run the test runner elevated.");
        }

        var received = 0;
        using (var source = new EtwPresentSource())
        {
            source.Presented += (_, _) => Interlocked.Increment(ref received);
            source.Start();

            Assert.Contains(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
            Thread.Sleep(3000);
        }

        Assert.DoesNotContain(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
        // O número depende de haver janelas animando; só registramos.
        Console.WriteLine($"present events received in 3 s: {received}");
    }

    [Fact]
    public void Etw_source_replaces_an_orphan_session_with_the_same_name()
    {
        if (!IsElevated)
        {
            Assert.Skip("ETW needs an administrator process; run the test runner elevated.");
        }

        // Simula um crash anterior: a sessão continua ativa depois que o processo "morre".
        var orphan = new TraceEventSession(EtwPresentSource.SessionName) { StopOnDispose = false };
        orphan.EnableProvider(new Guid("802ec45a-1e99-4b83-9920-87c98277ba9d"), TraceEventLevel.Informational, 0x8000000);
        orphan.Dispose();
        Assert.Contains(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());

        using (var source = new EtwPresentSource())
        {
            source.Start();

            Assert.Single(TraceEventSession.GetActiveSessionNames(), name => name == EtwPresentSource.SessionName);
        }

        Assert.DoesNotContain(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
    }

    [Fact]
    public void Etw_source_without_administrator_rights_fails_with_a_clear_error()
    {
        if (IsElevated)
        {
            Assert.Skip("Only meaningful in a non-administrator process.");
        }

        using var source = new EtwPresentSource();

        var error = Assert.Throws<UnauthorizedAccessException>(source.Start);
        Assert.Contains("administrator", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
