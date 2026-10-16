using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class FleetPageParserTests
{
    private const string FleetPage = """
        <html><body>
        <div id="fleet1_content_container">
          <div class="ship-item tooltip">
            <a href="#" data-ship-type="SMALL_CARGO" data-ship-quantity="120"></a>
          </div>
          <div class="ship-item tooltip">
            <a href="#" data-ship-type="ESPIONAGE_PROBE" data-ship-quantity="1.500"></a>
          </div>
          <div class="ship-item tooltip">
            <a href="#" data-ship-type="DEATHSTAR" data-ship-quantity="0"></a>
          </div>
          <a href="/help">Help</a>
        </div>
        <input id="fleet2_target_x" value="3" />
        <input id="fleet2_target_y" value="142" />
        <input id="fleet2_target_z" value="8" />
        </body></html>
        """;

    [Fact]
    public void Parse_reads_available_ships_and_current_planet()
    {
        var fleet = FleetPageParser.Parse(FleetPage);

        Assert.Equal([new ShipStack("SMALL_CARGO", 120), new ShipStack("ESPIONAGE_PROBE", 1500)], fleet.Ships);
        Assert.Equal(new Coordinates(3, 142, 8), fleet.CurrentPlanet);
    }

    [Fact]
    public void Parse_throws_session_expired_when_container_is_missing()
    {
        Assert.Throws<SessionExpiredException>(() => FleetPageParser.Parse("<html><body>Login</body></html>"));
    }

    [Fact]
    public void Parse_throws_when_coordinates_are_missing()
    {
        var html = FleetPage.Replace("""<input id="fleet2_target_z" value="8" />""", string.Empty);

        Assert.Throws<OgamexException>(() => FleetPageParser.Parse(html));
    }
}
