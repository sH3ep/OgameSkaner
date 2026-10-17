using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class ColonizationServiceTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Near = new(new PlanetId("2"), "Near", new Coordinates(5, 288, 8));
    private static readonly OwnPlanet Far = new(new PlanetId("3"), "Far", new Coordinates(3, 100, 8));
    private static readonly Coordinates Target = new(5, 290, 4);

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));

    public ColonizationServiceTests()
    {
        _client.OwnPlanets.AddRange([Home, Near, Far]);
        _client.CurrentPlanet = Home.Id;
        _client.Fleets[Home.Id] = Fleet(Home, colonyShips: 1);
        _client.Fleets[Near.Id] = Fleet(Near, colonyShips: 1);
        _client.Fleets[Far.Id] = Fleet(Far, colonyShips: 1);
        _client.Briefing = _ => Briefing([MissionType.Colonize, MissionType.Transport]);
    }

    private ColonizationService Service() =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), NullLogger<ColonizationService>.Instance);

    private static FleetSnapshot Fleet(OwnPlanet planet, int colonyShips) =>
        new(colonyShips > 0 ? [new ShipStack(ColonizationService.ColonyShipType, colonyShips)] : [], planet.Coordinates);

    private static FleetBriefing Briefing(IReadOnlyList<MissionType> missions, string? owner = null, string? warning = null) =>
        new(missions, TimeSpan.FromMinutes(30), 10, 2890, owner is null ? null : "id", owner, null, 0, 0, PlayerStatus.None, warning);

    [Fact]
    public async Task One_colony_ship_is_sent_from_the_nearest_planet_and_the_game_switched_back()
    {
        var result = await Service().ColonizeAsync(_client, Target);

        Assert.True(result.Sent, result.Message);
        var mission = Assert.Single(_client.SentMissions);
        Assert.Equal(Target, mission.Target);
        Assert.Equal(PlanetType.Planet, mission.TargetType);
        Assert.Equal(MissionType.Colonize, mission.Mission);
        Assert.Equal([new ShipStack(ColonizationService.ColonyShipType, 1)], mission.Ships);
        Assert.Equal(Near.Id, Assert.Single(_client.SentOrigins));
        Assert.Equal([(PlanetId?)Near.Id], _client.FleetReads); // nearest first, stops at the first with a ship
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last());
    }

    [Fact]
    public async Task Planets_without_a_colony_ship_are_skipped()
    {
        _client.Fleets[Near.Id] = Fleet(Near, colonyShips: 0);

        var result = await Service().ColonizeAsync(_client, Target);

        Assert.True(result.Sent, result.Message);
        Assert.Equal(Far.Id, Assert.Single(_client.SentOrigins));
    }

    [Fact]
    public async Task Without_any_colony_ship_nothing_is_sent()
    {
        foreach (var planet in new[] { Home, Near, Far })
            _client.Fleets[planet.Id] = Fleet(planet, colonyShips: 0);

        var result = await Service().ColonizeAsync(_client, Target);

        Assert.False(result.Sent);
        Assert.Contains("No Colony Ship", result.Message);
        Assert.Empty(_client.SentMissions);
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last());
    }

    [Fact]
    public async Task A_slot_taken_since_the_scan_is_refused_by_the_briefing()
    {
        _client.Briefing = _ => Briefing([MissionType.Transport, MissionType.Spy], owner: "Newcomer");

        var result = await Service().ColonizeAsync(_client, Target);

        Assert.False(result.Sent);
        Assert.Contains("Newcomer", result.Message);
        Assert.Empty(_client.SentMissions);
    }

    [Fact]
    public async Task A_server_refusal_is_reported()
    {
        _client.SendAnswer = _ => new FleetSendOutcome(false, "Not enough deuterium");

        var result = await Service().ColonizeAsync(_client, Target);

        Assert.False(result.Sent);
        Assert.Contains("Not enough deuterium", result.Message);
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last());
    }
}
