using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Tests.Services;

public class WakeUpTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Now_ends_a_running_wait()
    {
        var wakeUp = new WakeUp();
        var wait = wakeUp.DelayAsync(Long, TimeProvider.System, CancellationToken.None);

        wakeUp.Now();

        Assert.True(await wait.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Now_between_waits_ends_only_the_next_one()
    {
        var wakeUp = new WakeUp();

        wakeUp.Now(); // while a cycle runs

        Assert.True(await wakeUp.DelayAsync(Long, TimeProvider.System, CancellationToken.None).WaitAsync(Timeout));
        Assert.False(await wakeUp.DelayAsync(TimeSpan.FromMilliseconds(20), TimeProvider.System, CancellationToken.None).WaitAsync(Timeout));
    }

    [Fact]
    public async Task Reset_forgets_an_earlier_now()
    {
        var wakeUp = new WakeUp();
        wakeUp.Now();

        wakeUp.Reset();

        Assert.False(await wakeUp.DelayAsync(TimeSpan.FromMilliseconds(20), TimeProvider.System, CancellationToken.None).WaitAsync(Timeout));
    }

    [Fact]
    public async Task Cancelling_ends_the_wait_with_an_exception()
    {
        var wakeUp = new WakeUp();
        using var cancel = new CancellationTokenSource();
        var wait = wakeUp.DelayAsync(Long, TimeProvider.System, cancel.Token);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(Timeout));
    }
}
