using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class RaidRunnerTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(5, 288, 8));

    private static readonly PlayerPlanet Rich = Inactive("Rich", new Coordinates(5, 288, 3), rank: 31);
    private static readonly PlayerPlanet Second = Inactive("Second", new Coordinates(8, 250, 10), rank: 909);
    private static readonly PlayerPlanet Third = Inactive("Third", new Coordinates(1, 1, 1), rank: 1500);

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly FakePlanetRepository _planets = new();
    private readonly FakeRaidStore _store = new();
    private readonly List<string> _log = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public RaidRunnerTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.CurrentPlanet = Home.Id;
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 0);
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 0, slotsUsed: 0);
        _planets.GetAsync("tron").Result.ReplaceSystem(Rich.Coordinates.SolarSystem, [Rich]);
        _planets.GetAsync("tron").Result.ReplaceSystem(Second.Coordinates.SolarSystem, [Second, Inactive("Active", new Coordinates(8, 250, 11), 5, PlayerStatus.None)]);
        _planets.GetAsync("tron").Result.ReplaceSystem(Third.Coordinates.SolarSystem, [Third]);
    }

    private RaidRunner Runner() =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), _planets, _store, TimeProvider.System, NullLogger<RaidRunner>.Instance);

    /// <summary>Runs one cycle: the run is cancelled when it announces the next check.</summary>
    private Task<RaidRunResult> RunCycleAsync(RaidSettings settings, CancellationTokenSource? cancellation = null)
    {
        cancellation ??= _cancellation;
        return Runner().RunAsync(_client, settings, new SyncProgress(p =>
        {
            _log.Add(p.Message);
            if (p.Message.StartsWith("Next check", StringComparison.Ordinal))
                cancellation.Cancel();
        }), cancellation.Token);
    }

    [Fact]
    public async Task Without_reports_the_best_ranked_inactives_are_spied_on_from_the_current_planet()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 8); // spies get all but one: 2 of 11 left

        var result = await RunCycleAsync(RaidSettings.Default with { NearestTargetsFirst = false });

        Assert.Equal(RaidOutcome.Cancelled, result.Outcome);
        Assert.Equal([Rich.Coordinates, Second.Coordinates], _client.SentMissions.Select(m => m.Target));
        Assert.All(_client.SentMissions, m => Assert.Equal(MissionType.Spy, m.Mission));
        Assert.All(_client.SentMissions, m => Assert.Equal([new ShipStack(RaidRunner.ProbeType, 5)], m.Ships));
        Assert.All(_client.SentOrigins, o => Assert.Equal(Home.Id, o));
        Assert.Equal(2, _store.Raids.Count(r => r.Mission == MissionType.Spy));
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last()); // switched back
    }

    [Fact]
    public async Task Nearest_targets_first_spies_on_the_closest_inactives()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 8); // spies get all but one: 2 of 11 left

        await RunCycleAsync(RaidSettings.Default);

        // From Home 8:260:3: Second (same galaxy), then Third (galaxy 1, two steps with the wrap), not Rich (galaxy 5, three steps)
        Assert.Equal([Second.Coordinates, Third.Coordinates], _client.SentMissions.Select(m => m.Target));
    }

    [Fact]
    public async Task A_fresh_clean_report_is_read_and_attacked_with_enough_cargo()
    {
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1 });

        var attack = _client.SentMissions.First();
        Assert.Equal(MissionType.Attack, attack.Mission);
        Assert.Equal(Rich.Coordinates, attack.Target);
        // 50% of 5.5M = 2.75M, +10% = 3.025M, / 25,000 per Heavy Cargo = 121
        Assert.Equal([new ShipStack("HEAVY_CARGO", 100)], attack.Ships); // only 100 on the planet
        var raid = _store.Raids.Single(r => r.Mission == MissionType.Attack);
        Assert.Equal(2_500_000, raid.ExpectedLoot); // 100 × 25,000
        Assert.Equal(raid.SentAt + TimeSpan.FromMinutes(10), raid.ReturnsAt); // 2 × the briefing's 5 minutes
        Assert.Equal(["r-Rich"], _client.DetailReads);
        Assert.DoesNotContain(_client.SentMissions, m => m.Mission == MissionType.Spy && m.Target == Rich.Coordinates);
        Assert.Contains(_client.SentMissions, m => m.Mission == MissionType.Spy && m.Target == Second.Coordinates);
    }

    [Fact]
    public async Task Defended_targets_are_not_attacked()
    {
        AddReport(Rich, resources: new ResourceSet(4_000_000, 0, 0), defense: 2_000, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, MaxDefensePoints = 1_000 });

        Assert.DoesNotContain(_client.SentMissions, m => m.Mission == MissionType.Attack);
    }

    [Fact]
    public async Task Nothing_is_sent_when_all_slots_but_one_are_taken()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 10);

        await RunCycleAsync(RaidSettings.Default);

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, m => m.Contains("fleet slots 10/11, 0 usable for attacks, 0 for spies", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attacks_keep_the_slot_share_while_spies_use_all_slots_but_one()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 9); // 90% of 11 = 9: no attack slot left
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1 });

        Assert.Equal([MissionType.Spy], _client.SentMissions.Select(m => m.Mission)); // the 10th slot; the 11th stays free
    }

    [Fact]
    public async Task With_nearest_planet_the_attack_leaves_from_the_closest_planet_with_cargo()
    {
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 500, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, UseNearestPlanet = true });

        var index = _client.SentMissions.FindIndex(m => m.Mission == MissionType.Attack);
        Assert.Equal(Colony.Id, _client.SentOrigins[index]);
        Assert.Equal([new ShipStack("HEAVY_CARGO", 121)], _client.SentMissions[index].Ships);
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last());
    }

    [Fact]
    public async Task A_refused_send_stops_the_batch()
    {
        _client.SendAnswer = _ => new FleetSendOutcome(false, "No probes.");

        await RunCycleAsync(RaidSettings.Default);

        Assert.Empty(_client.SentMissions);
        Assert.Single(_log, m => m.Contains("was refused: No probes.", StringComparison.Ordinal));
        Assert.Empty(_store.Raids);
    }

    [Fact]
    public async Task Manual_attack_sends_the_selected_targets()
    {
        AddReport(Second, resources: new ResourceSet(1_000_000, 0, 0), defense: 0, fleet: 0);
        var runner = Runner();
        await runner.ReadReportsAsync(_client, RaidSettings.Default);
        var targets = await runner.GetTargetsAsync("tron", RaidSettings.Default with { MinLoot = 1 });

        var sent = await runner.AttackAsync(_client, targets.Where(t => t.Planet == Second).ToList(), RaidSettings.Default with { MinLoot = 1 });

        Assert.Equal(1, sent);
        var attack = Assert.Single(_client.SentMissions);
        Assert.Equal(Second.Coordinates, attack.Target);
        Assert.Equal([new ShipStack("HEAVY_CARGO", 22)], attack.Ships); // 500k × 1.1 / 25k
    }

    private void AddReport(PlayerPlanet planet, ResourceSet resources, long defense, long fleet)
    {
        var summary = new EspionageReport($"r-{planet.PlayerName}", planet.Coordinates, PlanetType.Planet, _now.AddSeconds(-30),
            planet.PlayerName, null, PlayerStatus.Inactive, resources, 0);
        _client.EspionageReports.Add(summary);
        _client.ReportDetails[summary.Id] = summary with { Ships = new UnitSection(fleet, []), Defenses = new UnitSection(defense, []) };
    }

    private static FleetSnapshot Fleet(OwnPlanet planet, long heavyCargo, int slotsUsed) =>
        new([new ShipStack("HEAVY_CARGO", heavyCargo), new ShipStack(RaidRunner.ProbeType, 100)], planet.Coordinates,
            new FleetSlots(slotsUsed, 11), new Dictionary<string, long> { ["HEAVY_CARGO"] = 25_000 });

    [Fact]
    public async Task The_whole_defense_ranking_is_read_and_shown_with_the_targets()
    {
        _client.RankingPageSize = 2;
        _client.DefenseRanking.AddRange(
        [
            new RankingEntry(1, "big", "Big", 90_000, PlayerStatus.None),
            new RankingEntry(2, "rich", "Rich", 0, PlayerStatus.Inactive),
            new RankingEntry(3, "x", "X", 0, PlayerStatus.None),
            new RankingEntry(4, "y", "Y", 0, PlayerStatus.None),
            new RankingEntry(5, "second", "Second", 12, PlayerStatus.Inactive)
        ]);
        var planets = await _planets.GetAsync("tron");
        planets.ReplaceSystem(Rich.Coordinates.SolarSystem, [Rich with { PlayerId = "rich" }]);
        planets.ReplaceSystem(Second.Coordinates.SolarSystem, [Second with { PlayerId = "second" }]);

        var read = await Runner().ReadDefenseRankingAsync(_client);
        var targets = await Runner().GetTargetsAsync("tron", RaidSettings.Default);

        Assert.Equal(5, read);
        Assert.Equal([1, 2, 3], _client.RankingPageReads);
        Assert.Equal(0, targets.Single(t => t.Planet.PlayerName == "Rich").RankingDefensePoints);
        Assert.Equal(12, targets.Single(t => t.Planet.PlayerName == "Second").RankingDefensePoints);
        Assert.Null(targets.Single(t => t.Planet.PlayerName == "Third").RankingDefensePoints); // no id from the galaxy
        Assert.NotNull(Runner().DefenseRankingReadAt("tron"));
    }

    [Fact]
    public async Task A_run_reads_the_defense_ranking_only_when_it_is_old()
    {
        await RunCycleAsync(RaidSettings.Default, new CancellationTokenSource());
        Assert.Equal([1], _client.RankingPageReads); // never read: read now

        _client.RankingPageReads.Clear();
        await RunCycleAsync(RaidSettings.Default, new CancellationTokenSource());
        Assert.Contains(_log, l => l.StartsWith("Next check", StringComparison.Ordinal));
        Assert.Empty(_client.RankingPageReads); // fresh: not again

        _store.DefenseRanking = _store.DefenseRanking with { ReadAt = DateTimeOffset.UtcNow - RaidRunner.RankingMaxAge - TimeSpan.FromMinutes(1) };
        await RunCycleAsync(RaidSettings.Default, new CancellationTokenSource());
        Assert.Equal([1], _client.RankingPageReads); // old: read again
    }

    private static PlayerPlanet Inactive(string name, Coordinates coordinates, int rank, PlayerStatus status = PlayerStatus.Inactive) =>
        new(name, coordinates, false, DateTimeOffset.UtcNow, status, null, rank);

    private sealed class SyncProgress(Action<RaidProgress> handler) : IProgress<RaidProgress>
    {
        public void Report(RaidProgress value) => handler(value);
    }
}
