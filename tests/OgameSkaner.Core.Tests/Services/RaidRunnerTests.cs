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

    private static readonly IReadOnlyList<string> Both = [Home.Id.Value, Colony.Id.Value];

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

    private RaidRunner Runner(TimeProvider? clock = null) =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), TestQueues.Immediate, _planets, _store, clock ?? TimeProvider.System, NullLogger<RaidRunner>.Instance, TestLicense.Valid);

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
    }

    [Fact]
    public async Task A_top_target_by_its_history_is_spied_on_before_better_ranked_ones_and_a_banned_one_not_at_all()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 8); // 2 spies
        // Third (worst ranked) refilled 2M a day; its newest report is too old to attack on.
        _store.AddReports("tron", [
            new EspionageReport("t1", Third.Coordinates, PlanetType.Planet, _now.AddHours(-48), "Third", null, PlayerStatus.Inactive, new ResourceSet(1_000_000, 0, 0), 0),
            new EspionageReport("t2", Third.Coordinates, PlanetType.Planet, _now.AddHours(-24), "Third", null, PlayerStatus.Inactive, new ResourceSet(3_000_000, 0, 0), 0)]);
        _store.Bans.Add(new TargetBan(Rich.Coordinates, _now.AddDays(-1), _now.AddDays(2), 1, "test"));

        await RunCycleAsync(RaidSettings.Default with { NearestTargetsFirst = false });

        Assert.Equal([Third.Coordinates, Second.Coordinates], _client.SentMissions.Select(m => m.Target));
        var targets = await Runner().GetTargetsAsync("tron", RaidSettings.Default);
        Assert.Equal(RaidTargetState.LowResource, targets.Single(t => t.Planet.Coordinates == Rich.Coordinates).State);
    }

    [Fact]
    public async Task High_value_targets_are_spied_on_right_after_the_top_ones()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 7); // 3 spies
        EspionageReport Old(string id, PlayerPlanet planet, int hours, long metal) =>
            new(id, planet.Coordinates, PlanetType.Planet, _now.AddHours(hours), planet.PlayerName, null, PlayerStatus.Inactive, new ResourceSet(metal, 0, 0), 0);
        // Third refills fast (top); Second once held 20M (high value, its score is lower); Rich is best ranked.
        _store.AddReports("tron", [
            Old("t1", Third, -48, 1_000_000), Old("t2", Third, -24, 30_000_000),
            Old("s1", Second, -72, 20_000_000), Old("s2", Second, -24, 2_000_000)]);

        await RunCycleAsync(RaidSettings.Default with { NearestTargetsFirst = false, TopTargetsPercent = 50 });

        Assert.Equal([Third.Coordinates, Second.Coordinates, Rich.Coordinates], _client.SentMissions.Select(m => m.Target));
    }

    [Fact]
    public async Task Reading_reports_marks_a_planet_whose_reports_are_always_poor()
    {
        EspionageReport Poor(string id, int hours) =>
            new(id, Second.Coordinates, PlanetType.Planet, _now.AddHours(hours), "Second", null, PlayerStatus.Inactive, new ResourceSet(100_000, 0, 0), 0);
        _store.AddReports("tron", [Poor("p1", -30), Poor("p2", -20)]);
        _client.EspionageReports.Add(Poor("p3", -1));

        await Runner().ReadReportsAsync(_client, RaidSettings.Default, new SyncProgress(p => _log.Add(p.Message)));

        var ban = Assert.Single(_store.Bans);
        Assert.Equal((Second.Coordinates, 1), (ban.Target, ban.Nth));
        Assert.Contains(_log, l => l.Contains("marked low resource", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Spy_briefings_fly_from_the_home_planet_even_after_reading_the_other_fleets()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 0, slotsUsed: 8);
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 0, slotsUsed: 8);

        await RunCycleAsync(RaidSettings.Default with { NearestTargetsFirst = false, UseNearestPlanet = true, OriginPlanetIds = [Home.Id.Value] });

        Assert.Equal(Home.Id, _client.FleetReads.Last()); // only the ticked planet is read
        Assert.NotEmpty(_client.BriefingOrigins);
        Assert.All(_client.BriefingOrigins, o => Assert.Equal(Home.Id, o));
    }

    [Fact]
    public async Task With_nearest_planet_spies_go_best_ranked_first_from_the_nearest_ticked_planet()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 0, slotsUsed: 8); // 2 spies
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 0, slotsUsed: 8);

        await RunCycleAsync(RaidSettings.Default with { UseNearestPlanet = true, OriginPlanetIds = Both }); // nearest targets first is ignored

        // Rank 31 and 909, although Third is nearer to Home than Rich; Rich (galaxy 5) is next to the colony.
        Assert.Equal([Rich.Coordinates, Second.Coordinates], _client.SentMissions.Select(m => m.Target));
        Assert.Equal([Colony.Id, Home.Id], _client.SentOrigins);
        Assert.Equal(_client.SentOrigins, _client.BriefingOrigins);
    }

    [Fact]
    public async Task With_nearest_planet_an_unticked_planet_sends_nothing()
    {
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 500, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1, UseNearestPlanet = true, OriginPlanetIds = [Home.Id.Value] });

        Assert.NotEmpty(_client.SentOrigins);
        Assert.All(_client.SentOrigins, o => Assert.Equal(Home.Id, o));
        Assert.DoesNotContain(Colony.Id, _client.FleetReads);
    }

    [Fact]
    public async Task With_nearest_planet_and_no_planet_ticked_nothing_is_sent()
    {
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, UseNearestPlanet = true });

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.StartsWith("No planet or moon is ticked",StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_nearest_planet_the_most_loot_is_attacked_first_not_the_nearest()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 1_000, slotsUsed: 0);
        AddReport(Second, resources: new ResourceSet(2_000_000, 0, 0), defense: 0, fleet: 0); // next to Home
        AddReport(Rich, resources: new ResourceSet(8_000_000, 0, 0), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1, UseNearestPlanet = true, OriginPlanetIds = Both });

        Assert.Equal([Rich.Coordinates, Second.Coordinates],
            _client.SentMissions.Where(m => m.Mission == MissionType.Attack).Select(m => m.Target));
    }

    [Fact]
    public async Task Nearest_targets_first_still_spies_on_the_best_ranked_inactives_first()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 8); // spies get all but one: 2 of 11 left

        await RunCycleAsync(RaidSettings.Default);

        // Rank 31 and 909, although from Home 8:260:3 Third (galaxy 1) is nearer than Rich (galaxy 5).
        Assert.Equal([Rich.Coordinates, Second.Coordinates], _client.SentMissions.Select(m => m.Target));
    }

    [Fact]
    public async Task Load_reports_stops_at_the_first_page_with_a_stored_report()
    {
        _client.ReportPageSize = 2;
        foreach (var name in new[] { "n1", "n2", "n3", "old1", "old2", "old3" }) // newest first
        {
            var report = new EspionageReport(name, Rich.Coordinates, PlanetType.Planet, _now.AddSeconds(-30), "Rich", null, PlayerStatus.Inactive, new ResourceSet(1, 0, 0), 0);
            _client.EspionageReports.Add(report);
            _client.ReportDetails[name] = report with { Ships = new UnitSection(0, []), Defenses = new UnitSection(0, []) };
        }
        _store.AddReports("tron", _client.EspionageReports.Skip(3).ToList()); // the old ones were loaded before

        var read = await Runner().ReadReportsAsync(_client, RaidSettings.Default);

        Assert.Equal(3, read);
        Assert.Equal([1, 2], _client.ReportPageReads); // page 2 holds "old1": nothing after it is new
        Assert.Equal(["n1", "n2", "n3"], _client.DetailReads);
        Assert.Equal(6, _store.LoadReportIds("tron").Count);
    }

    [Fact]
    public async Task Check_now_starts_the_next_cycle_at_once()
    {
        var runner = Runner();
        var checks = 0;

        var result = await runner.RunAsync(_client, RaidSettings.Default, new SyncProgress(p =>
        {
            _log.Add(p.Message);
            if (!p.Message.StartsWith("Next check", StringComparison.Ordinal))
                return;
            if (++checks == 1)
                runner.CheckNow(); // the wait is minutes long
            else
                _cancellation.Cancel();
        }), _cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RaidOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, checks);
        Assert.Contains("Checking now.", _log);
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
    public async Task At_the_slot_limit_nothing_is_attacked_or_spied_on()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 9); // 90% of 11 = 9: the limit is reached
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1 });

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.StartsWith("No spying: 9/11", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Below_the_slot_limit_spies_may_go_past_it_but_leave_one_slot_free()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 100, slotsUsed: 8); // 1 below the limit of 9

        await RunCycleAsync(RaidSettings.Default);

        Assert.Equal([MissionType.Spy, MissionType.Spy], _client.SentMissions.Select(m => m.Mission)); // slots 9 and 10; the 11th stays free
    }

    [Fact]
    public async Task With_nearest_planet_the_attack_leaves_from_the_closest_planet_with_cargo()
    {
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 500, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1, UseNearestPlanet = true, OriginPlanetIds = Both });

        var index = _client.SentMissions.FindIndex(m => m.Mission == MissionType.Attack);
        Assert.Equal(Colony.Id, _client.SentOrigins[index]);
        Assert.Equal([new ShipStack("HEAVY_CARGO", 121)], _client.SentMissions[index].Ships);
    }

    [Fact]
    public async Task With_nearest_planet_a_ticked_moon_sends_with_its_own_ships()
    {
        // The moon shares the colony's coordinates; the colony itself has no cargo ships and isn't ticked.
        var moon = new OwnPlanet(new PlanetId("m2"), "Moon", Colony.Coordinates);
        _client.OwnMoons.Add(moon);
        _client.Fleets[moon.Id] = Fleet(moon, heavyCargo: 500, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 1_000_000, 500_000), defense: 0, fleet: 0);

        await RunCycleAsync(RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1, UseNearestPlanet = true, OriginPlanetIds = [Home.Id.Value, moon.Id.Value] });

        var index = _client.SentMissions.FindIndex(m => m.Mission == MissionType.Attack);
        Assert.Equal(moon.Id, _client.SentOrigins[index]);
        Assert.Equal([new ShipStack("HEAVY_CARGO", 121)], _client.SentMissions[index].Ships);
    }

    [Fact]
    public async Task Without_nearest_planet_a_selected_moon_is_the_origin()
    {
        var moon = new OwnPlanet(new PlanetId("m1"), "Moon", Home.Coordinates);
        _client.OwnMoons.Add(moon);
        _client.Fleets[moon.Id] = Fleet(moon, heavyCargo: 100, slotsUsed: 8);
        _client.CurrentPlanet = moon.Id;

        await RunCycleAsync(RaidSettings.Default with { NearestTargetsFirst = false });

        Assert.NotEmpty(_client.SentMissions);
        Assert.All(_client.SentOrigins, o => Assert.Equal(moon.Id, o));
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

        var sent = await runner.AttackAsync(_client, targets.Where(t => t.Planet == Second).ToList(), RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1 });

        Assert.Equal(1, sent);
        var attack = Assert.Single(_client.SentMissions);
        Assert.Equal(Second.Coordinates, attack.Target);
        Assert.Equal([new ShipStack("HEAVY_CARGO", 22)], attack.Ships); // 500k × 1.1 / 25k
    }

    [Fact]
    public async Task An_attack_takes_the_first_cargo_type_and_tops_up_with_the_next()
    {
        _client.Fleets[Home.Id] = new FleetSnapshot(
            [new ShipStack("HEAVY_CARGO", 100), new ShipStack("LIGHT_CARGO", 50), new ShipStack(RaidRunner.ProbeType, 100)], Home.Coordinates,
            new FleetSlots(0, 11), new Dictionary<string, long> { ["HEAVY_CARGO"] = 25_000, ["LIGHT_CARGO"] = 5_000 });
        AddReport(Second, resources: new ResourceSet(1_000_000, 0, 0), defense: 0, fleet: 0);
        var settings = RaidSettings.Default with { MinLoot = 1, MaxAttacksPerTarget = 1, CargoShipTypes = ["LIGHT_CARGO", "HEAVY_CARGO"] };
        var runner = Runner();
        await runner.ReadReportsAsync(_client, settings);
        var targets = await runner.GetTargetsAsync("tron", settings);

        await runner.AttackAsync(_client, targets.Where(t => t.Planet == Second).ToList(), settings);

        // 550k: the 50 Light Cargos carry 250k, 12 Heavy Cargos the other 300k.
        Assert.Equal([new ShipStack("LIGHT_CARGO", 50), new ShipStack("HEAVY_CARGO", 12)], Assert.Single(_client.SentMissions).Ships);
        var raid = _store.Raids.Single(r => r.Mission == MissionType.Attack);
        Assert.Equal(("LIGHT_CARGO+HEAVY_CARGO", 62L), (raid.ShipType, raid.Ships));
    }

    [Fact]
    public async Task A_rich_planet_gets_more_attacks_each_for_half_of_what_is_left_landing_in_order()
    {
        var clock = new InstantTimeProvider(DateTimeOffset.UtcNow);
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 1_000, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 0, 0), defense: 0, fleet: 0);
        var settings = RaidSettings.Default with { MinLoot = 500_000 };
        var runner = Runner(clock);
        await runner.ReadReportsAsync(_client, settings);
        var targets = await runner.GetTargetsAsync("tron", settings);

        var sent = await runner.AttackAsync(_client, targets.Where(t => t.Planet == Rich).ToList(), settings);

        // 2M, then 1M of the 2M left, then 500k; a fourth would bring 250k, under the min loot.
        Assert.Equal(3, sent);
        var attacks = _store.Raids.Where(r => r.Mission == MissionType.Attack).ToList();
        Assert.Equal([2_000_000L, 1_000_000L, 500_000L], attacks.Select(a => a.ExpectedLoot));
        Assert.Equal([new ShipStack("HEAVY_CARGO", 88), new ShipStack("HEAVY_CARGO", 44), new ShipStack("HEAVY_CARGO", 22)],
            _client.SentMissions.Select(m => m.Ships.Single()));
        // Same planet, same speed: each one lands after the one before it.
        Assert.All(attacks.Zip(attacks.Skip(1)), p => Assert.True(p.Second.ArrivesAt >= p.First.ArrivesAt + RaidPlanner.MinWaveGap));
        Assert.All(_client.SentMissions, m => Assert.Equal(100, m.SpeedPercentage));
    }

    [Fact]
    public async Task A_later_attack_from_a_nearer_planet_flies_slower_to_land_after_the_first()
    {
        var clock = new InstantTimeProvider(DateTimeOffset.UtcNow);
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 1_000, slotsUsed: 0);
        _client.Fleets[Colony.Id] = Fleet(Colony, heavyCargo: 50, slotsUsed: 0); // too few for the first attack
        // 10 minutes from Home, 2 from Colony at full speed.
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.Attack, MissionType.Spy],
            TimeSpan.FromMinutes((_client.CurrentPlanet == Colony.Id ? 2.0 : 10.0) * 100 / mission.SpeedPercentage),
            10, 3650, "player", "Target", 10, 1_000_000, 0, PlayerStatus.Inactive, null);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 0, 0), defense: 0, fleet: 0);
        var settings = RaidSettings.Default with { MinLoot = 1_000_000, UseNearestPlanet = true, OriginPlanetIds = Both };
        var runner = Runner(clock);
        await runner.ReadReportsAsync(_client, settings);
        var targets = await runner.GetTargetsAsync("tron", settings);

        await runner.AttackAsync(_client, targets.Where(t => t.Planet == Rich).ToList(), settings);

        Assert.Equal([Home.Id, Colony.Id], _client.SentOrigins);
        Assert.Equal([100, 20], _client.SentMissions.Select(m => m.SpeedPercentage)); // 2 min at 20% = 10 min
        var attacks = _store.Raids.Where(r => r.Mission == MissionType.Attack).ToList();
        Assert.True(attacks[1].ArrivesAt >= attacks[0].ArrivesAt + RaidPlanner.MinWaveGap);
    }

    [Fact]
    public async Task One_attack_per_report_when_the_setting_says_so()
    {
        _client.Fleets[Home.Id] = Fleet(Home, heavyCargo: 1_000, slotsUsed: 0);
        AddReport(Rich, resources: new ResourceSet(4_000_000, 0, 0), defense: 0, fleet: 0);
        var settings = RaidSettings.Default with { MinLoot = 500_000, MaxAttacksPerTarget = 1 };
        var runner = Runner();
        await runner.ReadReportsAsync(_client, settings);

        Assert.Equal(1, await runner.AttackAsync(_client, (await runner.GetTargetsAsync("tron", settings)).Where(t => t.Planet == Rich).ToList(), settings));
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

    [Fact]
    public void Probes_back_during_the_cycle_bring_the_next_check_forward()
    {
        // The slots were read while the probes flew; they came back during the attacks, which then used up the budget.
        var readAt = _now;
        var probes = Raid(MissionType.Spy, sent: readAt.AddSeconds(-30), flight: TimeSpan.FromSeconds(25)); // back 20 s after the read
        var attack = Raid(MissionType.Attack, sent: readAt.AddSeconds(30), flight: TimeSpan.FromMinutes(14));

        Assert.Equal(TimeSpan.FromSeconds(15), RaidRunner.NextWait([probes, attack], [], readAt, readAt.AddSeconds(35), slotsFull: true));
        Assert.Equal(TimeSpan.FromSeconds(15), RaidRunner.NextWait([probes, attack], [], readAt, readAt.AddSeconds(35), slotsFull: false));
    }

    [Fact]
    public void The_next_check_waits_for_the_next_report_or_return()
    {
        var readAt = _now;
        var probes = Raid(MissionType.Spy, sent: readAt, flight: TimeSpan.FromMinutes(1)); // report due at 1:10
        var attack = Raid(MissionType.Attack, sent: readAt.AddHours(-1), flight: TimeSpan.FromMinutes(14)); // back long ago

        Assert.Equal(TimeSpan.FromSeconds(60), RaidRunner.NextWait([probes, attack], [], readAt, readAt.AddSeconds(10), slotsFull: false));
        Assert.Equal(TimeSpan.FromMinutes(10), RaidRunner.NextWait([Raid(MissionType.Attack, readAt, TimeSpan.FromMinutes(30))], [], readAt, readAt, slotsFull: false));
        Assert.Equal(TimeSpan.FromMinutes(5), RaidRunner.NextWait([attack], [], readAt, readAt.AddSeconds(10), slotsFull: false));
    }

    [Fact]
    public void A_report_not_in_by_its_due_time_is_checked_again_when_it_is_overdue()
    {
        // Landed 30 s ago, the read at its due time (+10 s) found no report; the probes need 5 minutes back.
        var readAt = _now;
        var probes = Raid(MissionType.Spy, sent: readAt.AddMinutes(-5).AddSeconds(-30), flight: TimeSpan.FromMinutes(5));

        // Landing + 2 minutes, 90 s from now, before the probes return in 5 minutes.
        Assert.Equal(TimeSpan.FromSeconds(90), RaidRunner.NextWait([probes], [], readAt, readAt, slotsFull: false));
    }

    [Fact]
    public void A_read_report_adds_no_report_checks()
    {
        var readAt = _now;
        var probes = Raid(MissionType.Spy, sent: readAt.AddMinutes(-5).AddSeconds(-30), flight: TimeSpan.FromMinutes(5));
        var report = new EspionageReport("r", Rich.Coordinates, PlanetType.Planet, readAt.AddSeconds(-20), "Rich", null, PlayerStatus.Inactive, ResourceSet.Zero, 0);

        // Only the probes' return, 4.5 minutes from now.
        Assert.Equal(TimeSpan.FromSeconds(270), RaidRunner.NextWait([probes], [report], readAt, readAt, slotsFull: false));
    }

    [Fact]
    public void With_full_slots_it_waits_for_the_whole_batch_of_probes_to_return()
    {
        // 19 probe fleets sent 1.5 s apart, 25 s each way: back from 50 s to 77 s after the first send.
        var readAt = _now;
        var batch = Enumerable.Range(0, 19)
            .Select(i => Raid(MissionType.Spy, sent: readAt.AddSeconds(i * 1.5), flight: TimeSpan.FromSeconds(25)))
            .ToList();
        var attack = Raid(MissionType.Attack, sent: readAt.AddMinutes(-1), flight: TimeSpan.FromMinutes(14));
        var now = readAt.AddSeconds(30);

        // Not at the first report (landing + 10 s) or the first return: the last probes back, 77 s after the read.
        Assert.Equal(TimeSpan.FromSeconds(47), RaidRunner.NextWait([.. batch, attack], [], readAt, now, slotsFull: true));
        // With free slots the first report is worth a cycle: landing at 25 s + 10 s.
        Assert.Equal(TimeSpan.FromSeconds(15), RaidRunner.NextWait([.. batch, attack], [], readAt, now, slotsFull: false));
    }

    private static RaidRecord Raid(MissionType mission, DateTimeOffset sent, TimeSpan flight) =>
        new(Rich.Coordinates, PlanetType.Planet, "Rich", mission, Home.Coordinates, "HEAVY_CARGO", 1, 0, sent, sent + flight, sent + 2 * flight);

    private static PlayerPlanet Inactive(string name, Coordinates coordinates, int rank, PlayerStatus status = PlayerStatus.Inactive) =>
        new(name, coordinates, false, DateTimeOffset.UtcNow, status, null, rank);

    private sealed class SyncProgress(Action<RaidProgress> handler) : IProgress<RaidProgress>
    {
        public void Report(RaidProgress value) => handler(value);
    }
}
