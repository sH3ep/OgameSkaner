using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services.Raiding;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Services;

public class RaidTargetFilterTests
{
    private static RaidTarget Target(int galaxy, int system, long? defense = null) =>
        new(Planet("P", galaxy, system, 5), null, RaidTargetState.NeedsSpy, 0, RankingDefensePoints: defense);

    [Fact]
    public void No_limits_shows_everything()
    {
        Assert.True(RaidTargetFilter.None.Matches(Target(9, 499)));
    }

    [Theory]
    [InlineData(2, 50, true)] // both lower ends
    [InlineData(3, 200, true)] // both upper ends
    [InlineData(1, 100, false)] // galaxy below
    [InlineData(4, 100, false)] // galaxy above
    [InlineData(2, 49, false)] // system below
    [InlineData(3, 201, false)] // system above
    public void Galaxy_and_system_ranges_include_their_ends(int galaxy, int system, bool shown)
    {
        var filter = new RaidTargetFilter(GalaxyFrom: 2, GalaxyTo: 3, SystemFrom: 50, SystemTo: 200);

        Assert.Equal(shown, filter.Matches(Target(galaxy, system)));
    }

    [Fact]
    public void A_defense_limit_keeps_players_at_or_below_it_and_hides_unknown_ones()
    {
        var onlySafe = new RaidTargetFilter(MaxRankingDefensePoints: 0);

        Assert.True(onlySafe.Matches(Target(1, 1, defense: 0)));
        Assert.False(onlySafe.Matches(Target(1, 1, defense: 3)));
        Assert.False(onlySafe.Matches(Target(1, 1, defense: null)));
        Assert.True(new RaidTargetFilter(MaxRankingDefensePoints: 5).Matches(Target(1, 1, defense: 5)));
    }
}
