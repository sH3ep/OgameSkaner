using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Services.Raiding;

/// <summary>What the spy reports of the last <see cref="RaidSettings.StatsDays"/> say about one planet.</summary>
/// <param name="Reports">Reports in the window.</param>
/// <param name="MedianResources">The median of the reports' total resources.</param>
/// <param name="RefillPerHour">
/// The median gain per hour between two consecutive reports (at least <see cref="TargetStatistics.MinRefillGap"/> apart),
/// with the expected loot of our attacks landing between them added back; null with fewer than two such reports.
/// </param>
/// <param name="Attacks">Our attacks that landed in the window.</param>
/// <param name="LootTaken">Their expected loot (the combat reports aren't read).</param>
/// <param name="HighValue">The planet showed a big stock lately (<see cref="TargetStatistics.HighValue"/>); null when not.</param>
public sealed record TargetStats(
    Coordinates Target,
    string? PlayerName,
    int Reports,
    DateTimeOffset LastReport,
    long LastResources,
    long MedianResources,
    long? RefillPerHour,
    int Attacks,
    long LootTaken,
    HighValueMark? HighValue = null)
{
    /// <summary>
    /// How rich the planet is, in resources: a day of its refill when known, else the median stock seen (one report
    /// shows roughly what piled up, so it stands in until a second one).
    /// </summary>
    public long Score => RefillPerHour is { } perHour ? perHour * 24 : MedianResources;
}

/// <summary>A planet whose report showed a big stock: spied on more often until poor reports follow.</summary>
/// <param name="Since">The newest big report.</param>
/// <param name="Loot">Its expected loot.</param>
/// <param name="PoorSince">Reports after it with loot below <see cref="RaidSettings.MinLoot"/> (fewer than <see cref="RaidSettings.HighValueDropReports"/>).</param>
public sealed record HighValueMark(DateTimeOffset Since, long Loot, int PoorSince);

/// <summary>The statistics, the top targets and the low-resource bans the raider plans with.</summary>
public sealed record TargetAnalytics(IReadOnlyDictionary<Coordinates, TargetStats> Stats, IReadOnlySet<Coordinates> Top, IReadOnlyList<TargetBan> Bans)
{
    public static TargetAnalytics Empty { get; } = new(new Dictionary<Coordinates, TargetStats>(), new HashSet<Coordinates>(), []);

    public TargetBan? ActiveBan(Coordinates target, DateTimeOffset now) => TargetStatistics.ActiveBan(Bans, target, now);
}

/// <summary>
/// Pure statistics over the stored spy reports (planets only): how rich each target is (<see cref="TargetStats.Score"/>),
/// the best <see cref="RaidSettings.TopTargetsPercent"/> of them, which the raider spies first, and the planets whose
/// reports are always poor, which it stops spying on for a while (<see cref="NewBans"/>).
/// </summary>
public static class TargetStatistics
{
    /// <summary>Two reports closer than this say little about the refill (the stock barely moved).</summary>
    public static readonly TimeSpan MinRefillGap = TimeSpan.FromHours(1);

    /// <summary>The statistics, top targets and bans in one go, of the planets <paramref name="include"/> accepts (all by default).</summary>
    public static TargetAnalytics Analyze(
        IEnumerable<EspionageReport> reports, IEnumerable<RaidRecord> raids, IReadOnlyList<TargetBan> bans, DateTimeOffset now, RaidSettings settings,
        Func<Coordinates, bool>? include = null)
    {
        var stats = Compute(include is null ? reports : reports.Where(r => include(r.Target)), raids, now, settings);
        return new TargetAnalytics(stats, Top(stats.Values, bans, now, settings.TopTargetsPercent), bans);
    }

    /// <summary>Statistics of every planet with a report in the window.</summary>
    public static IReadOnlyDictionary<Coordinates, TargetStats> Compute(
        IEnumerable<EspionageReport> reports, IEnumerable<RaidRecord> raids, DateTimeOffset now, RaidSettings settings)
    {
        var since = WindowStart(now, settings);
        var attacks = Attacks(raids, since, now);
        return InWindow(reports, since, now)
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g =>
            {
                var list = g.OrderBy(r => r.Time).ToList();
                var landed = attacks.GetValueOrDefault(g.Key) ?? [];
                return new TargetStats(
                    g.Key,
                    list[^1].PlayerName ?? list.LastOrDefault(r => r.PlayerName is not null)?.PlayerName,
                    list.Count,
                    list[^1].Time,
                    list[^1].TotalResources,
                    Median(list.Select(r => r.TotalResources).ToList()),
                    RefillPerHour(list, landed),
                    landed.Count,
                    landed.Sum(a => a.ExpectedLoot),
                    HighValue(list, settings));
            });
    }

    /// <summary>
    /// The high-value mark of a planet's reports (oldest first): its newest report with loot of at least
    /// <see cref="RaidSettings.HighValueLootFactor"/> × <see cref="RaidSettings.MinLoot"/>, as long as fewer than
    /// <see cref="RaidSettings.HighValueDropReports"/> later ones in a row had loot below <see cref="RaidSettings.MinLoot"/>
    /// (a lower report after our attack is normal; still poor means the stock was a one-time thing). Null when not.
    /// </summary>
    public static HighValueMark? HighValue(IReadOnlyList<EspionageReport> ordered, RaidSettings settings)
    {
        if (settings.HighValueLootFactor <= 0 || settings.MinLoot <= 0)
            return null;

        var threshold = settings.MinLoot * settings.HighValueLootFactor;
        var bigIndex = -1;
        for (var i = ordered.Count - 1; i >= 0 && bigIndex < 0; i--)
        {
            if (RaidPlanner.ExpectedLoot(ordered[i], settings) >= threshold)
                bigIndex = i;
        }

        if (bigIndex < 0)
            return null;

        var poorInARow = 0;
        for (var i = bigIndex + 1; i < ordered.Count; i++)
            poorInARow = RaidPlanner.ExpectedLoot(ordered[i], settings) < settings.MinLoot ? poorInARow + 1 : 0;

        return poorInARow >= Math.Max(1, settings.HighValueDropReports)
            ? null
            : new HighValueMark(ordered[bigIndex].Time, RaidPlanner.ExpectedLoot(ordered[bigIndex], settings), poorInARow);
    }

    /// <summary>
    /// The best <paramref name="percent"/> of the planets by <see cref="TargetStats.Score"/> (at least one when any has a
    /// score), not counting banned ones; none with 0 %.
    /// </summary>
    public static IReadOnlySet<Coordinates> Top(IEnumerable<TargetStats> stats, IReadOnlyList<TargetBan> bans, DateTimeOffset now, int percent)
    {
        if (percent <= 0)
            return new HashSet<Coordinates>();

        var ranked = stats.Where(s => s.Score > 0 && ActiveBan(bans, s.Target, now) is null).OrderByDescending(s => s.Score).ToList();
        var count = (int)Math.Ceiling(ranked.Count * Math.Min(100, percent) / 100.0);
        return ranked.Take(count).Select(s => s.Target).ToHashSet();
    }

    /// <summary>
    /// Planets to mark low resource now: not banned, and every report since the window started or their last ban ended
    /// (at least <see cref="RaidSettings.LowResourceMinReports"/> of them; one for a planet banned before, which already
    /// proved poor) has loot below <see cref="RaidSettings.MinLoot"/>, and their refill wouldn't reach it within
    /// <see cref="RaidSettings.LowLootRespyHours"/> either. The first ban lasts <see cref="RaidSettings.LowResourceBanDays"/>,
    /// every later one <see cref="RaidSettings.LowResourceRepeatBanDays"/>. None when the minimum is 0 (off).
    /// </summary>
    public static IReadOnlyList<TargetBan> NewBans(
        IEnumerable<EspionageReport> reports, IEnumerable<RaidRecord> raids, IReadOnlyList<TargetBan> bans, DateTimeOffset now, RaidSettings settings)
    {
        if (settings.LowResourceMinReports <= 0 || settings.MinLoot <= 0)
            return [];

        var windowStart = WindowStart(now, settings);
        var attacks = Attacks(raids, windowStart, now);
        var result = new List<TargetBan>();
        foreach (var group in InWindow(reports, windowStart, now).GroupBy(r => r.Target))
        {
            if (ActiveBan(bans, group.Key, now) is not null)
                continue;

            var earlier = bans.Where(b => b.Target == group.Key).ToList();
            var since = earlier.Count == 0 ? windowStart : Max(windowStart, earlier.Max(b => b.EndsAt));
            var list = group.Where(r => r.Time >= since).OrderBy(r => r.Time).ToList();
            var needed = earlier.Count == 0 ? settings.LowResourceMinReports : 1;
            if (list.Count < needed)
                continue;

            var best = list.Max(r => RaidPlanner.ExpectedLoot(r, settings));
            if (best >= settings.MinLoot)
                continue;

            var refill = RefillPerHour(list, attacks.GetValueOrDefault(group.Key) ?? []);
            var hours = Math.Max(1, settings.LowLootRespyHours);
            if (refill is { } perHour && (long)(perHour * hours * (settings.LootPercent / 100.0)) >= settings.MinLoot)
                continue;

            var nth = earlier.Count + 1;
            var days = nth == 1 ? settings.LowResourceBanDays : settings.LowResourceRepeatBanDays;
            if (days <= 0)
                continue;
            result.Add(new TargetBan(group.Key, now, now.AddDays(days), nth,
                $"{list.Count} report(s), best loot {best:N0}" + (refill is { } r ? $", refill {r:N0}/h" : "")));
        }

        return result;
    }

    /// <summary>The ban that keeps the raider from <paramref name="target"/> now; null when none.</summary>
    public static TargetBan? ActiveBan(IReadOnlyList<TargetBan> bans, Coordinates target, DateTimeOffset now) =>
        bans.Where(b => b.Target == target && b.IsActive(now)).MaxBy(b => b.Until);

    /// <summary>The median gain per hour between consecutive reports, our attacks' loot added back; null without a pair.</summary>
    public static long? RefillPerHour(IReadOnlyList<EspionageReport> ordered, IReadOnlyList<RaidRecord> attacks)
    {
        var gains = new List<long>();
        EspionageReport? previous = null;
        foreach (var report in ordered)
        {
            if (previous is not null && report.Time - previous.Time < MinRefillGap)
                continue; // too close: keep measuring from the earlier one
            if (previous is not null)
            {
                var taken = attacks.Where(a => a.ArrivesAt > previous.Time && a.ArrivesAt <= report.Time).Sum(a => a.ExpectedLoot);
                var left = Math.Max(0, previous.TotalResources - taken);
                var hours = (report.Time - previous.Time).TotalHours;
                gains.Add((long)(Math.Max(0, report.TotalResources - left) / hours));
            }

            previous = report;
        }

        return gains.Count == 0 ? null : Median(gains);
    }

    private static DateTimeOffset WindowStart(DateTimeOffset now, RaidSettings settings) => now.AddDays(-Math.Max(1, settings.StatsDays));

    private static IEnumerable<EspionageReport> InWindow(IEnumerable<EspionageReport> reports, DateTimeOffset since, DateTimeOffset now) =>
        reports.Where(r => r.TargetType == PlanetType.Planet && r.Time >= since && r.Time <= now);

    private static Dictionary<Coordinates, List<RaidRecord>> Attacks(IEnumerable<RaidRecord> raids, DateTimeOffset since, DateTimeOffset now) =>
        raids.Where(r => r.Mission == MissionType.Attack && r.TargetType == PlanetType.Planet && r.ArrivesAt >= since && r.ArrivesAt <= now)
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g => g.ToList());

    private static long Median(List<long> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
