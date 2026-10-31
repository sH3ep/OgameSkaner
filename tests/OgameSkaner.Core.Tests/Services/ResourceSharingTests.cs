using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class ResourceSharingTests
{
    private static readonly OwnPlanet Rich = new(new PlanetId("rich"), "Rich", new Coordinates(1, 100, 5));
    private static readonly OwnPlanet Poor = new(new PlanetId("poor"), "Poor", new Coordinates(1, 101, 5));

    private static readonly ResourceShareSettings Settings = new(["HEAVY_CARGO"], MinTransportAmount: 0);

    private readonly FakeOgamexClient _client = new(new UniverseProfile("genesis"));
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private readonly FleetSlotClaimsProvider _claims;
    private readonly List<string> _log = [];

    public ResourceSharingTests()
    {
        _claims = new FleetSlotClaimsProvider(_clock);
        _client.OwnPlanets.AddRange([Rich, Poor]);
        _client.CurrentPlanet = Rich.Id;
        _client.Fleets[Rich.Id] = Fleet(Rich, new ResourceSet(10_000_000, 10_000_000, 10_000_000), new FleetSlots(0, 10));
        _client.Fleets[Poor.Id] = Fleet(Poor, new ResourceSet(100_000, 100_000, 100_000), new FleetSlots(0, 10));
    }

    private static FleetSnapshot Fleet(OwnPlanet planet, ResourceSet stock, FleetSlots slots) =>
        new([new ShipStack("HEAVY_CARGO", 1_000)], planet.Coordinates, slots, new Dictionary<string, long> { ["HEAVY_CARGO"] = 25_000 }, Resources: stock);

    private ResourceSharing Sharing() => new(TestQueues.Immediate, _clock, NullLogger<ResourceSharing>.Instance, _claims);

    /// <summary>Both planets are Builder planets: Rich keeps 1M of each, Poor waits for 500k of each.</summary>
    private static Dictionary<PlanetId, ShareDemand?> Demands() => new()
    {
        [Rich.Id] = new ShareDemand(new ResourceSet(1_000_000, 1_000_000, 1_000_000), null),
        [Poor.Id] = new ShareDemand(new ResourceSet(500_000, 500_000, 500_000), new ResourceSet(500_000, 500_000, 500_000))
    };

    private async Task<ShareResult> RunAsync(ResourceShareSettings settings) =>
        await Sharing().RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), Demands(), settings, _log.Add);

    [Fact]
    public async Task The_waiting_planet_gets_what_it_misses()
    {
        var result = await RunAsync(Settings);

        Assert.Equal(1, result.Sent);
        var mission = Assert.Single(_client.SentMissions);
        Assert.Equal((MissionType.Transport, PlanetType.Planet, Poor.Coordinates), (mission.Mission, mission.TargetType, mission.Target));
        Assert.Equal(new ResourceSet(400_000, 400_000, 400_000), mission.Cargo);
        Assert.Equal(Rich.Id, Assert.Single(_client.SentOrigins));
        Assert.NotNull(result.WakeIn); // when it lands
    }

    [Fact]
    public async Task More_ships_are_added_until_their_fuel_leaves_room_for_the_whole_cargo()
    {
        // 2,000 deuterium per ship: 51 ships (1.2M + 5%) burn 102k and leave 1,173k; 55 ships (1,302k + 5%) leave 1,265k.
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.Transport], TimeSpan.FromMinutes(5), mission.Ships.Sum(s => s.Quantity) * 2_000, 3650,
            "player", "Target", 10, 1_000_000, 0, PlayerStatus.Inactive, null);

        var result = await RunAsync(Settings);

        Assert.Equal(1, result.Sent);
        var mission = Assert.Single(_client.SentMissions);
        Assert.Equal(new ResourceSet(400_000, 400_000, 400_000), mission.Cargo);
        Assert.Equal(55, mission.Ships.Single().Quantity);
    }

    [Fact]
    public async Task What_is_on_its_way_is_not_sent_again()
    {
        var sharing = Sharing();
        var empire = await _client.GetEmpireAsync();
        await sharing.RunAsync(_client, [Rich, Poor], empire, Demands(), Settings, _log.Add);
        await sharing.RunAsync(_client, [Rich, Poor], empire, Demands(), Settings, _log.Add);

        Assert.Single(_client.SentMissions);
    }

    [Fact]
    public async Task Test_mode_sends_nothing()
    {
        await RunAsync(Settings with { TestMode = true });

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.StartsWith("Sharing: [Test] Would send", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_a_free_slot_the_sharing_claims_one_and_other_modules_leave_it()
    {
        _client.Fleets[Rich.Id] = _client.Fleets[Rich.Id] with { Slots = new FleetSlots(10, 10) };

        var result = await RunAsync(Settings);

        Assert.Equal(0, result.Sent);
        Assert.Equal(("Builder", 1), Assert.Single(_claims.For(_client).Snapshot()) is var c ? (c.Owner, c.Count) : default);
        Assert.Equal(new FleetSlots(10, 10), _claims.For(_client).Effective("Raids", new FleetSlots(9, 10)));

        // Fleets came back: the sharing takes a slot (one stays free for emergencies) and gives up its claim.
        _client.Fleets[Rich.Id] = _client.Fleets[Rich.Id] with { Slots = new FleetSlots(8, 10) };
        Assert.Equal(1, (await RunAsync(Settings)).Sent);
        Assert.Empty(_claims.For(_client).Snapshot());
    }

    [Fact]
    public async Task A_transport_the_app_did_not_send_blocks_its_receiver()
    {
        _client.FleetMovements.Add(new FleetMovement("x", MovementMission.Transport, MovementRelation.Own, false, DateTimeOffset.UtcNow.AddHours(1),
            Rich.Coordinates, Rich.Name, Poor.Coordinates, Poor.Name, 10));

        await RunAsync(Settings);

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.Contains("didn't send is on its way", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_transport_that_landed_after_the_stock_was_read_still_counts()
    {
        var sharing = Sharing();
        var empire = await _client.GetEmpireAsync();
        var readAt = _clock.GetUtcNow();
        await sharing.RunAsync(_client, [Rich, Poor], empire, Demands(), Settings, _log.Add, readAt);

        // The 5-minute flight landed, but the empire page the plan uses was read before.
        _clock.Advance(TimeSpan.FromMinutes(10));
        await sharing.RunAsync(_client, [Rich, Poor], empire, Demands(), Settings, _log.Add, readAt);
        Assert.Single(_client.SentMissions);

        // A page read after the landing would show the cargo, so it isn't counted on top any more.
        await sharing.RunAsync(_client, [Rich, Poor], empire, Demands(), Settings, _log.Add, _clock.GetUtcNow());
        Assert.Equal(2, _client.SentMissions.Count);
    }

    [Fact]
    public async Task A_planet_that_just_got_a_transport_gives_nothing_for_a_while()
    {
        var sharing = Sharing();
        await sharing.RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), Demands(), Settings, _log.Add);

        // Now Rich waits and Poor has a surplus: it doesn't pass on what it just got.
        var reversed = new Dictionary<PlanetId, ShareDemand?>
        {
            [Rich.Id] = new ShareDemand(new ResourceSet(10_500_000, 10_500_000, 10_500_000), new ResourceSet(10_500_000, 10_500_000, 10_500_000)),
            [Poor.Id] = new ShareDemand(ResourceSet.Zero, null)
        };
        _client.Fleets[Poor.Id] = Fleet(Poor, new ResourceSet(2_000_000, 2_000_000, 2_000_000), new FleetSlots(0, 10));
        _clock.Advance(TimeSpan.FromMinutes(30));
        await sharing.RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), reversed, Settings, _log.Add);
        Assert.Single(_client.SentMissions);

        _clock.Advance(ResourceSharing.ReceiverRest);
        await sharing.RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), reversed, Settings, _log.Add);
        Assert.Equal(Poor.Id, _client.SentOrigins[^1]);
    }

    [Fact]
    public async Task The_sharing_claims_slots_for_a_while_then_leaves_them_as_long()
    {
        _client.Fleets[Rich.Id] = _client.Fleets[Rich.Id] with { Slots = new FleetSlots(10, 10) };
        var sharing = Sharing();
        async Task Run() => await sharing.RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), Demands(), Settings, _log.Add);

        await Run();
        Assert.Single(_claims.For(_client).Snapshot());

        _clock.Advance(ResourceSharing.ClaimLimit / 2);
        await Run();
        Assert.Single(_claims.For(_client).Snapshot());

        _clock.Advance(ResourceSharing.ClaimLimit / 2);
        await Run();
        Assert.Empty(_claims.For(_client).Snapshot());

        _clock.Advance(ResourceSharing.ClaimLimit / 2);
        await Run();
        Assert.Empty(_claims.For(_client).Snapshot());

        _clock.Advance(ResourceSharing.ClaimLimit / 2);
        await Run();
        Assert.Single(_claims.For(_client).Snapshot());
    }

    [Fact]
    public async Task A_donor_that_cannot_carry_all_sends_nothing()
    {
        // 30 Large Cargo carry 750,000; Poor misses 1,200,000.
        _client.Fleets[Rich.Id] = _client.Fleets[Rich.Id] with { Ships = [new ShipStack("HEAVY_CARGO", 30)] };

        var result = await RunAsync(Settings);

        Assert.Equal(0, result.Sent);
        Assert.Empty(_client.SentMissions);
    }

    [Fact]
    public async Task A_builder_planet_whose_demand_is_not_known_gives_nothing()
    {
        var demands = Demands();
        demands[Rich.Id] = null;

        await Sharing().RunAsync(_client, [Rich, Poor], await _client.GetEmpireAsync(), demands, Settings, _log.Add);

        Assert.Empty(_client.SentMissions);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
