using System.Diagnostics;

namespace Supermonitor.Core.Sensors.Hardware;

public static class PawnIoInstaller
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    // Returns the winget exit code, or -1 on timeout (the process is killed).
    public static async Task<int> InstallAsync(CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(new ProcessStartInfo(
            "winget.exe",
            "install --id namazso.PawnIO --exact --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
        }) ?? throw new InvalidOperationException("winget did not start.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }
    }
}
