using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class GalaxyPageParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_returns_occupied_positions_with_moon_flags()
    {
        var rows = GalaxyPageParser.Parse(Fixture("galaxy_1_1.html"));

        Assert.Equal(
        [
            new GalaxyRow(1, "Admin", false),
            new GalaxyRow(2, "Bartolo", true),
            new GalaxyRow(4, "Tom & Jerry", true),
            new GalaxyRow(15, "LastOne", false)
        ], rows);
    }

    [Fact]
    public void Parse_reads_status_player_id_and_rank_from_the_player_tooltip()
    {
        var rows = GalaxyPageParser.Parse(Fixture("galaxy_statuses.html"));

        Assert.Equal(
        [
            new GalaxyRow(6, "Arthur Dayne", false, PlayerStatus.Vacation, "aaaaaaaa-0000-0000-0000-000000000006", 1204),
            new GalaxyRow(10, "MoE", true, PlayerStatus.Inactive, "aaaaaaaa-0000-0000-0000-000000000010", 909),
            new GalaxyRow(12, "Disruptor Hector & Co", false, PlayerStatus.Strong | PlayerStatus.LongInactive, "aaaaaaaa-0000-0000-0000-000000000012", 72),
            new GalaxyRow(13, "Priest Sandy", false, PlayerStatus.None, "aaaaaaaa-0000-0000-0000-000000000013", 2471)
        ], rows);
    }

    [Fact]
    public void Parse_ignores_expedition_position()
    {
        var rows = GalaxyPageParser.Parse(Fixture("galaxy_1_1.html"));

        Assert.DoesNotContain(rows, r => r.PlayerName == "ExpeditionShouldBeIgnored");
    }

    [Fact]
    public void Parse_returns_empty_list_for_system_without_players()
    {
        const string html = """
            <div class="galaxy-col col-planet-index">
            <span class="planet-index">1</span>
            </div>
            <div class="galaxy-col col-planet-index">
            <span class="planet-index">16</span>
            </div>
            """;

        Assert.Empty(GalaxyPageParser.Parse(html));
    }

    [Fact]
    public void Parse_keeps_last_row_when_page_ends_without_expedition_row()
    {
        const string html = """
            <div class="galaxy-col col-planet-index">
            <span class="planet-index">7</span>
            <span style='margin-top:6px;float:left;'>Solo</span>
            """;

        Assert.Equal([new GalaxyRow(7, "Solo", false)], GalaxyPageParser.Parse(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body><form id=\"login\"></form></body></html>")]
    public void Parse_throws_session_expired_for_non_galaxy_content(string? html)
    {
        Assert.Throws<SessionExpiredException>(() => GalaxyPageParser.Parse(html));
    }
}
