using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class OwnPlanetServiceTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly OwnPlanetService _service = new(new FakeOwnPlanetStore(), TimeProvider.System);

    public OwnPlanetServiceTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.BuildStates[Home.Id] = State(planet: Home);
        _client.BuildStates[Colony.Id] = State(new() { [BuildingType.MetalMine] = 7 }, planet: Colony);
        _client.CurrentPlanet = Home.Id;
    }

    [Fact]
    public async Task Inspect_reads_the_planet_with_facilities_and_selects_the_previous_planet_again()
    {
        var state = await _service.InspectAsync(_client, Colony.Id);

        Assert.Equal(7, state.LevelOf(BuildingType.MetalMine));
        Assert.Equal((Colony.Id, true), Assert.Single(_client.StateRequests));
        Assert.Equal(Home.Id, _client.CurrentPlanet);
    }

    [Fact]
    public async Task Inspect_of_the_current_planet_does_not_switch()
    {
        await _service.InspectAsync(_client, Home.Id);

        Assert.Empty(_client.SelectedPlanets);
    }

    [Fact]
    public async Task Refresh_reports_the_planet_selected_in_the_game()
    {
        _client.CurrentPlanet = Colony.Id;

        var refresh = await _service.RefreshAsync(_client);

        Assert.Equal(Colony.Id, refresh.CurrentPlanet);
        Assert.Equal([Home, Colony], refresh.List.Planets);
    }
}
