using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Services.Raiding;

public enum RaidTargetState
{
    /// <summary>No usable report: never spied, too old, or attacked since.</summary>
    NeedsSpy,

    /// <summary>Probes are on their way or the report hasn't arrived yet.</summary>
    SpyPending,

    /// <summary>A fresh report passes every limit.</summary>
    Ready,

    /// <summary>One of our attacks is flying there.</summary>
    UnderAttack,

    /// <summary>The probes didn't see the ships or defenses; more probes are needed.</summary>
    Unseen,

    TooMuchDefense,
    TooMuchFleet,
    TooLittleLoot,

    /// <summary>The report shows the player isn't a raidable inactive any more.</summary>
    NotRaidable
}

/// <summary>A candidate planet with what the raider knows and plans for it.</summary>
/// <param name="RankingDefensePoints">The player's defense points in the ranking (0 = no defenses); null when not read.</param>
public sealed record RaidTarget(
    PlayerPlanet Planet,
    EspionageReport? Report,
    RaidTargetState State,
    long ExpectedLoot,
    string? Note = null,
    long? RankingDefensePoints = null);

/// <summary>
/// Pure rules of the inactive farmer: which planets are candidates (raidable inactives from the galaxy database, best
/// ranked first), what each one needs next (spy, attack, nothing), how many cargo ships an attack takes, how many fleet
/// slots may be used and which own planet is nearest.
/// </summary>
public static class RaidPlanner
{
    /// <summary>How long after the probes arrived a missing report still counts as on its way.</summary>
    public static readonly TimeSpan ReportDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Raidable inactive planets (no own planets), ordered by the player's rank (best first, unknown last), then by
    /// coordinates. A report newer than the galaxy scan decides the status, and planets known only from a report
    /// (not scanned yet) are candidates too.
    /// </summary>
    public static IReadOnlyList<PlayerPlanet> Candidates(
        IEnumerable<PlayerPlanet> galaxy, IReadOnlyCollection<Coordinates> ownPlanets, RaidSettings settings, IEnumerable<EspionageReport>? reports = null)
    {
        var newest = (reports ?? [])
            .Where(r => r.TargetType == PlanetType.Planet && r.PlayerStatus != PlayerStatus.None)
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Time)!);

        var planets = galaxy
            .Select(p => newest.TryGetValue(p.Coordinates, out var report) && report.Time > p.ScannedAt ? p with { Status = report.PlayerStatus } : p)
            .ToList();
        var scanned = planets.Select(p => p.Coordinates).ToHashSet();
        planets.AddRange(newest.Values
            .Where(r => !scanned.Contains(r.Target))
            .Select(r => new PlayerPlanet(r.PlayerName ?? "?", r.Target, false, r.Time, r.PlayerStatus, r.PlayerId)));

        return planets
            .Where(p => p.Status.IsRaidable() && !ownPlanets.Contains(p.Coordinates))
            .OrderBy(p => p.Rank ?? int.MaxValue)
            .ThenBy(p => p.Coordinates.Galaxy).ThenBy(p => p.Coordinates.System).ThenBy(p => p.Coordinates.Position)
            .ToList();
    }

    /// <summary>Evaluates every candidate against its newest report, the fleets sent and the defense ranking.</summary>
    public static IReadOnlyList<RaidTarget> Evaluate(
        IReadOnlyList<PlayerPlanet> candidates,
        IEnumerable<EspionageReport> reports,
        IReadOnlyList<RaidRecord> raids,
        DateTimeOffset now,
        RaidSettings settings,
        RankingSnapshot? defenseRanking = null)
    {
        var newest = reports
            .Where(r => r.TargetType == PlanetType.Planet)
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Time)!);
        var fleets = raids
            .Where(r => r.TargetType == PlanetType.Planet)
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RaidRecord>)g.ToList());

        return candidates
            .Select(p => (Planet: p, Defense: defenseRanking?.PointsOf(p.PlayerId)))
            .Where(c => settings.MaxRankingDefensePoints is not { } max || c.Defense <= max)
            .Select(c => Evaluate(c.Planet, newest.GetValueOrDefault(c.Planet.Coordinates), fleets.GetValueOrDefault(c.Planet.Coordinates) ?? [], now, settings, c.Defense))
            .ToList();
    }

    /// <param name="rankingDefensePoints">
    /// The player's defense points in the ranking. 0 means the player has no defenses, which stands in for defenses the
    /// report hides.
    /// </param>
    public static RaidTarget Evaluate(
        PlayerPlanet planet, EspionageReport? report, IReadOnlyList<RaidRecord> raids, DateTimeOffset now, RaidSettings settings, long? rankingDefensePoints = null)
    {
        var loot = report is null ? 0 : ExpectedLoot(report, settings);
        RaidTarget Target(RaidTargetState state, string? note = null) => new(planet, report, state, loot, note, rankingDefensePoints);

        if (raids.Any(r => r.Mission == MissionType.Attack && r.ArrivesAt > now))
            return Target(RaidTargetState.UnderAttack);

        var spy = raids.Where(r => r.Mission == MissionType.Spy).MaxBy(r => r.SentAt);
        var reportAfterSpy = spy is not null && report is not null && report.Time >= spy.SentAt - TimeSpan.FromMinutes(1);
        if (spy is not null && !reportAfterSpy && spy.ArrivesAt + ReportDelay > now)
            return Target(RaidTargetState.SpyPending);

        if (report is null)
            return Target(RaidTargetState.NeedsSpy);

        // Hidden sections: 0 in the defense ranking means no defenses, and an inactive player keeps no fleet, so with
        // that ranking hidden ships count as none too (the probes see ships before defenses: both are hidden then).
        var noDefenses = rankingDefensePoints == 0;
        var defensePoints = report.Defenses?.Points ?? (noDefenses ? 0 : null);
        var fleetPoints = report.Ships?.Points ?? (noDefenses ? 0 : null);
        var hidden = fleetPoints is null ? "ships" : defensePoints is null ? "defenses" : null;

        // Probes that didn't see it won't see it next time either: spying again only wastes them and a fleet slot.
        if (hidden is not null && SpiedWith(raids, report) is { } probes && probes >= settings.ProbeCount)
            return Target(RaidTargetState.Unseen, $"{hidden} not seen with {probes:N0} probes; spy with more");

        var lastAttack = raids.Where(r => r.Mission == MissionType.Attack).MaxBy(r => r.ArrivesAt);
        var attackedSince = lastAttack is not null && report.Time < lastAttack.ArrivesAt;

        // Too little loot in the newest report: production won't make it worth an attack soon, so don't spy again yet.
        if (loot < settings.MinLoot && !attackedSince && settings.LowLootRespyHours > 0
            && report.Time + TimeSpan.FromHours(settings.LowLootRespyHours) is var respyAt && respyAt > now)
            return Target(RaidTargetState.TooLittleLoot, $"spied again after {respyAt.ToLocalTime():g}");

        if (report.Time < now - settings.ReportMaxAge)
            return Target(RaidTargetState.NeedsSpy, "report too old");

        if (attackedSince)
            return Target(RaidTargetState.NeedsSpy, "attacked since the report");

        if (report.PlayerStatus != PlayerStatus.None && !report.PlayerStatus.IsRaidable())
            return Target(RaidTargetState.NotRaidable, $"status ({report.PlayerStatus.ToLetters()})");
        if (fleetPoints is null || defensePoints is null)
            return Target(RaidTargetState.Unseen, $"{hidden} not seen");

        if (defensePoints > settings.MaxDefensePoints)
            return Target(RaidTargetState.TooMuchDefense, $"{defensePoints:N0} defense points");
        if (fleetPoints > settings.MaxFleetPoints)
            return Target(RaidTargetState.TooMuchFleet, $"{fleetPoints:N0} fleet points");
        if (loot < settings.MinLoot)
            return Target(RaidTargetState.TooLittleLoot);

        return Target(RaidTargetState.Ready, report.Ships is null || report.Defenses is null ? "hidden by the report, 0 in the defense ranking" : null);
    }

    /// <summary>The probes of our last spy mission that the report answers; null when it answers none of ours.</summary>
    private static long? SpiedWith(IReadOnlyList<RaidRecord> raids, EspionageReport report) =>
        raids.Where(r => r.Mission == MissionType.Spy && r.SentAt - TimeSpan.FromMinutes(1) <= report.Time).MaxBy(r => r.SentAt)?.Ships;

    public static long ExpectedLoot(EspionageReport report, RaidSettings settings) =>
        (long)(report.TotalResources * (settings.LootPercent / 100.0));

    /// <summary>Cargo ships for <paramref name="loot"/> plus the margin; 0 when the capacity is unknown.</summary>
    public static long ShipsNeeded(long loot, long capacityPerShip, RaidSettings settings) =>
        capacityPerShip <= 0 || loot <= 0
            ? 0
            : CeilingDivide(loot * (100L + Math.Max(0, settings.CargoMarginPercent)), 100L * capacityPerShip); // integers: 2.75M × 1.1 isn't exact

    private static long CeilingDivide(long dividend, long divisor) => (dividend + divisor - 1) / divisor;

    /// <summary>Fleet slots attacks may still fill: the allowed share of all slots minus those in use.</summary>
    public static int SlotBudget(FleetSlots slots, RaidSettings settings) =>
        Math.Max(0, (int)Math.Floor(slots.Max * Math.Clamp(settings.FleetSlotUsePercent, 0, 100) / 100.0) - slots.Used);

    /// <summary>
    /// Fleet slots spy missions may still fill: every slot but one (kept free for the player), minus those in use and
    /// the <paramref name="sentSince"/> fleets sent since the slots were read. Probes are back within minutes, so they
    /// don't follow <see cref="RaidSettings.FleetSlotUsePercent"/>.
    /// </summary>
    public static int SpySlotBudget(FleetSlots slots, int sentSince = 0) =>
        Math.Max(0, slots.Max - 1 - slots.Used - sentSince);

    /// <summary>The game's distance (the briefing's <c>Distance</c>), with galaxies and systems wrapping around.</summary>
    public static long Distance(Coordinates from, Coordinates to, UniverseProfile universe)
    {
        if (from.Galaxy != to.Galaxy)
            return 20_000L * Wrapped(from.Galaxy, to.Galaxy, universe.Galaxies);
        if (from.System != to.System)
            return 2_700 + 95L * Wrapped(from.System, to.System, universe.Systems);
        if (from.Position != to.Position)
            return 1_000 + 5L * Math.Abs(from.Position - to.Position);
        return 5;
    }

    /// <summary>
    /// <paramref name="targets"/> ordered by the distance from the nearest of <paramref name="from"/>; the sort is stable,
    /// so targets at the same distance keep their order.
    /// </summary>
    public static IReadOnlyList<RaidTarget> NearestFirst(IEnumerable<RaidTarget> targets, IReadOnlyCollection<Coordinates> from, UniverseProfile universe) =>
        from.Count == 0
            ? targets.ToList()
            : targets.OrderBy(t => from.Min(o => Distance(o, t.Planet.Coordinates, universe))).ToList();

    /// <summary>The nearest origin that has <paramref name="shipsNeeded"/>; otherwise the one with the most ships, or null without any.</summary>
    public static T? ChooseOrigin<T>(
        Coordinates target,
        IEnumerable<T> origins,
        Func<T, Coordinates> coordinatesOf,
        Func<T, long> shipsOf,
        long shipsNeeded,
        UniverseProfile universe)
        where T : class
    {
        var withShips = origins.Where(o => shipsOf(o) > 0).ToList();
        return withShips.Where(o => shipsOf(o) >= shipsNeeded).MinBy(o => Distance(coordinatesOf(o), target, universe))
               ?? withShips.MaxBy(shipsOf);
    }

    private static int Wrapped(int a, int b, int count)
    {
        var direct = Math.Abs(a - b);
        return count > 0 ? Math.Min(direct, count - direct) : direct;
    }
}
