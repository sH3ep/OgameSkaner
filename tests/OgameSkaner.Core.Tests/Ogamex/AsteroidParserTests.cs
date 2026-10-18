using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class AsteroidParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 20, 0, 0, TimeSpan.Zero);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParseHints_reads_every_row_and_keeps_repeated_ones()
    {
        var hints = AsteroidParser.ParseHints(Fixture("asteroid_location.html"));

        Assert.Equal(12, hints.Count);
        Assert.Equal(new AsteroidHint(7, 1, 65), hints[0]);
        Assert.Equal(new AsteroidHint(7, 451, 499), hints[^1]);
        Assert.Equal(new AsteroidHint(7, 451, 499), hints[^2]);
    }

    [Fact]
    public void ParseHints_without_a_table_is_empty_and_a_login_page_means_the_session_expired()
    {
        Assert.Empty(AsteroidParser.ParseHints("<div class=\"galaxy-asteroid-modal\"><div class=\"asteroid-modal-desc\">No asteroids.</div></div>"));
        Assert.Throws<SessionExpiredException>(() => AsteroidParser.ParseHints("<html><body>Login</body></html>"));
    }

    [Fact]
    public void ParseGalaxy_reads_the_asteroid_and_its_time_left()
    {
        var asteroid = AsteroidParser.ParseGalaxy(Fixture("galaxy_asteroid.html"), new SolarSystemAddress(7, 16), Now);

        Assert.Equal(new Asteroid(new Coordinates(7, 16, 17), Now.AddSeconds(402), Now), asteroid);
    }

    [Fact]
    public void ParseGalaxy_without_an_asteroid_is_null()
    {
        Assert.Null(AsteroidParser.ParseGalaxy(Fixture("galaxy_no_asteroid.html"), new SolarSystemAddress(8, 260), Now));
        Assert.Null(AsteroidParser.ParseGalaxy(Fixture("galaxy_1_1.html"), new SolarSystemAddress(1, 1), Now));
    }

    [Fact]
    public void The_galaxy_parser_still_ignores_positions_16_and_17()
    {
        Assert.Equal([new GalaxyRow(8, "Neighbour", false)], GalaxyPageParser.Parse(Fixture("galaxy_asteroid.html")));
    }

    [Fact]
    public void ParseReports_reads_the_mining_reports_and_skips_other_messages()
    {
        var offset = TimeSpan.FromHours(2);

        var reports = AsteroidParser.ParseReports(Fixture("asteroid_reports.html"), offset);

        Assert.Equal(
        [
            new AsteroidReport("a2705dcd-17c4-4617-9787-c9f60bb40a25", new DateTimeOffset(2026, 10, 17, 21, 39, 33, offset), new Coordinates(7, 193, 17),
                AsteroidResultKind.Container, 0, ResourceSet.Zero, 0),
            new AsteroidReport("b1", new DateTimeOffset(2026, 10, 17, 21, 35, 50, offset), new Coordinates(7, 472, 17),
                AsteroidResultKind.Resources, 186_276_153_246 + 72_440_777_586, new ResourceSet(587_500, 0, 0), 0),
            new AsteroidReport("b2", new DateTimeOffset(2026, 10, 17, 21, 34, 49, offset), new Coordinates(7, 249, 17),
                AsteroidResultKind.Resources, 213_093_529_775 + 71_031_253_641, new ResourceSet(587_500, 0, 0), 0)
        ], reports);
    }

    [Fact]
    public void ParseReports_of_a_login_page_means_the_session_expired()
    {
        Assert.Throws<SessionExpiredException>(() => AsteroidParser.ParseReports("<html><body>Login</body></html>", TimeSpan.Zero));
    }
}
