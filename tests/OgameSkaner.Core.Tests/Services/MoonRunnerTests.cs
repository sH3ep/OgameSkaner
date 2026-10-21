using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Moons;
using OgameSkaner.Core.Services.Transports;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class MoonRunnerTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("home"), "Home planet", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Moon = new(new PlanetId("moon"), "Moon", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Colony = new(new PlanetId("colony"), "Colony", new Coordinates(8, 260, 4));

    private static readonly MoonSettings Settings = MoonSettings.Default with { MoonIds = [Moon.Id.Value], HorizonHours = 1 };

    private readonly FakeOgamexClient _client = new(new UniverseProfile("genesis"));
    private readonly List<string> _log = [];

    public MoonRunnerTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.OwnMoons.Add(Moon);
        _client.CurrentPlanet = Home.Id;
        _client.BuildStates[Moon.Id] = MoonPlannerTests.FreshMoon(new ResourceSet(1_000_000, 1_000_000, 1_000_000));
        _client.Fleets[Home.Id] = new FleetSnapshot(
            [new ShipStack("HEAVY_CARGO", 10_000)], Home.Coordinates, new FleetSlots(0, 20),
            new Dictionary<string, long> { ["HEAVY_CARGO"] = 25_000 }, Resources: new ResourceSet(10_000_000_000, 10_000_000_000, 10_000_000_000));
    }

    private MoonRunner Runner() =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), TestQueues.Immediate, TimeProvider.System, NullLogger<MoonRunner>.Instance);

    private SyncProgress Progress() => new(p => _log.Add(p.Message));

    [Fact]
    public async Task The_moon_gets_what_it_can_pay_queued_and_a_transport_for_the_next_levels()
    {
        await Runner().TransportNowAsync(_client, Settings, Progress());

        Assert.Equal([BuildingType.MoonBase, BuildingType.RoboticsFactory, BuildingType.RoboticsFactory], _client.EnqueuedBuildings.Select(e => e.Building));
        var transport = Assert.Single(_client.SentMissions);
        Assert.Equal((MissionType.Transport, PlanetType.Moon, Moon.Coordinates), (transport.Mission, transport.TargetType, transport.Target));
        Assert.Equal(Home.Id, Assert.Single(_client.SentOrigins));
        Assert.Equal("HEAVY_CARGO", Assert.Single(transport.Ships).ShipType);

        // The 3 levels just queued (paid from the moon's million of each) and the next 3: far less than the whole plan.
        var moon = MoonPlannerTests.FreshMoon(new ResourceSet(1_000_000, 1_000_000, 1_000_000));
        var plan = MoonPlanner.Simulate(moon, Settings);
        // Rounded up like a player would type it (the planet has plenty).
        var shipment = MoonPlanner.Shipment(plan, moon, ResourceSet.Zero, Settings.Horizon, levels: 3 + Settings.MaxServerQueueDepth);
        Assert.Equal(new ResourceSet(CargoRounding.Up(shipment.Metal), CargoRounding.Up(shipment.Crystal), CargoRounding.Up(shipment.Deuterium)), transport.Cargo);
        Assert.True(transport.Cargo.Metal < plan.Steps.Sum(s => s.Cost.Metal) / 10);
    }

    [Fact]
    public async Task What_is_already_on_its_way_is_not_sent_again()
    {
        _client.EnqueueAnswer = (_, _) => new EnqueueOutcome(EnqueueResult.Rejected, "later"); // the moon stays as it is
        _client.BuildStates[Moon.Id] = MoonPlannerTests.FreshMoon(); // empty, so it needs a transport
        var runner = Runner();
        await runner.TransportNowAsync(_client, Settings, Progress());
        _client.FleetMovements.Add(OwnTransport());

        await runner.TransportNowAsync(_client, Settings, Progress());

        Assert.Single(_client.SentMissions);
        Assert.Contains(_log, l => l.Contains("covers its next queue items", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_free_fleet_slot_the_moons_pause_and_go_on_with_the_same_moon()
    {
        var second = new OwnPlanet(new PlanetId("moon2"), "Moon", Colony.Coordinates);
        _client.OwnMoons.Insert(0, second); // listed first
        _client.BuildStates[second.Id] = MoonPlannerTests.FreshMoon(id: second.Id.Value) with { Planet = second };
        _client.Fleets[Colony.Id] = _client.Fleets[Home.Id] with { CurrentPlanet = Colony.Coordinates };
        var full = new FleetSlots(20, 20);
        _client.FleetOf = planet => _client.Fleets[planet] with { Slots = full };
        var settings = Settings with { MoonIds = [Moon.Id.Value, second.Id.Value] };
        var runner = Runner();
        var cancellation = new CancellationTokenSource();
        var checks = new List<string>();

        var run = runner.RunAsync(_client, settings, new SyncProgress(p =>
        {
            _log.Add(p.Message);
            if (!p.Message.StartsWith("Next check", StringComparison.Ordinal))
                return;
            checks.Add(p.Message);
            if (checks.Count == 1)
            {
                full = new FleetSlots(0, 20); // a slot came back
                runner.CheckNow();
            }
            else
                cancellation.Cancel();
        }), cancellation.Token);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(_log, l => l.StartsWith("No free fleet slot: the moons pause, checking again in 30 s", StringComparison.Ordinal));
        // The first cycle stopped at the first moon; the second started with it again and then did the other one.
        var buildOrder = _client.StateRequests.Select(r => r.Planet).ToList();
        Assert.Equal([second.Id, second.Id, Moon.Id], buildOrder);
        Assert.Equal([Colony.Id, Home.Id], _client.SentOrigins);
    }

    [Fact]
    public async Task No_transport_while_one_the_app_did_not_send_is_on_its_way()
    {
        _client.FleetMovements.Add(OwnTransport());

        await Runner().TransportNowAsync(_client, Settings, Progress());

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.Contains("didn't send is on its way", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_moon_with_enough_gets_no_transport()
    {
        _client.BuildStates[Moon.Id] = MoonPlannerTests.FreshMoon(new ResourceSet(10_000_000_000, 10_000_000_000, 10_000_000_000));

        await Runner().TransportNowAsync(_client, Settings, Progress());

        Assert.Empty(_client.SentMissions);
    }

    [Fact]
    public async Task The_transport_takes_only_what_the_planet_has_and_leaves_the_fuel()
    {
        _client.BuildStates[Moon.Id] = MoonPlannerTests.FreshMoon();
        _client.Fleets[Home.Id] = _client.Fleets[Home.Id] with { Resources = new ResourceSet(50_000, 60_000, 1_010) };

        await Runner().TransportNowAsync(_client, Settings, Progress());

        Assert.Equal(new ResourceSet(50_000, 60_000, 1_000), Assert.Single(_client.SentMissions).Cargo); // the briefing's 10 deuterium stay
    }

    [Fact]
    public async Task Unticked_moons_and_test_mode_send_nothing()
    {
        await Runner().TransportNowAsync(_client, Settings with { MoonIds = [] }, Progress());
        // Test mode queues nothing, so the moon must lack what its next levels cost to show a transport.
        _client.BuildStates[Moon.Id] = MoonPlannerTests.FreshMoon(new ResourceSet(200_000, 400_000, 100_000)); // Moon Base 1, not Robot 1 and 2
        await Runner().TransportNowAsync(_client, Settings with { TestMode = true }, Progress());

        Assert.Empty(_client.SentMissions);
        Assert.Empty(_client.EnqueuedBuildings);
        Assert.Contains("No moon is ticked.", _log);
        Assert.Contains(_log, l => l.StartsWith("[Test] Would send", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.StartsWith("[Test] Would queue Moon Base 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Moons_are_created_on_every_planet_that_can_have_one()
    {
        var third = new OwnPlanet(new PlanetId("third"), "Third", new Coordinates(1, 1, 1));
        var poor = new OwnPlanet(new PlanetId("poor"), "Poor", new Coordinates(1, 1, 2));
        _client.MoonFormations[Home.Id] = new MoonFormationInfo(Home.Id, 4, 4_000, 1_000, 9_999, 1_000_000_000); // has one
        _client.MoonFormations[poor.Id] = new MoonFormationInfo(poor.Id, 4, 0, 1_000, 9_999, 1_000);
        _client.CreateMoonRefusal = planet => planet == third.Id ? "Something went wrong." : null;

        var results = await Runner().CreateMoonsAsync(_client, [Home, Colony, third, poor], 1_000, testMode: false, Progress());

        Assert.Equal([(Colony.Id, 1_000)], _client.CreatedMoons);
        Assert.Equal(new PlanetId("moon-colony"), results.Single(r => r.Planet == Colony).Moon);
        Assert.Contains("moon already", results.Single(r => r.Planet == Home).Error);
        Assert.Equal("Something went wrong.", results.Single(r => r.Planet == third).Error);
        Assert.Contains(MoonFormation.MetalCost(1_000).ToString("N0"), results.Single(r => r.Planet == poor).Error);
    }

    [Fact]
    public async Task A_moon_larger_than_the_graviton_level_allows_is_refused()
    {
        var results = await Runner().CreateMoonsAsync(_client, [Colony], 5_000, testMode: false, Progress());

        Assert.Empty(_client.CreatedMoons);
        Assert.Contains("Graviton Research 6", Assert.Single(results).Error);
    }

    private static FleetMovement OwnTransport() =>
        new("t1", MovementMission.Transport, MovementRelation.Own, false, DateTimeOffset.UtcNow.AddMinutes(5),
            Home.Coordinates, Home.Name, Moon.Coordinates, Moon.Name, 10);

    private sealed class SyncProgress(Action<MoonProgress> handler) : IProgress<MoonProgress>
    {
        public void Report(MoonProgress value) => handler(value);
    }
}
