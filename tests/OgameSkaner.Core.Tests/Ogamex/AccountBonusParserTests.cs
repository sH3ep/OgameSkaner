using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class AccountBonusParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Resource_settings_give_the_increases_as_shares_of_the_mine()
    {
        var settings = AccountBonusParser.ParseResourceSettings(Fixture("resource_settings.html"));

        Assert.Equal(2000, settings.EconomySpeed);
        AssertBonus(settings.Metal, research: 0.04, officers: 0.30, academy: 0.55, ranking: 5.0);
        AssertBonus(settings.Crystal, research: 0.04, officers: 0.30, academy: 0.55, ranking: 5.0);
        AssertBonus(settings.Deuterium, research: 0.04, officers: 0.30, academy: 0.55, ranking: 5.0);
        AssertBonus(settings.Energy, research: 0.16, officers: 0.06, academy: 0.08, ranking: 0);
        Assert.Equal(0.5, settings.ClassBonus, 3);
    }

    [Fact]
    public void Officers_give_levels_and_what_each_level_provides()
    {
        var officers = AccountBonusParser.ParseOfficers(Fixture("officers.html"));

        var construction = Assert.Single(officers, o => o.Name == "Construction expert");
        Assert.Equal(5, construction.Level);
        Assert.Equal(("CONSTRUCTION_EXPERT", 0.10), (construction.Id, construction.PercentPerLevel));
        Assert.Equal(6, Assert.Single(officers, o => o.Name == "Geologist").Level);
    }

    [Fact]
    public void Academy_gives_the_class_and_skill_levels()
    {
        var (className, skills) = AccountBonusParser.ParseAcademy(Fixture("academy.html"));

        Assert.Equal("Miner", className);
        var mastery = Assert.Single(skills, s => s.Name == "Construction mastery");
        Assert.Equal(("MINER_CONSTRUCTION_SPEED", 6, 0.05), (mastery.Id, mastery.Level, mastery.PercentPerLevel));
        Assert.Equal(5, Assert.Single(skills, s => s.Name == "Storage mastery").Level);
        Assert.Equal(0, Assert.Single(skills, s => s.Name == "Shipyard mastery").Level);
    }

    [Fact]
    public void Combined_bonuses_match_the_calibration_account()
    {
        var settings = AccountBonusParser.ParseResourceSettings(Fixture("resource_settings.html"));
        var officers = AccountBonusParser.ParseOfficers(Fixture("officers.html"));
        var (className, skills) = AccountBonusParser.ParseAcademy(Fixture("academy.html"));

        var bonuses = AccountBonusParser.Combine(settings, officers, className, skills, DateTimeOffset.Now);

        // Construction expert 5 × 10% + Construction mastery 6 × 5%; Technocrat 1 × 10%; Storage mastery 5 × 2%.
        Assert.Equal(0.8, bonuses.ConstructionSpeed, 3);
        Assert.Equal(0.1, bonuses.ShipyardSpeed, 3);
        Assert.Equal(0.1, bonuses.StorageIncrease, 3);
        Assert.Equal(AccountBonuses.Calibration.Signature, bonuses.Signature);
        Assert.Contains("Geologist 6", bonuses.Details);
    }

    [Fact]
    public void Another_page_means_the_session_expired()
    {
        Assert.Throws<SessionExpiredException>(() => AccountBonusParser.ParseResourceSettings("<html><body>Login</body></html>"));
        Assert.Throws<SessionExpiredException>(() => AccountBonusParser.ParseOfficers("<html><body>Login</body></html>"));
        Assert.Throws<SessionExpiredException>(() => AccountBonusParser.ParseAcademy("<html><body>Login</body></html>"));
    }

    private static void AssertBonus(ProductionBonus bonus, double research, double officers, double academy, double ranking)
    {
        Assert.Equal(research, bonus.Research, 3);
        Assert.Equal(officers, bonus.Officers, 3);
        Assert.Equal(academy, bonus.Academy, 3);
        Assert.Equal(0, bonus.Premium, 3);
        Assert.Equal(ranking, bonus.Ranking, 3);
    }
}
