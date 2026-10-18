using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Asteroids;

namespace OgameSkaner.Core.Tests.Services;

public class AsteroidPlanningTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    private static int _id;

    private static AsteroidReport Mined(DateTimeOffset time, long available, AsteroidResultKind kind = AsteroidResultKind.Resources) =>
        new($"r{++_id}", time, new Coordinates(7, 16, 17), kind, available, new ResourceSet(available / 2, available / 2, 0), 0);

    [Fact]
    public void Without_a_report_that_shows_resources_there_is_no_estimate()
    {
        Assert.Null(AsteroidEstimator.Estimate([], Midnight));
        Assert.Null(AsteroidEstimator.Estimate([Mined(Midnight.AddHours(1), 0, AsteroidResultKind.DarkMatter)], Midnight.AddHours(2)));
    }

    [Fact]
    public void A_new_day_starts_from_the_first_report_of_the_previous_day()
    {
        var yesterday = Midnight.AddDays(-1);
        var reports = new[] { Mined(yesterday.AddHours(1), 1_000_000), Mined(yesterday.AddHours(2), 980_000), Mined(yesterday.AddHours(3), 960_000) };

        var estimate = AsteroidEstimator.Estimate(reports, Midnight.AddHours(5))!;

        Assert.Equal(1_000_000, estimate.Resources);
        Assert.Equal(0, estimate.Successes);
        Assert.Same(reports[0], estimate.Basis);
    }

    [Fact]
    public void During_the_day_the_newest_report_is_scaled_by_the_daily_decline()
    {
        // Two successes today (one without resources): the newest report was taken at 98%, the next mining gets 96%.
        var reports = new[]
        {
            Mined(Midnight.AddDays(-1).AddHours(1), 2_000_000),
            Mined(Midnight.AddHours(1), 0, AsteroidResultKind.Container),
            Mined(Midnight.AddHours(2), 1_470_000),
            Mined(Midnight.AddHours(3), 0, AsteroidResultKind.Nothing)
        };

        var estimate = AsteroidEstimator.Estimate(reports, Midnight.AddHours(4))!;

        Assert.Equal(2, estimate.Successes);
        Assert.Equal(0.96, estimate.Multiplier, 6);
        Assert.Equal(1_440_000, estimate.Resources); // 1,470,000 / 0.98 × 0.96
    }

    [Fact]
    public void The_decline_stops_at_40_percent()
    {
        Assert.Equal(0.4, AsteroidEstimator.Multiplier(30), 6);
        Assert.Equal(0.4, AsteroidEstimator.Multiplier(100), 6);
        Assert.Equal(0.42, AsteroidEstimator.Multiplier(29), 6);
    }

    [Fact]
    public void Miners_arriving_after_midnight_UTC_get_the_full_gains_again()
    {
        var reports = Enumerable.Range(1, 10).Select(i => Mined(Midnight.AddHours(i), 1_000_000 - 20_000 * (i - 1))).ToList();

        var tonight = AsteroidEstimator.Estimate(reports, Midnight.AddHours(23))!;
        var tomorrow = AsteroidEstimator.Estimate(reports, Midnight.AddDays(1).AddMinutes(5))!;

        Assert.Equal(800_000, tonight.Resources); // 10 successes: 80%
        Assert.Equal(1_000_000, tomorrow.Resources);
    }

    [Fact]
    public void Miners_cover_the_estimate_plus_the_margin()
    {
        var settings = AsteroidSettings.Default with { MarginPercent = 20, MinMiners = 10 };

        Assert.Equal(103, AsteroidPlanner.MinersNeeded(1_000_000, 11_750, settings)); // 1.2M / 11,750 = 102.1
        Assert.Equal(10, AsteroidPlanner.MinersNeeded(1_000, 11_750, settings));
        Assert.Equal(1_000_000, AsteroidPlanner.MinersNeeded(null, 11_750, settings));
        Assert.Equal(1_000_000, AsteroidPlanner.MinersNeeded(1_000_000, 0, settings)); // capacity unknown
    }

    [Fact]
    public void Every_hint_row_needs_its_own_asteroid()
    {
        AsteroidHint[] hints = [new(7, 1, 65), new(7, 11, 75), new(7, 451, 499), new(7, 451, 499)];

        var searches = AsteroidPlanner.Uncovered(hints, [new Coordinates(7, 16, 17), new Coordinates(7, 460, 17)]);

        // Each row stands for its own asteroid: 7:16 serves 1–65 only, and 451–499 is listed twice.
        Assert.Equal([new AsteroidHint(7, 11, 75), new AsteroidHint(7, 451, 499)], searches);
    }

    [Fact]
    public void Miners_must_arrive_before_the_asteroid_is_gone()
    {
        var asteroid = new Asteroid(new Coordinates(7, 16, 17), Midnight.AddMinutes(30), Midnight);
        var settings = AsteroidSettings.Default with { ArrivalSafetySeconds = 60 };

        Assert.True(AsteroidPlanner.ArrivesInTime(asteroid, TimeSpan.FromMinutes(29), Midnight, settings));
        Assert.False(AsteroidPlanner.ArrivesInTime(asteroid, TimeSpan.FromMinutes(29.5), Midnight, settings));
    }
}
