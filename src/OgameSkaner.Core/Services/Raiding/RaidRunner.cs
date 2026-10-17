using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Raiding;

public sealed record RaidProgress(string Message);

public enum RaidOutcome
{
    Cancelled,
    Failed
}

public sealed record RaidRunResult(RaidOutcome Outcome, int Attacks, int Spies, string? Error = null);

/// <summary>
/// The inactive farmer. Each cycle of <see cref="RunAsync"/> reads the fleet slots and new espionage reports, attacks the
/// ready targets with the most loot, spies on the best ranked candidates that need a report (both within
/// <see cref="RaidSettings.FleetSlotUsePercent"/> of the slots), then sleeps until reports arrive or fleets return.
/// The same steps are available one by one for the page's buttons.
/// </summary>
/// <remarks>
/// Fleets leave from the planet selected in the game at the start of the cycle, or with
/// <see cref="RaidSettings.UseNearestPlanet"/> from the nearest own planet that has the cargo ships. Each send switches
/// to its origin under the client's planet lock, and the game is switched back afterwards.
/// </remarks>
public sealed class RaidRunner(
    OwnPlanetService ownPlanets,
    IPlanetRepository planetRepository,
    IRaidStore store,
    TimeProvider timeProvider,
    ILogger<RaidRunner> logger)
{
    public const string ProbeType = "SPY_PROBE";

    private const int ReportPages = 3;

    /// <summary>Pages read when every report in the messages is loaded.</summary>
    private const int AllReportPages = 20;

    /// <summary>A run reads the defense ranking again when it's older (the game updates it every few minutes to hours).</summary>
    public static readonly TimeSpan RankingMaxAge = TimeSpan.FromHours(6);

    /// <summary>Guard against a ranking that never ends (34 pages on genesis).</summary>
    private const int MaxRankingPages = 200;
    private static readonly TimeSpan MinWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IdleWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromMinutes(1);

    /// <summary>Every candidate of the universe with its state, best ranked first.</summary>
    public async Task<IReadOnlyList<RaidTarget>> GetTargetsAsync(string universe, RaidSettings settings, CancellationToken cancellationToken = default)
    {
        var database = await planetRepository.GetAsync(universe, cancellationToken).ConfigureAwait(false);
        var own = ownPlanets.GetStored(universe).Planets.Select(p => p.Coordinates).ToHashSet();
        var reports = store.LoadReports(universe);
        var candidates = RaidPlanner.Candidates(database.Snapshot(), own, settings, reports);
        var defense = store.LoadRanking(universe, RankingCategory.Defense);
        return RaidPlanner.Evaluate(candidates, reports, store.LoadRaids(universe), timeProvider.GetUtcNow(), settings, defense);
    }

    /// <summary>When the defense ranking was read last (null: never).</summary>
    public DateTimeOffset? DefenseRankingReadAt(string universe) => store.LoadRanking(universe, RankingCategory.Defense).ReadAt;

    /// <summary>
    /// Reads the whole defense ranking (every page) and stores each player's points: 0 means no defenses worth a point,
    /// so the player's planets are safe from defenses. Returns the number of players read.
    /// </summary>
    public async Task<int> ReadDefenseRankingAsync(IOgamexClient client, IProgress<RaidProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var first = await client.GetRankingPageAsync(RankingCategory.Defense, 1, cancellationToken).ConfigureAwait(false);
        var entries = new List<RankingEntry>(first.Entries);
        var pages = Math.Min(first.PageCount, MaxRankingPages);
        for (var page = 2; page <= pages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.AddRange((await client.GetRankingPageAsync(RankingCategory.Defense, page, cancellationToken).ConfigureAwait(false)).Entries);
        }

        store.SaveRanking(client.Universe.Name, RankingCategory.Defense, entries, timeProvider.GetUtcNow());
        Report(progress, $"Read the defense ranking: {entries.Count:N0} players, {entries.Count(e => e.Points == 0):N0} without defenses");
        return entries.Count;
    }

    public IReadOnlyList<RaidRecord> GetRaids(string universe) => store.LoadRaids(universe);

    public async Task<RaidRunResult> RunAsync(
        IOgamexClient client, RaidSettings settings, IProgress<RaidProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        int attacks = 0, spies = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    var cycle = await RunCycleAsync(client, settings, progress, cancellationToken).ConfigureAwait(false);
                    attacks += cycle.Attacks;
                    spies += cycle.Spies;
                    wait = cycle.Wait;
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Raid cycle on {Universe} failed", client.Universe.Name);
                    Report(progress, $"{ex.Message} (retrying)");
                    wait = ErrorWait;
                }

                Report(progress, $"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RaidRunResult(RaidOutcome.Cancelled, attacks, spies);
        }
        catch (SessionExpiredException ex)
        {
            return new RaidRunResult(RaidOutcome.Failed, attacks, spies, ex.Message);
        }
    }

    /// <summary>
    /// Reads the espionage reports the store doesn't know yet, with ships and defenses when still fresh enough.
    /// <paramref name="all"/> loads every report in the messages (up to <see cref="AllReportPages"/> pages) with its
    /// details, including stored ones read without details before.
    /// </summary>
    public async Task<int> ReadReportsAsync(
        IOgamexClient client, RaidSettings settings, IProgress<RaidProgress>? progress = null, CancellationToken cancellationToken = default, bool all = false)
    {
        var universe = client.Universe.Name;
        var known = store.LoadReportIds(universe, withDetailsOnly: all).ToHashSet();
        var cutoff = all ? DateTimeOffset.MinValue : timeProvider.GetUtcNow() - settings.ReportMaxAge;
        var fresh = new List<EspionageReport>();

        for (var page = 1; page <= (all ? AllReportPages : ReportPages); page++)
        {
            var list = await client.GetEspionageReportsAsync(page, cancellationToken).ConfigureAwait(false);
            var unknown = list.Where(r => known.Add(r.Id)).ToList();
            foreach (var report in unknown)
            {
                if (report.Time < cutoff)
                {
                    fresh.Add(report); // too old to attack on, the summary is enough
                    continue;
                }

                try
                {
                    fresh.Add(await client.GetEspionageReportDetailAsync(report, cancellationToken).ConfigureAwait(false));
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Reading the details of report {Report} failed", report.Id);
                    fresh.Add(report);
                }
            }

            // The list is newest first: a page with reports read before ends the new ones.
            if (list.Count == 0 || (!all && unknown.Count < list.Count))
                break;
        }

        if (fresh.Count > 0)
        {
            store.AddReports(universe, fresh);
            Report(progress, $"Read {fresh.Count} new espionage report(s)");
        }
        else if (all)
        {
            Report(progress, "No new espionage reports in the messages.");
        }

        return fresh.Count;
    }

    /// <summary>Spies on <paramref name="targets"/> from the current planet, in every free fleet slot but one.</summary>
    public Task<int> SpyAsync(
        IOgamexClient client, IReadOnlyList<RaidTarget> targets, RaidSettings settings, IProgress<RaidProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunManualAsync(client, settings, progress, cancellationToken, (refresh, origins, slots) =>
            SpyAsync(client, targets.Take(RaidPlanner.SpySlotBudget(slots)).ToList(), settings, Home(origins, refresh), progress, cancellationToken));

    /// <summary>Attacks <paramref name="targets"/> (with fresh reports), as far as the free fleet slots allow.</summary>
    public Task<int> AttackAsync(
        IOgamexClient client, IReadOnlyList<RaidTarget> targets, RaidSettings settings, IProgress<RaidProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunManualAsync(client, settings, progress, cancellationToken, (_, origins, slots) =>
            AttackAsync(client, targets.Where(t => t.Report is not null).ToList(), settings, origins, slots.Free, progress, cancellationToken));

    private async Task<int> RunManualAsync(
        IOgamexClient client,
        RaidSettings settings,
        IProgress<RaidProgress>? progress,
        CancellationToken cancellationToken,
        Func<OwnPlanetRefresh, IReadOnlyList<Origin>, FleetSlots, Task<int>> action)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
            var origins = await ReadOriginsAsync(client, refresh, settings, cancellationToken).ConfigureAwait(false);
            var slots = Slots(origins);
            if (slots.Free == 0)
            {
                Report(progress, $"No free fleet slot ({slots.Used}/{slots.Max}).");
                return 0;
            }

            return await action(refresh, origins, slots).ConfigureAwait(false);
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, progress).ConfigureAwait(false);
        }
    }

    private async Task<(int Attacks, int Spies, TimeSpan Wait)> RunCycleAsync(
        IOgamexClient client, RaidSettings settings, IProgress<RaidProgress>? progress, CancellationToken cancellationToken)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
            var origins = await ReadOriginsAsync(client, refresh, settings, cancellationToken).ConfigureAwait(false);
            var slots = Slots(origins);
            var budget = RaidPlanner.SlotBudget(slots, settings);

            await ReadReportsAsync(client, settings, progress, cancellationToken).ConfigureAwait(false);
            if (DefenseRankingReadAt(client.Universe.Name) is not { } rankingAt || timeProvider.GetUtcNow() - rankingAt >= RankingMaxAge)
                await ReadDefenseRankingAsync(client, progress, cancellationToken).ConfigureAwait(false);
            var targets = await GetTargetsAsync(client.Universe.Name, settings, cancellationToken).ConfigureAwait(false);
            Report(progress, Summary(targets, slots, budget, RaidPlanner.SpySlotBudget(slots)));

            var attacks = 0;
            var spies = 0;
            if (budget > 0)
            {
                IReadOnlyList<RaidTarget> ready = targets.Where(t => t.State == RaidTargetState.Ready).OrderByDescending(t => t.ExpectedLoot).ToList();
                if (settings.NearestTargetsFirst)
                    ready = RaidPlanner.NearestFirst(ready, origins.Select(o => o.Planet.Coordinates).ToList(), client.Universe);
                attacks = await AttackAsync(client, ready, settings, origins, budget, progress, cancellationToken).ConfigureAwait(false);
            }

            var spyBudget = RaidPlanner.SpySlotBudget(slots, attacks);
            if (spyBudget > 0)
            {
                var home = Home(origins, refresh);
                IReadOnlyList<RaidTarget> needSpy = targets.Where(t => t.State == RaidTargetState.NeedsSpy).ToList();
                if (settings.NearestTargetsFirst)
                    needSpy = RaidPlanner.NearestFirst(needSpy, [home.Planet.Coordinates], client.Universe);
                spies = await SpyAsync(client, needSpy.Take(spyBudget).ToList(), settings, home, progress, cancellationToken).ConfigureAwait(false);
            }

            return (attacks, spies, NextWait(client.Universe.Name));
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, progress).ConfigureAwait(false);
        }
    }

    private async Task<int> SpyAsync(
        IOgamexClient client, IReadOnlyList<RaidTarget> targets, RaidSettings settings, Origin home, IProgress<RaidProgress>? progress, CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
            return 0;

        var probes = home.Fleet.QuantityOf(ProbeType);
        if (probes < settings.ProbeCount)
        {
            Report(progress, $"Not enough spy probes on {home.Planet.Name} ({probes:N0} of {settings.ProbeCount}).");
            return 0;
        }

        var sent = new List<RaidRecord>();
        try
        {
            foreach (var target in targets)
            {
                if (probes < settings.ProbeCount)
                    break;

                var mission = new FleetMission(target.Planet.Coordinates, PlanetType.Planet, MissionType.Spy, [new ShipStack(ProbeType, settings.ProbeCount)], settings.SpeedPercentage);
                var briefing = await client.GetFleetBriefingAsync(mission, cancellationToken).ConfigureAwait(false);
                if (!briefing.AvailableMissions.Contains(MissionType.Spy))
                {
                    Report(progress, $"{Describe(target)}: spying isn't possible ({briefing.Warning ?? "no spy mission"})");
                    continue;
                }

                if (settings.TestMode)
                {
                    Report(progress, $"[Test] Would spy on {Describe(target)} with {settings.ProbeCount} probes from {home.Planet.Name} (flight {briefing.OneWayFlight:g})");
                    continue;
                }

                var outcome = await client.SendFleetAsync(mission, home.Planet.Id, cancellationToken).ConfigureAwait(false);
                if (!outcome.Sent)
                {
                    Report(progress, $"Spying on {Describe(target)} was refused: {outcome.Message}");
                    break;
                }

                probes -= settings.ProbeCount;
                var now = timeProvider.GetUtcNow();
                sent.Add(new RaidRecord(
                    target.Planet.Coordinates, PlanetType.Planet, target.Planet.PlayerName, MissionType.Spy, home.Planet.Coordinates,
                    ProbeType, settings.ProbeCount, 0, now, now + briefing.OneWayFlight, now + 2 * briefing.OneWayFlight));
                Report(progress, $"Spying on {Describe(target)}");
            }
        }
        finally
        {
            Save(client.Universe.Name, sent);
        }

        return sent.Count;
    }

    private async Task<int> AttackAsync(
        IOgamexClient client,
        IReadOnlyList<RaidTarget> targets,
        RaidSettings settings,
        IReadOnlyList<Origin> origins,
        int slots,
        IProgress<RaidProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0 || slots <= 0)
            return 0;

        var cargoType = settings.CargoShipType;
        var cargoName = UnitCatalog.Get(cargoType)?.Name ?? cargoType;
        var sent = new List<RaidRecord>();
        try
        {
            foreach (var target in targets)
            {
                if (sent.Count >= slots)
                    break;

                var capacity = origins.Select(o => o.Fleet.CargoCapacityOf(cargoType)).FirstOrDefault(c => c > 0);
                var needed = RaidPlanner.ShipsNeeded(target.ExpectedLoot, capacity, settings);
                var origin = RaidPlanner.ChooseOrigin(target.Planet.Coordinates, origins, o => o.Planet.Coordinates, o => o.Cargo(cargoType), needed, client.Universe);
                if (origin is null || capacity <= 0)
                {
                    if (settings.TestMode)
                    {
                        // Without ships the page shows no cargo capacity, so the count can't be worked out.
                        Report(progress, $"[Test] Would attack {Describe(target)} for {target.ExpectedLoot:N0}, but there's no {cargoName} on {string.Join(", ", origins.Select(o => o.Planet.Name))} to work out the ship count");
                        continue;
                    }

                    Report(progress, $"No {cargoName} left on {string.Join(", ", origins.Select(o => o.Planet.Name))}.");
                    break;
                }

                var ships = Math.Min(needed, origin.Cargo(cargoType));
                var carried = Math.Min(target.ExpectedLoot, ships * origin.Fleet.CargoCapacityOf(cargoType));
                if (carried < settings.MinLoot)
                {
                    Report(progress, $"{Describe(target)}: {ships:N0} {cargoName} would carry only {carried:N0}, skipped");
                    continue;
                }

                var mission = new FleetMission(target.Planet.Coordinates, PlanetType.Planet, MissionType.Attack, [new ShipStack(cargoType, ships)], settings.SpeedPercentage);
                var briefing = await client.GetFleetBriefingAsync(mission, cancellationToken).ConfigureAwait(false);
                if (!briefing.AvailableMissions.Contains(MissionType.Attack))
                {
                    Report(progress, $"{Describe(target)}: attacking isn't possible ({briefing.Warning ?? "no attack mission"})");
                    continue;
                }
                if (briefing.PlayerStatus != PlayerStatus.None && !briefing.PlayerStatus.IsRaidable())
                {
                    Report(progress, $"{Describe(target)}: the player is no longer a raidable inactive ({briefing.PlayerStatus.ToLetters()})");
                    continue;
                }

                if (settings.TestMode)
                {
                    origin.Use(cargoType, ships);
                    Report(progress, $"[Test] Would attack {Describe(target)} from {origin.Planet.Name} with {ships:N0} {cargoName} for {carried:N0} (flight {briefing.OneWayFlight:g})");
                    continue;
                }

                var outcome = await client.SendFleetAsync(mission, origin.Planet.Id, cancellationToken).ConfigureAwait(false);
                if (!outcome.Sent)
                {
                    Report(progress, $"Attack on {Describe(target)} was refused: {outcome.Message}");
                    break;
                }

                origin.Use(cargoType, ships);
                var now = timeProvider.GetUtcNow();
                sent.Add(new RaidRecord(
                    target.Planet.Coordinates, PlanetType.Planet, target.Planet.PlayerName, MissionType.Attack, origin.Planet.Coordinates,
                    cargoType, ships, carried, now, now + briefing.OneWayFlight, now + 2 * briefing.OneWayFlight));
                Report(progress, $"Attacking {Describe(target)} from {origin.Planet.Name} with {ships:N0} {cargoName} for {carried:N0}, back at {(now + 2 * briefing.OneWayFlight).ToLocalTime():T}");
            }
        }
        finally
        {
            Save(client.Universe.Name, sent);
        }

        return sent.Count;
    }

    /// <summary>The current planet's fleet, and with <see cref="RaidSettings.UseNearestPlanet"/> every other planet's too.</summary>
    private static async Task<IReadOnlyList<Origin>> ReadOriginsAsync(
        IOgamexClient client, OwnPlanetRefresh refresh, RaidSettings settings, CancellationToken cancellationToken)
    {
        var current = refresh.List.Planets.FirstOrDefault(p => p.Id == refresh.CurrentPlanet);
        if (current is null)
        {
            // The current planet isn't a planet (a moon?): send from what the fleet page shows.
            var fleet = await client.GetFleetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return [new Origin(new OwnPlanet(refresh.CurrentPlanet, fleet.CurrentPlanet.ToString(), fleet.CurrentPlanet), fleet)];
        }

        var planets = settings.UseNearestPlanet
            ? refresh.List.Planets.OrderBy(p => p.Id == current.Id ? 0 : 1).ToList()
            : [current];
        var origins = new List<Origin>();
        foreach (var planet in planets)
            origins.Add(new Origin(planet, await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false)));
        return origins;
    }

    private static Origin Home(IReadOnlyList<Origin> origins, OwnPlanetRefresh refresh) =>
        origins.FirstOrDefault(o => o.Planet.Id == refresh.CurrentPlanet) ?? origins[0];

    private static FleetSlots Slots(IReadOnlyList<Origin> origins) =>
        origins.Select(o => o.Fleet.Slots).FirstOrDefault(s => s is not null)
        ?? throw new OgamexException("The fleet page shows no fleet slots.");

    /// <summary>Until the next report is due or the next fleet is back, within <see cref="MinWait"/>..<see cref="MaxWait"/>.</summary>
    private TimeSpan NextWait(string universe)
    {
        var now = timeProvider.GetUtcNow();
        var events = store.LoadRaids(universe)
            .SelectMany(r => r.Mission == MissionType.Spy ? [r.ArrivesAt + TimeSpan.FromSeconds(10), r.ReturnsAt] : new[] { r.ReturnsAt })
            .Where(t => t > now)
            .ToList();
        if (events.Count == 0)
            return IdleWait;

        var next = events.Min() - now;
        return TimeSpan.FromTicks(Math.Clamp(next.Ticks, MinWait.Ticks, MaxWait.Ticks));
    }

    private void Save(string universe, IReadOnlyList<RaidRecord> sent)
    {
        if (sent.Count == 0)
            return;
        store.AddRaids(universe, sent);
    }

    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet, IProgress<RaidProgress>? progress)
    {
        try
        {
            await client.SelectPlanetAsync(planet, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Could not switch {Universe} back to planet {Planet}", client.Universe.Name, planet);
            Report(progress, $"Could not switch back to the planet selected before: {ex.Message}");
        }
    }

    private static string Summary(IReadOnlyList<RaidTarget> targets, FleetSlots slots, int attackBudget, int spyBudget)
    {
        var counts = targets.GroupBy(t => t.State).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}");
        return $"{targets.Count} inactive planets ({string.Join(", ", counts)}); fleet slots {slots.Used}/{slots.Max}, {attackBudget} usable for attacks, {spyBudget} for spies";
    }

    private static string Describe(RaidTarget target) =>
        $"{target.Planet.PlayerName} [{target.Planet.Coordinates}]" + (target.Planet.Rank is { } rank ? $" #{rank}" : "");

    private static void Report(IProgress<RaidProgress>? progress, string message) => progress?.Report(new RaidProgress(message));

    /// <summary>An own planet fleets can leave from, with the ships still unused this cycle.</summary>
    private sealed class Origin(OwnPlanet planet, FleetSnapshot fleet)
    {
        private readonly Dictionary<string, long> _used = new(StringComparer.Ordinal);

        public OwnPlanet Planet { get; } = planet;

        public FleetSnapshot Fleet { get; } = fleet;

        public long Cargo(string shipType) => Fleet.QuantityOf(shipType) - _used.GetValueOrDefault(shipType);

        public void Use(string shipType, long ships) => _used[shipType] = _used.GetValueOrDefault(shipType) + ships;
    }
}
