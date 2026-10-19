using OgameSkaner.Core.Domain.Asteroids;

namespace OgameSkaner.Core.Services.Asteroids;

/// <param name="Resources">Resources the asteroid is expected to hold.</param>
/// <param name="Successes">Successful minings on the day the miners arrive, before them.</param>
/// <param name="Basis">The report the estimate scales (the one with the biggest full gains among those considered).</param>
public sealed record AsteroidEstimate(long Resources, int Successes, double Multiplier, AsteroidReport Basis);

/// <summary>
/// Estimates an asteroid's resources from the mining reports. The game lowers the gains by 2% per successful mining of
/// the day, down to 40%, and resets the count at 00:00 UTC (the wiki); otherwise the amount follows the account's
/// production, so a recent report scaled by that factor is the estimate. Only reports with resources (metal, crystal,
/// deuterium) count:
/// <list type="bullet">
/// <item>with such reports on the arrival day, the newest <see cref="RecentReports"/> of them (fewer when there are fewer),
/// each worked back to the day's full gains, and the biggest of those;</item>
/// <item>otherwise the first report of the latest earlier day (taken at the day's full gains, like the first mining of a new day).</item>
/// </list>
/// Missing an asteroid's resources costs more than a few extra miners, hence the biggest rather than the newest.
/// </summary>
public static class AsteroidEstimator
{
    public const double DailyDecline = 0.02;
    public const double Floor = 0.4;

    /// <summary>The arrival day's newest reports with resources the estimate takes the biggest of.</summary>
    public const int RecentReports = 4;

    /// <summary>Share of the full gains after <paramref name="successes"/> successful minings of the day.</summary>
    public static double Multiplier(int successes) => Math.Max(Floor, 1 - DailyDecline * successes);

    /// <summary>A mining counts for the daily decline when it found something.</summary>
    public static bool IsSuccess(AsteroidReport report) => report.Kind != AsteroidResultKind.Nothing;

    /// <summary>A report that shows the resources the asteroid held (not dark matter, a container or nothing).</summary>
    public static bool HasResources(AsteroidReport report) => report.Kind == AsteroidResultKind.Resources && report.Available > 0;

    public static DateTime Day(DateTimeOffset time) => time.UtcDateTime.Date;

    /// <summary>The estimate for miners arriving at <paramref name="arrival"/>; null without a report that shows the resources.</summary>
    public static AsteroidEstimate? Estimate(IReadOnlyList<AsteroidReport> reports, DateTimeOffset arrival)
    {
        var ordered = reports.Where(r => r.Time <= arrival).OrderBy(r => r.Time).ToList();
        var day = Day(arrival);
        var successes = ordered.Count(r => Day(r.Time) == day && IsSuccess(r));

        var withResources = ordered.Where(HasResources).ToList();
        var recent = withResources.Where(r => Day(r.Time) == day).TakeLast(RecentReports).ToList();
        if (recent.Count == 0 && withResources.Where(r => Day(r.Time) < day).GroupBy(r => Day(r.Time)).MaxBy(g => g.Key)?.First() is { } first)
            recent.Add(first);
        if (recent.Count == 0)
            return null;

        var (basis, full) = recent.Select(r => (Report: r, Full: r.Available / Multiplier(SuccessesBefore(ordered, r)))).MaxBy(b => b.Full);
        var multiplier = Multiplier(successes);
        return new AsteroidEstimate((long)Math.Round(full * multiplier), successes, multiplier, basis);
    }

    /// <summary>Successful minings of the report's day before it.</summary>
    public static int SuccessesBefore(IReadOnlyList<AsteroidReport> reports, AsteroidReport report) =>
        reports.Count(r => Day(r.Time) == Day(report.Time) && r.Time < report.Time && IsSuccess(r));
}
