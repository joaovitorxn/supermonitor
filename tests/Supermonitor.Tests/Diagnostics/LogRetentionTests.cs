using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Tests.Diagnostics;

public class LogRetentionTests
{
    [Fact]
    public void Deletes_only_log_files_older_than_the_keep_window()
    {
        string[] files =
        [
            "supermonitor-20261003.log",
            "supermonitor-20261004.log",
            "supermonitor-20261010.log",
            "other.txt",
            "supermonitor-garbage.log",
        ];

        var result = LogRetention.FilesToDelete(files, new DateOnly(2026, 10, 10), keepDays: 7).ToArray();

        Assert.Equal(new[] { "supermonitor-20261003.log" }, result);
    }
}
