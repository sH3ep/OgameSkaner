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
    public void Parse_reads_fleet_slots_cargo_capacity_and_server_clock()
    {
        const string html = """
            <html><body>
            <div class="fleet-header">No fleet movement<br>16.10.2026 20:43:23</div>
            <span class="info-item info_item_active_fleets  tooltip" data-tooltip-content="Fleets">Fleets: 3/11</span>
            <span class="info-item info_item_active_expeditions  tooltip">Expeditions: 0/1</span>
            <div id="fleet1_content_container">
              <div class="ship-item tooltip" data-tooltip-position="top" data-tooltip-content="<div class='player-level-info-tooltip'><table style='max-width:320px;'><tr><td colspan='2'>Colony Ship (2)</td></tr><tr><td>Speed: </td><td><span>9.000</span></td></tr><tr><td>Cargo capacity: </td><td><span>200.000</span></td></tr><tr><td>Total cargo capacity: </td><td><span>400.000</span></td></tr></table></div>">
                <a href="#" class="ship-btn" data-ship-type="COLONY_SHIP" data-ship-quantity="2" data-ship-points="40"></a>
              </div>
            </div>
            <input id="fleet2_target_x" value="8" /><input id="fleet2_target_y" value="260" /><input id="fleet2_target_z" value="3" />
            </body></html>
            """;

        var fleet = FleetPageParser.Parse(html);

        Assert.Equal(new FleetSlots(3, 11), fleet.Slots);
        Assert.Equal(8, fleet.Slots!.Value.Free);
        Assert.Equal(200_000, fleet.CargoCapacityOf("COLONY_SHIP"));
        Assert.Equal(2, fleet.QuantityOf("COLONY_SHIP"));
        Assert.Equal(new DateTime(2026, 10, 16, 20, 43, 23), fleet.ServerClock);
    }

    [Fact]
    public void Parse_leaves_slots_and_capacities_empty_when_the_page_has_none()
    {
        var fleet = FleetPageParser.Parse(FleetPage);

        Assert.Null(fleet.Slots);
        Assert.Equal(0, fleet.CargoCapacityOf("SMALL_CARGO"));
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
