using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Tests.Diagnostics;

public sealed class LogTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smlog");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Initialize_skips_old_logs_it_cannot_delete()
    {
        var old = Path.Combine(_dir.FullName, "supermonitor-20200101.log");
        File.WriteAllText(old, "x");
        File.SetAttributes(old, FileAttributes.ReadOnly);

        var exception = Record.Exception(() => Log.Initialize(_dir.FullName));
        File.SetAttributes(old, FileAttributes.Normal);

        Assert.Null(exception);
    }
}
