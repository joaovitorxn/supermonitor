using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Supermonitor.Core.Shell;

namespace Supermonitor;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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
}
