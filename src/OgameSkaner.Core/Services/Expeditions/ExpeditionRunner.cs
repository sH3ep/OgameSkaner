using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Expeditions;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;

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
/// (<see cref="ExpeditionPlanner.Fleet"/>), as far as the expedition and fleet slots allow. Then the game is switched
/// back to the planet selected before and the runner sleeps until an expedition comes back or the check interval passed.
/// </summary>
/// <remarks>
/// The event list may not list an expedition while it stays at position 16, so the runner also counts the expeditions it
/// sent itself until their computed return and takes the larger count.
/// </remarks>
public sealed class ExpeditionRunner(OwnPlanetService ownPlanets, TimeProvider timeProvider, ILogger<ExpeditionRunner> logger)
{
    private static readonly TimeSpan ErrorWait = TimeSpan.FromMinutes(1);

    public async Task<ExpeditionRunResult> RunAsync(
        IOgamexClient client, ExpeditionSettings settings, IProgress<ExpeditionProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var state = new RunState();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    await RunCycleAsync(client, settings, state, progress, cancellationToken).ConfigureAwait(false);
                    var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CheckIntervalMinutes));
                    wait = ExpeditionPlanner.NextWait(state.Changes(), timeProvider.GetUtcNow(), interval);
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Expedition cycle on {Universe} failed", client.Universe.Name);
                    Report(progress, $"{ex.Message} (retrying)");
                    wait = ErrorWait;
                }

                Report(progress, $"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
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
    }

    private async Task RunCycleAsync(
        IOgamexClient client, ExpeditionSettings settings, RunState state, IProgress<ExpeditionProgress>? progress, CancellationToken cancellationToken)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
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

            progress?.Report(new ExpeditionProgress("", new ExpeditionOverview(WithFlying(origins, state, timeProvider.GetUtcNow()), fleets, expeditions)));
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, progress).ConfigureAwait(false);
        }
    }

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

                // Reading the origin's fleet page selects it, so the briefing flies from there.
                var fleet = await client.GetFleetAsync(place.Id, cancellationToken).ConfigureAwait(false);
                if (budget is null)
                {
                    fleets = fleet.Slots ?? throw new OgamexException("The fleet page shows no fleet slots.");
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
                var briefing = await client.GetFleetBriefingAsync(mission, cancellationToken).ConfigureAwait(false);
                if (!briefing.AvailableMissions.Contains(MissionType.Expedition))
                {
                    var warning = briefing.Warning is { } w ? $" ({w})" : "";
                    Report(progress, $"{place}: the game doesn't offer an expedition to [{mission.Target}]{warning}.");
                    break;
                }

                var hold = ExpeditionPlanner.Hold(briefing.ExpeditionDurations, settings.HoldMinutes);
                mission = mission with { HoldMinutes = hold };
                var returnsAt = timeProvider.GetUtcNow() + 2 * briefing.OneWayFlight + TimeSpan.FromMinutes(hold);
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
                state.Sent.Add(new SentExpedition(place.Id, returnsAt));
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

    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet, IProgress<ExpeditionProgress>? progress)
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

    private static void Report(IProgress<ExpeditionProgress>? progress, string message) => progress?.Report(new ExpeditionProgress(message));

    private sealed record SentExpedition(PlanetId Origin, DateTimeOffset ReturnsAt);

    private sealed class RunState
    {
        public IReadOnlyList<FleetMovement> Movements { get; set; } = [];

        public List<SentExpedition> Sent { get; } = [];

        public HashSet<string> ReportedMissing { get; } = [];

        public int Total { get; set; }

        /// <summary>When an expedition arrives or comes back: the event list's times and this run's computed returns.</summary>
        public IEnumerable<DateTimeOffset> Changes() =>
            ExpeditionPlanner.OwnExpeditions(Movements).Select(m => m.ArrivesAt).Concat(Sent.Select(s => s.ReturnsAt));
    }
}
