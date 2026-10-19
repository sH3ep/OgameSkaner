using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class ActionQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly OwnPlanet Home = new(new PlanetId("home"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("colony"), "Colony", new Coordinates(1, 2, 3));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("genesis"));
    private readonly List<string> _log = [];

    public ActionQueueTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.CurrentPlanet = Home.Id;
    }

    private ActionQueue Queue(ActionQueueSettings? settings = null) =>
        new(_client, settings ?? ActionQueueSettings.Immediate, TimeProvider.System, NullLogger.Instance);

    private void Log(string entry)
    {
        lock (_log)
            _log.Add(entry);
    }

    /// <summary>A group that logs its start and end and holds the queue until <paramref name="release"/> completes.</summary>
    private Func<CancellationToken, Task> Group(string name, Task? release = null) => async _ =>
    {
        Log($"{name} start");
        await (release ?? Task.CompletedTask);
        await Task.Yield();
        Log($"{name} end");
    };

    [Fact]
    public async Task Groups_of_different_modules_run_one_at_a_time()
    {
        var queue = Queue();

        async Task ModuleAsync(string owner)
        {
            for (var i = 0; i < 5; i++)
                await queue.RunAsync(owner, $"{owner} {i}", Group($"{owner}{i}"));
        }

        await Task.WhenAll(ModuleAsync("Builder"), ModuleAsync("Raids")).WaitAsync(Timeout);

        Assert.Equal(20, _log.Count);
        for (var i = 0; i < _log.Count; i += 2)
        {
            Assert.EndsWith(" start", _log[i]);
            Assert.Equal(_log[i].Replace(" start", " end"), _log[i + 1]); // nothing in between
        }
    }

    [Fact]
    public async Task Player_groups_go_before_the_waiting_automation_groups()
    {
        var queue = Queue();
        var gate = new TaskCompletionSource();
        var first = queue.RunAsync("Builder", "first", Group("first", gate.Task));
        await WaitUntilAsync(() => _log.Contains("first start"));

        var automation = queue.RunAsync("Raids", "automation", Group("automation"));
        var player = queue.RunAsync("Shipyard", "player", Group("player"), ActionPriority.User);
        gate.SetResult();
        await Task.WhenAll(first, automation, player).WaitAsync(Timeout);

        Assert.Equal(["first start", "first end", "player start", "player end", "automation start", "automation end"], _log);
    }

    [Fact]
    public async Task Modules_take_turns_so_one_with_many_groups_cannot_hold_up_the_others()
    {
        var queue = Queue();
        var gate = new TaskCompletionSource();
        var tasks = new List<Task> { queue.RunAsync("Raids", "r1", Group("r1", gate.Task)) };
        await WaitUntilAsync(() => _log.Contains("r1 start"));

        tasks.Add(queue.RunAsync("Raids", "r2", Group("r2")));
        tasks.Add(queue.RunAsync("Raids", "r3", Group("r3")));
        tasks.Add(queue.RunAsync("Builder", "b1", Group("b1")));
        tasks.Add(queue.RunAsync("Expeditions", "e1", Group("e1")));
        gate.SetResult();
        await Task.WhenAll(tasks).WaitAsync(Timeout);

        Assert.Equal(["r1", "b1", "e1", "r2", "r3"], _log.Where(l => l.EndsWith(" start", StringComparison.Ordinal)).Select(l => l[..^6]));
    }

    [Fact]
    public async Task A_waiting_group_cancelled_never_runs()
    {
        var queue = Queue();
        var gate = new TaskCompletionSource();
        var first = queue.RunAsync("Builder", "first", Group("first", gate.Task));
        await WaitUntilAsync(() => _log.Contains("first start"));

        using var cancel = new CancellationTokenSource();
        var waiting = queue.RunAsync("Raids", "cancelled", Group("cancelled"), cancellationToken: cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Timeout));
        gate.SetResult();
        await first.WaitAsync(Timeout);
        Assert.DoesNotContain("cancelled start", _log);
        Assert.Contains(queue.Snapshot().Finished, g => g.Description == "cancelled" && g.State == ActionGroupState.Cancelled);
    }

    [Fact]
    public async Task A_failing_group_fails_only_its_own_caller()
    {
        var queue = Queue();

        var failing = queue.RunAsync<int>("Builder", "failing", _ => throw new InvalidOperationException("boom"));
        var next = queue.RunAsync("Raids", "next", _ => Task.FromResult(42));

        Assert.Equal("boom", (await Assert.ThrowsAsync<InvalidOperationException>(() => failing.WaitAsync(Timeout))).Message);
        Assert.Equal(42, await next.WaitAsync(Timeout));
        Assert.Contains(queue.Snapshot().Finished, g => g.Description == "failing" && g.State == ActionGroupState.Failed && g.Error == "boom");
    }

    [Fact]
    public async Task Every_finished_group_goes_to_the_history_and_a_failing_history_stops_nothing()
    {
        var history = new FakeActionHistoryStore();
        var queue = new ActionQueue(_client, ActionQueueSettings.Immediate, TimeProvider.System, NullLogger.Instance, history);

        await queue.RunAsync("Builder", "done", Group("done")).WaitAsync(Timeout);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.RunAsync<int>("Raids", "failing", _ => throw new InvalidOperationException("boom")).WaitAsync(Timeout));
        await queue.RunUrgentAsync("Defense", "escape", _ => Task.CompletedTask).WaitAsync(Timeout);
        await WaitUntilAsync(() => history.Count == 3);

        Assert.Equal([("genesis", "done", ActionGroupState.Done), ("genesis", "failing", ActionGroupState.Failed), ("genesis", "escape", ActionGroupState.Done)],
            history.Groups.Select(g => (g.Universe, g.Group.Description, g.Group.State)));
        Assert.All(history.Groups, g => Assert.NotNull(g.Group.FinishedAt));

        history.Throw = true;
        Assert.Equal(7, await queue.RunAsync("Builder", "still runs", _ => Task.FromResult(7)).WaitAsync(Timeout));
    }

    private sealed class FakeActionHistoryStore : IActionHistoryStore
    {
        private readonly List<(string Universe, ActionGroupInfo Group)> _groups = [];

        public bool Throw { get; set; }

        public int Count
        {
            get
            {
                lock (_groups)
                    return _groups.Count;
            }
        }

        public IReadOnlyList<(string Universe, ActionGroupInfo Group)> Groups
        {
            get
            {
                lock (_groups)
                    return _groups.ToList();
            }
        }

        public void Add(string universe, ActionGroupInfo group)
        {
            if (Throw)
                throw new IOException("disk full");
            lock (_groups)
                _groups.Add((universe, group));
        }

        public IReadOnlyList<ActionGroupInfo> Load(string universe, DateTimeOffset since) => [];
    }

    [Fact]
    public async Task A_group_queuing_more_work_runs_it_inline_instead_of_waiting_for_itself()
    {
        var queue = Queue();

        var result = await queue.RunAsync("Raids", "outer", async ct => 1 + await queue.RunAsync("Raids", "inner", _ => Task.FromResult(1), cancellationToken: ct))
            .WaitAsync(Timeout);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task Urgent_work_does_not_wait_for_the_running_group()
    {
        var queue = Queue();
        var gate = new TaskCompletionSource();
        var group = queue.RunAsync("Builder", "slow", Group("slow", gate.Task));
        await WaitUntilAsync(() => _log.Contains("slow start"));

        var escaped = await queue.RunUrgentAsync("Defense", "escape", _ => Task.FromResult(true)).WaitAsync(Timeout);

        Assert.True(escaped);
        Assert.DoesNotContain("slow end", _log);
        gate.SetResult();
        await group.WaitAsync(Timeout);
    }

    [Fact]
    public async Task The_game_returns_to_the_players_planet_once_the_queue_is_idle()
    {
        var queue = Queue(new ActionQueueSettings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(50)));
        PlanetId? home = null;

        await queue.RunAsync("Builder", "build on the colony", async ct =>
        {
            home = queue.HomePlanet;
            await _client.SelectPlanetAsync(Colony.Id, ct);
        }).WaitAsync(Timeout);
        await WaitUntilAsync(() => _client.CurrentPlanet == Home.Id);

        Assert.Equal(Home.Id, home);
        Assert.Equal([Colony.Id, Home.Id], _client.SelectedPlanets);
        await WaitUntilAsync(() => queue.Snapshot().HomePlanet is null); // idle again: the next busy period reads it anew
    }

    [Fact]
    public async Task Groups_queued_close_together_switch_back_only_once()
    {
        var queue = Queue(new ActionQueueSettings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        await queue.RunAsync("Builder", "colony", ct => _client.SelectPlanetAsync(Colony.Id, ct)).WaitAsync(Timeout);
        await queue.RunAsync("Raids", "colony again", ct => _client.SelectPlanetAsync(Colony.Id, ct)).WaitAsync(Timeout);
        await WaitUntilAsync(() => _client.CurrentPlanet == Home.Id);

        Assert.Equal([Colony.Id, Colony.Id, Home.Id], _client.SelectedPlanets);
    }

    [Fact]
    public async Task Groups_of_different_modules_pause_between_each_other()
    {
        var queue = Queue(new ActionQueueSettings(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200), null));
        var starts = new List<DateTimeOffset>();

        await queue.RunAsync("Builder", "first", _ => { starts.Add(DateTimeOffset.UtcNow); return Task.CompletedTask; }).WaitAsync(Timeout);
        await queue.RunAsync("Raids", "second", _ => { starts.Add(DateTimeOffset.UtcNow); return Task.CompletedTask; }).WaitAsync(Timeout);

        Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(150), $"only {(starts[1] - starts[0]).TotalMilliseconds} ms apart");
    }

    [Fact]
    public async Task One_modules_groups_run_back_to_back()
    {
        var queue = Queue(new ActionQueueSettings(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null));
        var starts = new List<DateTimeOffset>();

        await queue.RunAsync("Builder", "first", _ => { starts.Add(DateTimeOffset.UtcNow); return Task.CompletedTask; }).WaitAsync(Timeout);
        await queue.RunAsync("Builder", "second", _ => { starts.Add(DateTimeOffset.UtcNow); return Task.CompletedTask; }).WaitAsync(Timeout);

        Assert.True(starts[1] - starts[0] < TimeSpan.FromMilliseconds(500), $"{(starts[1] - starts[0]).TotalMilliseconds} ms apart");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("The condition never came true.");
            await Task.Delay(10);
        }
    }
}
