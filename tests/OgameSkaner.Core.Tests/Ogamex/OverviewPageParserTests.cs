using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class OverviewPageParserTests
{
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "home_overview.html"));

    [Fact]
    public void Reads_the_planet_temperature() =>
        Assert.Equal((69, 74), OverviewPageParser.ParseTemperature(Fixture));

    [Fact]
    public void Reads_temperatures_below_zero_in_any_language() =>
        Assert.Equal((-48, -8), OverviewPageParser.ParseTemperature(Fixture.Replace("Approximately 69&#176;C to 74°C", "około −48°C do -8°C")));

    [Fact]
    public void A_page_without_a_temperature_is_an_error() =>
        Assert.Throws<OgamexException>(() => OverviewPageParser.ParseTemperature(Fixture.Replace("74°C", "74")));

    [Fact]
    public void A_login_page_means_the_session_expired() =>
        Assert.Throws<SessionExpiredException>(() => OverviewPageParser.ParseTemperature("<html>login</html>"));
}
