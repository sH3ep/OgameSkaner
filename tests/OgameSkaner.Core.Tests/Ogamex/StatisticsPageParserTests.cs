using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class StatisticsPageParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Reads_rank_player_id_name_points_and_status_of_every_row()
    {
        var page = StatisticsPageParser.Parse(Fixture("statistics_defense.html"));

        Assert.Equal(
        [
            new RankingEntry(3299, "0a1b2c3d-0000-4000-8000-000000000001", "Admiral & Co", 5_054_976, PlayerStatus.Strong), // after an alliance tag
            new RankingEntry(3301, "0a1b2c3d-0000-4000-8000-000000000002", "Xyrior", 0, PlayerStatus.LongInactive),
            new RankingEntry(3302, "0a1b2c3d-0000-4000-8000-000000000003", "Kowalski", 0, PlayerStatus.None)
        ], page.Entries);
        Assert.Equal(34, page.PageCount);
    }

    [Fact]
    public void Content_that_isnt_a_ranking_means_the_session_expired()
    {
        Assert.Throws<SessionExpiredException>(() => StatisticsPageParser.Parse("<html><body>Login</body></html>"));
    }
}
