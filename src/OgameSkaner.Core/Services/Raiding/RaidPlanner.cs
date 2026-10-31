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
    NotRaidable,

    /// <summary>Its reports were always poor: not spied on until the ban ends (<see cref="TargetStatistics.NewBans"/>).</summary>
    LowResource
}

/// <summary>A candidate planet with what the raider knows and plans for it.</summary>
/// <param name="RankingDefensePoints">The player's defense points in the ranking (0 = no defenses); null when not read.</param>
/// <param name="Stats">What its reports of the last days say (<see cref="TargetStatistics"/>); null without one.</param>
/// <param name="IsTop">One of the best <see cref="RaidSettings.TopTargetsPercent"/>: spied on first.</param>
/// <param name="Ban">Its active low-resource ban.</param>
/// <param name="EstimatedLoot">
/// The loot an attack could take now (<see cref="RaidPlanner.EstimateLoot"/>): what spying on it is worth.
/// </param>
public sealed record RaidTarget(
    PlayerPlanet Planet,
    EspionageReport? Report,
    RaidTargetState State,
    long ExpectedLoot,
    string? Note = null,
    long? RankingDefensePoints = null,
    TargetStats? Stats = null,
    bool IsTop = false,
    TargetBan? Ban = null,
    long EstimatedLoot = 0)
{
    /// <summary>Showed a big stock lately (<see cref="TargetStatistics.HighValue"/>): spied on right after the top targets.</summary>
    public bool IsHighValue => Stats?.HighValue is not null;
}

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
        RankingSnapshot? defenseRanking = null,
        TargetAnalytics? analytics = null)
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
            .Select(c => Evaluate(c.Planet, newest.GetValueOrDefault(c.Planet.Coordinates), fleets.GetValueOrDefault(c.Planet.Coordinates) ?? [], now, settings, c.Defense, analytics))
            .ToList();
    }

    /// <param name="rankingDefensePoints">
    /// The player's defense points in the ranking. 0 means the player has no defenses, which stands in for defenses the
    /// report hides.
    /// </param>
    /// <param name="analytics">The target statistics: top targets re-spy sooner, banned ones aren't spied on.</param>
    public static RaidTarget Evaluate(
        PlayerPlanet planet, EspionageReport? report, IReadOnlyList<RaidRecord> raids, DateTimeOffset now, RaidSettings settings, long? rankingDefensePoints = null,
        TargetAnalytics? analytics = null)
    {
        var loot = report is null ? 0 : ExpectedLoot(report, settings);
        var stats = analytics?.Stats.GetValueOrDefault(planet.Coordinates);
        var isTop = analytics?.Top.Contains(planet.Coordinates) == true;
        var ban = analytics?.ActiveBan(planet.Coordinates, now);
        var estimate = EstimateLoot(report, raids, stats, now, settings);
        RaidTarget Target(RaidTargetState state, string? note = null) => new(planet, report, state, loot, note, rankingDefensePoints, stats, isTop, ban, estimate);

        if (raids.Any(r => r.Mission == MissionType.Attack && r.ArrivesAt > now))
            return Target(RaidTargetState.UnderAttack);

        var spy = raids.Where(r => r.Mission == MissionType.Spy).MaxBy(r => r.SentAt);
        var reportAfterSpy = spy is not null && report is not null && report.Time >= spy.SentAt - TimeSpan.FromMinutes(1);
        if (spy is not null && !reportAfterSpy && spy.ArrivesAt + ReportDelay > now)
            return Target(RaidTargetState.SpyPending);

        // During a farming event a banned planet may have been boosted too.
        var farmingEvent = settings.ActiveFarmingEvent;
        if (ban is not null && farmingEvent is null)
            return Target(RaidTargetState.LowResource, $"low resource ({ban.Reason}), spied again after {ban.Until.ToLocalTime():g}");

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

        // Too little loot in the newest report: production won't make it worth an attack soon, so don't spy again yet
        // (a top or high-value target may hold a lot again soon, so sooner).
        // During a farming event any planet may get resources at any time: soon, and at once after a report from before it.
        var respyHours = isTop || stats?.HighValue is not null ? Math.Min(settings.TopRespyHours, settings.LowLootRespyHours) : settings.LowLootRespyHours;
        var respyAfter = farmingEvent is null ? TimeSpan.FromHours(respyHours)
            : report.Time < farmingEvent.Start ? TimeSpan.Zero
            : TimeSpan.FromMinutes(Math.Max(0, settings.FarmingEventRespyMinutes));
        if (loot < settings.MinLoot && !attackedSince && respyAfter > TimeSpan.Zero
            && report.Time + respyAfter is var respyAt && respyAt > now)
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

    /// <summary>The refill counted since the newest report at most: storages fill up and the statistics cover a day.</summary>
    public static readonly TimeSpan MaxRefillEstimate = TimeSpan.FromHours(24);

    /// <summary>
    /// The loot an attack could take now: the newest report's stock less what our attacks since took (each
    /// <see cref="RaidSettings.LootPercent"/> of what was left), plus the refill since (<see cref="TargetStats.RefillPerHour"/>,
    /// at most <see cref="MaxRefillEstimate"/>). Without a report the median stock of the statistics, else
    /// <see cref="RaidSettings.MinLoot"/>: an unknown planet ranks below the known rich ones and above the known poor ones.
    /// </summary>
    public static long EstimateLoot(EspionageReport? report, IReadOnlyList<RaidRecord> raids, TargetStats? stats, DateTimeOffset now, RaidSettings settings)
    {
        var share = Math.Clamp(settings.LootPercent, 1, 100) / 100.0;
        if (report is null)
            return stats is null ? settings.MinLoot : (long)(stats.MedianResources * share);

        var attacksSince = raids.Count(r => r.Mission == MissionType.Attack && r.ArrivesAt > report.Time && r.ArrivesAt <= now);
        var left = report.TotalResources * Math.Pow(1 - share, attacksSince);
        var hours = Math.Clamp((now - report.Time).TotalHours, 0, MaxRefillEstimate.TotalHours);
        return (long)((left + (stats?.RefillPerHour ?? 0) * hours) * share);
    }

    /// <summary>A later attack on the same planet must land at least this long after the one before it.</summary>
    public static readonly TimeSpan MinWaveGap = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The loot of the <paramref name="wave"/>-th attack (1 = the first) on the report's resources: each attack takes
    /// <see cref="RaidSettings.LootPercent"/> of what the attacks before it left.
    /// </summary>
    public static long WaveLoot(EspionageReport report, RaidSettings settings, int wave)
    {
        var share = Math.Clamp(settings.LootPercent, 1, 100) / 100.0;
        return (long)(report.TotalResources * share * Math.Pow(1 - share, Math.Max(0, wave - 1)));
    }

    /// <summary>
    /// How many attacks each of <paramref name="targets"/> gets within <paramref name="slots"/>: every attack (first,
    /// second, … up to <see cref="RaidSettings.MaxAttacksPerTarget"/>, a later one only while its
    /// <see cref="WaveLoot"/> is at least <see cref="RaidSettings.MinLoot"/>) competes for a slot by its own loot per
    /// flight time (÷ √distance from the nearest of <paramref name="from"/>; loot alone without it), so a rich planet's
    /// second attack beats a poor planet's first. Targets without a report get one attack. Ties keep the targets' order.
    /// </summary>
    public static IReadOnlyDictionary<Coordinates, int> PlanAttacks(
        IReadOnlyList<RaidTarget> targets, RaidSettings settings, int slots, IReadOnlyCollection<Coordinates> from, UniverseProfile universe)
    {
        var maxAttacks = Math.Max(1, settings.MaxAttacksPerTarget);
        var attacks = new List<(Coordinates Target, int Index, int Wave, double Value)>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var weight = from.Count == 0 ? 1 : 1 / Math.Sqrt(Math.Max(1, from.Min(o => Distance(o, target.Planet.Coordinates, universe))));
            if (target.Report is not { } report)
            {
                attacks.Add((target.Planet.Coordinates, i, 1, target.ExpectedLoot * weight));
                continue;
            }

            for (var wave = 1; wave <= maxAttacks; wave++)
            {
                var loot = WaveLoot(report, settings, wave);
                if (wave > 1 && (loot <= 0 || loot < settings.MinLoot))
                    break;
                attacks.Add((target.Planet.Coordinates, i, wave, loot * weight));
            }
        }

        return attacks
            .OrderByDescending(a => a.Value).ThenBy(a => a.Index).ThenBy(a => a.Wave)
            .Take(Math.Max(0, slots))
            .GroupBy(a => a.Target)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// How much later an attack landing at <paramref name="arrival"/> must land to come <see cref="MinWaveGap"/> after one
    /// landing at <paramref name="previous"/>; zero or less when it already does.
    /// </summary>
    public static TimeSpan LandingShortfall(DateTimeOffset arrival, DateTimeOffset previous) => previous + MinWaveGap - arrival;

    /// <summary>Cargo capacity an attack for <paramref name="loot"/> needs: the loot plus the margin.</summary>
    public static long CargoNeeded(long loot, RaidSettings settings) =>
        loot <= 0 ? 0 : CeilingDivide(loot * (100L + Math.Max(0, settings.CargoMarginPercent)), 100L); // integers: 2.75M × 1.1 isn't exact

    /// <summary>
    /// The cargo ships for <paramref name="cargo"/> capacity in priority order: as many of the first type as needed
    /// (or as there are), then the next types for the rest. Types without ships or a known capacity are skipped; the
    /// fleet carries less than <paramref name="cargo"/> when the ships run out.
    /// </summary>
    public static IReadOnlyList<ShipStack> CargoFleet(
        long cargo, IReadOnlyList<string> priority, Func<string, long> available, Func<string, long> capacityPerShip)
    {
        var fleet = new List<ShipStack>();
        var left = cargo;
        foreach (var type in priority.Distinct())
        {
            if (left <= 0)
                break;
            var capacity = capacityPerShip(type);
            var ships = Math.Min(available(type), capacity > 0 ? CeilingDivide(left, capacity) : 0);
            if (ships <= 0)
                continue;
            fleet.Add(new ShipStack(type, ships));
            left -= ships * capacity;
        }

        return fleet;
    }

    private static long CeilingDivide(long dividend, long divisor) => (dividend + divisor - 1) / divisor;

    /// <summary>Fleet slots attacks may still fill: the allowed share of all slots minus those in use.</summary>
    public static int SlotBudget(FleetSlots slots, RaidSettings settings) => Math.Max(0, AllowedSlots(slots, settings) - slots.Used);

    /// <summary>
    /// Fleet slots the raid cycle's spy missions may fill: none once the fleets in use (with the <paramref name="sentSince"/>
    /// sent since the slots were read) reach <see cref="RaidSettings.FleetSlotUsePercent"/>, so probes don't keep flying
    /// while the slots are full; below it every slot but one (<see cref="SpySlotBudget(FleetSlots, int)"/>), so a batch
    /// of probes may go past the share. During a farming event every slot but one regardless of the share: finding the
    /// boosted planets is what pays then.
    /// </summary>
    public static int SpySlotBudget(FleetSlots slots, RaidSettings settings, int sentSince = 0) =>
        SlotsFull(slots, settings, sentSince) && settings.ActiveFarmingEvent is null ? 0 : SpySlotBudget(slots, sentSince);

    /// <summary>
    /// Whether the fleets in use (with the <paramref name="sentSince"/> sent since the slots were read) reach
    /// <see cref="RaidSettings.FleetSlotUsePercent"/>: the raid cycle can neither attack nor spy until a fleet returns.
    /// </summary>
    public static bool SlotsFull(FleetSlots slots, RaidSettings settings, int sentSince = 0) =>
        slots.Used + sentSince >= AllowedSlots(slots, settings);

    /// <summary><see cref="RaidSettings.FleetSlotUsePercent"/> of all fleet slots, at most all but those kept for emergencies.</summary>
    private static int AllowedSlots(FleetSlots slots, RaidSettings settings) =>
        Math.Min((int)Math.Floor(slots.Max * Math.Clamp(settings.FleetSlotUsePercent, 0, 100) / 100.0), slots.Max - FleetSlots.KeptForEmergencies);

    /// <summary>
    /// Fleet slots spy missions may still fill: every slot but one (kept free for the player), minus those in use and
    /// the <paramref name="sentSince"/> fleets sent since the slots were read; "Spy selected" uses it as is, the raid
    /// cycle only below <see cref="RaidSettings.FleetSlotUsePercent"/> (<see cref="SpySlotBudget(FleetSlots, RaidSettings, int)"/>).
    /// </summary>
    public static int SpySlotBudget(FleetSlots slots, int sentSince = 0) =>
        Math.Max(0, slots.Max - FleetSlots.KeptForEmergencies - slots.Used - sentSince);

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
    /// <paramref name="targets"/> ordered by loot per flight time, best first: <paramref name="loot"/> divided by the
    /// square root of the distance from the nearest of <paramref name="from"/> (a flight takes about that long, so this
    /// is the loot per hour a fleet slot is busy); by loot alone without <paramref name="from"/>. The sort is stable.
    /// </summary>
    public static IReadOnlyList<RaidTarget> MostLootPerFlightFirst(
        IEnumerable<RaidTarget> targets, Func<RaidTarget, long> loot, IReadOnlyCollection<Coordinates> from, UniverseProfile universe) =>
        targets.OrderByDescending(t => from.Count == 0 ? loot(t) : loot(t) / Math.Sqrt(from.Min(o => Distance(o, t.Planet.Coordinates, universe)))).ToList();

    /// <summary>
    /// The order spies go in: the top targets and then the high-value ones, both by the loot they likely hold per flight
    /// time (<see cref="MostLootPerFlightFirst"/> on <see cref="RaidTarget.EstimatedLoot"/>), then the rest best rank
    /// first (a player's points are what their mines produce; most targets have no report, and distance alone would
    /// pick tiny nearby players), a player's planets by loot per flight time. The attacks then leave from the origin
    /// nearest each target.
    /// During a <paramref name="farmingEvent"/> the statistics don't tell who got resources: the planets without a report
    /// of the event go first, nearest first (most planets per fleet slot hour; best rank first without
    /// <paramref name="from"/>), then the others, oldest report first.
    /// </summary>
    public static IReadOnlyList<RaidTarget> SpyOrder(
        IEnumerable<RaidTarget> targets, IReadOnlyCollection<Coordinates> from, UniverseProfile universe, FarmingEventPeriod? farmingEvent = null)
    {
        if (farmingEvent is not null)
        {
            bool SpiedInEvent(RaidTarget t) => t.Report is { } r && r.Time >= farmingEvent.Start;
            return targets
                .OrderBy(SpiedInEvent)
                .ThenBy(t => SpiedInEvent(t) ? 0 : from.Count > 0 ? from.Min(o => Distance(o, t.Planet.Coordinates, universe)) : t.Planet.Rank ?? int.MaxValue)
                .ThenBy(t => t.Report?.Time ?? DateTimeOffset.MinValue)
                .ToList();
        }

        static int Group(RaidTarget t) => t.IsTop ? 0 : t.IsHighValue ? 1 : 2;
        return MostLootPerFlightFirst(targets, t => t.EstimatedLoot, from, universe)
            .OrderBy(Group)
            .ThenBy(t => Group(t) == 2 ? t.Planet.Rank ?? int.MaxValue : 0) // stable: equal keys keep the loot order
            .ToList();
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
