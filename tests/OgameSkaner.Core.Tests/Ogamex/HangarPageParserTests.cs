using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

/// <summary>Fixtures are real OgameX shipyard pages (2026-10-16) with the player and planet ids replaced.</summary>
public class HangarPageParserTests
{
    private static ShipyardState Load(string name) =>
        HangarPageParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void Reads_satellites_on_a_planet_with_a_shipyard()
    {
        var state = Load("hangar.html");

        Assert.Equal(new ShipyardState(
            new PlanetId("11111111-1111-1111-1111-111111111111"), 300, 55.97, new ResourceSet(0, 2_000, 500), SatellitesLocked: false, new TimeSpan(0, 0, 6)), state);
    }

    [Fact]
    public void Without_a_shipyard_satellites_are_locked()
    {
        var state = Load("hangar_locked.html");

        Assert.Equal(new PlanetId("33333333-3333-3333-3333-333333333333"), state.Planet);
        Assert.True(state.SatellitesLocked);
        Assert.Equal(0, state.Satellites);
        Assert.Equal(54.67, state.EnergyPerSatellite);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>login</body></html>")]
    public void Throws_session_expired_for_anything_else(string html) =>
        Assert.Throws<SessionExpiredException>(() => HangarPageParser.Parse(html));
}
