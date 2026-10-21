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
public sealed record ResourceShareSettings(
    IReadOnlyList<string> CargoShipTypes,
    int SpeedPercent = 100,
    int NextItems = ResourceShareSettings.DefaultNextItems,
    long MinTransportAmount = ResourceShareSettings.DefaultMinTransportAmount,
    int MaxSlots = ResourceShareSettings.DefaultMaxSlots,
    bool TestMode = false,
    long DonorDeuteriumReserve = ResourceShareSettings.DefaultDonorDeuteriumReserve)
{
    public const long DefaultDonorDeuteriumReserve = 500_000;

    public const int DefaultNextItems = 3;
    public const long DefaultMinTransportAmount = 100_000;
    public const int DefaultMaxSlots = 2;

    public static IReadOnlyList<string> DefaultCargoShipTypes { get; } = ["HEAVY_CARGO", "LIGHT_CARGO"];
}

/// <summary>What a Builder planet holds back and asks for.</summary>
/// <param name="Keep">The stock it keeps for its waiting queue items and its next items; only the rest is shared.</param>
/// <param name="Wants">The stock that pays its next items while it waits for resources; null when it waits for something else (or is done).</param>
public sealed record ShareDemand(ResourceSet Keep, ResourceSet? Wants);

/// <summary>An own planet as the sharing sees it.</summary>
/// <param name="CargoCapacity">What its cargo ships carry together; null = unknown (no limit while planning).</param>
/// <param name="Demand">Its Builder demand; null for a planet the Builder doesn't build on, which gives everything.</param>
/// <param name="Blocked">It neither gives nor gets (e.g. a transport the app didn't send is on its way to it).</param>
public sealed record SharePlanet(
    OwnPlanet Planet,
    ResourceSet Stock,
    ResourceSet ProductionPerHour,
    long? CargoCapacity,
    ShareDemand? Demand,
    bool Blocked = false);

/// <param name="SelfFundedIn">When the receiver would pay it from its own production; null = never. A slower transport is pointless.</param>
public sealed record ShareTransport(OwnPlanet From, OwnPlanet To, ResourceSet Cargo, TimeSpan? SelfFundedIn)
{
    public override string ToString() => $"{From} → {To}: {Cargo}";
}

/// <summary>
/// The pure part of the Builder's resource sharing: each planet first keeps what its own next items need
/// (<see cref="ShareDemand.Keep"/>); the rest of every own planet is surplus. Planets waiting for stock get what they
/// miss (<see cref="ShareDemand.Wants"/> − stock − cargo on its way), the dearest shortfall first (metal 1, crystal 1.5,
/// deuterium 3), each from the nearest planets with surplus, one transport per donor and receiver. Amounts are rounded
/// like a player would type them (<see cref="CargoRounding"/>): up when the donor's surplus and ships allow, else down.
/// </summary>
public static class ResourceSharePlanner
{
    /// <param name="deuteriumReserve">Deuterium every donor keeps on top of its own needs (fleet fuel).</param>
    public static IReadOnlyList<ShareTransport> Plan(
        IReadOnlyList<SharePlanet> planets, IReadOnlyDictionary<PlanetId, ResourceSet> inFlight, long minTransportAmount, UniverseProfile universe,
        long deuteriumReserve = 0)
    {
        var reserve = new ResourceSet(0, 0, Math.Max(0, deuteriumReserve));
        var surplus = planets.ToDictionary(p => p.Planet.Id, p => p.Blocked ? ResourceSet.Zero : Positive(p.Stock - (p.Demand?.Keep ?? ResourceSet.Zero) - reserve));
        var capacity = planets.ToDictionary(p => p.Planet.Id, p => p.CargoCapacity ?? long.MaxValue);

        var receivers = planets
            .Where(p => !p.Blocked && p.Demand?.Wants is not null)
            .Select(p => (Planet: p, Missing: Missing(p, inFlight)))
            .Where(r => TransportSender.Total(r.Missing) > 0)
            .OrderByDescending(r => r.Missing.Value)
            .ToList();

        var transports = new List<ShareTransport>();
        foreach (var (receiver, missing) in receivers)
        {
            var left = missing;
            var selfFunded = SelfFundedIn(missing, receiver.ProductionPerHour);
            var donors = planets
                .Where(d => d.Planet.Id != receiver.Planet.Id)
                .OrderBy(d => RaidPlanner.Distance(d.Planet.Coordinates, receiver.Planet.Coordinates, universe));
            foreach (var donor in donors)
            {
                var give = Round(TransportSender.Fit(TransportSender.Min(left, surplus[donor.Planet.Id]), capacity[donor.Planet.Id]),
                    surplus[donor.Planet.Id], capacity[donor.Planet.Id]);
                var total = TransportSender.Total(give);
                if (total == 0 || total < minTransportAmount)
                    continue;

                transports.Add(new ShareTransport(donor.Planet, receiver.Planet, give, selfFunded));
                surplus[donor.Planet.Id] -= give;
                capacity[donor.Planet.Id] = capacity[donor.Planet.Id] == long.MaxValue ? long.MaxValue : capacity[donor.Planet.Id] - total;
                left = Positive(left - give); // rounding up may give a little more than missing
                if (TransportSender.Total(left) == 0)
                    break;
            }
        }

        return transports;
    }

    /// <summary>What a waiting planet misses: what it wants minus its stock and the cargo on its way, per resource.</summary>
    public static ResourceSet Missing(SharePlanet planet, IReadOnlyDictionary<PlanetId, ResourceSet> inFlight) =>
        planet.Demand?.Wants is { } wants
            ? Positive(wants - planet.Stock - inFlight.GetValueOrDefault(planet.Planet.Id))
            : ResourceSet.Zero;

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

    /// <summary>Rounded up within the donor's surplus and ship capacity, else down.</summary>
    private static ResourceSet Round(ResourceSet give, ResourceSet surplus, long capacity)
    {
        var rounded = CargoRounding.Within(give, surplus);
        return TransportSender.Total(rounded) <= capacity ? rounded : CargoRounding.Down(give);
    }

    private static ResourceSet Positive(ResourceSet set) => new(Math.Max(0, set.Metal), Math.Max(0, set.Crystal), Math.Max(0, set.Deuterium));
}
