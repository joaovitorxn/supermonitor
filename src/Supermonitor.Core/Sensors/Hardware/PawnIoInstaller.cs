using System.Diagnostics;

namespace Supermonitor.Core.Sensors.Hardware;

public static class PawnIoInstaller
{
    public static async Task<int> InstallAsync(CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(new ProcessStartInfo(
            "winget.exe",
            "install --id namazso.PawnIO --exact --silent --accept-package-agreements --accept-source-agreements")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("winget did not start.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
