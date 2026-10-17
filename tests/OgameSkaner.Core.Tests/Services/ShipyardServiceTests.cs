using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class ShipyardServiceTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));
    private static readonly OwnPlanet Outpost = new(new PlanetId("3"), "Outpost", new Coordinates(2, 5, 4));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));

    public ShipyardServiceTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony, Outpost]);
        _client.CurrentPlanet = Home.Id;
    }

    private ShipyardService Service() =>
        new(new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System), NullLogger<ShipyardService>.Instance);

    [Fact]
    public async Task Orders_the_ships_on_every_selected_planet_and_switches_back()
    {
        var results = await Service().OrderAsync(_client, [Colony.Id, Outpost.Id], "BATTLESHIP", 10);

        Assert.Equal([(Colony.Id, "BATTLESHIP", 10), (Outpost.Id, "BATTLESHIP", 10)], _client.OrderedShips);
        Assert.Empty(_client.OrderedDefenses);
        Assert.All(results, r => Assert.True(r.Queued));
        Assert.Equal(Home.Id, _client.SelectedPlanets.Last());
    }

    [Fact]
    public async Task Defenses_go_to_the_defense_order()
    {
        await Service().OrderAsync(_client, [Home.Id], "PLASMA_CANNON", 5);

        Assert.Equal([(Home.Id, "PLASMA_CANNON", 5)], _client.OrderedDefenses);
        Assert.Empty(_client.OrderedShips);
    }

    [Fact]
    public async Task A_refused_planet_is_reported_and_the_others_still_get_their_order()
    {
        _client.UnitOrderAnswer = planet => planet == Colony.Id ? new EnqueueOutcome(EnqueueResult.Rejected, "Not enough resources") : EnqueueOutcome.Queued;

        var results = await Service().OrderAsync(_client, [Home.Id, Colony.Id, Outpost.Id], "LIGHT_FIGHTER", 3);

        Assert.Equal([true, false, true], results.Select(r => r.Queued));
        Assert.Contains("Not enough resources", results[1].Message, StringComparison.Ordinal);
        Assert.Equal([Home.Id, Outpost.Id], _client.OrderedShips.Select(o => o.Planet));
    }
}
