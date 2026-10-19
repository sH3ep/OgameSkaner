using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Services.Moons;

public sealed record MoonProgress(string Message);

public enum MoonOutcome
{
    Cancelled,
    Failed
}

public sealed record MoonRunResult(MoonOutcome Outcome, int Queued, int Transports, string? Error = null);

/// <summary>A moon creation's result: the new moon, or why not.</summary>
public sealed record MoonCreation(OwnPlanet Planet, PlanetId? Moon, string? Error);

/// <summary>
/// The Builder's Moons tab. Each cycle of <see cref="RunAsync"/> refreshes the planets and moons, reads the event list,
/// and for every ticked moon reads its facility page, plans it (<see cref="MoonPlanner"/>: simulation or schema), queues
/// what the moon can pay and, with <see cref="MoonSettings.AutoTransport"/>, sends its planet's cargo ships with what the
/// next queue items still need (at most <see cref="MoonSettings.MaxServerQueueDepth"/> of them, starting within
/// <see cref="MoonSettings.HorizonHours"/>; moons are unguarded, so never more). Then it
/// sleeps until a queue item ends, a transport lands or the check interval passes.
/// </summary>
/// <remarks>
/// Transports this app sent are remembered until they land (the event list shows no cargo); while a transport to a moon
/// the app didn't send is on its way, that moon gets none. Every game request is an <see cref="IActionQueue"/> group:
/// reading, one per moon for building, one per transport.
/// </remarks>
public sealed class MoonRunner(OwnPlanetService ownPlanets, IActionQueues queues, TimeProvider timeProvider, ILogger<MoonRunner> logger)
{
    private readonly WakeUp _wakeUp = new();

    /// <summary>Ends the running loop's wait, so the next cycle starts now ("Check now"); no effect without a run.</summary>
    public void CheckNow() => _wakeUp.Now();

    private const string Owner = "Moons";

    private static readonly TimeSpan MinWait = TimeSpan.FromSeconds(30);

    /// <summary>With every fleet slot taken the moons pause and this much later the slots are checked again.</summary>
    public static readonly TimeSpan NoSlotWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromMinutes(1);

    /// <summary>A moment after an event, so the game shows its result.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly List<Transport> _sent = [];

    /// <summary>The moon a cycle stopped at for want of a fleet slot; the next cycle starts with it.</summary>
    private PlanetId? _resumeAt;

    public async Task<MoonRunResult> RunAsync(
        IOgamexClient client, MoonSettings settings, IProgress<MoonProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        int queued = 0, transports = 0;
        try
        {
            _wakeUp.Reset();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan wait;
                try
                {
                    var cycle = await RunCycleAsync(client, settings, settings.AutoTransport, ActionPriority.Automation, progress, cancellationToken).ConfigureAwait(false);
                    queued += cycle.Queued;
                    transports += cycle.Transports;
                    wait = cycle.Wait;
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Moon cycle on {Universe} failed", client.Universe.Name);
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
            return new MoonRunResult(MoonOutcome.Cancelled, queued, transports);
        }
        catch (SessionExpiredException ex)
        {
            return new MoonRunResult(MoonOutcome.Failed, queued, transports, ex.Message);
        }
    }

    /// <summary>
    /// The "Send resources now" button: one cycle with transports (and queuing what the moons can pay), as player groups.
    /// Returns the number of transports sent.
    /// </summary>
    public async Task<int> TransportNowAsync(
        IOgamexClient client, MoonSettings settings, IProgress<MoonProgress>? progress = null, CancellationToken cancellationToken = default) =>
        (await RunCycleAsync(client, settings, transport: true, ActionPriority.User, progress, cancellationToken).ConfigureAwait(false)).Transports;

    /// <summary>
    /// Creates a moon of <paramref name="diameter"/> km around each of <paramref name="planets"/> (one player group): each
    /// planet's formation page is read first, a planet with a moon is skipped, one without the Graviton level or the metal
    /// is refused, and a refusal doesn't stop the others.
    /// </summary>
    public Task<IReadOnlyList<MoonCreation>> CreateMoonsAsync(
        IOgamexClient client, IReadOnlyList<OwnPlanet> planets, int diameter, bool testMode,
        IProgress<MoonProgress>? progress = null, CancellationToken cancellationToken = default) =>
        queues.For(client).RunAsync<IReadOnlyList<MoonCreation>>(Owner, $"Create moons on {planets.Count} planet(s)", async ct =>
        {
            var cost = MoonFormation.MetalCost(diameter);
            var results = new List<MoonCreation>();
            foreach (var planet in planets)
            {
                ct.ThrowIfCancellationRequested();
                string? error = null;
                PlanetId? moon = null;
                try
                {
                    var info = await client.GetMoonFormationAsync(planet.Id, ct).ConfigureAwait(false);
                    var graviton = MoonFormation.GravitonNeeded(diameter);
                    if (info.HasMoon)
                        error = $"it has a moon already ({info.CurrentDiameter:N0} km)";
                    else if (diameter < info.MinDiameter || diameter > info.MaxDiameter)
                        error = $"the size must be {info.MinDiameter:N0}–{info.MaxDiameter:N0} km";
                    else if (graviton > info.GravitonLevel)
                        error = $"{diameter:N0} km needs Graviton Research {graviton} (you have {info.GravitonLevel})";
                    else if (info.Metal < cost)
                        error = $"it has {info.Metal:N0} metal, the moon costs {cost:N0}";
                    else if (testMode)
                        Report(progress, $"[Test] Would create a {diameter:N0} km moon at {planet} for {cost:N0} metal");
                    else
                        moon = await client.CreateMoonAsync(planet.Id, diameter, ct).ConfigureAwait(false);
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    error = ex.Message;
                }

                results.Add(new MoonCreation(planet, moon, error));
                if (error is not null)
                    Report(progress, $"No moon at {planet}: {error}.");
                else if (moon is not null)
                    Report(progress, $"Created a {diameter:N0} km moon at {planet} for {cost:N0} metal.");
            }

            if (results.Any(r => r.Moon is not null))
                await ownPlanets.RefreshAsync(client, ct).ConfigureAwait(false);
            return results;
        }, ActionPriority.User, cancellationToken);

    private async Task<(int Queued, int Transports, TimeSpan Wait)> RunCycleAsync(
        IOgamexClient client, MoonSettings settings, bool transport, ActionPriority priority, IProgress<MoonProgress>? progress, CancellationToken cancellationToken)
    {
        var queue = queues.For(client);
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.CheckIntervalMinutes));
        var (list, movements) = await queue.RunAsync(Owner, "Read the planets, moons and fleets", async ct =>
        {
            var refresh = await ownPlanets.RefreshAsync(client, ct).ConfigureAwait(false);
            return (refresh.List, transport ? await client.GetFleetMovementsAsync(ct).ConfigureAwait(false) : []);
        }, priority, cancellationToken).ConfigureAwait(false);

        var moons = list.Moons.Where(m => settings.MoonIds.Contains(m.Id.Value)).ToList();
        if (moons.Count == 0)
        {
            Report(progress, list.Moons.Count == 0 ? "You have no moon yet." : "No moon is ticked.");
            return (0, 0, interval);
        }

        // After a pause for fleet slots, go on with the moon that had to wait.
        if (_resumeAt is { } resume && moons.FindIndex(m => m.Id == resume) is > 0 and var start)
            moons = [.. moons.Skip(start), .. moons.Take(start)];
        _resumeAt = null;

        int queued = 0, transports = 0;
        var wakes = new List<TimeSpan> { interval };
        foreach (var moon in moons)
        {
            var parent = list.Planets.FirstOrDefault(p => p.Coordinates == moon.Coordinates);
            var name = $"the moon at [{moon.Coordinates}]";
            var (state, plan, pushed) = await queue.RunAsync(Owner, $"Build on {name}", ct => BuildAsync(client, moon, name, settings, progress, ct), priority, cancellationToken)
                .ConfigureAwait(false);
            queued += pushed;
            if (state.RunningRemaining is { } running)
                wakes.Add(running + Settle);

            if (!transport || plan is null)
                continue;
            if (parent is null)
            {
                Report(progress, $"{name}: its planet isn't in the planet list, so nothing is sent.");
                continue;
            }

            if (Shipment(moon, parent, state, plan, pushed, movements, settings, name, progress) is { } shipment && Total(shipment) > 0)
            {
                var result = await queue.RunAsync(Owner, $"Transport to {name}", ct => TransportAsync(client, parent, moon, name, shipment, settings, progress, ct), priority, cancellationToken)
                    .ConfigureAwait(false);
                if (result == TransportResult.Sent)
                    transports++;
                else if (result == TransportResult.NoFleetSlot)
                {
                    // Pause the moons: the rest waits too, and the next check (soon) starts again with this one.
                    _resumeAt = moon.Id;
                    Report(progress, priority == ActionPriority.User
                        ? "No free fleet slot: stopped here; the next run starts with this moon."
                        : $"No free fleet slot: the moons pause, checking again in {NoSlotWait.TotalSeconds:0} s.");
                    return (queued, transports, NoSlotWait);
                }
            }
        }

        lock (_sync)
            wakes.AddRange(_sent.Select(s => s.ArrivesAt - timeProvider.GetUtcNow() + Settle));
        var wait = wakes.Where(w => w > TimeSpan.Zero).DefaultIfEmpty(interval).Min();
        return (queued, transports, TimeSpan.FromTicks(Math.Clamp(wait.Ticks, MinWait.Ticks, interval.Ticks)));
    }

    /// <summary>Reads the moon, plans it and queues what it can pay; the plan is null when the moon can't build (vacation mode).</summary>
    private async Task<(PlanetBuildState State, MoonPlan? Plan, int Queued)> BuildAsync(
        IOgamexClient client, OwnPlanet moon, string name, MoonSettings settings, IProgress<MoonProgress>? progress, CancellationToken cancellationToken)
    {
        var state = await client.GetMoonBuildStateAsync(moon.Id, cancellationToken).ConfigureAwait(false);
        if (state.VacationMode)
        {
            Report(progress, $"{name}: vacation mode, nothing is built.");
            return (state, null, 0);
        }

        var plan = settings.Mode == MoonBuildMode.Schema ? MoonPlanner.FromSchema(state, settings.SchemaSteps) : MoonPlanner.Simulate(state, settings);
        if (plan.Steps.Count == 0)
        {
            Report(progress, plan.Blocked is { } reason ? $"{name}: can't go on, {reason}." : $"{name}: every target is built or queued.");
            return (state, plan, 0);
        }

        Report(progress, $"{name}: next {string.Join(", ", plan.Steps.Take(5))}{(plan.Steps.Count > 5 ? $" … ({plan.Steps.Count} levels)" : "")}, "
                         + $"all done in {Describe(plan.Total)}{(plan.Blocked is { } stop ? $"; stops: {stop}" : "")}");

        var queued = 0;
        foreach (var step in MoonPlanner.ToQueue(plan, state, settings.MaxServerQueueDepth))
        {
            if (settings.TestMode)
            {
                Report(progress, $"[Test] Would queue {step} on {name} ({step.Cost})");
                continue;
            }

            var outcome = await client.EnqueueBuildingAsync(moon.Id, step.Building, cancellationToken).ConfigureAwait(false);
            if (outcome.Result != EnqueueResult.Queued)
            {
                Report(progress, $"{name}: {step} refused: {outcome.Message ?? outcome.Result.ToString()}");
                break;
            }

            queued++;
            Report(progress, $"Queued {step} on {name} ({step.Cost}).");
        }

        return (state, plan, queued);
    }

    /// <summary>
    /// What a transport should bring to the moon: the levels <paramref name="queued"/> just now (paid from the stock
    /// <paramref name="state"/> shows) and the next <see cref="MoonSettings.MaxServerQueueDepth"/>; null while a transport
    /// the app doesn't know is on its way.
    /// </summary>
    private ResourceSet? Shipment(
        OwnPlanet moon, OwnPlanet parent, PlanetBuildState state, MoonPlan plan, int queued, IReadOnlyList<FleetMovement> movements,
        MoonSettings settings, string name, IProgress<MoonProgress>? progress)
    {
        // A transport from the planet to its own coordinates goes to the moon; from elsewhere the target's name tells.
        var flying = movements.Count(m => m is { Relation: MovementRelation.Own, Mission: MovementMission.Transport, Returning: false }
                                          && m.Target == moon.Coordinates
                                          && (m.Origin == moon.Coordinates || !string.Equals(m.TargetName, parent.Name, StringComparison.Ordinal)));
        List<Transport> known;
        lock (_sync)
        {
            _sent.RemoveAll(s => s.ArrivesAt <= timeProvider.GetUtcNow());
            known = _sent.Where(s => s.Moon == moon.Id).ToList();
        }

        if (flying > known.Count)
        {
            Report(progress, $"{name}: a transport this app didn't send is on its way; nothing more until it lands.");
            return null;
        }

        var inFlight = known.Aggregate(ResourceSet.Zero, (sum, t) => sum + t.Cargo);
        var shipment = MoonPlanner.Shipment(plan, state, inFlight, settings.Horizon, queued + Math.Max(1, settings.MaxServerQueueDepth));
        if (Total(shipment) == 0)
            Report(progress, $"{name}: what it has{(known.Count > 0 ? " and what's on its way" : "")} covers its next queue items.");
        return shipment;
    }

    private enum TransportResult
    {
        Sent,
        NotSent,

        /// <summary>Every fleet slot is in use.</summary>
        NoFleetSlot
    }

    /// <summary>Sends <paramref name="shipment"/> (as far as the planet has it and its cargo ships carry it) from the planet to its moon.</summary>
    private async Task<TransportResult> TransportAsync(
        IOgamexClient client, OwnPlanet planet, OwnPlanet moon, string name, ResourceSet shipment, MoonSettings settings,
        IProgress<MoonProgress>? progress, CancellationToken cancellationToken)
    {
        var fleet = await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false);
        if (fleet.Slots is { Free: 0 } slots)
        {
            Report(progress, $"{name}: no free fleet slot ({slots.Used}/{slots.Max}).");
            return TransportResult.NoFleetSlot;
        }

        var stock = fleet.Resources ?? ResourceSet.Zero;
        var send = Min(shipment, stock);
        if (Total(send) == 0)
        {
            Report(progress, $"{name}: {planet} has none of the {shipment} it needs.");
            return TransportResult.NotSent;
        }

        var priority = settings.CargoShipTypes.Count > 0 ? settings.CargoShipTypes : MoonSettings.Default.CargoShipTypes;
        var ships = RaidPlanner.CargoFleet(Total(send), priority, fleet.QuantityOf, fleet.CargoCapacityOf);
        var capacity = ships.Sum(s => s.Quantity * fleet.CargoCapacityOf(s.ShipType));
        if (capacity == 0)
        {
            Report(progress, $"{name}: no {string.Join(" / ", priority.Select(UnitCatalog.NameOf))} on {planet} to carry {send}.");
            return TransportResult.NotSent;
        }

        var mission = new FleetMission(moon.Coordinates, PlanetType.Moon, MissionType.Transport, ships, Math.Clamp(settings.TransportSpeedPercent, 10, 100));
        var briefing = await client.GetFleetBriefingAsync(mission, planet.Id, cancellationToken).ConfigureAwait(false);
        if (!briefing.AvailableMissions.Contains(MissionType.Transport))
        {
            Report(progress, $"{name}: transport isn't possible ({briefing.Warning ?? "no transport mission"}).");
            return TransportResult.NotSent;
        }

        // The flight's fuel stays on the planet and takes cargo space.
        var fuel = Math.Max(0, briefing.DeuteriumConsumption);
        send = send with { Deuterium = Math.Min(send.Deuterium, Math.Max(0, stock.Deuterium - fuel)) };
        send = Fit(send, Math.Max(0, capacity - fuel));
        if (Total(send) == 0)
        {
            Report(progress, $"{name}: the fuel leaves no room for cargo.");
            return TransportResult.NotSent;
        }

        var what = $"{string.Join(" + ", ships.Select(s => $"{s.Quantity:N0} {UnitCatalog.NameOf(s.ShipType)}"))} with {send} from {planet} to {name}";
        if (settings.TestMode)
        {
            Report(progress, $"[Test] Would send {what} (flight {briefing.OneWayFlight:g}).");
            return TransportResult.NotSent;
        }

        var outcome = await client.SendFleetAsync(mission with { Cargo = send }, planet.Id, cancellationToken).ConfigureAwait(false);
        if (!outcome.Sent)
        {
            Report(progress, $"{name}: transport refused: {outcome.Message}");
            return TransportResult.NotSent;
        }

        var arrives = timeProvider.GetUtcNow() + briefing.OneWayFlight;
        lock (_sync)
            _sent.Add(new Transport(moon.Id, send, arrives));
        Report(progress, $"Sent {what}, landing {arrives.ToLocalTime():T}.");
        return TransportResult.Sent;
    }

    private static ResourceSet Min(ResourceSet a, ResourceSet b) =>
        new(Math.Max(0, Math.Min(a.Metal, b.Metal)), Math.Max(0, Math.Min(a.Crystal, b.Crystal)), Math.Max(0, Math.Min(a.Deuterium, b.Deuterium)));

    /// <summary>The same shares of each resource, scaled down to <paramref name="capacity"/> when it's more.</summary>
    private static ResourceSet Fit(ResourceSet cargo, long capacity)
    {
        var total = Total(cargo);
        if (total <= capacity)
            return cargo;
        var factor = (double)capacity / total;
        return new ResourceSet((long)(cargo.Metal * factor), (long)(cargo.Crystal * factor), (long)(cargo.Deuterium * factor));
    }

    private static long Total(ResourceSet resources) => resources.Metal + resources.Crystal + resources.Deuterium;

    private static string Describe(TimeSpan time) => time.TotalDays >= 1 ? $"{(int)time.TotalDays}d {time:hh\\:mm}" : time.ToString(@"hh\:mm\:ss");

    private static void Report(IProgress<MoonProgress>? progress, string message) => progress?.Report(new MoonProgress(message));

    private sealed record Transport(PlanetId Moon, ResourceSet Cargo, DateTimeOffset ArrivesAt);
}
