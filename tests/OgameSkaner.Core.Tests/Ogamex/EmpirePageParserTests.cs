using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

/// <summary>
/// The fixture is the real OgameX empire page (genesis, 2026-10-21), cut down to the "Sum" column and the first two planets
/// and moons. The page has no player or planet ids.
/// </summary>
public class EmpirePageParserTests
{
    private static EmpirePage Load() =>
        EmpirePageParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "empire.html")));

    [Fact]
    public void Reads_the_planets_and_skips_the_sum_column()
    {
        var page = Load();

        Assert.Equal([new Coordinates(8, 260, 3), new Coordinates(8, 260, 4)], page.Planets.Select(p => p.Coordinates));
        var home = page.Planets[0];
        Assert.Equal("Home planet", home.Name);
        Assert.Equal((450, 480), (home.UsedFields, home.MaxFields));
        Assert.Equal((74, 75), (home.MinTemperature, home.MaxTemperature));
        Assert.Equal(670_458_644, home.Energy);
        Assert.Null(home.Diameter);
    }

    [Fact]
    public void Reads_stock_production_and_storage()
    {
        var home = Load().Planets[0];

        Assert.Equal(new ResourceSet(1_996_852_382_134, 2_066_757_426_948, 767_067_493_030), home.Resources);
        Assert.Equal(new ResourceSet(93_631_212_803, 57_079_459_189, 23_107_321_373), home.ProductionPerHour);
        Assert.Equal(new ResourceSet(65_017_372_347_127, 27_520_717_254_339, 11_619_987_756_276), home.StorageCapacity);
    }

    [Fact]
    public void Reads_building_levels()
    {
        var colony = Load().Planets[1];

        Assert.Equal(63, colony.LevelOf(BuildingType.MetalMine));
        Assert.Equal(62, colony.LevelOf(BuildingType.CrystalMine));
        Assert.Equal(58, colony.LevelOf(BuildingType.DeuteriumSynthesizer));
        Assert.Equal(16, colony.LevelOf(BuildingType.SolarPlant));
        Assert.Equal(30, colony.LevelOf(BuildingType.DeuteriumTank));
        Assert.Equal(23, colony.LevelOf(BuildingType.RoboticsFactory));
        Assert.Equal(18, colony.LevelOf(BuildingType.NaniteFactory));
        Assert.Equal(16, colony.LevelOf(BuildingType.Terraformer));
        Assert.Equal(0, colony.LevelOf(BuildingType.MissileSilo));
        Assert.Equal(15, colony.Buildings.Count);
    }

    [Fact]
    public void Reads_ships_and_defenses_by_unit_id()
    {
        var colony = Load().Planets[1];

        Assert.Equal(10_065_794, colony.ShipCount("SOLAR_SATELLITE"));
        Assert.Equal(20_000_000, colony.ShipCount("LIGHT_CARGO"));
        Assert.Equal(4_352_053, colony.ShipCount("HEAVY_CARGO"));
        Assert.Equal(18, colony.ShipCount("COLONY_SHIP"));
        Assert.Equal(0, colony.ShipCount("ASTEROID_MINER"));
        Assert.Equal(20, colony.Ships.Count);
        Assert.Equal(350_150_000, colony.Defenses["LIGHT_LASER_TURRET"]);
        Assert.Equal(253, colony.Defenses["ATMOSPHERIC_SHIELD"]);
        Assert.Equal(0, colony.Defenses["INTERPLANETARY_MISSILE"]);
    }

    [Fact]
    public void Reads_the_researches_once()
    {
        var researches = Load().Researches;

        Assert.Equal(16, researches.Count);
        Assert.Equal(23, researches["ENERGY_TECHNOLOGY"]);
        Assert.Equal(40, researches["ASTROPHYSICS"]);
        Assert.Equal(8, researches["GRAVITON_RESEARCH"]);
    }

    [Fact]
    public void Reads_the_moons_with_their_buildings()
    {
        var page = Load();

        var moon = Assert.Single(page.Moons, m => m.Coordinates == new Coordinates(8, 260, 4));
        Assert.Equal(moon, page.MoonAt(new Coordinates(8, 260, 4)));
        Assert.Equal((50, 58), (moon.UsedFields, moon.MaxFields));
        Assert.Equal(4_000, moon.Diameter);
        Assert.Null(moon.Energy);
        Assert.Equal((68, 74), (moon.MinTemperature, moon.MaxTemperature));
        Assert.Equal(ResourceSet.Zero, moon.Resources);
        Assert.Equal(19, moon.LevelOf(BuildingType.MoonBase));
        Assert.Equal(20, moon.LevelOf(BuildingType.RoboticsFactory));
        Assert.Equal(10, moon.LevelOf(BuildingType.NaniteFactory));
        Assert.Equal(1, moon.LevelOf(BuildingType.JumpGate));
        Assert.Equal(0, moon.ShipCount("LIGHT_CARGO"));
        Assert.Equal(2, page.Moons.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>login</body></html>")]
    public void Throws_session_expired_for_anything_else(string html) =>
        Assert.Throws<SessionExpiredException>(() => EmpirePageParser.Parse(html));
}
