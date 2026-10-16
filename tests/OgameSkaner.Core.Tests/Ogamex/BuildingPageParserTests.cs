using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

/// <summary>Fixtures are real OgameX pages (2026-10-16) with the player and planet ids replaced.</summary>
public class BuildingPageParserTests
{
    private static readonly PlanetId PlanetId = new("11111111-1111-1111-1111-111111111111");

    private static BuildingPage Load(string name) =>
        BuildingPageParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void Reads_the_planet_list_and_current_planet()
    {
        var page = Load("building_resource.html");

        Assert.Equal(PlanetId, page.CurrentPlanet);
        Assert.Equal([new OwnPlanet(PlanetId, "Home planet", new Coordinates(8, 260, 3))], page.Planets);
    }

    [Fact]
    public void Reads_exact_resources_and_energy_from_the_page_script()
    {
        var page = Load("building_resource.html");

        Assert.Equal(new ResourceSet(35_756_870, 21_815_592, 6_526_949), page.Resources);
        Assert.Equal(13_594.76, page.EnergyProduction, precision: 2);
        Assert.Equal(4_942.17, page.EnergyConsumption, precision: 2);
    }

    [Fact]
    public void Reads_storage_capacity_free_fields_and_vacation_mode()
    {
        var resources = Load("building_resource.html");
        var facilities = Load("building_facility.html");

        Assert.Equal(new ResourceSet(45_125_000, 35_906_250, 28_375_000), resources.StorageCapacity);
        Assert.Equal(246, resources.FreeFields);
        Assert.Null(facilities.FreeFields); // only the resources page has it
        Assert.False(resources.VacationMode);
        Assert.True(BuildingPageParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "building_resource.html"))
            .Replace("window.isVacation = false;", "window.isVacation = true;")).VacationMode);
    }

    [Fact]
    public void Planet_list_skips_moons()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "building_resource.html"))
            .Replace("<div id=\"other-planets\">", """
                <div id="other-planets">
                  <div class="planet-item"><a class="planet-select moon-select" href="?planet=33333333-3333-3333-3333-333333333333">
                    <span class="planet-name">Moon</span><span class="planet-coords">[8:260:3]</span></a></div>
                """);

        Assert.Equal([PlanetId], BuildingPageParser.Parse(html).Planets.Select(p => p.Id));
    }

    [Fact]
    public void Reads_resource_buildings_with_current_level_and_next_cost_after_the_queue()
    {
        var page = Load("building_resource.html");

        Assert.Equal(
        [
            new BuildingOnPage(BuildingType.MetalMine, 24, new ResourceSet(1_943_267, 485_817, 0)),
            new BuildingOnPage(BuildingType.CrystalMine, 23, new ResourceSet(1_064_802, 532_406, 0)),
            new BuildingOnPage(BuildingType.DeuteriumSynthesizer, 10, new ResourceSet(82_251, 24_192, 0)),
            new BuildingOnPage(BuildingType.SolarPlant, 7, new ResourceSet(2_563, 1_025, 0)),
            new BuildingOnPage(BuildingType.FusionReactor, 0, new ResourceSet(900, 360, 180)),
            new BuildingOnPage(BuildingType.MetalStorage, 5, new ResourceSet(151_875, 0, 0)),
            new BuildingOnPage(BuildingType.CrystalStorage, 4, new ResourceSet(101_250, 50_625, 0)),
            new BuildingOnPage(BuildingType.DeuteriumTank, 3, new ResourceSet(67_500, 67_500, 0))
        ], page.Buildings);
    }

    [Fact]
    public void Reads_the_queue_with_the_running_item_first()
    {
        var page = Load("building_resource.html");

        Assert.Equal(
        [
            new QueuedBuilding(BuildingType.MetalMine, 25, Started: true),
            new QueuedBuilding(BuildingType.CrystalMine, 24, Started: false),
            new QueuedBuilding(BuildingType.DeuteriumSynthesizer, 11, Started: false),
            new QueuedBuilding(BuildingType.MetalMine, 26, Started: false),
            new QueuedBuilding(BuildingType.CrystalMine, 25, Started: false),
            new QueuedBuilding(BuildingType.DeuteriumSynthesizer, 12, Started: false),
            new QueuedBuilding(BuildingType.SolarPlant, 8, Started: false),
            new QueuedBuilding(BuildingType.DeuteriumSynthesizer, 13, Started: false),
            new QueuedBuilding(BuildingType.DeuteriumSynthesizer, 14, Started: false)
        ], page.Queue);
    }

    [Fact]
    public void Empty_queue_page_has_no_queue_and_the_plain_next_level_costs()
    {
        var page = Load("building_resource_empty_queue.html");

        Assert.Empty(page.Queue);
        Assert.Equal(new ResourceSet(911_647, 227_912, 0), page.Buildings.Single(b => b.Building == BuildingType.MetalMine).NextLevelCost);
        Assert.Equal(new ResourceSet(28_375_000, 16_000_000, 6_073_469), page.Resources);
    }

    [Fact]
    public void Reads_facilities_and_marks_locked_ones()
    {
        var page = Load("building_facility.html");

        Assert.Equal(
        [
            new BuildingOnPage(BuildingType.RoboticsFactory, 10, new ResourceSet(409_600, 122_880, 204_800)),
            new BuildingOnPage(BuildingType.NaniteFactory, 0, null),
            new BuildingOnPage(BuildingType.Shipyard, 11, new ResourceSet(819_200, 409_600, 204_800)),
            new BuildingOnPage(BuildingType.ResearchLab, 8, new ResourceSet(51_200, 102_400, 51_200)),
            new BuildingOnPage(BuildingType.University, 0, null),
            new BuildingOnPage(BuildingType.Terraformer, 0, null),
            new BuildingOnPage(BuildingType.MissileSilo, 0, new ResourceSet(20_000, 20_000, 1_000))
        ], page.Buildings);
        Assert.True(page.Buildings[1].Locked);
    }

    [Fact]
    public void Facilities_page_shows_the_same_shared_queue()
    {
        Assert.Equal(Load("building_resource.html").Queue.Select(q => (q.Building, q.Level)), Load("building_facility.html").Queue.Select(q => (q.Building, q.Level)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body><form action='/home/login'><input name='Email' /></form></body></html>")]
    [InlineData("{\"IsSuccess\":false}")]
    public void Throws_session_expired_for_anything_else(string html) =>
        Assert.Throws<SessionExpiredException>(() => BuildingPageParser.Parse(html));
}
