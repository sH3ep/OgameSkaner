using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Tests.Services;

public class TargetStatisticsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly RaidSettings Settings = RaidSettings.Default with { MinLoot = 1_000_000, LootPercent = 50, LowLootRespyHours = 5 };
    private static readonly Coordinates A = new(1, 1, 1);
    private static readonly Coordinates B = new(1, 1, 2);

    [Fact]
    public void The_refill_adds_back_what_our_attacks_took_between_two_reports()
    {
        // 4M seen, our attack took 2M, 10 h later 3M: it refilled 1M, 100k per hour.
        IReadOnlyList<EspionageReport> reports = [Report(A, -20, 4_000_000), Report(A, -10, 3_000_000)];
        var stats = TargetStatistics.Compute(reports, [Attack(A, -15, 2_000_000)], Now, Settings)[A];

        Assert.Equal(100_000, stats.RefillPerHour);
        Assert.Equal(2_400_000, stats.Score); // a day of refill
        Assert.Equal(3_500_000, stats.MedianResources);
        Assert.Equal((2, 1, 2_000_000L), (stats.Reports, stats.Attacks, stats.LootTaken));
    }

    [Fact]
    public void One_report_scores_its_stock_and_reports_too_close_or_too_old_do_not_count()
    {
        var stats = TargetStatistics.Compute(
            [Report(A, -24 * 15, 9_000_000), Report(A, -3, 500_000), Report(A, -2.5, 900_000)], [], Now, Settings)[A];

        // The 15-day-old report is outside the window; the last two are 30 min apart: no refill measured.
        Assert.Null(stats.RefillPerHour);
        Assert.Equal(2, stats.Reports);
        Assert.Equal(700_000, stats.Score);
    }

    [Fact]
    public void The_top_share_is_the_best_scores_without_banned_planets()
    {
        var targets = Enumerable.Range(1, 10).Select(i => new Coordinates(2, 2, i)).ToList();
        var reports = targets.Select((t, i) => Report(t, -1, (i + 1) * 1_000_000L)).ToList();
        var stats = TargetStatistics.Compute(reports, [], Now, Settings);

        Assert.Equal([targets[9], targets[8]], TargetStatistics.Top(stats.Values, [], Now, 20).OrderByDescending(t => t.Position));

        // The richest is banned: the next two take its place.
        var banned = new TargetBan(targets[9], Now.AddDays(-1), Now.AddDays(2), 1, "test");
        Assert.Equal([targets[8], targets[7]], TargetStatistics.Top(stats.Values, [banned], Now, 20).OrderByDescending(t => t.Position));
        Assert.Empty(TargetStatistics.Top(stats.Values, [], Now, 0));
    }

    [Fact]
    public void Three_poor_reports_ban_a_planet_for_three_days_then_one_more_bans_it_for_seven()
    {
        // Loot is half the stock: 1.2M stock = 600k loot, below 1M; refill 100k/h × 5 h × 50% = 250k, too slow.
        IReadOnlyList<EspionageReport> poor = [Report(A, -30, 1_000_000), Report(A, -20, 1_200_000), Report(A, -10, 1_100_000)];

        var ban = Assert.Single(TargetStatistics.NewBans(poor, [], [], Now, Settings));
        Assert.Equal((A, 1, Now.AddDays(3)), (ban.Target, ban.Nth, ban.Until));

        // Banned: nothing new.
        Assert.Empty(TargetStatistics.NewBans(poor, [], [ban], Now.AddDays(1), Settings));

        // After the ban one poor report renews it, for 7 days; the reports before the ban don't count again.
        var later = Now.AddDays(3).AddHours(1);
        Assert.Empty(TargetStatistics.NewBans(poor, [], [ban], later, Settings));
        var second = Assert.Single(TargetStatistics.NewBans([.. poor, Report(A, 24 * 3 + 0.5, 900_000)], [], [ban], later, Settings));
        Assert.Equal((2, later.AddDays(7)), (second.Nth, second.Until));
    }

    [Fact]
    public void A_rich_report_too_few_reports_or_a_fast_refill_keep_a_planet_unbanned()
    {
        Assert.Empty(TargetStatistics.NewBans([Report(A, -30, 1_000_000), Report(A, -20, 1_000_000)], [], [], Now, Settings));
        Assert.Empty(TargetStatistics.NewBans([Report(A, -30, 1_000_000), Report(A, -20, 3_000_000), Report(A, -10, 1_000_000)], [], [], Now, Settings));

        // Each attack takes what's there, but 1M per hour comes back: worth spying.
        IReadOnlyList<EspionageReport> refilled = [Report(B, -3, 1_000_000), Report(B, -2, 1_000_000), Report(B, -1, 1_000_000)];
        Assert.Empty(TargetStatistics.NewBans(refilled, [Attack(B, -2.5, 10_000_000), Attack(B, -1.5, 10_000_000)], [], Now, Settings));
        Assert.Single(TargetStatistics.NewBans(refilled, [], [], Now, Settings)); // without the attacks it doesn't refill

        Assert.Empty(TargetStatistics.NewBans(refilled, [], [], Now, Settings with { LowResourceMinReports = 0 })); // off
    }

    [Fact]
    public void Farming_event_reports_and_refills_across_an_event_are_left_out()
    {
        // Event from -15 h to -12 h: the boosted report and the gain across it would read as stock and refill.
        var settings = Settings with { FarmingEvents = [new FarmingEventPeriod(Now.AddHours(-15), Now.AddHours(-12))] };
        IReadOnlyList<EspionageReport> reports =
            [Report(A, -30, 1_000_000), Report(A, -20, 2_000_000), Report(A, -14, 90_000_000), Report(A, -10, 30_000_000)];

        var stats = TargetStatistics.Compute(reports, [], Now, settings)[A];

        Assert.Equal(3, stats.Reports);
        Assert.Equal(100_000, stats.RefillPerHour); // -30 → -20 only
        Assert.Equal(Now.AddHours(-10), stats.HighValue?.Since); // not the event's 90M

        // Poor reports during the event don't ban.
        var eventNow = Settings with { FarmingEvents = [new FarmingEventPeriod(Now.AddHours(-40))] };
        Assert.Empty(TargetStatistics.NewBans([Report(B, -30, 1_000_000), Report(B, -20, 1_200_000), Report(B, -10, 1_100_000)], [], [], Now, eventNow));
    }

    [Fact]
    public void A_big_report_marks_a_planet_high_value_until_poor_reports_in_a_row_follow()
    {
        // Loot is half the stock: 12M stock = 6M loot, at least 5 × 1M.
        var big = Report(A, -40, 12_000_000);
        Assert.Null(TargetStatistics.HighValue([Report(A, -50, 8_000_000)], Settings)); // 4M loot: not big

        var mark = TargetStatistics.HighValue([Report(A, -50, 1_000_000), big], Settings)!;
        Assert.Equal((big.Time, 6_000_000L, 0), (mark.Since, mark.Loot, mark.PoorSince));

        // After our attack it's lower but still worth it, then one poor report: still marked.
        Assert.Equal(1, TargetStatistics.HighValue([big, Report(A, -30, 3_000_000), Report(A, -20, 1_000_000)], Settings)!.PoorSince);
        // Two poor ones in a row: it was a one-time stock.
        Assert.Null(TargetStatistics.HighValue([big, Report(A, -30, 1_000_000), Report(A, -20, 1_000_000)], Settings));
        // A new big report marks it again.
        Assert.NotNull(TargetStatistics.HighValue([big, Report(A, -30, 1_000_000), Report(A, -20, 1_000_000), Report(A, -10, 20_000_000)], Settings));

        Assert.Null(TargetStatistics.HighValue([big], Settings with { HighValueLootFactor = 0 })); // off
        Assert.NotNull(TargetStatistics.Compute([big], [], Now, Settings)[A].HighValue);
    }

    private static EspionageReport Report(Coordinates target, double hours, long resources) =>
        new($"{target}@{hours}", target, PlanetType.Planet, Now.AddHours(hours), "Kowalski", "p", PlayerStatus.Inactive, new ResourceSet(resources, 0, 0), 0);

    private static RaidRecord Attack(Coordinates target, double hours, long loot) =>
        new(target, PlanetType.Planet, "Kowalski", MissionType.Attack, new Coordinates(5, 5, 5), "HEAVY_CARGO", 1, loot,
            Now.AddHours(hours - 1), Now.AddHours(hours), Now.AddHours(hours + 1));
}
