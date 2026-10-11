using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Supermonitor.Core.Shell;

namespace Supermonitor;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (RestartArguments.TryGetWaitPid(args, out var previousPid))
        {
            WaitForExit(previousPid);
        }

        using var instance = SingleInstance.TryAcquire(SingleInstanceNames.Default);
        if (instance is null)
        {
            SingleInstance.SignalExisting(SingleInstanceNames.Default);
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App(args, instance);
        });
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Processo anterior já terminou.
        }
    }
}
