using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class DebrisParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_reads_each_debris_field_by_its_resource_images()
    {
        // Position 9 is markup seen on genesis; position 16 puts the same tooltip next to the own-fleet icon.
        var debris = DebrisParser.Parse(Fixture("galaxy_debris.html"));

        Assert.Equal(2, debris.Count);
        Assert.Equal(new ResourceSet(0, 4_956_800, 0), debris[9]);
        Assert.Equal(new ResourceSet(1_250_000, 830_400, 0), debris[16]);
    }

    [Fact]
    public void Parse_ignores_empty_columns_and_the_own_fleet_icon()
    {
        Assert.Empty(DebrisParser.Parse(Fixture("galaxy_asteroid.html")));
        Assert.Empty(DebrisParser.Parse(Fixture("galaxy_statuses.html")));
        Assert.Empty(DebrisParser.Parse("""
            <div class="galaxy-item"><div class="galaxy-col col-planet-index header-col"><span class="planet-index">16</span></div>
            <div class="galaxy-col col-debris"><div class="fleetActionIcon fleetActionFriendly"></div></div></div>
            """));
    }
}
