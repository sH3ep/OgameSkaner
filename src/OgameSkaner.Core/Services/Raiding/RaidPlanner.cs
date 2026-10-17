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
            .Where(p => settings.MaxRank <= 0 || p.Rank is { } rank && rank <= settings.MaxRank)
            .OrderBy(p => p.Rank ?? int.MaxValue)
            .ThenBy(p => p.Coordinates.Galaxy).ThenBy(p => p.Coordinates.System).ThenBy(p => p.Coordinates.Position)
            .ToList();
    }

    /// <summary>Evaluates every candidate against its newest report and the fleets sent.</summary>
    public static IReadOnlyList<RaidTarget> Evaluate(
        IReadOnlyList<PlayerPlanet> candidates,
        IEnumerable<EspionageReport> reports,
        IReadOnlyList<RaidRecord> raids,
        DateTimeOffset now,
        RaidSettings settings)
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
            .Select(p => Evaluate(p, newest.GetValueOrDefault(p.Coordinates), fleets.GetValueOrDefault(p.Coordinates) ?? [], now, settings))
            .ToList();
    }

    public static RaidTarget Evaluate(PlayerPlanet planet, EspionageReport? report, IReadOnlyList<RaidRecord> raids, DateTimeOffset now, RaidSettings settings)
    {
        var loot = report is null ? 0 : ExpectedLoot(report, settings);
        RaidTarget Target(RaidTargetState state, string? note = null) => new(planet, report, state, loot, note);

        if (raids.Any(r => r.Mission == MissionType.Attack && r.ArrivesAt > now))
            return Target(RaidTargetState.UnderAttack);

        var spy = raids.Where(r => r.Mission == MissionType.Spy).MaxBy(r => r.SentAt);
        var reportAfterSpy = spy is not null && report is not null && report.Time >= spy.SentAt - TimeSpan.FromMinutes(1);
        if (spy is not null && !reportAfterSpy && spy.ArrivesAt + ReportDelay > now)
            return Target(RaidTargetState.SpyPending);

        if (report is null)
            return Target(RaidTargetState.NeedsSpy);
        if (report.Time < now - settings.ReportMaxAge)
            return Target(RaidTargetState.NeedsSpy, "report too old");

        var lastAttack = raids.Where(r => r.Mission == MissionType.Attack).MaxBy(r => r.ArrivesAt);
        if (lastAttack is not null && report.Time < lastAttack.ArrivesAt)
            return Target(RaidTargetState.NeedsSpy, "attacked since the report");

        if (report.PlayerStatus != PlayerStatus.None && !report.PlayerStatus.IsRaidable())
            return Target(RaidTargetState.NotRaidable, $"status ({report.PlayerStatus.ToLetters()})");
        if (report.Ships is null || report.Defenses is null)
            return Target(RaidTargetState.Unseen, report.Ships is null ? "ships not seen" : "defenses not seen");

        if (report.Defenses.Points > settings.MaxDefensePoints)
            return Target(RaidTargetState.TooMuchDefense, $"{report.Defenses.Points:N0} defense points");
        if (report.Ships.Points > settings.MaxFleetPoints)
            return Target(RaidTargetState.TooMuchFleet, $"{report.Ships.Points:N0} fleet points");
        if (loot < settings.MinLoot)
            return Target(RaidTargetState.TooLittleLoot);

        return Target(RaidTargetState.Ready);
    }

    public static long ExpectedLoot(EspionageReport report, RaidSettings settings) =>
        (long)(report.TotalResources * (settings.LootPercent / 100.0));

    /// <summary>Cargo ships for <paramref name="loot"/> plus the margin; 0 when the capacity is unknown.</summary>
    public static long ShipsNeeded(long loot, long capacityPerShip, RaidSettings settings) =>
        capacityPerShip <= 0 || loot <= 0
            ? 0
            : CeilingDivide(loot * (100L + Math.Max(0, settings.CargoMarginPercent)), 100L * capacityPerShip); // integers: 2.75M × 1.1 isn't exact

    private static long CeilingDivide(long dividend, long divisor) => (dividend + divisor - 1) / divisor;

    /// <summary>Fleet slots the raider may still fill: the allowed share of all slots minus those in use.</summary>
    public static int SlotBudget(FleetSlots slots, RaidSettings settings) =>
        Math.Max(0, (int)Math.Floor(slots.Max * Math.Clamp(settings.FleetSlotUsePercent, 0, 100) / 100.0) - slots.Used);

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
