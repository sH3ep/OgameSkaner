using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Alerts;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Services.Defense;

/// <summary>A hostile fleet on its way and what the protection does about it.</summary>
public sealed record Threat(FleetMovement Movement, DateTimeOffset? EscapeAt, string State);

/// <param name="Threats">The incoming hostile fleets after a check; null for plain log lines.</param>
public sealed record DefenseProgress(string Message, IReadOnlyList<Threat>? Threats = null);

public enum DefenseOutcome
{
    Cancelled,
    Failed
}

public sealed record DefenseRunResult(DefenseOutcome Outcome, int Detected, int Escapes, string? Error = null);

/// <summary>
/// Attack protection. <see cref="RunAsync"/> reads the game's event list every
/// <see cref="DefenseSettings.CheckIntervalSeconds"/>, alerts the player about every new hostile fleet through the
/// enabled <see cref="IAlertChannel"/>s and, <see cref="DefenseSettings.EscapeSecondsBeforeArrival"/> before an attack
/// lands, deploys the attacked planet's ships (with its resources) to its moon or the nearest other planet at
/// <see cref="DefenseSettings.EscapeSpeedPercent"/>. With <see cref="DefenseSettings.RecallAfterAttack"/> it turns the
/// escaped fleet back (<see cref="IOgamexClient.ReturnFleetAsync"/>) once no attack on its planet is listed any more,
/// <see cref="DefenseSettings.RecallDelaySeconds"/> after the last one landed. Between checks it wakes up early for an
/// escape or a recall that falls due.
/// </summary>
/// <remarks>
/// Attacks on a moon are only alerted: moons aren't in the planet list yet. A fleet that landed before its recall
/// stays where it went.
/// The checks don't go through the <see cref="IActionQueue"/>, and escapes run at once next to the other modules' groups
/// (<see cref="IActionQueue.RunUrgentAsync{T}"/>).
/// </remarks>
public sealed class AttackMonitor(
    OwnPlanetService ownPlanets, IActionQueues queues, AlertDispatcher alerts, TimeProvider timeProvider, ILogger<AttackMonitor> logger)
{
    private readonly WakeUp _wakeUp = new();

    /// <summary>Ends the running loop's wait, so the next cycle starts now ("Check now"); no effect without a run.</summary>
    public void CheckNow() => _wakeUp.Now();

    private const string Owner = "Defense";

    /// <summary>After this many failed checks in a row the player is alerted that the protection is blind.</summary>
    private const int FailuresBeforeAlert = 3;

    private static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(15);

    public async Task<DefenseRunResult> RunAsync(
        IOgamexClient client, DefenseSettings settings, IProgress<DefenseProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var run = new RunState(ownPlanets.GetStored(client.Universe.Name).Planets);
        try
        {
            _wakeUp.Reset();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    wait = await RunCycleAsync(client, settings, run, progress, cancellationToken).ConfigureAwait(false);
                    run.Failures = 0;
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Attack check on {Universe} failed", client.Universe.Name);
                    Report(progress, $"{ex.Message} (retrying)");
                    if (++run.Failures == FailuresBeforeAlert)
                        Alert(run, client, "Attack protection can't read the game",
                            $"The last {FailuresBeforeAlert} checks failed: {ex.Message}", AlertSeverity.Warning);
                    wait = ErrorWait;
                }

                Report(progress, $"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                if (await _wakeUp.DelayAsync(wait, timeProvider, cancellationToken).ConfigureAwait(false))
                    Report(progress, "Checking now.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DefenseRunResult(DefenseOutcome.Cancelled, run.Seen.Count, run.Escapes);
        }
        catch (SessionExpiredException ex)
        {
            Alert(run, client, "Attack protection stopped", $"The session expired: {ex.Message}", AlertSeverity.Critical);
            return new DefenseRunResult(DefenseOutcome.Failed, run.Seen.Count, run.Escapes, ex.Message);
        }
        finally
        {
            await Task.WhenAll(run.PendingAlerts).ConfigureAwait(false);
        }
    }

    /// <summary>Sends a test alert through the enabled channels; returns the ones that failed.</summary>
    public Task<IReadOnlyList<string>> SendTestAlertAsync(string universe) =>
        alerts.SendAsync(new Alert(universe, "OgameSkaner test alert", "This is how an attack alert reaches you.", AlertSeverity.Critical));

    /// <summary>Reads the event list, alerts about new hostile fleets, escapes the due attacks. Returns the time to the next check.</summary>
    private async Task<TimeSpan> RunCycleAsync(
        IOgamexClient client, DefenseSettings settings, RunState run, IProgress<DefenseProgress>? progress, CancellationToken cancellationToken)
    {
        var movements = await client.GetFleetMovementsAsync(cancellationToken).ConfigureAwait(false);
        var incoming = movements
            .Where(m => m.IsIncomingHostile)
            .OrderBy(m => m.ArrivesAt)
            .ToList();

        // A new fleet heading for coordinates the stored list doesn't know: maybe a new colony.
        if (incoming.Any(m => !run.Seen.Contains(m.Id) && DefensePlanner.TargetOf(m, run.Planets).Planet is null))
            await RefreshPlanetsAsync(client, run, cancellationToken).ConfigureAwait(false);

        foreach (var movement in incoming.Where(m => run.Seen.Add(m.Id)))
            Detected(client, movement, settings, run, progress);

        var now = timeProvider.GetUtcNow();
        var due = DefensePlanner.DueEscapes(incoming, run.Handled, now, settings);
        if (due.Count > 0)
            await EscapeAsync(client, due, incoming, settings, run, progress, cancellationToken).ConfigureAwait(false);

        var recallIn = await RecallAsync(client, movements, incoming, settings, run, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new DefenseProgress(
            incoming.Count == 0 ? "No hostile fleets" : $"{incoming.Count} hostile fleet(s) incoming",
            incoming.Select(m => DescribeThreat(m, settings, run)).ToList()));

        // Forget fleets that left the list, so the sets don't grow over a long run.
        var listed = incoming.Select(m => m.Id).ToHashSet();
        run.Handled.IntersectWith(listed);
        foreach (var gone in run.States.Keys.Where(id => !listed.Contains(id)).ToList())
            run.States.Remove(gone);

        var wait = DefensePlanner.NextWait(incoming, run.Handled, timeProvider.GetUtcNow(), settings);
        return recallIn is { } recall && recall < wait ? TimeSpan.FromTicks(Math.Max(recall.Ticks, TimeSpan.FromSeconds(1).Ticks)) : wait;
    }

    /// <summary>
    /// Turns back the escaped fleets whose planet has no attack listed any more and whose recall time passed. Returns
    /// the time to the next recall that waits only for its time; null when none does.
    /// </summary>
    private async Task<TimeSpan?> RecallAsync(
        IOgamexClient client,
        IReadOnlyList<FleetMovement> movements,
        IReadOnlyList<FleetMovement> incoming,
        DefenseSettings settings,
        RunState run,
        IProgress<DefenseProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!settings.RecallAfterAttack)
        {
            run.Escaped.Clear();
            return null;
        }

        TimeSpan? next = null;
        var now = timeProvider.GetUtcNow();
        for (var i = run.Escaped.Count - 1; i >= 0; i--)
        {
            var escape = run.Escaped[i];
            var where = $"{escape.Planet.Name} [{escape.Planet.Coordinates}]";
            if (DefensePlanner.LastAttack(incoming, escape.Planet.Coordinates) is { } lands)
            {
                // Still under attack (maybe a new wave): keep it away.
                if (lands > escape.AttackLandsAt)
                    run.Escaped[i] = escape with { AttackLandsAt = lands };
                continue;
            }

            if (now >= escape.ArrivesAt)
            {
                run.Escaped.RemoveAt(i);
                Report(progress, $"The fleet escaped from {where} has landed at {escape.DestinationLabel} and stays there.");
                continue;
            }

            var recallAt = DefensePlanner.RecallAt(escape, settings);
            if (now < recallAt)
            {
                var left = recallAt - now;
                next = next is { } n && n < left ? n : left;
                continue;
            }

            if (DefensePlanner.FindEscaped(movements, escape, run.TurnedBack) is not { ReturnTimestamp: { } timestamp } fleet)
            {
                run.Escaped[i] = escape with { Misses = escape.Misses + 1 };
                if (escape.Misses + 1 >= FailuresBeforeAlert)
                {
                    run.Escaped.RemoveAt(i);
                    Report(progress, $"The fleet escaped from {where} isn't in the event list; it isn't turned back.");
                    Alert(run, client, $"Fleet from {escape.Planet.Name} not turned back",
                        $"The fleet escaped from {where} to {escape.DestinationLabel} wasn't found in the event list. Turn it back by hand.", AlertSeverity.Warning);
                }
                continue;
            }

            run.Escaped.RemoveAt(i);
            if (settings.TestMode)
            {
                Report(progress, $"[Test] Would turn back the fleet escaped from {where}.");
                continue;
            }

            var outcome = await queues.For(client).RunUrgentAsync(Owner, $"Turn back the fleet escaped from {where}",
                ct => client.ReturnFleetAsync(fleet.Id, timestamp, ct), cancellationToken).ConfigureAwait(false);
            if (outcome.Sent)
            {
                run.TurnedBack.Add(fleet.Id); // until the list shows it returning, it still looks like an outbound Deploy
                var home = now + (now - escape.SentAt);
                Report(progress, $"The attack on {where} is over: turned its fleet back, home about {home.ToLocalTime():T}.");
                Alert(run, client, $"Fleet returning to {escape.Planet.Name}",
                    $"The attack on {where} is over; the escaped fleet is on its way back, home about {home.ToLocalTime():T}.", AlertSeverity.Info);
            }
            else
            {
                Report(progress, $"Turning back the fleet escaped from {where} was refused: {outcome.Message}");
                Alert(run, client, $"Fleet from {escape.Planet.Name} not turned back",
                    $"The game refused to turn back the fleet escaped from {where}: {outcome.Message}. It lands at {escape.DestinationLabel} {escape.ArrivesAt.ToLocalTime():g}.",
                    AlertSeverity.Warning);
            }
        }

        return next;
    }

    private void Detected(IOgamexClient client, FleetMovement movement, DefenseSettings settings, RunState run, IProgress<DefenseProgress>? progress)
    {
        var target = DefensePlanner.TargetOf(movement, run.Planets);
        var place = target.Planet is { } planet
            ? $"{(target.OnMoon ? $"the moon {movement.TargetName}" : planet.Name)} [{movement.Target}]"
            : $"{movement.TargetName} [{movement.Target}]";
        var from = movement.SenderName is { } sender ? $"{sender} from [{movement.Origin}]" : $"[{movement.Origin}]";
        var ships = movement.ShipCount > 0 ? $", {movement.ShipCount:N0} ship(s)" : string.Empty;
        var arrival = $"arrives {movement.ArrivesAt.ToLocalTime():T} (in {Countdown(movement.ArrivesAt - timeProvider.GetUtcNow())})";

        var note = string.Empty;
        if (movement.IsAttack && settings.EscapeMode != FleetEscapeMode.Off)
        {
            if (target.OnMoon || target.Planet is null)
            {
                run.Handled.Add(movement.Id);
                run.States[movement.Id] = target.OnMoon ? "Moon: not escaped" : "Unknown planet: not escaped";
                note = target.OnMoon ? " Moons aren't escaped yet." : " The target isn't in the planet list, so it isn't escaped.";
            }
            else
            {
                note = $" The fleet leaves at {DefensePlanner.EscapeAt(movement, settings).ToLocalTime():T}.";
            }
        }

        var title = $"{MissionName(movement.Mission)} on {place}";
        var message = $"{MissionName(movement.Mission)} by {from}{ships}, {arrival}.{note}";
        logger.LogWarning("{Universe}: {Message}", client.Universe.Name, message);
        Report(progress, $"⚠ {title}: {message}");

        if (DefensePlanner.ShouldNotify(movement, settings))
            Alert(run, client, title, message, movement.Mission == MovementMission.Espionage ? AlertSeverity.Warning : AlertSeverity.Critical);
    }

    /// <summary>Escapes the attacked planets of <paramref name="due"/> (each once), without waiting for the action queue.</summary>
    private Task EscapeAsync(
        IOgamexClient client,
        IReadOnlyList<FleetMovement> due,
        IReadOnlyList<FleetMovement> incoming,
        DefenseSettings settings,
        RunState run,
        IProgress<DefenseProgress>? progress,
        CancellationToken cancellationToken) =>
        queues.For(client).RunUrgentAsync(Owner, $"Escape from {string.Join(", ", due.Select(m => m.Target).Distinct())}", async ct =>
        {
            await RefreshPlanetsAsync(client, run, ct).ConfigureAwait(false);
            var attacked = incoming.Where(m => m.IsAttack).Select(m => m.Target).ToHashSet();
            foreach (var group in due.GroupBy(m => m.Target))
            {
                var ids = group.Select(m => m.Id).ToList();
                var target = DefensePlanner.TargetOf(group.First(), run.Planets);
                var state = target.Planet is { } planet && !target.OnMoon
                    ? await EscapePlanetAsync(client, planet, attacked, DefensePlanner.LastAttack(incoming, planet.Coordinates), settings, run, progress, ct)
                        .ConfigureAwait(false)
                    : "Not escaped";
                foreach (var id in ids)
                {
                    run.Handled.Add(id);
                    run.States[id] = state;
                }
            }
        }, cancellationToken);

    /// <summary>Deploys the planet's ships to the first destination the server accepts. Returns the threat's new state.</summary>
    private async Task<string> EscapePlanetAsync(
        IOgamexClient client,
        OwnPlanet planet,
        IReadOnlyCollection<Coordinates> attacked,
        DateTimeOffset? attackLandsAt,
        DefenseSettings settings,
        RunState run,
        IProgress<DefenseProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fleet = await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false);
        var ships = DefensePlanner.EscapingShips(fleet);
        if (ships.Count == 0)
        {
            Report(progress, $"No ships on {planet.Name} [{planet.Coordinates}] to escape.");
            return "No ships to escape";
        }

        var shipCount = ships.Sum(s => s.Quantity);
        var speed = Math.Clamp(settings.EscapeSpeedPercent, 1, 100);
        var refusals = new List<string>();
        foreach (var destination in DefensePlanner.Destinations(planet, run.Planets, attacked, settings, client.Universe))
        {
            var mission = new FleetMission(destination.Target, destination.TargetType, MissionType.Deploy, ships, speed) { MayUseLastSlot = true };
            FleetBriefing briefing;
            try
            {
                briefing = await client.GetFleetBriefingAsync(mission, planet.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (OgamexException ex) when (ex is not SessionExpiredException)
            {
                refusals.Add($"{destination.Label}: {ex.Message}");
                continue;
            }

            if (!briefing.AvailableMissions.Contains(MissionType.Deploy))
            {
                refusals.Add($"{destination.Label}: can't deploy there");
                continue;
            }

            var cargo = settings.TakeResources
                ? DefensePlanner.Cargo(fleet.Resources ?? ResourceSet.Zero, DefensePlanner.CargoCapacity(ships, fleet), briefing.DeuteriumConsumption)
                : ResourceSet.Zero;
            mission = mission with { Cargo = cargo };
            var what = $"{shipCount:N0} ship(s) from {planet.Name} [{planet.Coordinates}] to {destination.Label} at {speed}%"
                       + (cargo == ResourceSet.Zero ? string.Empty : $" with {cargo.Metal:N0} metal, {cargo.Crystal:N0} crystal, {cargo.Deuterium:N0} deuterium")
                       + $" (flight {briefing.OneWayFlight:g})";

            if (settings.TestMode)
            {
                Report(progress, $"[Test] Would send {what}");
                return $"[Test] Would escape to {destination.Label}";
            }

            var outcome = await client.SendFleetAsync(mission, planet.Id, cancellationToken).ConfigureAwait(false);
            if (!outcome.Sent)
            {
                refusals.Add($"{destination.Label}: {outcome.Message}");
                continue;
            }

            run.Escapes++;
            var sentAt = timeProvider.GetUtcNow();
            if (settings.RecallAfterAttack)
                run.Escaped.Add(new EscapedFleet(planet, destination.Target, destination.Label, sentAt, sentAt + briefing.OneWayFlight, attackLandsAt ?? sentAt));
            Report(progress, $"Escaped: sent {what}");
            Alert(run, client, $"Fleet escaped from {planet.Name}", $"Sent {what}.", AlertSeverity.Info);
            return $"Escaped to {destination.Label}";
        }

        var reasons = refusals.Count == 0 ? "no destination (escape is off or there's no other planet)" : string.Join("; ", refusals);
        Report(progress, $"Escape from {planet.Name} [{planet.Coordinates}] failed: {reasons}");
        Alert(run, client, $"Fleet escape from {planet.Name} failed", $"{shipCount:N0} ship(s) stay on [{planet.Coordinates}]: {reasons}.", AlertSeverity.Critical);
        return "Escape failed";
    }

    private async Task RefreshPlanetsAsync(IOgamexClient client, RunState run, CancellationToken cancellationToken)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        run.Planets = refresh.List.Planets;
    }

    private Threat DescribeThreat(FleetMovement movement, DefenseSettings settings, RunState run)
    {
        if (run.States.TryGetValue(movement.Id, out var state))
            return new Threat(movement, null, state);
        if (!movement.IsAttack)
            return new Threat(movement, null, movement.Mission == MovementMission.Espionage ? "Spy probes" : "Alert only");
        return settings.EscapeMode == FleetEscapeMode.Off
            ? new Threat(movement, null, "Escape off")
            : new Threat(movement, DefensePlanner.EscapeAt(movement, settings), "Escape pending");
    }

    /// <summary>Sends without waiting, so a slow channel can't delay an escape; the run waits for all at its end.</summary>
    private void Alert(RunState run, IOgamexClient client, string title, string message, AlertSeverity severity)
    {
        run.PendingAlerts.RemoveAll(t => t.IsCompleted);
        run.PendingAlerts.Add(alerts.SendAsync(new Alert(client.Universe.Name, title, message, severity), CancellationToken.None));
    }

    private static string MissionName(MovementMission mission) => mission switch
    {
        MovementMission.AcsAttack => "ACS attack",
        MovementMission.MissileAttack => "Missile attack",
        MovementMission.Espionage => "Espionage",
        _ => mission.ToString()
    };

    private static string Countdown(TimeSpan left) =>
        left <= TimeSpan.Zero ? "0:00" : left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");

    private static void Report(IProgress<DefenseProgress>? progress, string message) => progress?.Report(new DefenseProgress(message));

    private sealed class RunState(IReadOnlyList<OwnPlanet> planets)
    {
        public IReadOnlyList<OwnPlanet> Planets { get; set; } = planets;

        /// <summary>Hostile fleets already alerted about.</summary>
        public HashSet<string> Seen { get; } = [];

        /// <summary>Attacks escaped (or found not escapable); still listed in the game.</summary>
        public HashSet<string> Handled { get; } = [];

        /// <summary>What happened to each handled fleet, for the page.</summary>
        public Dictionary<string, string> States { get; } = [];

        public List<Task> PendingAlerts { get; } = [];

        /// <summary>Fleets escaped by this run, waiting to be turned back after the attack.</summary>
        public List<EscapedFleet> Escaped { get; } = [];

        /// <summary>Ids of the fleets turned back, so another escape of the same planet never matches them.</summary>
        public HashSet<string> TurnedBack { get; } = [];

        public int Escapes { get; set; }

        public int Failures { get; set; }
    }
}
