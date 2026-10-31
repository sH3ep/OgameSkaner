using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class EmpireFleetsTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(5, 288, 8));
    private static readonly OwnPlanet Outpost = new(new PlanetId("3"), "Outpost", new Coordinates(1, 1, 1));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));

    public EmpireFleetsTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony, Outpost]);
        _client.Fleets[Home.Id] = Fleet(Home, ("HEAVY_CARGO", 100, 25_000));
        _client.Fleets[Colony.Id] = Fleet(Colony, ("HEAVY_CARGO", 50, 25_000), ("SPY_PROBE", 10, 5));
        _client.Fleets[Outpost.Id] = Fleet(Outpost, ("ASTEROID_MINER", 1_000, 40_000));
    }

    [Fact]
    public async Task One_fleet_page_of_the_preferred_planet_gives_the_slots_and_capacities_for_all()
    {
        var fleets = await EmpireFleets.ReadAsync(_client, [Home, Colony, Outpost], ["HEAVY_CARGO"], Home.Id);

        Assert.Equal([(PlanetId?)Home.Id], _client.FleetReads);
        Assert.Equal([Home, Colony, Outpost], fleets.Select(f => f.Planet));
        var colony = fleets[1].Fleet;
        Assert.Equal(50, colony.QuantityOf("HEAVY_CARGO"));
        Assert.Equal(10, colony.QuantityOf("SPY_PROBE"));
        Assert.Equal(25_000, colony.CargoCapacityOf("HEAVY_CARGO"));
        Assert.Equal(new FleetSlots(3, 11), colony.Slots);
        Assert.Equal(Colony.Coordinates, colony.CurrentPlanet);
        Assert.Equal(new ResourceSet(1_000, 2_000, 3_000), colony.Resources);
    }

    [Fact]
    public async Task A_type_the_preferred_planet_lacks_is_read_where_it_is()
    {
        var fleets = await EmpireFleets.ReadAsync(_client, [Home, Colony, Outpost], ["HEAVY_CARGO", "SPY_PROBE"], Home.Id);

        // The colony has both types: one page covers them, the home planet isn't needed.
        Assert.Equal([(PlanetId?)Colony.Id], _client.FleetReads);
        Assert.All(fleets, f => Assert.Equal(5, f.Fleet.CargoCapacityOf("SPY_PROBE")));
    }

    [Fact]
    public async Task Types_on_different_planets_take_a_page_each()
    {
        await EmpireFleets.ReadAsync(_client, [Home, Colony, Outpost], ["SPY_PROBE", "ASTEROID_MINER"], Home.Id);

        Assert.Equal([Colony.Id.Value, Outpost.Id.Value], _client.FleetReads.Select(r => r!.Value.Value).Order());
    }

    [Fact]
    public async Task Without_any_ship_of_the_types_the_preferred_planet_gives_the_slots()
    {
        var fleets = await EmpireFleets.ReadAsync(_client, [Home, Colony], ["RECYCLER"], Colony.Id);

        Assert.Equal([(PlanetId?)Colony.Id], _client.FleetReads);
        Assert.All(fleets, f => Assert.Equal(new FleetSlots(3, 11), f.Fleet.Slots));
    }

    [Fact]
    public async Task Planets_missing_from_the_empire_page_are_read_by_their_fleet_page()
    {
        _client.Empire = () => new EmpirePage([], [], new Dictionary<string, int>());

        var fleets = await EmpireFleets.ReadAsync(_client, [Home, Colony], ["HEAVY_CARGO"], Home.Id);

        Assert.Equal([(PlanetId?)Home.Id, Colony.Id], _client.FleetReads);
        Assert.Equal(50, fleets[1].Fleet.QuantityOf("HEAVY_CARGO"));
    }

    [Fact]
    public async Task An_unreadable_empire_page_falls_back_to_the_fleet_pages()
    {
        _client.Empire = () => throw new OgamexException("markup changed");

        await EmpireFleets.ReadAsync(_client, [Home, Colony, Outpost], ["HEAVY_CARGO"], Home.Id);

        Assert.Equal(3, _client.FleetReads.Count);
    }

    [Fact]
    public async Task A_moon_gets_its_own_column_not_its_planets()
    {
        var moon = new OwnPlanet(new PlanetId("m1"), "Moon", Home.Coordinates);
        _client.OwnMoons.Add(moon);
        _client.Fleets[moon.Id] = Fleet(moon, ("HEAVY_CARGO", 7, 25_000));

        var fleets = await EmpireFleets.ReadAsync(_client, [Home, moon], ["HEAVY_CARGO"], Home.Id, moons: [moon.Id]);

        Assert.Equal([(PlanetId?)Home.Id], _client.FleetReads);
        Assert.Equal(100, fleets[0].Fleet.QuantityOf("HEAVY_CARGO"));
        Assert.Equal(7, fleets[1].Fleet.QuantityOf("HEAVY_CARGO"));
    }

    [Fact]
    public async Task A_moon_missing_from_the_empire_page_is_read_by_its_fleet_page()
    {
        var moon = new OwnPlanet(new PlanetId("m1"), "Moon", Home.Coordinates);
        _client.Fleets[moon.Id] = Fleet(moon, ("HEAVY_CARGO", 7, 25_000));
        // The page shows the planet at the same coordinates, but no moon column.
        _client.Empire = null;

        var fleets = await EmpireFleets.ReadAsync(_client, [Home, moon], ["HEAVY_CARGO"], Home.Id, moons: [moon.Id]);

        Assert.Contains((PlanetId?)moon.Id, _client.FleetReads);
        Assert.Equal(7, fleets[1].Fleet.QuantityOf("HEAVY_CARGO"));
    }

    [Fact]
    public async Task An_expired_session_is_not_hidden()
    {
        _client.Empire = () => throw new SessionExpiredException("login");

        await Assert.ThrowsAsync<SessionExpiredException>(() => EmpireFleets.ReadAsync(_client, [Home], ["HEAVY_CARGO"], Home.Id));
    }

    private static FleetSnapshot Fleet(OwnPlanet planet, params (string Type, long Count, long Capacity)[] ships) =>
        new(ships.Select(s => new ShipStack(s.Type, s.Count)).ToList(),
            planet.Coordinates,
            new FleetSlots(3, 11),
            ships.ToDictionary(s => s.Type, s => s.Capacity),
            Resources: new ResourceSet(1_000, 2_000, 3_000));
}
