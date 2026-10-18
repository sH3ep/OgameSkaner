using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Asteroids;

public sealed record AsteroidProgress(string Message);

public enum AsteroidOutcome
{
    Cancelled,
    Failed
}

public sealed record AsteroidRunResult(AsteroidOutcome Outcome, int Sent, int Found, string? Error = null);

public enum AsteroidState
{
    /// <summary>Still there and no miners sent.</summary>
    Open,

    /// <summary>Miners are on their way.</summary>
    Sent,

    /// <summary>The miners arrived.</summary>
    Mined,

    /// <summary>Gone without miners.</summary>
    Expired
}

/// <summary>An asteroid with the miners sent to it.</summary>
public sealed record AsteroidTarget(Asteroid Asteroid, AsteroidMission? Mission, AsteroidState State);

/// <summary>What the Asteroids page shows.</summary>
/// <param name="Estimate">The estimate for miners arriving now; null without a report that shows the resources.</param>
public sealed record AsteroidOverview(
    IReadOnlyList<AsteroidTarget> Asteroids,
    IReadOnlyList<AsteroidReport> Reports,
    AsteroidEstimate? Estimate,
    int SuccessesToday);

/// <summary>
/// Asteroid mining. Each cycle of <see cref="RunAsync"/> reads the new mining reports, sends Asteroid Miners to the known
/// asteroids they reach in time, then reads the galaxy's "Find asteroids" hint and scans the hinted systems that may hold
/// an asteroid not known yet, nearest first, sending miners to each one as soon as it's found and stopping when no miners
/// or fleet slots are left. Each fleet takes as many miners as the estimated resources need
/// (<see cref="AsteroidEstimator"/>). Then it sleeps until a report is due, a fleet returns or
/// <see cref="AsteroidSettings.CheckIntervalMinutes"/> passed.
/// </summary>
/// <remarks>
/// Asteroids appear only in the galaxy with most of the player's planets and are the player's alone. Each asteroid gets
/// its miners from the nearest own planet that has enough; the game is switched back to the planet selected before.
/// The systems scanned are saved to the galaxy database too.
/// </remarks>
public sealed class AsteroidRunner(
    OwnPlanetService ownPlanets,
    IPlanetRepository planetRepository,
    IAsteroidStore store,
    TimeProvider timeProvider,
    ILogger<AsteroidRunner> logger)
{
    public const string MinerType = "ASTEROID_MINER";

    /// <summary>Report pages read per cycle (newest first; reading stops at a known report).</summary>
    private const int ReportPages = 5;

    /// <summary>The page shows asteroids gone for up to this long.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(1);

    /// <summary>The report is read this long after the miners arrive.</summary>
    private static readonly TimeSpan ReportDelay = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MinWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromMinutes(1);

    public AsteroidOverview GetOverview(string universe)
    {
        var now = timeProvider.GetUtcNow();
        var missions = store.LoadMissions(universe);
        var reports = store.LoadReports(universe);
        var targets = store.LoadAsteroids(universe, now - History)
            .Select(a => Target(a, missions, now))
            .OrderBy(t => t.State).ThenBy(t => t.Asteroid.DisappearsAt)
            .ToList();
        var today = AsteroidEstimator.Day(now);
        return new AsteroidOverview(
            targets,
            reports.OrderByDescending(r => r.Time).ToList(),
            AsteroidEstimator.Estimate(reports, now),
            reports.Count(r => AsteroidEstimator.Day(r.Time) == today && AsteroidEstimator.IsSuccess(r)));
    }

    public async Task<AsteroidRunResult> RunAsync(
        IOgamexClient client, AsteroidSettings settings, IProgress<AsteroidProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        int sent = 0, found = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    var cycle = await RunCycleAsync(client, settings, progress, cancellationToken).ConfigureAwait(false);
                    sent += cycle.Sent;
                    found += cycle.Found;
                    wait = NextWait(client.Universe.Name, settings);
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Asteroid cycle on {Universe} failed", client.Universe.Name);
                    Report(progress, $"{ex.Message} (retrying)");
                    wait = ErrorWait;
                }

                Report(progress, $"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AsteroidRunResult(AsteroidOutcome.Cancelled, sent, found);
        }
        catch (SessionExpiredException ex)
        {
            return new AsteroidRunResult(AsteroidOutcome.Failed, sent, found, ex.Message);
        }
    }

    /// <summary>Reads the mining reports the store doesn't know yet.</summary>
    public async Task<int> ReadReportsAsync(IOgamexClient client, IProgress<AsteroidProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var universe = client.Universe.Name;
        var known = store.LoadReports(universe).Select(r => r.Id).ToHashSet();
        var fresh = new List<AsteroidReport>();
        for (var page = 1; page <= ReportPages; page++)
        {
            var list = await client.GetAsteroidReportsAsync(page, cancellationToken).ConfigureAwait(false);
            var unknown = list.Where(r => known.Add(r.Id)).ToList();
            fresh.AddRange(unknown);

            // The list is newest first: a page with reports read before ends the new ones.
            if (list.Count == 0 || unknown.Count < list.Count)
                break;
        }

        if (fresh.Count > 0)
        {
            store.AddReports(universe, fresh);
            foreach (var report in fresh.OrderBy(r => r.Time))
                Report(progress, $"Mining report [{report.Target}]: {Describe(report)}");
        }

        return fresh.Count;
    }

    /// <summary>
    /// Reads the hint and scans the hinted systems for the asteroids not known yet, nearest to the own planets first.
    /// Returns the number of new asteroids.
    /// </summary>
    public Task<int> FindAsync(IOgamexClient client, IProgress<AsteroidProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var from = ownPlanets.GetStored(client.Universe.Name).Planets.Select(p => p.Coordinates).ToList();
        return ScanAsync(client, from, null, progress, cancellationToken);
    }

    /// <summary>
    /// Sends miners to the known open asteroids (nearest first) that they reach in time, within the fleet slots the
    /// settings allow. Returns the number of fleets sent.
    /// </summary>
    public async Task<int> SendAsync(
        IOgamexClient client, AsteroidSettings settings, IProgress<AsteroidProgress>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyCollection<Coordinates>? only = null)
    {
        if (OpenAsteroids(client.Universe.Name, only).Count == 0)
            return 0;

        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
            var round = await StartRoundAsync(client, refresh, settings, cancellationToken).ConfigureAwait(false);
            await SendToKnownAsync(client, round, settings, progress, only, cancellationToken).ConfigureAwait(false);
            return round.Sent;
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, progress).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the reports, sends miners to the known open asteroids, then scans for new ones nearest first and sends
    /// miners to each as soon as it's found; the scan stops when no miners or fleet slots are left.
    /// </summary>
    private async Task<(int Sent, int Found)> RunCycleAsync(
        IOgamexClient client, AsteroidSettings settings, IProgress<AsteroidProgress>? progress, CancellationToken cancellationToken)
    {
        await ReadReportsAsync(client, progress, cancellationToken).ConfigureAwait(false);
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
            var round = await StartRoundAsync(client, refresh, settings, cancellationToken).ConfigureAwait(false);
            if (!await SendToKnownAsync(client, round, settings, progress, null, cancellationToken).ConfigureAwait(false))
                return (round.Sent, 0);

            var found = await ScanAsync(client, round.WithMiners(), async asteroid =>
            {
                await TrySendAsync(client, round, asteroid, settings, progress, cancellationToken).ConfigureAwait(false);
                return CanSend(round, progress);
            }, progress, cancellationToken).ConfigureAwait(false);
            return (round.Sent, found);
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, progress).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Scans the hinted systems that may hold an asteroid not known yet, nearest to <paramref name="from"/> first, until
    /// each hint row has its asteroid. <paramref name="onFound"/> gets every new asteroid as soon as it's seen and
    /// returns whether to go on scanning. The systems scanned are saved to the galaxy database.
    /// </summary>
    private async Task<int> ScanAsync(
        IOgamexClient client,
        IReadOnlyCollection<Coordinates> from,
        Func<Asteroid, Task<bool>>? onFound,
        IProgress<AsteroidProgress>? progress,
        CancellationToken cancellationToken)
    {
        var universe = client.Universe.Name;
        var now = timeProvider.GetUtcNow();
        var hints = await client.GetAsteroidHintsAsync(cancellationToken).ConfigureAwait(false);
        if (hints.Count == 0)
        {
            Report(progress, "The game shows no asteroid locations.");
            return 0;
        }

        var missions = store.LoadMissions(universe);
        var known = store.LoadAsteroids(universe, now)
            .Where(a => Target(a, missions, now).State is AsteroidState.Open or AsteroidState.Sent)
            .Select(a => a.Coordinates)
            .ToHashSet();
        var searches = AsteroidPlanner.Uncovered(hints, known);
        Report(progress, $"{hints.Count} asteroid location(s): {string.Join(", ", hints)}; "
                         + (searches.Count == 0 ? "all asteroids known" : $"searching {string.Join(", ", searches)}"));
        if (searches.Count == 0)
            return 0;

        var database = await planetRepository.GetAsync(universe, cancellationToken).ConfigureAwait(false);
        var scanned = 0;
        var found = new List<Coordinates>();
        try
        {
            foreach (var address in AsteroidPlanner.ScanOrder(searches, from, client.Universe))
            {
                // The rows still without an asteroid (the ones found are matched as in AsteroidPlanner.Uncovered).
                var left = AsteroidPlanner.Uncovered(searches, found);
                if (left.Count == 0)
                    break;
                if (!left.Any(s => s.Contains(new Coordinates(address.Galaxy, address.System, 17))))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                var view = await client.GetGalaxySystemAsync(address, cancellationToken).ConfigureAwait(false);
                database.ReplaceSystem(address, view.Planets, timeProvider.GetUtcNow());
                scanned++;
                if (view.Asteroid is not { } asteroid || !known.Add(asteroid.Coordinates))
                    continue;

                found.Add(asteroid.Coordinates);
                store.SaveAsteroids(universe, [asteroid]);
                Report(progress, $"Asteroid at [{asteroid.Coordinates}], gone at {asteroid.DisappearsAt.ToLocalTime():T}");
                if (onFound is not null && !await onFound(asteroid).ConfigureAwait(false))
                {
                    Report(progress, "Scan stopped: no miners or fleet slots left to send.");
                    break;
                }
            }
        }
        finally
        {
            if (scanned > 0)
                await planetRepository.SaveAsync(database, CancellationToken.None).ConfigureAwait(false);
        }

        Report(progress, $"Scanned {scanned} system(s), found {found.Count} new asteroid(s)");
        return found.Count;
    }

    private List<Asteroid> OpenAsteroids(string universe, IReadOnlyCollection<Coordinates>? only)
    {
        var now = timeProvider.GetUtcNow();
        var missions = store.LoadMissions(universe);
        return store.LoadAsteroids(universe, now)
            .Where(a => Target(a, missions, now).State == AsteroidState.Open)
            .Where(a => only is null || only.Contains(a.Coordinates))
            .ToList();
    }

    /// <summary>Reads every planet's fleet, so each asteroid gets its miners from the nearest planet that has enough.</summary>
    private async Task<Round> StartRoundAsync(IOgamexClient client, OwnPlanetRefresh refresh, AsteroidSettings settings, CancellationToken cancellationToken)
    {
        var origins = new List<Origin>();
        foreach (var planet in refresh.List.Planets)
            origins.Add(new Origin(planet, await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false)));
        var slots = origins.Select(o => o.Fleet.Slots).FirstOrDefault(s => s is not null)
                    ?? throw new OgamexException("The fleet page shows no fleet slots.");
        var cargo = origins.Select(o => o.Fleet.CargoCapacityOf(MinerType)).FirstOrDefault(c => c > 0);
        return new Round(origins, slots, AsteroidPlanner.SlotBudget(slots, settings), cargo, store.LoadReports(client.Universe.Name));
    }

    /// <summary>Sends miners to the known open asteroids, nearest first. Returns whether more can be sent.</summary>
    private async Task<bool> SendToKnownAsync(
        IOgamexClient client, Round round, AsteroidSettings settings, IProgress<AsteroidProgress>? progress,
        IReadOnlyCollection<Coordinates>? only, CancellationToken cancellationToken)
    {
        var from = round.WithMiners();
        var open = OpenAsteroids(client.Universe.Name, only)
            .OrderBy(a => from.Count == 0 ? 0 : from.Min(c => RaidPlanner.Distance(c, a.Coordinates, client.Universe)))
            .ToList();
        foreach (var asteroid in open)
        {
            if (!CanSend(round, progress))
                return false;
            await TrySendAsync(client, round, asteroid, settings, progress, cancellationToken).ConfigureAwait(false);
        }

        return CanSend(round, progress);
    }

    /// <summary>Whether a fleet slot and miners are left (reported when not).</summary>
    private static bool CanSend(Round round, IProgress<AsteroidProgress>? progress)
    {
        if (round.Sent >= round.Budget)
        {
            Report(progress, $"No free fleet slot for more asteroids ({round.Slots.Used + round.Sent}/{round.Slots.Max} in use, {round.Budget} usable).");
            return false;
        }

        if (round.WithMiners().Count == 0)
        {
            Report(progress, "No Asteroid Miners left on any planet.");
            return false;
        }

        return true;
    }

    /// <summary>Sends miners to one asteroid when they reach it in time; the mission is saved at once.</summary>
    private async Task TrySendAsync(
        IOgamexClient client, Round round, Asteroid asteroid, AsteroidSettings settings, IProgress<AsteroidProgress>? progress, CancellationToken cancellationToken)
    {
        var mission = await PlanAsync(client, asteroid, round.Origins, round.Reports, round.Cargo, settings, progress, cancellationToken).ConfigureAwait(false);
        if (mission is null)
            return;

        var (fleet, origin, flight, estimate) = mission.Value;
        var miners = fleet.Ships[0].Quantity;
        var expected = estimate is { } e ? $"~{e.Resources:N0} resources" : "no estimate yet";
        if (settings.TestMode)
        {
            origin.Use(miners);
            round.Sent++;
            Report(progress, $"[Test] Would send {miners:N0} miners from {origin.Planet.Name} to [{asteroid.Coordinates}] ({expected}, flight {flight:g})");
            return;
        }

        var outcome = await client.SendFleetAsync(fleet, origin.Planet.Id, cancellationToken).ConfigureAwait(false);
        if (!outcome.Sent)
        {
            Report(progress, $"Mining [{asteroid.Coordinates}] was refused: {outcome.Message}");
            return;
        }

        origin.Use(miners);
        round.Sent++;
        var now = timeProvider.GetUtcNow();
        store.AddMissions(client.Universe.Name, [new AsteroidMission(asteroid.Coordinates, origin.Planet.Coordinates, miners, estimate?.Resources ?? 0, now, now + flight, now + 2 * flight)]);
        Report(progress, $"Sent {miners:N0} miners from {origin.Planet.Name} to [{asteroid.Coordinates}] ({expected}), arriving {(now + flight).ToLocalTime():T}");
    }

    /// <summary>The fleet for one asteroid, or null (reported) when no planet's miners reach it in time.</summary>
    private async Task<(FleetMission Fleet, Origin Origin, TimeSpan Flight, AsteroidEstimate? Estimate)?> PlanAsync(
        IOgamexClient client,
        Asteroid asteroid,
        IReadOnlyList<Origin> origins,
        IReadOnlyList<AsteroidReport> reports,
        long cargo,
        AsteroidSettings settings,
        IProgress<AsteroidProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = asteroid.Coordinates;
        var needed = AsteroidPlanner.MinersNeeded(AsteroidEstimator.Estimate(reports, timeProvider.GetUtcNow())?.Resources, cargo, settings);

        // The nearest planet with enough miners (or the one with most), then the nearest with any: flights only get
        // longer with the distance, so a farther planet can't make it when these don't.
        var best = RaidPlanner.ChooseOrigin(target, origins, o => o.Planet.Coordinates, o => o.Miners, needed, client.Universe);
        var nearest = origins.Where(o => o.Miners > 0).MinBy(o => RaidPlanner.Distance(o.Planet.Coordinates, target, client.Universe));
        if (best is null || nearest is null)
        {
            Report(progress, $"[{target}]: no Asteroid Miners left on any planet.");
            return null;
        }

        string? reason = null;
        foreach (var origin in new[] { best, nearest }.Distinct())
        {
            // The briefing flies from the current planet: reading the origin's fleet page selects it.
            await client.GetFleetAsync(origin.Planet.Id, cancellationToken).ConfigureAwait(false);
            var probe = new FleetMission(target, PlanetType.Planet, MissionType.AsteroidMining, [new ShipStack(MinerType, Math.Min(needed, origin.Miners))], settings.SpeedPercentage);
            var briefing = await client.GetFleetBriefingAsync(probe, cancellationToken).ConfigureAwait(false);
            if (!briefing.AvailableMissions.Contains(MissionType.AsteroidMining))
            {
                reason = briefing.Warning ?? "the game doesn't offer mining it (gone already?)";
                break;
            }

            var now = timeProvider.GetUtcNow();
            if (!AsteroidPlanner.ArrivesInTime(asteroid, briefing.OneWayFlight, now, settings))
            {
                reason = $"the flight from {origin.Planet.Name} ({briefing.OneWayFlight:g}) is too long, it's gone at {asteroid.DisappearsAt.ToLocalTime():T}";
                continue;
            }

            // The day the miners arrive decides the gains (the count restarts at 00:00 UTC).
            var estimate = AsteroidEstimator.Estimate(reports, now + briefing.OneWayFlight);
            var miners = Math.Min(AsteroidPlanner.MinersNeeded(estimate?.Resources, cargo, settings), origin.Miners);
            return (probe with { Ships = [new ShipStack(MinerType, miners)] }, origin, briefing.OneWayFlight, estimate);
        }

        Report(progress, $"[{target}] skipped: {reason}.");
        return null;
    }

    /// <summary>Until the next report is due, a fleet is back or the check interval passed (at least <see cref="MinWait"/>).</summary>
    private TimeSpan NextWait(string universe, AsteroidSettings settings)
    {
        var now = timeProvider.GetUtcNow();
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CheckIntervalMinutes));
        var next = store.LoadMissions(universe)
            .SelectMany(m => new[] { m.ArrivesAt + ReportDelay, m.ReturnsAt })
            .Where(t => t > now)
            .Select(t => t - now)
            .Append(interval)
            .Min();
        return TimeSpan.FromTicks(Math.Clamp(next.Ticks, MinWait.Ticks, interval.Ticks));
    }

    private static AsteroidTarget Target(Asteroid asteroid, IReadOnlyList<AsteroidMission> missions, DateTimeOffset now)
    {
        // A mission belongs to the asteroid when sent after it was first seen (an earlier one went to an earlier asteroid there).
        var mission = missions.LastOrDefault(m => m.Target == asteroid.Coordinates && m.SentAt >= asteroid.SeenAt);
        var state = mission is not null
            ? mission.ArrivesAt <= now ? AsteroidState.Mined : AsteroidState.Sent
            : asteroid.DisappearsAt <= now ? AsteroidState.Expired : AsteroidState.Open;
        return new AsteroidTarget(asteroid, mission, state);
    }

    private static string Describe(AsteroidReport report) => report.Kind switch
    {
        AsteroidResultKind.Resources =>
            $"{report.Gained.Metal:N0} metal, {report.Gained.Crystal:N0} crystal, {report.Gained.Deuterium:N0} deuterium"
            + (report.Available > 0 ? $" of {report.Available:N0}" : ""),
        AsteroidResultKind.DarkMatter => $"{report.DarkMatter:N0} dark matter",
        AsteroidResultKind.Container => "a container",
        _ => "nothing found"
    };

    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet, IProgress<AsteroidProgress>? progress)
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

    private static void Report(IProgress<AsteroidProgress>? progress, string message) => progress?.Report(new AsteroidProgress(message));

    /// <summary>An own planet with the miners still unused this cycle.</summary>
    private sealed class Origin(OwnPlanet planet, FleetSnapshot fleet)
    {
        private long _used;

        public OwnPlanet Planet { get; } = planet;

        public FleetSnapshot Fleet { get; } = fleet;

        public long Miners => Fleet.QuantityOf(MinerType) - _used;

        public void Use(long miners) => _used += miners;
    }

    /// <summary>One cycle's sending: the planets' miners, the fleet slots and the fleets sent so far.</summary>
    private sealed class Round(IReadOnlyList<Origin> origins, FleetSlots slots, int budget, long cargo, IReadOnlyList<AsteroidReport> reports)
    {
        public IReadOnlyList<Origin> Origins { get; } = origins;

        public FleetSlots Slots { get; } = slots;

        /// <summary>Fleets this cycle may send (<see cref="AsteroidPlanner.SlotBudget"/>).</summary>
        public int Budget { get; } = budget;

        public long Cargo { get; } = cargo;

        public IReadOnlyList<AsteroidReport> Reports { get; } = reports;

        public int Sent { get; set; }

        /// <summary>The planets that still have miners.</summary>
        public IReadOnlyList<Coordinates> WithMiners() => Origins.Where(o => o.Miners > 0).Select(o => o.Planet.Coordinates).ToList();
    }
}
