using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Asteroids;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class AsteroidRunnerTests
{
    private const string Miner = AsteroidRunner.MinerType;

    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Near = new(new PlanetId("2"), "Near", new Coordinates(7, 130, 6));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly FakePlanetRepository _planets = new();
    private readonly FakeAsteroidStore _store = new();
    private readonly List<string> _log = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public AsteroidRunnerTests()
    {
        _client.OwnPlanets.AddRange([Home, Near]);
        _client.CurrentPlanet = Home.Id;
        _client.Fleets[Home.Id] = Fleet(Home, miners: 2_000_000);
        _client.Fleets[Near.Id] = Fleet(Near, miners: 500);
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.AsteroidMining], TimeSpan.FromMinutes(10), 10, 20_000, null, null, 0, 0, 0, PlayerStatus.None, null);
        _client.AsteroidHints.AddRange([new AsteroidHint(7, 1, 65), new AsteroidHint(7, 11, 75)]);
        _client.Asteroids[new SolarSystemAddress(7, 16)] = Asteroid(7, 16, TimeSpan.FromHours(1));
        _client.Asteroids[new SolarSystemAddress(7, 40)] = Asteroid(7, 40, TimeSpan.FromHours(1));
    }

    private AsteroidRunner Runner() =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), TestQueues.Immediate, _planets, _store, TimeProvider.System, NullLogger<AsteroidRunner>.Instance);

    private Asteroid Asteroid(int galaxy, int system, TimeSpan left) => new(new Coordinates(galaxy, system, 17), _now + left, _now);

    private static FleetSnapshot Fleet(OwnPlanet planet, long miners) =>
        new([new ShipStack(Miner, miners)], planet.Coordinates, new FleetSlots(0, 10), new Dictionary<string, long> { [Miner] = 10_000 });

    /// <summary>Runs one cycle: the run is cancelled when it announces the next check.</summary>
    private Task<AsteroidRunResult> RunCycleAsync(AsteroidSettings settings) =>
        Runner().RunAsync(_client, settings, new SyncProgress(p =>
        {
            _log.Add(p.Message);
            if (p.Message.StartsWith("Next check", StringComparison.Ordinal))
                _cancellation.Cancel();
        }), _cancellation.Token);

    [Fact]
    public async Task The_hinted_systems_are_scanned_until_each_range_has_its_asteroid()
    {
        var found = await Runner().FindAsync(_client);

        // 7:16 is in both ranges (one asteroid each), so 7:40 is found for the second one, and the scan stops there.
        Assert.Equal(2, found);
        Assert.Equal(Enumerable.Range(1, 40).Select(s => new SolarSystemAddress(7, s)), _client.RequestedSystems);
        Assert.Equal([new Coordinates(7, 16, 17), new Coordinates(7, 40, 17)], _store.Asteroids.Select(a => a.Coordinates));
        Assert.Equal(40, (await _planets.GetAsync("tron")).ScannedSystems().Count); // the scanned systems are kept
    }

    [Fact]
    public async Task The_whole_scan_is_one_queue_group()
    {
        var queues = TestQueues.Immediate;
        var runner = new AsteroidRunner(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), queues, _planets, _store, TimeProvider.System,
            NullLogger<AsteroidRunner>.Instance);

        await runner.FindAsync(_client);

        Assert.Equal(40, _client.RequestedSystems.Count);
        Assert.Equal(["Scan for asteroids"], queues.For(_client).Snapshot().Finished.Select(g => g.Description));
    }

    [Fact]
    public async Task Known_asteroids_are_not_searched_again()
    {
        await Runner().FindAsync(_client);
        _client.RequestedSystems.Clear();

        Assert.Equal(0, await Runner().FindAsync(_client));
        Assert.Empty(_client.RequestedSystems);
    }

    [Fact]
    public async Task Without_an_estimate_the_default_miners_go_from_the_nearest_planet_that_has_them()
    {
        var result = await RunCycleAsync(AsteroidSettings.Default with { MinersWithoutEstimate = 1_000_000 });

        Assert.Equal(AsteroidOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, result.Sent);
        Assert.All(_client.SentMissions, m => Assert.Equal(MissionType.AsteroidMining, m.Mission));
        Assert.All(_client.SentMissions, m => Assert.Equal([new ShipStack(Miner, 1_000_000)], m.Ships));
        Assert.All(_client.SentOrigins, o => Assert.Equal(Home.Id, o)); // Near has only 500
        Assert.Equal([new Coordinates(7, 40, 17), new Coordinates(7, 16, 17)], _store.Missions.Select(m => m.Target)); // nearest first
    }

    [Fact]
    public async Task Known_asteroids_get_miners_gone_soonest_first()
    {
        _store.SaveAsteroids("tron", [Asteroid(7, 40, TimeSpan.FromHours(1)), Asteroid(7, 16, TimeSpan.FromMinutes(20))]); // 7:40 is nearer to Near
        _client.Fleets[Home.Id] = Fleet(Home, miners: 1_000_000); // enough for one

        await RunCycleAsync(AsteroidSettings.Default with { MinersWithoutEstimate = 1_000_000 });

        Assert.Equal([new Coordinates(7, 16, 17), new Coordinates(7, 40, 17)], _client.SentMissions.Select(m => m.Target));
    }

    [Fact]
    public async Task Asteroids_found_in_one_scan_group_get_miners_gone_soonest_first()
    {
        _client.Asteroids.Clear();
        _client.Asteroids[new SolarSystemAddress(7, 43)] = Asteroid(7, 43, TimeSpan.FromHours(1)); // scanned first (from Near, 75 down)
        _client.Asteroids[new SolarSystemAddress(7, 41)] = Asteroid(7, 41, TimeSpan.FromMinutes(20));

        await RunCycleAsync(AsteroidSettings.Default with { MinersWithoutEstimate = 1_000_000 });

        Assert.Equal([new Coordinates(7, 41, 17), new Coordinates(7, 43, 17)], _client.SentMissions.Select(m => m.Target));
    }

    [Fact]
    public async Task The_run_scans_nearest_first_and_sends_miners_after_the_scan_group_that_found_an_asteroid()
    {
        var sentBeforeScanning16 = -1;
        _client.Briefing = mission =>
        {
            sentBeforeScanning16 = _client.RequestedSystems.Contains(new SolarSystemAddress(7, 16)) ? sentBeforeScanning16 : _client.SentMissions.Count;
            return new FleetBriefing([MissionType.AsteroidMining], TimeSpan.FromMinutes(10), 10, 20_000, null, null, 0, 0, 0, PlayerStatus.None, null);
        };

        await RunCycleAsync(AsteroidSettings.Default with { MinersWithoutEstimate = 1_000_000 });

        // Near (7:130) is closest to the end of the ranges: 75 down to 40, then on to 16 for the second row.
        Assert.Equal(Enumerable.Range(16, 60).Reverse().Select(s => new SolarSystemAddress(7, s)), _client.RequestedSystems);
        Assert.Equal(0, sentBeforeScanning16); // 7:40 was planned after its group (44 to 40), before the scan went on
        Assert.Equal(2, _client.SentMissions.Count);
    }

    [Fact]
    public async Task The_scan_stops_when_no_miners_are_left()
    {
        _client.Fleets[Home.Id] = Fleet(Home, miners: 1_000_000);
        _client.Fleets[Near.Id] = Fleet(Near, miners: 0);

        await RunCycleAsync(AsteroidSettings.Default with { MinersWithoutEstimate = 1_000_000 });

        // Only Home (galaxy 8) has miners, so every system is as far: the rows are scanned in order.
        Assert.Equal([new Coordinates(7, 16, 17)], _store.Missions.Select(m => m.Target));
        Assert.Equal(new SolarSystemAddress(7, 20), _client.RequestedSystems.Last()); // the end of 7:16's scan group (16 to 20)
        Assert.Contains(_log, l => l.StartsWith("No Asteroid Miners left", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_miners_nothing_is_scanned()
    {
        _client.Fleets[Home.Id] = Fleet(Home, miners: 0);
        _client.Fleets[Near.Id] = Fleet(Near, miners: 0);

        await RunCycleAsync(AsteroidSettings.Default);

        Assert.Empty(_client.RequestedSystems);
        Assert.Empty(_client.SentMissions);
    }

    [Fact]
    public async Task With_a_report_the_miners_cover_the_estimate()
    {
        _client.AsteroidReports.Add(new AsteroidReport(
            "r1", _now.AddMinutes(-1), new Coordinates(7, 99, 17), AsteroidResultKind.Resources, 4_000_000, new ResourceSet(2_000_000, 2_000_000, 0), 0));
        // Mined today: the next one gets 98% unless the miners arrive after midnight UTC.
        var expected = AsteroidEstimator.Day(_now.AddMinutes(10)) == AsteroidEstimator.Day(_now.AddMinutes(-1)) ? 3_920_000 : 4_000_000;

        await RunCycleAsync(AsteroidSettings.Default with { MarginPercent = 0 });

        Assert.Single(_store.Reports);
        Assert.Equal(new ShipStack(Miner, expected / 10_000), _client.SentMissions[0].Ships[0]);
        Assert.Equal(expected, _store.Missions[0].EstimatedResources);
    }

    [Fact]
    public async Task A_planet_with_too_few_miners_is_still_used_when_only_it_arrives_in_time()
    {
        _client.Asteroids.Remove(new SolarSystemAddress(7, 40));
        _client.AsteroidHints.RemoveAt(1);
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.AsteroidMining], _client.CurrentPlanet == Near.Id ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(2),
            10, 20_000, null, null, 0, 0, 0, PlayerStatus.None, null);
        _client.Fleets[Home.Id] = Fleet(Home, miners: 2_000_000);

        await RunCycleAsync(AsteroidSettings.Default);

        Assert.Equal([Near.Id], _client.SentOrigins);
        Assert.Equal([new ShipStack(Miner, 500)], _client.SentMissions[0].Ships);
    }

    [Fact]
    public async Task An_asteroid_out_of_reach_is_skipped()
    {
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.AsteroidMining], TimeSpan.FromHours(2), 10, 20_000, null, null, 0, 0, 0, PlayerStatus.None, null);

        await RunCycleAsync(AsteroidSettings.Default);

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.Contains("too long", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Test_mode_sends_nothing_and_an_asteroid_with_miners_on_the_way_gets_no_more()
    {
        await RunCycleAsync(AsteroidSettings.Default with { TestMode = true });
        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.StartsWith("[Test] Would send", StringComparison.Ordinal));

        await Runner().SendAsync(_client, AsteroidSettings.Default);
        Assert.Equal(2, _client.SentMissions.Count);

        Assert.Equal(0, await Runner().SendAsync(_client, AsteroidSettings.Default));
        Assert.All(Runner().GetOverview("tron").Asteroids, a => Assert.Equal(AsteroidState.Sent, a.State));
    }

    [Fact]
    public async Task An_asteroid_whose_miners_were_recalled_gets_miners_again()
    {
        await Runner().FindAsync(_client);
        await Runner().SendAsync(_client, AsteroidSettings.Default);
        var asteroid = _store.Asteroids.Single(a => a.Coordinates == new Coordinates(7, 16, 17));

        Assert.True(Runner().MarkRecalled("tron", asteroid));

        Assert.Equal(AsteroidState.Open, Runner().GetOverview("tron").Asteroids.Single(t => t.Asteroid == asteroid).State);
        Assert.Equal(1, await Runner().SendAsync(_client, AsteroidSettings.Default));
        Assert.Equal(3, _client.SentMissions.Count);
        Assert.Equal(AsteroidState.Sent, Runner().GetOverview("tron").Asteroids.Single(t => t.Asteroid == asteroid).State);
    }

    [Fact]
    public void An_asteroid_without_miners_cannot_be_marked_recalled()
    {
        var asteroid = Asteroid(7, 16, TimeSpan.FromHours(1));
        _store.Asteroids.Add(asteroid);

        Assert.False(Runner().MarkRecalled("tron", asteroid));
    }

    [Fact]
    public async Task Fleet_slots_limit_the_fleets_sent()
    {
        _client.Fleets[Home.Id] = Fleet(Home, miners: 2_000_000) with { Slots = new FleetSlots(8, 10) };

        await RunCycleAsync(AsteroidSettings.Default with { FleetSlotUsePercent = 90, MinersWithoutEstimate = 1_000 });

        Assert.Single(_client.SentMissions); // 9 of 10 usable, 8 in use
    }

    private sealed class SyncProgress(Action<AsteroidProgress> handler) : IProgress<AsteroidProgress>
    {
        public void Report(AsteroidProgress value) => handler(value);
    }
}
