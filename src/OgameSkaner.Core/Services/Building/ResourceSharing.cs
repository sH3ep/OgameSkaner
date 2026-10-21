using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Services.Transports;

namespace OgameSkaner.Core.Services.Building;

/// <param name="WakeIn">When to look again: a transport lands, or (while waiting for a slot) an own fleet arrives or returns.</param>
public sealed record ShareResult(int Sent, TimeSpan? WakeIn);

/// <summary>
/// The Builder's resource sharing, run after each Builder cycle: plans with <see cref="ResourceSharePlanner"/> from the
/// empire page the cycle read and, when something is worth sending, one queue group reads the event list and the donors'
/// fleets, plans again with their real stock and cargo ships and sends the transports. When the fleet slots run out it
/// claims the slots it still needs (<see cref="IFleetSlotClaims"/>, at most <see cref="ResourceShareSettings.MaxSlots"/>),
/// so the other modules leave the next free ones to it.
/// </summary>
/// <remarks>
/// Transports it sent are remembered until they land (the event list shows no cargo) and count for their receiver;
/// while an own transport the app didn't send flies to a planet, that planet neither gets nor gives.
/// </remarks>
public sealed class ResourceSharing(IActionQueues queues, TimeProvider timeProvider, ILogger<ResourceSharing> logger, IFleetSlotClaimsProvider? claims = null)
{
    public const string Owner = "Builder";

    /// <summary>A moment after an event, so the game shows its result.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private readonly List<SentTransport> _sent = [];

    /// <param name="planets">Every own planet (any of them may give).</param>
    /// <param name="builder">The Builder's planets with their demand; null = not known yet (it keeps all its stock).</param>
    public async Task<ShareResult> RunAsync(
        IOgamexClient client,
        IReadOnlyList<OwnPlanet> planets,
        EmpirePage empire,
        IReadOnlyDictionary<PlanetId, ShareDemand?> builder,
        ResourceShareSettings settings,
        Action<string> report,
        ActionPriority priority = ActionPriority.Automation,
        CancellationToken cancellationToken = default)
    {
        var inFlight = InFlight(out var landings);
        var draft = SharePlanets(planets, empire, builder, fleets: null, types: [], blocked: new HashSet<PlanetId>());
        var plan = ResourceSharePlanner.Plan(draft, inFlight, settings.MinTransportAmount, client.Universe, settings.DonorDeuteriumReserve);
        if (plan.Count == 0)
        {
            Release(client);
            var waiting = draft.Count(p => TransportSender.Total(ResourceSharePlanner.Missing(p, inFlight)) > 0);
            if (waiting > 0)
                report($"Sharing: {waiting} planet(s) wait for resources; no other planet has a surplus worth a transport.");
            return new ShareResult(0, Earliest(landings));
        }

        var donors = plan.Select(t => t.From).DistinctBy(t => t.Id).ToList();
        var (sent, remaining, wake) = await queues.For(client).RunAsync(Owner, $"Share resources from {string.Join(", ", donors.AsEnumerable())}",
            ct => ShareAsync(client, planets, empire, builder, donors, settings, report, ct), priority, cancellationToken).ConfigureAwait(false);

        if (remaining > 0 && !settings.TestMode)
        {
            var count = Math.Min(remaining, Math.Max(1, settings.MaxSlots));
            claims?.For(client).Claim(Owner, count);
            report($"Sharing: no free fleet slot for {remaining} transport(s); {count} slot(s) claimed, so the other modules leave the next free ones.");
        }
        else
            Release(client);

        InFlight(out landings);
        return new ShareResult(sent, Earliest([.. landings, .. wake is { } w ? new[] { w } : []]));
    }

    /// <summary>Gives up the sharing's slot claim (when the Builder stops).</summary>
    public void Release(IOgamexClient client) => claims?.For(client).Release(Owner);

    /// <summary>The queue group: event list, the donors' fleets, the final plan and the transports.</summary>
    private async Task<(int Sent, int Remaining, TimeSpan? Wake)> ShareAsync(
        IOgamexClient client,
        IReadOnlyList<OwnPlanet> planets,
        EmpirePage empire,
        IReadOnlyDictionary<PlanetId, ShareDemand?> builder,
        IReadOnlyList<OwnPlanet> donors,
        ResourceShareSettings settings,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        var movements = await client.GetFleetMovementsAsync(cancellationToken).ConfigureAwait(false);
        var types = settings.CargoShipTypes.Count > 0 ? settings.CargoShipTypes : ResourceShareSettings.DefaultCargoShipTypes;
        var fleets = (await EmpireFleets.ReadAsync(client, donors, types, queues.For(client).HomePlanet, cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => f.Planet.Id, f => f.Fleet);

        var inFlight = InFlight(out _);
        var blocked = Unknown(planets, movements);
        foreach (var planet in blocked)
            report($"Sharing: a transport this app didn't send is on its way to {planet}; it neither gets nor gives until it lands.");

        var plan = ResourceSharePlanner.Plan(SharePlanets(planets, empire, builder, fleets, types, blocked.Select(p => p.Id).ToHashSet()), inFlight,
            settings.MinTransportAmount, client.Universe, settings.DonorDeuteriumReserve);
        if (plan.Count == 0)
        {
            report("Sharing: the donors' fleets or stock don't allow a transport worth sending.");
            return (0, 0, null);
        }

        var ledger = claims?.For(client);
        var sent = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = plan[i];
            var request = new TransportRequest(transport.From, transport.To.Coordinates, PlanetType.Planet, transport.To.ToString(), transport.Cargo, types,
                settings.SpeedPercent, settings.TestMode, transport.SelfFundedIn);
            var outcome = await TransportSender.SendAsync(client, request, ledger is null ? null : s => ledger.Effective(Owner, s),
                m => report(m.StartsWith('[') ? $"Sharing: {m}" : $"Sharing to {transport.To}: {m}"), cancellationToken).ConfigureAwait(false);
            switch (outcome.Status)
            {
                case TransportStatus.Sent:
                    sent++;
                    ledger?.Used(Owner, 1);
                    lock (_sync)
                        _sent.Add(new SentTransport(transport.To.Id, outcome.Cargo, timeProvider.GetUtcNow() + outcome.Flight));
                    break;
                case TransportStatus.NoFleetSlot:
                    // Fleets free their slot when they arrive (one-way missions) or come back.
                    var next = movements.Where(m => m.Relation == MovementRelation.Own).Select(m => m.ArrivesAt - timeProvider.GetUtcNow() + Settle)
                        .Where(w => w > TimeSpan.Zero).DefaultIfEmpty().Min();
                    return (sent, plan.Count - i, next > TimeSpan.Zero ? next : null);
            }
        }

        if (sent > 0)
            logger.LogInformation("Shared resources on {Universe}: {Sent} transport(s)", client.Universe.Name, sent);
        return (sent, 0, null);
    }

    private static List<SharePlanet> SharePlanets(
        IReadOnlyList<OwnPlanet> planets, EmpirePage empire, IReadOnlyDictionary<PlanetId, ShareDemand?> builder,
        IReadOnlyDictionary<PlanetId, FleetSnapshot>? fleets, IReadOnlyList<string> types, IReadOnlySet<PlanetId> blocked)
    {
        var result = new List<SharePlanet>();
        foreach (var planet in planets)
        {
            if (empire.PlanetAt(planet.Coordinates) is not { } column)
                continue;
            var fleet = fleets?.GetValueOrDefault(planet.Id);
            var stock = fleet?.Resources ?? column.Resources;
            long? capacity = fleets is null ? null : fleet is null ? 0 : types.Sum(t => fleet.QuantityOf(t) * fleet.CargoCapacityOf(t));
            // A Builder planet whose demand isn't known yet keeps all it has.
            var demand = builder.TryGetValue(planet.Id, out var known) ? known ?? new ShareDemand(stock, null) : null;
            result.Add(new SharePlanet(planet, stock, column.ProductionPerHour, capacity, demand, blocked.Contains(planet.Id)));
        }

        return result;
    }

    /// <summary>Own planets an own transport flies to that this app didn't send (or more of them than it sent).</summary>
    private List<OwnPlanet> Unknown(IReadOnlyList<OwnPlanet> planets, IReadOnlyList<FleetMovement> movements)
    {
        List<SentTransport> known;
        lock (_sync)
            known = [.. _sent];
        return planets
            .Where(p => movements.Count(m => m is { Relation: MovementRelation.Own, Mission: MovementMission.Transport, Returning: false }
                                             && m.Target == p.Coordinates && string.Equals(m.TargetName, p.Name, StringComparison.Ordinal))
                        > known.Count(s => s.To == p.Id))
            .ToList();
    }

    /// <summary>The cargo of the transports still flying, per receiver, and when each lands.</summary>
    private Dictionary<PlanetId, ResourceSet> InFlight(out List<TimeSpan> landings)
    {
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            _sent.RemoveAll(s => s.LandsAt <= now);
            landings = _sent.Select(s => s.LandsAt - now + Settle).ToList();
            return _sent.GroupBy(s => s.To).ToDictionary(g => g.Key, g => g.Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cargo));
        }
    }

    private static TimeSpan? Earliest(IEnumerable<TimeSpan> waits) => waits.Where(w => w > TimeSpan.Zero).Select(w => (TimeSpan?)w).Min();

    private sealed record SentTransport(PlanetId To, ResourceSet Cargo, DateTimeOffset LandsAt);
}
