using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class RestartArgumentsTests
{
    [Fact]
    public void Build_and_parse_round_trip()
    {
        var args = RestartArguments.Build(4321);

        Assert.True(RestartArguments.TryGetWaitPid(args, out var pid));
        Assert.Equal(4321, pid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--minimized")]
    [InlineData("--wait-for-pid")]
    [InlineData("--wait-for-pid abc")]
    [InlineData("--wait-for-pid -5")]
    public void Missing_or_invalid_pid_is_rejected(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.False(RestartArguments.TryGetWaitPid(args, out _));
    }
}
