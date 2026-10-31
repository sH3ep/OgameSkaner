using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Expeditions;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Services.Transports;

namespace OgameSkaner.Core.Services.Expeditions;

/// <summary>A planet or moon with its configured and flying expeditions.</summary>
public sealed record ExpeditionOriginState(OwnPlanet Place, bool IsMoon, int Count, int Flying);

/// <summary>What the Expeditions page shows after a check.</summary>
public sealed record ExpeditionOverview(IReadOnlyList<ExpeditionOriginState> Origins, FleetSlots? Fleets, FleetSlots? Expeditions);

/// <param name="Overview">Set (with an empty message) after each check.</param>
public sealed record ExpeditionProgress(string Message, ExpeditionOverview? Overview = null);

public enum ExpeditionOutcome
{
    Cancelled,
    Failed
}

public sealed record ExpeditionRunResult(ExpeditionOutcome Outcome, int Sent, string? Error = null);

/// <summary>
/// Keeps expeditions flying. Each cycle of <see cref="RunAsync"/> reads the planets (and moons) and the event list, and
/// every origin short of its configured count sends expeditions to position 16 of its system with the ticked ships
/// (<see cref="ExpeditionPlanner.Fleet"/>), as far as the expedition and fleet slots allow (the cycle is one
/// <see cref="IActionQueue"/> group). With <see cref="ExpeditionSettings.CollectDebris"/> the same group then reads the
/// galaxy page of every origin's system once one of its expeditions reached position 16 (fights with pirates or aliens
/// leave a debris field there) and sends Recyclers from that system's origin to a debris field of at least
/// <see cref="ExpeditionSettings.MinDebris"/>. Then the runner sleeps until an expedition (or Recycler) arrives or comes
/// back, or the check interval passed.
/// </summary>
/// <remarks>
/// The event list may not list an expedition while it stays at position 16, so the runner also counts the expeditions it
/// sent itself until their computed return and takes the larger count.
/// </remarks>
public sealed class ExpeditionRunner(
    OwnPlanetService ownPlanets, IActionQueues queues, TimeProvider timeProvider, ILogger<ExpeditionRunner> logger, IFleetSlotClaimsProvider? claims = null)
{
    private readonly WakeUp _wakeUp = new();

    /// <summary>Ends the running loop's wait, so the next cycle starts now ("Check now"); no effect without a run.</summary>
    public void CheckNow() => _wakeUp.Now();

    private const string Owner = "Expeditions";

    private static readonly TimeSpan ErrorWait = TimeSpan.FromMinutes(1);

    public async Task<ExpeditionRunResult> RunAsync(
        IOgamexClient client, ExpeditionSettings settings, IProgress<ExpeditionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var state = new RunState();
        try
        {
            _wakeUp.Reset();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    await queues.For(client).RunAsync(Owner, "Send the due expeditions", ct => RunCycleAsync(client, settings, state, progress, ct), cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    Reserve(client, settings, state);
                    var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CheckIntervalMinutes));
                    wait = ExpeditionPlanner.NextWait(state.Changes(settings.CollectDebris), timeProvider.GetUtcNow(), interval);
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Expedition cycle on {Universe} failed", client.Universe.Name);
                    Report(progress, $"{ex.Message} (retrying)");
                    wait = ErrorWait;
                }

                Report(progress, $"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                if (await _wakeUp.DelayAsync(wait, timeProvider, cancellationToken).ConfigureAwait(false))
                    Report(progress, "Checking now.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ExpeditionRunResult(ExpeditionOutcome.Cancelled, state.Total);
        }
        catch (SessionExpiredException ex)
        {
            return new ExpeditionRunResult(ExpeditionOutcome.Failed, state.Total, ex.Message);
        }
        finally
        {
            claims?.For(client).Release(Owner);
        }
    }

    /// <summary>
    /// With <see cref="ExpeditionSettings.ReserveSlots"/>, reserves the fleet slots of all configured expeditions (at most
    /// the expedition slots): the ones in the air hold theirs until they return, the rest are kept from the other modules.
    /// </summary>
    private void Reserve(IOgamexClient client, ExpeditionSettings settings, RunState state)
    {
        if (claims?.For(client) is not { } ledger)
            return;
        // Test mode sends nothing, so it holds nothing back from the other modules either.
        if (!settings.ReserveSlots || settings.TestMode || settings.ShipTypes.Count == 0)
        {
            ledger.Release(Owner);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var origins = state.Origins.Where(o => o.Count > 0).ToList();
        var busy = origins.SelectMany(o => ExpeditionPlanner.BusyUntil(state.Movements, o.Place, state.Origins.Count(p => p.Place.Coordinates == o.Place.Coordinates) > 1,
            state.Sent.Where(s => s.Origin == o.Place.Id).Select(s => s.ReturnsAt), o.Count, now)).ToList();
        var slots = origins.Sum(o => o.Count);
        if (state.ExpeditionSlots is { } expeditionSlots)
            slots = Math.Min(slots, expeditionSlots.Max);
        ledger.Reserve(Owner, slots, busy);
    }

    private async Task RunCycleAsync(
        IOgamexClient client, ExpeditionSettings settings, RunState state, IProgress<ExpeditionProgress>? progress, CancellationToken cancellationToken)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        var places = refresh.List.Planets.Select(p => (Place: p, IsMoon: false))
            .Concat(refresh.List.Moons.Select(m => (Place: m, IsMoon: true)))
            .ToList();
        ReportMissing(settings, places.Select(p => p.Place.Id.Value).ToHashSet(), state, progress);

        state.Movements = await client.GetFleetMovementsAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        state.Sent.RemoveAll(s => s.ReturnsAt <= now);
        var origins = places
            .Select(p => new ExpeditionOriginState(p.Place, p.IsMoon, Math.Max(0, settings.CountFor(p.Place.Id.Value)), 0))
            .ToList();
        origins = WithFlying(origins, state, now);
        state.Origins = origins;

        var due = origins.Where(o => o.Count > o.Flying)
            .OrderBy(o => IndexOf(settings, o.Place.Id))
            .ToList();
        FleetSlots? fleets = null, expeditions = null;
        if (settings.ShipTypes.Count == 0)
            Report(progress, "No ship type is ticked.");
        else if (origins.All(o => o.Count == 0))
            Report(progress, "No planet sends expeditions (set a count above 0).");
        else if (due.Count == 0)
            Report(progress, "Every planet has its expeditions in the air.");
        else
            (fleets, expeditions) = await SendAsync(client, settings, due, state, progress, cancellationToken).ConfigureAwait(false);
        state.ExpeditionSlots = expeditions ?? state.ExpeditionSlots;

        if (settings.CollectDebris)
            await CollectDebrisAsync(client, settings, origins, state, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ExpeditionProgress("", new ExpeditionOverview(WithFlying(origins, state, timeProvider.GetUtcNow()), fleets, expeditions)));
    }

    /// <summary>
    /// Reads the systems of the origins whose expeditions (or Recyclers) arrived since the system's last read (all of them
    /// on the run's first cycle) and sends Recyclers to a debris field at position 16 that has none on the way yet.
    /// </summary>
    private async Task CollectDebrisAsync(
        IOgamexClient client, ExpeditionSettings settings, IReadOnlyList<ExpeditionOriginState> origins, RunState state,
        IProgress<ExpeditionProgress>? progress, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var movement in ExpeditionPlanner.OwnExpeditions(state.Movements).Where(m => m.Target.Position == ExpeditionPlanner.Position))
            state.Arrivals.Add((System(movement.Target), movement.ArrivesAt));
        state.Recycling.RemoveAll(r => r.ArrivesAt <= now);

        var systems = origins.Where(o => o.Count > 0)
            .OrderBy(o => IndexOf(settings, o.Place.Id))
            .GroupBy(o => System(o.Place.Coordinates));
        foreach (var system in systems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.DebrisChecked.TryGetValue(system.Key, out var last) && !state.Arrivals.Any(a => a.System == system.Key && a.At > last && a.At <= now))
                continue;

            var view = await client.GetGalaxySystemAsync(system.Key, cancellationToken).ConfigureAwait(false);
            state.DebrisChecked[system.Key] = now;
            state.Arrivals.RemoveWhere(a => a.System == system.Key && a.At <= now);

            var target = ExpeditionPlanner.Target(system.First().Place.Coordinates);
            var debris = view.DebrisAt(ExpeditionPlanner.Position);
            var total = TransportSender.Total(debris);
            if (total == 0 || ExpeditionPlanner.Recycling(state.Movements, target) || state.Recycling.Any(r => r.Target == target))
                continue;
            if (total < settings.MinDebris)
            {
                Report(progress, $"Debris field at [{target}] ({Describe(debris)}) is below {settings.MinDebris:N0}; no Recyclers sent.");
                continue;
            }

            await SendRecyclersAsync(client, settings, system.ToList(), target, debris, state, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends Recyclers to <paramref name="target"/> from the first origin of its system that has any at home.</summary>
    private async Task SendRecyclersAsync(
        IOgamexClient client, ExpeditionSettings settings, IReadOnlyList<ExpeditionOriginState> origins, Coordinates target, ResourceSet debris,
        RunState state, IProgress<ExpeditionProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var origin in origins)
        {
            var place = origin.Place;
            var fleet = await client.GetFleetAsync(place.Id, cancellationToken).ConfigureAwait(false);
            var count = ExpeditionPlanner.Recyclers(debris, fleet.CargoCapacityOf(ExpeditionPlanner.RecyclerType), fleet.QuantityOf(ExpeditionPlanner.RecyclerType));
            if (count == 0)
                continue;

            // Slots other modules claimed count as used.
            var read = fleet.Slots ?? throw new OgamexException("The fleet page shows no fleet slots.");
            var slots = claims?.For(client).Effective(Owner, read) ?? read;
            if (slots.Free - Math.Max(FleetSlots.KeptForEmergencies, settings.KeepFleetSlotsFree) <= 0)
            {
                Report(progress, $"Debris field at [{target}] ({Describe(debris)}): no free fleet slot for the Recyclers (fleets {slots.Used}/{slots.Max}).");
                return;
            }

            var mission = new FleetMission(target, PlanetType.DebrisField, MissionType.Recycle, [new ShipStack(ExpeditionPlanner.RecyclerType, count)]);
            var briefing = await client.GetFleetBriefingAsync(mission, place.Id, cancellationToken).ConfigureAwait(false);
            if (!briefing.AvailableMissions.Contains(MissionType.Recycle))
            {
                var warning = briefing.Warning is { } w ? $" ({w})" : "";
                Report(progress, $"{place}: the game doesn't offer Recycle to the debris field at [{target}]{warning}.");
                return;
            }

            var description = $"{count:N0} {UnitCatalog.NameOf(ExpeditionPlanner.RecyclerType)} from {place} to the debris field at [{target}] ({Describe(debris)})";
            if (!settings.TestMode)
            {
                var outcome = await client.SendFleetAsync(mission, place.Id, cancellationToken).ConfigureAwait(false);
                if (!outcome.Sent)
                {
                    Report(progress, $"{place}: the Recyclers were refused: {outcome.Message}");
                    return;
                }
            }

            // Their arrival reads the system again, so what they couldn't carry gets more Recyclers.
            var arrivesAt = timeProvider.GetUtcNow() + briefing.OneWayFlight;
            state.Recycling.Add(new SentRecycling(target, arrivesAt));
            state.Arrivals.Add((System(target), arrivesAt));
            Report(progress, settings.TestMode ? $"[Test] Would send {description}" : $"Sent {description}");
            return;
        }

        Report(progress, $"Debris field at [{target}] ({Describe(debris)}): no Recyclers at home in that system.");
    }

    private static SolarSystemAddress System(Coordinates coordinates) => new(coordinates.Galaxy, coordinates.System);

    private static string Describe(ResourceSet debris) =>
        $"{debris.Metal:N0} metal, {debris.Crystal:N0} crystal" + (debris.Deuterium > 0 ? $", {debris.Deuterium:N0} deuterium" : "");

    /// <summary>Sends the due expeditions in the settings' order until the slots run out; returns the slots read.</summary>
    private async Task<(FleetSlots? Fleets, FleetSlots? Expeditions)> SendAsync(
        IOgamexClient client, ExpeditionSettings settings, IReadOnlyList<ExpeditionOriginState> due, RunState state,
        IProgress<ExpeditionProgress>? progress, CancellationToken cancellationToken)
    {
        FleetSlots? fleets = null, expeditions = null;
        int? budget = null;
        var sent = 0;
        foreach (var origin in due)
        {
            var place = origin.Place;
            for (var remaining = origin.Count - origin.Flying; remaining > 0; remaining--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fleet = await client.GetFleetAsync(place.Id, cancellationToken).ConfigureAwait(false);
                if (budget is null)
                {
                    // Slots other modules claimed count as used.
                    var read = fleet.Slots ?? throw new OgamexException("The fleet page shows no fleet slots.");
                    fleets = claims?.For(client).Effective(Owner, read) ?? read;
                    expeditions = fleet.ExpeditionSlots;
                    budget = ExpeditionPlanner.Budget(fleets.Value, expeditions, settings.KeepFleetSlotsFree);
                }

                if (sent >= budget)
                {
                    Report(progress, $"No free slot for more expeditions (expeditions {Describe(expeditions, sent)}, fleets {Describe(fleets, sent)}, "
                                     + $"{settings.KeepFleetSlotsFree} fleet slot(s) kept free).");
                    return (fleets, expeditions);
                }

                var ships = ExpeditionPlanner.Fleet(fleet, settings.ShipTypes, remaining);
                if (ships.Count == 0)
                {
                    Report(progress, $"{place}: none of the ticked ships is home.");
                    break;
                }

                var mission = new FleetMission(
                    ExpeditionPlanner.Target(place.Coordinates), PlanetType.Planet, MissionType.Expedition, ships, Math.Clamp(settings.SpeedPercent, 10, 100));
                var briefing = await client.GetFleetBriefingAsync(mission, place.Id, cancellationToken).ConfigureAwait(false);
                if (!briefing.AvailableMissions.Contains(MissionType.Expedition))
                {
                    var warning = briefing.Warning is { } w ? $" ({w})" : "";
                    Report(progress, $"{place}: the game doesn't offer an expedition to [{mission.Target}]{warning}.");
                    break;
                }

                var hold = ExpeditionPlanner.Hold(briefing.ExpeditionDurations, settings.HoldMinutes);
                mission = mission with { HoldMinutes = hold };
                var arrivesAt = timeProvider.GetUtcNow() + briefing.OneWayFlight;
                var returnsAt = arrivesAt + briefing.OneWayFlight + TimeSpan.FromMinutes(hold);
                var description = $"{Describe(ships)} from {place} to [{mission.Target}] for {Duration(hold)}, back at {returnsAt.ToLocalTime():g}";
                if (!settings.TestMode)
                {
                    var outcome = await client.SendFleetAsync(mission, place.Id, cancellationToken).ConfigureAwait(false);
                    if (!outcome.Sent)
                    {
                        Report(progress, $"{place}: the expedition was refused: {outcome.Message}");
                        break;
                    }
                }

                sent++;
                state.Total++;
                state.Sent.Add(new SentExpedition(place.Id, arrivesAt, returnsAt));
                state.Arrivals.Add((System(mission.Target), arrivesAt));
                Report(progress, settings.TestMode ? $"[Test] Would send {description}" : $"Sent {description}");
            }
        }

        return (fleets, expeditions);
    }

    /// <summary>
    /// Each origin's flying expeditions: the larger of the event list's count and the expeditions this run sent that
    /// aren't back yet. A planet and its moon share coordinates, so then the event list's name tells them apart.
    /// </summary>
    private static List<ExpeditionOriginState> WithFlying(IReadOnlyList<ExpeditionOriginState> origins, RunState state, DateTimeOffset now) =>
        origins.Select(o => o with
        {
            Flying = Math.Max(
                ExpeditionPlanner.InFlight(state.Movements, o.Place, origins.Count(p => p.Place.Coordinates == o.Place.Coordinates) > 1),
                state.Sent.Count(s => s.Origin == o.Place.Id && s.ReturnsAt > now))
        }).ToList();

    private static int IndexOf(ExpeditionSettings settings, PlanetId id)
    {
        for (var i = 0; i < settings.Origins.Count; i++)
        {
            if (settings.Origins[i].Id == id.Value)
                return i;
        }

        return int.MaxValue;
    }

    /// <summary>Origins of the settings the game doesn't list (any more), reported once per run.</summary>
    private static void ReportMissing(ExpeditionSettings settings, HashSet<string> known, RunState state, IProgress<ExpeditionProgress>? progress)
    {
        var missing = settings.Origins.Count(o => o.Count > 0 && !known.Contains(o.Id) && state.ReportedMissing.Add(o.Id));
        if (missing > 0)
            Report(progress, $"{missing} planet(s) or moon(s) set to send expeditions aren't in the planet list any more and are skipped.");
    }

    private static string Describe(IEnumerable<ShipStack> ships) => string.Join(", ", ships.Select(s => $"{s.Quantity:N0} {UnitCatalog.NameOf(s.ShipType)}"));

    /// <summary>The slots as read at the start of the sending plus the expeditions sent since.</summary>
    private static string Describe(FleetSlots? slots, int sent) => slots is { } s ? $"{s.Used + sent}/{s.Max}" : "?";

    private static string Duration(int minutes) => minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";

    private static void Report(IProgress<ExpeditionProgress>? progress, string message) => progress?.Report(new ExpeditionProgress(message));

    private sealed record SentExpedition(PlanetId Origin, DateTimeOffset ArrivesAt, DateTimeOffset ReturnsAt);

    private sealed record SentRecycling(Coordinates Target, DateTimeOffset ArrivesAt);

    private sealed class RunState
    {
        public IReadOnlyList<FleetMovement> Movements { get; set; } = [];

        public List<SentExpedition> Sent { get; } = [];

        public HashSet<string> ReportedMissing { get; } = [];

        public int Total { get; set; }

        /// <summary>The places with their configured counts, from the last cycle.</summary>
        public IReadOnlyList<ExpeditionOriginState> Origins { get; set; } = [];

        /// <summary>The expedition slots as last read.</summary>
        public FleetSlots? ExpeditionSlots { get; set; }

        /// <summary>When each system's debris field was last read.</summary>
        public Dictionary<SolarSystemAddress, DateTimeOffset> DebrisChecked { get; } = new();

        /// <summary>When expeditions (or Recyclers) arrive in a system, so its debris field is read again after.</summary>
        public HashSet<(SolarSystemAddress System, DateTimeOffset At)> Arrivals { get; } = [];

        /// <summary>The Recyclers this run sent that haven't arrived yet (the event list may not show them yet).</summary>
        public List<SentRecycling> Recycling { get; } = [];

        /// <summary>
        /// When an expedition arrives or comes back: the event list's times and this run's computed returns; with
        /// <paramref name="debris"/> also this run's computed arrivals at position 16 and the sent Recyclers' arrivals.
        /// </summary>
        public IEnumerable<DateTimeOffset> Changes(bool debris) =>
            ExpeditionPlanner.OwnExpeditions(Movements).Select(m => m.ArrivesAt)
                .Concat(Sent.Select(s => s.ReturnsAt))
                .Concat(debris ? Sent.Select(s => s.ArrivesAt).Concat(Recycling.Select(r => r.ArrivesAt)) : []);
    }
}
