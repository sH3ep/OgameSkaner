using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Services.Transports;

namespace OgameSkaner.Core.Services.Building;

/// <summary>The Builder's resource sharing options.</summary>
/// <param name="CargoShipTypes">The cargo ships in priority order.</param>
/// <param name="NextItems">How many of its next items a planet keeps resources for, and asks for when it waits for stock.</param>
/// <param name="MinTransportAmount">Transports carrying less (metal + crystal + deuterium) aren't sent.</param>
/// <param name="MaxSlots">Fleet slots the sharing claims at most while it waits for them.</param>
/// <param name="TestMode">Plan and report the transports, but send none.</param>
/// <param name="DonorDeuteriumReserve">Deuterium every donor keeps on top of its own needs: the fuel of raids, expeditions and escapes.</param>
/// <param name="MinHoursSaved">
/// A transport goes only when the receiver can build at least this many hours sooner than with its own production
/// (and more than the fleet slot's round trip).
/// </param>
public sealed record ResourceShareSettings(
    IReadOnlyList<string> CargoShipTypes,
    int SpeedPercent = 100,
    int NextItems = ResourceShareSettings.DefaultNextItems,
    long MinTransportAmount = ResourceShareSettings.DefaultMinTransportAmount,
    int MaxSlots = ResourceShareSettings.DefaultMaxSlots,
    bool TestMode = false,
    long DonorDeuteriumReserve = ResourceShareSettings.DefaultDonorDeuteriumReserve,
    double MinHoursSaved = ResourceShareSettings.DefaultMinHoursSaved)
{
    public const long DefaultDonorDeuteriumReserve = 500_000;

    public const int DefaultNextItems = 3;
    public const long DefaultMinTransportAmount = 100_000;
    public const int DefaultMaxSlots = 2;
    public const double DefaultMinHoursSaved = 1;

    public static IReadOnlyList<string> DefaultCargoShipTypes { get; } = ["HEAVY_CARGO", "LIGHT_CARGO"];

    public TimeSpan MinTimeSaved => TimeSpan.FromHours(Math.Max(0, MinHoursSaved));
}

/// <summary>What a Builder planet holds back and asks for.</summary>
/// <param name="Keep">The stock it keeps for its waiting queue items and its next items; only the rest is shared.</param>
/// <param name="Wants">The stock that pays its next items while it waits for resources; null when it waits for something else (or is done).</param>
/// <param name="NeededAt">When its build queue runs empty, so its next items could start; default = at once.</param>
public sealed record ShareDemand(ResourceSet Keep, ResourceSet? Wants, DateTimeOffset NeededAt = default);

/// <summary>An own planet as the sharing sees it.</summary>
/// <param name="CargoCapacity">What its cargo ships carry together; null = unknown (no limit while planning).</param>
/// <param name="Demand">Its Builder demand; null for a planet the Builder doesn't build on, which gives everything.</param>
/// <param name="Blocked">It neither gives nor gets (e.g. a transport the app didn't send is on its way to it).</param>
/// <param name="ReceivedRecently">A transport of the sharing flies to it or landed lately: it gives nothing, so resources don't hop on.</param>
public sealed record SharePlanet(
    OwnPlanet Planet,
    ResourceSet Stock,
    ResourceSet ProductionPerHour,
    long? CargoCapacity,
    ShareDemand? Demand,
    bool Blocked = false,
    bool ReceivedRecently = false);

/// <param name="MaxFlight">The longest flight still worth it (<see cref="ResourceSharePlanner.MaxFlight"/>); null = any.</param>
public sealed record ShareTransport(OwnPlanet From, OwnPlanet To, ResourceSet Cargo, TimeSpan? MaxFlight)
{
    public override string ToString() => $"{From} → {To}: {Cargo}";
}

/// <summary>What a waiting planet gets: what its own production won't have made by the time its queue is free.</summary>
/// <param name="MaxFlight">The longest flight still worth it; null = any (it doesn't produce a missing resource).</param>
public sealed record ShareNeed(ResourceSet Missing, TimeSpan? MaxFlight);

/// <summary>
/// The pure part of the Builder's resource sharing: each planet first keeps what its own next items need
/// (<see cref="ShareDemand.Keep"/>); the rest of every planet that neither waits for stock itself nor just got a
/// transport is surplus (so resources don't hop from planet to planet). Planets waiting for stock get what they miss
/// (<see cref="ShareDemand.Wants"/> − stock − cargo on its way − what they make until their queue is free), the
/// dearest shortfall first (metal 1, crystal 1.5, deuterium 3), but only when that lets them build at least
/// <c>minTimeSaved</c> sooner than their own production would (<see cref="Need"/>). A receiver gets one transport or
/// none: from the nearest planet whose surplus and cargo ships cover all it misses; when no planet can alone, it waits
/// until one can (several partial transports took a fleet slot each). While a transport flies to it, it gets no other.
/// Amounts are rounded like a player would type them (<see cref="CargoRounding"/>): up when the donor's surplus and
/// ships allow, else down.
/// </summary>
public static class ResourceSharePlanner
{
    /// <param name="deuteriumReserve">Deuterium every donor keeps on top of its own needs (fleet fuel).</param>
    /// <param name="now">The moment <see cref="ShareDemand.NeededAt"/> is measured from.</param>
    /// <param name="skip">Donor and receiver pairs not to plan (e.g. their flight was too long a moment ago).</param>
    public static IReadOnlyList<ShareTransport> Plan(
        IReadOnlyList<SharePlanet> planets, IReadOnlyDictionary<PlanetId, ResourceSet> inFlight, long minTransportAmount, UniverseProfile universe,
        long deuteriumReserve = 0, DateTimeOffset now = default, TimeSpan minTimeSaved = default, IReadOnlySet<(PlanetId From, PlanetId To)>? skip = null)
    {
        var reserve = new ResourceSet(0, 0, Math.Max(0, deuteriumReserve));
        var surplus = planets.ToDictionary(p => p.Planet.Id, p => Gives(p) ? Positive(p.Stock - (p.Demand?.Keep ?? ResourceSet.Zero) - reserve) : ResourceSet.Zero);
        var capacity = planets.ToDictionary(p => p.Planet.Id, p => p.CargoCapacity ?? long.MaxValue);

        var receivers = planets
            .Where(p => !p.Blocked && !inFlight.ContainsKey(p.Planet.Id))
            .Select(p => (Planet: p, Need: Need(p, inFlight, now, minTimeSaved)))
            .Where(r => r.Need is not null)
            .OrderByDescending(r => r.Need!.Missing.Value)
            .ToList();

        var transports = new List<ShareTransport>();
        foreach (var (receiver, need) in receivers)
        {
            var missing = need!.Missing;
            if (TransportSender.Total(missing) < minTransportAmount)
                continue;
            var donor = planets
                .Where(d => d.Planet.Id != receiver.Planet.Id && skip?.Contains((d.Planet.Id, receiver.Planet.Id)) != true)
                .Where(d => surplus[d.Planet.Id].CanAfford(missing) && TransportSender.Total(missing) <= capacity[d.Planet.Id])
                .MinBy(d => RaidPlanner.Distance(d.Planet.Coordinates, receiver.Planet.Coordinates, universe));
            if (donor is null)
                continue;

            var give = Round(missing, surplus[donor.Planet.Id], capacity[donor.Planet.Id]);
            var total = TransportSender.Total(give);
            transports.Add(new ShareTransport(donor.Planet, receiver.Planet, give, need.MaxFlight));
            surplus[donor.Planet.Id] -= give;
            capacity[donor.Planet.Id] = capacity[donor.Planet.Id] == long.MaxValue ? long.MaxValue : capacity[donor.Planet.Id] - total;
        }

        return transports;
    }

    /// <summary>Whether a planet gives its surplus: not while it waits for stock itself, is blocked or just got a transport.</summary>
    private static bool Gives(SharePlanet planet) => !planet.Blocked && !planet.ReceivedRecently && planet.Demand?.Wants is null;

    /// <summary>What a waiting planet misses: what it wants minus its stock and the cargo on its way, per resource.</summary>
    public static ResourceSet Missing(SharePlanet planet, IReadOnlyDictionary<PlanetId, ResourceSet> inFlight) =>
        planet.Demand?.Wants is { } wants
            ? Positive(wants - planet.Stock - inFlight.GetValueOrDefault(planet.Planet.Id))
            : ResourceSet.Zero;

    /// <summary>
    /// What a waiting planet should get, or null when a transport isn't worth it: the planet makes it itself before
    /// its queue is free (<see cref="ShareDemand.NeededAt"/>), or its production pays it less than
    /// <paramref name="minTimeSaved"/> after that. What it makes until its queue is free is left out of the cargo.
    /// </summary>
    public static ShareNeed? Need(SharePlanet planet, IReadOnlyDictionary<PlanetId, ResourceSet> inFlight, DateTimeOffset now, TimeSpan minTimeSaved)
    {
        if (planet.Demand is not { Wants: not null } demand)
            return null;
        var missing = Missing(planet, inFlight);
        if (TransportSender.Total(missing) == 0)
            return null;

        var neededIn = demand.NeededAt > now ? demand.NeededAt - now : TimeSpan.Zero;
        var afterQueue = Positive(missing - planet.ProductionPerHour.Scale(neededIn.TotalHours));
        if (TransportSender.Total(afterQueue) == 0)
            return null;

        var selfFunded = SelfFundedIn(missing, planet.ProductionPerHour);
        if (selfFunded is { } s && s - neededIn < minTimeSaved)
            return null;
        return new ShareNeed(afterQueue, MaxFlight(selfFunded, neededIn, minTimeSaved));
    }

    /// <summary>
    /// The longest flight for which a transport still saves <paramref name="minTimeSaved"/> and more than twice its
    /// flight (the fleet slot's round trip). Saved = <paramref name="selfFundedIn"/> − the later of the landing and
    /// <paramref name="neededIn"/>. Null = any flight (the planet never pays it itself).
    /// </summary>
    public static TimeSpan? MaxFlight(TimeSpan? selfFundedIn, TimeSpan neededIn, TimeSpan minTimeSaved)
    {
        if (selfFundedIn is not { } s)
            return null;
        // Landing after the queue is free saves s − flight.
        var landingLater = Min(s - minTimeSaved, s / 3);
        if (landingLater > neededIn)
            return landingLater;
        // Landing before it saves s − neededIn (at least minTimeSaved, which Need checks), which must cover the round trip.
        return Min(neededIn, (s - neededIn) / 2);
    }

    /// <summary>How long the planet's production takes for <paramref name="missing"/>; null when it doesn't produce a missing resource.</summary>
    public static TimeSpan? SelfFundedIn(ResourceSet missing, ResourceSet productionPerHour)
    {
        double hours = 0;
        foreach (var (need, rate) in new[] { (missing.Metal, productionPerHour.Metal), (missing.Crystal, productionPerHour.Crystal), (missing.Deuterium, productionPerHour.Deuterium) })
        {
            if (need <= 0)
                continue;
            if (rate <= 0)
                return null;
            hours = Math.Max(hours, (double)need / rate);
        }

        return TimeSpan.FromHours(hours);
    }

    /// <summary>Rounded up within the donor's surplus and ship capacity; where that doesn't fit, the exact amount (it must all go).</summary>
    private static ResourceSet Round(ResourceSet give, ResourceSet surplus, long capacity)
    {
        var rounded = CargoRounding.Within(give, surplus);
        rounded = new ResourceSet(Math.Max(rounded.Metal, give.Metal), Math.Max(rounded.Crystal, give.Crystal), Math.Max(rounded.Deuterium, give.Deuterium));
        return TransportSender.Total(rounded) <= capacity ? rounded : give;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static ResourceSet Positive(ResourceSet set) => new(Math.Max(0, set.Metal), Math.Max(0, set.Crystal), Math.Max(0, set.Deuterium));
}
