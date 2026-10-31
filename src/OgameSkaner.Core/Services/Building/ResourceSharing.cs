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
/// so the other modules leave the next free ones to it, but for <see cref="ClaimLimit"/> at most in a row: then it
/// leaves the slots to them as long again.
/// </summary>
/// <remarks>
/// Transports it sent count for their receiver until the stock the plan uses was read after they landed, and their
/// receiver gives nothing for <see cref="ReceiverRest"/> after landing; while an own transport the app didn't send
/// flies to a planet, that planet neither gets nor gives. A donor whose flight to a receiver was too long, or that
/// couldn't send all of it (stock, ships or fuel), isn't asked again for that receiver for <see cref="PairRetry"/>.
/// </remarks>
public sealed class ResourceSharing(IActionQueues queues, TimeProvider timeProvider, ILogger<ResourceSharing> logger, IFleetSlotClaimsProvider? claims = null)
{
    public const string Owner = "Builder";

    /// <summary>A moment after an event, so the game shows its result.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    /// <summary>How long a planet that got a transport gives nothing, so resources don't hop on to the next planet.</summary>
    public static readonly TimeSpan ReceiverRest = TimeSpan.FromHours(2);

    /// <summary>How long a donor isn't asked again for a receiver its flight to was too long, or it couldn't send all to.</summary>
    public static readonly TimeSpan PairRetry = TimeSpan.FromMinutes(30);

    /// <summary>How long the sharing claims slots in a row; then it claims none for as long.</summary>
    public static readonly TimeSpan ClaimLimit = TimeSpan.FromMinutes(20);

    private readonly object _sync = new();
    private readonly List<SentTransport> _sent = [];
    private readonly Dictionary<(PlanetId From, PlanetId To), DateTimeOffset> _tooLong = [];
    private readonly Dictionary<string, ClaimState> _claiming = [];

    /// <param name="planets">Every own planet (any of them may give).</param>
    /// <param name="builder">The Builder's planets with their demand; null = not known yet (it keeps all its stock).</param>
    /// <param name="stockReadAt">When <paramref name="empire"/> was read (its stock is older than the transports that landed since); null = now.</param>
    public async Task<ShareResult> RunAsync(
        IOgamexClient client,
        IReadOnlyList<OwnPlanet> planets,
        EmpirePage empire,
        IReadOnlyDictionary<PlanetId, ShareDemand?> builder,
        ResourceShareSettings settings,
        Action<string> report,
        DateTimeOffset? stockReadAt = null,
        ActionPriority priority = ActionPriority.Automation,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var asOf = stockReadAt ?? now;
        var (inFlight, received, landings) = Sent(asOf);
        var skip = TooLong();
        var draft = SharePlanets(planets, empire, builder, fleets: null, types: [], blocked: new HashSet<PlanetId>(), received);
        var plan = ResourceSharePlanner.Plan(draft, inFlight, settings.MinTransportAmount, client.Universe, settings.DonorDeuteriumReserve, now, settings.MinTimeSaved, skip);
        if (plan.Count == 0)
        {
            Release(client);
            var waiting = draft.Count(p => TransportSender.Total(ResourceSharePlanner.Missing(p, inFlight)) > 0);
            var worth = draft.Count(p => !p.Blocked && ResourceSharePlanner.Need(p, inFlight, now, settings.MinTimeSaved) is not null);
            if (worth > 0)
                report($"Sharing: {worth} planet(s) wait for resources; no single planet has the surplus to cover one alone (or a transport is already on its way).");
            else if (waiting > 0)
                report($"Sharing: {waiting} planet(s) wait for resources, but they make them within {settings.MinTimeSaved:g} of when their queue is free: no transport.");
            return new ShareResult(0, Earliest(landings));
        }

        var donors = plan.Select(t => t.From).DistinctBy(t => t.Id).ToList();
        var (sent, remaining, wake) = await queues.For(client).RunAsync(Owner, $"Share resources from {string.Join(", ", donors.AsEnumerable())}",
            ct => ShareAsync(client, planets, empire, builder, donors, settings, asOf, report, ct), priority, cancellationToken).ConfigureAwait(false);

        if (remaining > 0 && !settings.TestMode)
            ClaimOrLeave(client, remaining, settings, report);
        else
            Release(client);

        (_, _, landings) = Sent(asOf);
        return new ShareResult(sent, Earliest([.. landings, .. wake is { } w ? new[] { w } : []]));
    }

    /// <summary>Gives up the sharing's slot claim (when the Builder stops, or nothing waits for a slot any more).</summary>
    public void Release(IOgamexClient client)
    {
        lock (_sync)
            _claiming.Remove(client.Universe.Name);
        claims?.For(client).Release(Owner);
    }

    /// <summary>Claims the slots the transports wait for, but not longer than <see cref="ClaimLimit"/> in a row.</summary>
    private void ClaimOrLeave(IOgamexClient client, int remaining, ResourceShareSettings settings, Action<string> report)
    {
        var now = timeProvider.GetUtcNow();
        bool claim;
        lock (_sync)
        {
            var state = _claiming.GetValueOrDefault(client.Universe.Name);
            if (state is { PauseUntil: { } pause } && now < pause)
                claim = false;
            else if (state is { Since: { } since } && now - since >= ClaimLimit)
            {
                _claiming[client.Universe.Name] = new ClaimState(null, now + ClaimLimit);
                claim = false;
            }
            else
            {
                _claiming[client.Universe.Name] = new ClaimState(state?.Since ?? now, null);
                claim = true;
            }
        }

        if (!claim)
        {
            claims?.For(client).Release(Owner);
            report($"Sharing: no free fleet slot for {remaining} transport(s); it claimed slots for {ClaimLimit.TotalMinutes:0} min, so now it leaves them to the other modules for as long.");
            return;
        }

        var count = Math.Min(remaining, Math.Max(1, settings.MaxSlots));
        claims?.For(client).Claim(Owner, count);
        report($"Sharing: no free fleet slot for {remaining} transport(s); {count} slot(s) claimed, so the other modules leave the next free ones.");
    }

    /// <summary>The queue group: event list, the donors' fleets, the final plan and the transports.</summary>
    private async Task<(int Sent, int Remaining, TimeSpan? Wake)> ShareAsync(
        IOgamexClient client,
        IReadOnlyList<OwnPlanet> planets,
        EmpirePage empire,
        IReadOnlyDictionary<PlanetId, ShareDemand?> builder,
        IReadOnlyList<OwnPlanet> donors,
        ResourceShareSettings settings,
        DateTimeOffset stockReadAt,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        var movements = await client.GetFleetMovementsAsync(cancellationToken).ConfigureAwait(false);
        var types = settings.CargoShipTypes.Count > 0 ? settings.CargoShipTypes : ResourceShareSettings.DefaultCargoShipTypes;
        var fleets = (await EmpireFleets.ReadAsync(client, donors, types, queues.For(client).HomePlanet, cancellationToken: cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => f.Planet.Id, f => f.Fleet);

        var (inFlight, received, _) = Sent(stockReadAt);
        var blocked = Unknown(planets, movements);
        foreach (var planet in blocked)
            report($"Sharing: a transport this app didn't send is on its way to {planet}; it neither gets nor gives until it lands.");

        var now = timeProvider.GetUtcNow();
        var plan = ResourceSharePlanner.Plan(SharePlanets(planets, empire, builder, fleets, types, blocked.Select(p => p.Id).ToHashSet(), received), inFlight,
            settings.MinTransportAmount, client.Universe, settings.DonorDeuteriumReserve, now, settings.MinTimeSaved, TooLong());
        if (plan.Count == 0)
        {
            report("Sharing: no donor's stock and cargo ships cover a waiting planet alone.");
            return (0, 0, null);
        }

        var production = planets.ToDictionary(p => p.Id, p => empire.PlanetAt(p.Coordinates)?.ProductionPerHour ?? ResourceSet.Zero);
        var neededIn = builder.ToDictionary(b => b.Key, b => b.Value is { } d && d.NeededAt > now ? d.NeededAt - now : TimeSpan.Zero);
        var ledger = claims?.For(client);
        var sent = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = plan[i];
            var request = new TransportRequest(transport.From, transport.To.Coordinates, PlanetType.Planet, transport.To.ToString(), transport.Cargo, types,
                settings.SpeedPercent, settings.TestMode, transport.MaxFlight, production.GetValueOrDefault(transport.To.Id), neededIn.GetValueOrDefault(transport.To.Id),
                Whole: true);
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
                case TransportStatus.TooLong or TransportStatus.Short:
                    lock (_sync)
                        _tooLong[(transport.From.Id, transport.To.Id)] = timeProvider.GetUtcNow() + PairRetry;
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
        IReadOnlyDictionary<PlanetId, FleetSnapshot>? fleets, IReadOnlyList<string> types, IReadOnlySet<PlanetId> blocked, IReadOnlySet<PlanetId> received)
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
            result.Add(new SharePlanet(planet, stock, column.ProductionPerHour, capacity, demand, blocked.Contains(planet.Id), received.Contains(planet.Id)));
        }

        return result;
    }

    /// <summary>Own planets an own transport flies to that this app didn't send (or more of them than it sent).</summary>
    private List<OwnPlanet> Unknown(IReadOnlyList<OwnPlanet> planets, IReadOnlyList<FleetMovement> movements)
    {
        var now = timeProvider.GetUtcNow();
        List<SentTransport> flying;
        lock (_sync)
            flying = _sent.Where(s => s.LandsAt + Settle > now).ToList();
        return planets
            .Where(p => movements.Count(m => m is { Relation: MovementRelation.Own, Mission: MovementMission.Transport, Returning: false }
                                             && m.Target == p.Coordinates && string.Equals(m.TargetName, p.Name, StringComparison.Ordinal))
                        > flying.Count(s => s.To == p.Id))
            .ToList();
    }

    /// <summary>
    /// The transports it sent: the cargo per receiver that the stock read at <paramref name="stockReadAt"/> doesn't
    /// show yet (landed after it, or still flying), the receivers that give nothing now (<see cref="ReceiverRest"/>),
    /// and when the flying ones land.
    /// </summary>
    private (Dictionary<PlanetId, ResourceSet> InFlight, HashSet<PlanetId> Received, List<TimeSpan> Landings) Sent(DateTimeOffset stockReadAt)
    {
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            _sent.RemoveAll(s => s.LandsAt + ReceiverRest <= now && s.LandsAt + Settle <= stockReadAt);
            var inFlight = _sent.Where(s => s.LandsAt + Settle > stockReadAt)
                .GroupBy(s => s.To).ToDictionary(g => g.Key, g => g.Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cargo));
            var received = _sent.Where(s => s.LandsAt + ReceiverRest > now).Select(s => s.To).ToHashSet();
            var landings = _sent.Where(s => s.LandsAt > now).Select(s => s.LandsAt - now + Settle).ToList();
            return (inFlight, received, landings);
        }
    }

    /// <summary>The donor and receiver pairs whose flight was too long a moment ago.</summary>
    private HashSet<(PlanetId From, PlanetId To)> TooLong()
    {
        var now = timeProvider.GetUtcNow();
        lock (_sync)
        {
            foreach (var key in _tooLong.Where(t => t.Value <= now).Select(t => t.Key).ToList())
                _tooLong.Remove(key);
            return [.. _tooLong.Keys];
        }
    }

    private static TimeSpan? Earliest(IEnumerable<TimeSpan> waits) => waits.Where(w => w > TimeSpan.Zero).Select(w => (TimeSpan?)w).Min();

    private sealed record SentTransport(PlanetId To, ResourceSet Cargo, DateTimeOffset LandsAt);

    /// <param name="Since">Since when it claims slots without a break; null = it doesn't.</param>
    /// <param name="PauseUntil">Until when it claims none after <see cref="ClaimLimit"/>.</param>
    private sealed record ClaimState(DateTimeOffset? Since, DateTimeOffset? PauseUntil);
}
