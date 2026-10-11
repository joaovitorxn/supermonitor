using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class SingleInstanceTests
{
    private static SingleInstanceNames UniqueNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SingleInstanceNames($@"Local\SmTest.{id}.Mutex", $@"Local\SmTest.{id}.Activate");
    }

    [Fact]
    public void Second_acquire_fails_while_first_is_held()
    {
        var names = UniqueNames();
        using var first = SingleInstance.TryAcquire(names);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(names));
    }

    [Fact]
    public void Acquire_succeeds_again_after_dispose()
    {
        var names = UniqueNames();
        SingleInstance.TryAcquire(names)!.Dispose();

        using var again = SingleInstance.TryAcquire(names);
        Assert.NotNull(again);
    }

    [Fact]
    public void Signal_from_second_instance_invokes_activation_callback()
    {
        var names = UniqueNames();
        using var first = SingleInstance.TryAcquire(names)!;
        using var activated = new ManualResetEventSlim();
        first.ListenForActivation(activated.Set);

        SingleInstance.SignalExisting(names);

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }
}
