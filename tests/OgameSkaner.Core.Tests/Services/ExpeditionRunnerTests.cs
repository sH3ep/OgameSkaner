using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Expeditions;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Expeditions;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class ExpeditionRunnerTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(7, 130, 6));
    private static readonly OwnPlanet Selected = new(new PlanetId("3"), "Selected", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Luna = new(new PlanetId("m"), "Luna", Home.Coordinates);

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly InstantTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _log = [];
    private readonly List<ExpeditionOverview> _overviews = [];

    /// <summary>Ships home per planet; each sent mission takes its ships away, like on the server.</summary>
    private readonly Dictionary<PlanetId, List<ShipStack>> _home = new();

    private FleetSlots _expeditionSlots = new(0, 12);

    public ExpeditionRunnerTests()
    {
        _claims = new FleetSlotClaimsProvider(_time);
        _client.OwnPlanets.AddRange([Home, Colony, Selected]);
        _client.CurrentPlanet = Selected.Id;
        _home[Home.Id] = [new ShipStack("LIGHT_CARGO", 10), new ShipStack("HEAVY_CARGO", 6), new ShipStack("SOLAR_SATELLITE", 50)];
        _home[Colony.Id] = [new ShipStack("LIGHT_CARGO", 5)];
        _home[Luna.Id] = [new ShipStack("HEAVY_CARGO", 4)];
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.Expedition], TimeSpan.FromMinutes(10), 50, 1065, null, null, null, 0, 0, PlayerStatus.None, null)
        {
            ExpeditionDurations = [60, 120, 180]
        };
    }

    private static ExpeditionSettings Settings(params (OwnPlanet Place, int Count)[] origins) => ExpeditionSettings.Default with
    {
        ShipTypes = ["LIGHT_CARGO", "HEAVY_CARGO"],
        Origins = origins.Select(o => new ExpeditionOrigin(o.Place.Id.Value, o.Count)).ToList(),
        HoldMinutes = 100,
        KeepFleetSlotsFree = 0,
        CollectDebris = false
    };

    private readonly FleetSlotClaimsProvider _claims;

    private ExpeditionRunner Runner() => new(new OwnPlanetService(new FakeOwnPlanetStore(), _time), TestQueues.Immediate, _time, NullLogger<ExpeditionRunner>.Instance, _claims);

    /// <summary>What the other modules saw at each "Next check": the slots 2/20 as Raids may use them.</summary>
    private readonly List<FleetSlots> _raidsSaw = [];

    /// <summary>When each mission of <see cref="FakeOgamexClient.SentMissions"/> was sent.</summary>
    private readonly List<DateTimeOffset> _sentAt = [];

    /// <summary>
    /// A planet's fleet page: the ships home minus those of the expeditions still out (a flight of 10 min each way and a
    /// stay of 2 h), and the slots those expeditions take.
    /// </summary>
    private FleetSnapshot FleetOf(PlanetId id)
    {
        var now = _time.GetUtcNow();
        var away = _client.SentMissions.Zip(_client.SentOrigins, _sentAt)
            .Where(m => m.Third + TimeSpan.FromMinutes(140) > now)
            .ToList();
        var place = _client.OwnPlanets.Concat(_client.OwnMoons).First(p => p.Id == id);
        var left = _home.GetValueOrDefault(id, [])
            .Select(s => new ShipStack(s.ShipType, s.Quantity - away.Where(m => m.Second == id).Sum(m => m.First.Ships.Where(x => x.ShipType == s.ShipType).Sum(x => x.Quantity))))
            .ToList();
        return new FleetSnapshot(left, place.Coordinates, new FleetSlots(away.Count, 20), ExpeditionSlots: _expeditionSlots with { Used = _expeditionSlots.Used + away.Count });
    }

    /// <summary>Runs until <paramref name="checks"/> checks are done (each ends with "Next check").</summary>
    private Task<ExpeditionRunResult> RunAsync(ExpeditionSettings settings, int checks = 1)
    {
        var cancellation = new CancellationTokenSource();
        var done = 0;
        var answer = _client.SendAnswer;
        _client.SendAnswer = mission =>
        {
            var outcome = answer(mission);
            if (outcome.Sent)
                _sentAt.Add(_time.GetUtcNow());
            return outcome;
        };
        _client.FleetOf = FleetOf;
        return Runner().RunAsync(_client, settings, new SyncProgress(p =>
        {
            if (p.Message.Length > 0)
                _log.Add(p.Message);
            if (p.Overview is not null)
                _overviews.Add(p.Overview);
            if (p.Message.StartsWith("Next check", StringComparison.Ordinal))
            {
                _raidsSaw.Add(_claims.For(_client).Effective("Raids", new FleetSlots(2, 20)));
                if (++done == checks)
                    cancellation.Cancel();
            }
        }), cancellation.Token);
    }

    [Fact]
    public async Task Each_planet_sends_its_expeditions_with_its_share_of_the_ticked_ships()
    {
        var result = await RunAsync(Settings((Home, 2), (Colony, 1)));

        Assert.Equal(new ExpeditionRunResult(ExpeditionOutcome.Cancelled, 3), result);
        Assert.Equal([Home.Id, Home.Id, Colony.Id], _client.SentOrigins.Select(o => o!.Value));
        Assert.Equal([new ShipStack("LIGHT_CARGO", 5), new ShipStack("HEAVY_CARGO", 3)], _client.SentMissions[0].Ships);
        Assert.Equal([new ShipStack("LIGHT_CARGO", 5), new ShipStack("HEAVY_CARGO", 3)], _client.SentMissions[1].Ships); // the satellites stay
        Assert.Equal([new ShipStack("LIGHT_CARGO", 5)], _client.SentMissions[2].Ships);
        Assert.All(_client.SentMissions, m =>
            Assert.Equal((MissionType.Expedition, PlanetType.Planet, 120, 100), (m.Mission, m.TargetType, m.HoldMinutes!.Value, m.SpeedPercentage)));
        Assert.Equal(new Coordinates(8, 260, 16), _client.SentMissions[0].Target);
        Assert.Equal(new Coordinates(7, 130, 16), _client.SentMissions[2].Target);
        Assert.Equal([2, 1, 0], _overviews.Last().Origins.Select(o => o.Flying));
    }

    [Fact]
    public async Task The_slots_of_expeditions_not_in_the_air_are_reserved_while_running()
    {
        _home[Colony.Id] = []; // its expedition can't go: its slot stays reserved

        await RunAsync(Settings((Home, 2), (Colony, 1)));

        Assert.Equal(2, _client.SentMissions.Count);
        Assert.Equal(new FleetSlots(3, 20), Assert.Single(_raidsSaw));
        Assert.Empty(_claims.For(_client).Snapshot()); // released when stopped
    }

    [Fact]
    public async Task Without_reserving_the_other_modules_see_the_slots_as_read()
    {
        _home[Colony.Id] = [];

        await RunAsync(Settings((Home, 2), (Colony, 1)) with { ReserveSlots = false });

        Assert.Equal(new FleetSlots(2, 20), Assert.Single(_raidsSaw));
    }

    [Fact]
    public async Task The_next_check_sends_nothing_while_the_expeditions_are_out()
    {
        await RunAsync(Settings((Home, 1)), checks: 3);

        Assert.Single(_client.SentMissions);
        Assert.Contains("Every planet has its expeditions in the air.", _log);
    }

    [Fact]
    public async Task A_returned_expedition_is_replaced()
    {
        // 10 min out, 2 h (nearest to 100 min) at position 16, 10 min back: the check after that sends again.
        var result = await RunAsync(Settings((Home, 1)) with { CheckIntervalMinutes = 60 }, checks: 4);

        Assert.Equal(2, result.Sent);
    }

    [Fact]
    public async Task Expeditions_already_listed_in_the_event_list_count()
    {
        _client.FleetMovements.Add(new FleetMovement("x", MovementMission.Expedition, MovementRelation.Own, false, _time.GetUtcNow().AddHours(1),
            Home.Coordinates, Home.Name, new Coordinates(8, 260, 16), "", 3));

        await RunAsync(Settings((Home, 2)));

        Assert.Single(_client.SentMissions);
        Assert.Equal([new ShipStack("LIGHT_CARGO", 10), new ShipStack("HEAVY_CARGO", 6)], _client.SentMissions[0].Ships);
    }

    [Fact]
    public async Task Sending_stops_at_the_free_expedition_slots()
    {
        _expeditionSlots = new FleetSlots(10, 12);

        await RunAsync(Settings((Home, 2), (Colony, 2)));

        Assert.Equal(2, _client.SentMissions.Count);
        Assert.Contains(_log, l => l.StartsWith("No free slot for more expeditions (expeditions 12/12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fleet_slots_are_kept_free_for_the_other_pages()
    {
        await RunAsync(Settings((Home, 3)) with { KeepFleetSlotsFree = 19 });

        Assert.Single(_client.SentMissions); // 20 fleet slots, 19 kept
    }

    [Fact]
    public async Task A_refusal_moves_on_to_the_next_planet()
    {
        _client.SendAnswer = mission => mission.Target.Galaxy == 8 ? new FleetSendOutcome(false, "Not enough deuterium") : new FleetSendOutcome(true);

        await RunAsync(Settings((Home, 2), (Colony, 1)));

        Assert.Equal([Colony.Id], _client.SentOrigins.Select(o => o!.Value));
        Assert.Contains("Home [8:260:3]: the expedition was refused: Not enough deuterium", _log);
    }

    [Fact]
    public async Task A_planet_without_the_ticked_ships_is_skipped()
    {
        _home[Home.Id] = [new ShipStack("SPY_PROBE", 10)];

        await RunAsync(Settings((Home, 1), (Colony, 1)));

        Assert.Equal([Colony.Id], _client.SentOrigins.Select(o => o!.Value));
        Assert.Contains("Home [8:260:3]: none of the ticked ships is home.", _log);
    }

    [Fact]
    public async Task Moons_send_expeditions_too()
    {
        _client.OwnMoons.Add(Luna);
        _client.FleetMovements.Add(new FleetMovement("x", MovementMission.Expedition, MovementRelation.Own, false, _time.GetUtcNow().AddHours(1),
            Home.Coordinates, Home.Name, new Coordinates(8, 260, 16), "", 3)); // the planet's, not the moon's

        await RunAsync(Settings((Luna, 1)));

        Assert.Equal([Luna.Id], _client.SentOrigins.Select(o => o!.Value));
        Assert.Equal([new ShipStack("HEAVY_CARGO", 4)], _client.SentMissions[0].Ships);
        Assert.Equal(new Coordinates(8, 260, 16), _client.SentMissions[0].Target);
    }

    [Fact]
    public async Task Test_mode_sends_nothing()
    {
        var result = await RunAsync(Settings((Home, 2)) with { TestMode = true });

        Assert.Empty(_client.SentMissions);
        Assert.Equal(2, result.Sent);
        Assert.Contains(_log, l => l.StartsWith("[Test] Would send 5 Light Cargo, 3 Heavy Cargo from Home [8:260:3] to [8:260:16] for 2 h", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_expired_session_stops_the_run()
    {
        _client.MovementsError = new SessionExpiredException("expired");

        var result = await RunAsync(Settings((Home, 1)));

        Assert.Equal(new ExpeditionRunResult(ExpeditionOutcome.Failed, 0, "expired"), result);
    }

    [Fact]
    public async Task Missing_origins_and_no_ship_types_are_reported()
    {
        await RunAsync(Settings((new OwnPlanet(new PlanetId("gone"), "Gone", new Coordinates(1, 2, 3)), 1)) with { ShipTypes = [] });

        Assert.Empty(_client.SentMissions);
        Assert.Contains("1 planet(s) or moon(s) set to send expeditions aren't in the planet list any more and are skipped.", _log);
        Assert.Contains("No ship type is ticked.", _log);
    }

    /// <summary>A debris field at Home's position 16, Recyclers at home and a briefing that offers both missions.</summary>
    private ExpeditionSettings WithDebris(long metal, long crystal)
    {
        _client.Debris[new SolarSystemAddress(8, 260)] = new() { [16] = new ResourceSet(metal, crystal, 0) };
        _home[Home.Id] = [.. _home[Home.Id], new ShipStack("RECYCLER", 80)];
        _client.Briefing = mission => new FleetBriefing(
            [mission.Mission], TimeSpan.FromMinutes(10), 50, 1065, null, null, null, 0, 0, PlayerStatus.None, null)
        {
            ExpeditionDurations = [60, 120, 180]
        };
        return Settings((Home, 1)) with { CollectDebris = true };
    }

    private List<FleetMission> RecycleMissions => _client.SentMissions.Where(m => m.Mission == MissionType.Recycle).ToList();

    [Fact]
    public async Task Recyclers_go_to_a_debris_field_at_position_16_of_the_origins_system()
    {
        await RunAsync(WithDebris(1_250_000, 830_400));

        var recycle = Assert.Single(RecycleMissions);
        Assert.Equal((new Coordinates(8, 260, 16), PlanetType.DebrisField), (recycle.Target, recycle.TargetType));
        Assert.Equal([new ShipStack("RECYCLER", 80)], recycle.Ships); // 2,080,400 needs 105 of 20,000, 80 are home
        Assert.Equal(Home.Id, _client.SentOrigins[_client.SentMissions.IndexOf(recycle)]);
        Assert.Contains(_log, l => l.StartsWith("Sent 80 Recycler from Home", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recyclers_take_only_what_the_debris_fills()
    {
        await RunAsync(WithDebris(100_000, 50_000));

        Assert.Equal([new ShipStack("RECYCLER", 8)], Assert.Single(RecycleMissions).Ships);
    }

    [Fact]
    public async Task No_recyclers_for_a_small_field_or_one_they_already_fly_to()
    {
        await RunAsync(WithDebris(50_000, 40_000));
        Assert.Empty(RecycleMissions);

        _client.FleetMovements.Add(new FleetMovement("r", MovementMission.Recycle, MovementRelation.Own, false, _time.GetUtcNow().AddMinutes(5),
            Home.Coordinates, Home.Name, new Coordinates(8, 260, 16), "", 80));
        await RunAsync(WithDebris(1_000_000, 0));
        Assert.Empty(RecycleMissions);
    }

    [Fact]
    public async Task The_system_is_not_read_again_before_an_expedition_arrived()
    {
        // The first check reads it; the second (5 min later, the expedition arrives after 10) doesn't.
        await RunAsync(WithDebris(0, 0) with { CheckIntervalMinutes = 5 }, checks: 2);

        Assert.Single(_client.RequestedSystems);
    }

    [Fact]
    public async Task The_runner_wakes_when_an_expedition_arrives_and_reads_the_system_again()
    {
        await RunAsync(WithDebris(0, 0) with { CheckIntervalMinutes = 60 }, checks: 2);

        Assert.Equal(2, _client.RequestedSystems.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 12, 10, 30, TimeSpan.Zero), _time.GetUtcNow());
    }

    [Fact]
    public async Task Without_collecting_no_galaxy_page_is_read()
    {
        await RunAsync(WithDebris(1_000_000, 0) with { CollectDebris = false });

        Assert.Empty(RecycleMissions);
        Assert.Empty(_client.RequestedSystems);
    }

    private sealed class SyncProgress(Action<ExpeditionProgress> handler) : IProgress<ExpeditionProgress>
    {
        public void Report(ExpeditionProgress value) => handler(value);
    }
}
