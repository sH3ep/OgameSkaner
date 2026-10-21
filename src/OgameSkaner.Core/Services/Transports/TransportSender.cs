using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Services.Transports;

/// <summary>A transport to send: <paramref name="Cargo"/> (as far as the planet has it and its ships carry it) from <paramref name="From"/>.</summary>
/// <param name="TargetName">How the log names the target.</param>
/// <param name="CargoShipTypes">The cargo ship types in priority order (<see cref="RaidPlanner.CargoFleet"/>).</param>
/// <param name="MaxFlight">Not sent when the flight takes this long or longer (e.g. the target pays it itself before).</param>
public sealed record TransportRequest(
    OwnPlanet From,
    Coordinates Target,
    PlanetType TargetType,
    string TargetName,
    ResourceSet Cargo,
    IReadOnlyList<string> CargoShipTypes,
    int SpeedPercent,
    bool TestMode,
    TimeSpan? MaxFlight = null);

public enum TransportStatus
{
    Sent,
    NotSent,

    /// <summary>Every fleet slot (the module may use) is in use.</summary>
    NoFleetSlot
}

/// <param name="Cargo">What was sent.</param>
public sealed record TransportOutcome(TransportStatus Status, ResourceSet Cargo = default, TimeSpan Flight = default)
{
    public static readonly TransportOutcome NotSent = new(TransportStatus.NotSent);
}

/// <summary>
/// Sends one transport of own resources (the Moons tab's transports, the Builder's resource sharing), inside the
/// caller's queue group: the origin's fleet page (slots, stock, ships, capacities), the cargo ships by priority, the
/// briefing (the flight's fuel stays on the origin and takes cargo space), then <c>submitfleet</c>. The cargo is rounded
/// like a player would type it (<see cref="CargoRounding"/>): up as far as the stock allows, down where the stock, the
/// fuel or the ships cut it.
/// </summary>
public static class TransportSender
{
    /// <param name="slots">How the caller may use the slots the fleet page shows (claims of other modules counted as used); null = as read.</param>
    /// <param name="report">Gets what happened (without the target's name, which the caller puts first).</param>
    public static async Task<TransportOutcome> SendAsync(
        IOgamexClient client, TransportRequest request, Func<FleetSlots, FleetSlots>? slots, Action<string> report, CancellationToken cancellationToken)
    {
        var planet = request.From;
        var fleet = await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false);
        if (fleet.Slots is { } read && (slots?.Invoke(read) ?? read) is var usable && usable.Free <= FleetSlots.KeptForEmergencies)
        {
            report(usable.Used == read.Used
                ? $"no free fleet slot ({read.Used}/{read.Max})."
                : $"no free fleet slot ({read.Used}/{read.Max}, {usable.Used - read.Used} claimed by other modules).");
            return new TransportOutcome(TransportStatus.NoFleetSlot);
        }

        var stock = fleet.Resources ?? ResourceSet.Zero;
        var send = CargoRounding.Within(Min(request.Cargo, stock), stock);
        if (Total(send) == 0)
        {
            report($"{planet} has none of the {request.Cargo} to send.");
            return TransportOutcome.NotSent;
        }

        var ships = RaidPlanner.CargoFleet(Total(send), request.CargoShipTypes, fleet.QuantityOf, fleet.CargoCapacityOf);
        var capacity = ships.Sum(s => s.Quantity * fleet.CargoCapacityOf(s.ShipType));
        if (capacity == 0)
        {
            report($"no {string.Join(" / ", request.CargoShipTypes.Select(UnitCatalog.NameOf))} on {planet} to carry {send}.");
            return TransportOutcome.NotSent;
        }

        var mission = new FleetMission(request.Target, request.TargetType, MissionType.Transport, ships, Math.Clamp(request.SpeedPercent, 10, 100));
        var briefing = await client.GetFleetBriefingAsync(mission, planet.Id, cancellationToken).ConfigureAwait(false);
        if (!briefing.AvailableMissions.Contains(MissionType.Transport))
        {
            report($"transport isn't possible ({briefing.Warning ?? "no transport mission"}).");
            return TransportOutcome.NotSent;
        }

        if (request.MaxFlight is { } max && briefing.OneWayFlight >= max)
        {
            report($"not sent: the flight from {planet} takes {briefing.OneWayFlight:g}, the target pays it itself in {max:g}.");
            return TransportOutcome.NotSent;
        }

        // The flight's fuel stays on the planet and takes cargo space: when that leaves the ships short, take more
        // (as far as the planet has them) and ask again, since the fuel depends on the ships.
        var fuel = Math.Max(0, briefing.DeuteriumConsumption);
        if (capacity - fuel < Total(send))
        {
            var more = RaidPlanner.CargoFleet(Total(send) + fuel, request.CargoShipTypes, fleet.QuantityOf, fleet.CargoCapacityOf);
            var moreCapacity = more.Sum(s => s.Quantity * fleet.CargoCapacityOf(s.ShipType));
            if (moreCapacity > capacity)
            {
                var retry = mission with { Ships = more };
                var second = await client.GetFleetBriefingAsync(retry, planet.Id, cancellationToken).ConfigureAwait(false);
                if (second.AvailableMissions.Contains(MissionType.Transport) && (request.MaxFlight is not { } limit || second.OneWayFlight < limit))
                    (ships, capacity, mission, briefing, fuel) = (more, moreCapacity, retry, second, Math.Max(0, second.DeuteriumConsumption));
            }
        }

        var deuteriumLeft = Math.Max(0, stock.Deuterium - fuel);
        if (send.Deuterium > deuteriumLeft)
            send = send with { Deuterium = CargoRounding.Down(deuteriumLeft) };
        var room = Math.Max(0, capacity - fuel);
        if (Total(send) > room)
            send = CargoRounding.Down(Fit(send, room));
        if (Total(send) == 0)
        {
            report("the fuel leaves no room for cargo.");
            return TransportOutcome.NotSent;
        }

        var what = $"{string.Join(" + ", ships.Select(s => $"{s.Quantity:N0} {UnitCatalog.NameOf(s.ShipType)}"))} with {send} from {planet} to {request.TargetName}";
        if (request.TestMode)
        {
            report($"[Test] Would send {what} (flight {briefing.OneWayFlight:g}).");
            return TransportOutcome.NotSent;
        }

        var outcome = await client.SendFleetAsync(mission with { Cargo = send }, planet.Id, cancellationToken).ConfigureAwait(false);
        if (outcome.NoFleetSlot)
        {
            report($"{outcome.Message}.");
            return new TransportOutcome(TransportStatus.NoFleetSlot);
        }

        if (!outcome.Sent)
        {
            report($"transport refused: {outcome.Message}");
            return TransportOutcome.NotSent;
        }

        report($"sent {what}, flight {briefing.OneWayFlight:g}.");
        return new TransportOutcome(TransportStatus.Sent, send, briefing.OneWayFlight);
    }

    public static ResourceSet Min(ResourceSet a, ResourceSet b) =>
        new(Math.Max(0, Math.Min(a.Metal, b.Metal)), Math.Max(0, Math.Min(a.Crystal, b.Crystal)), Math.Max(0, Math.Min(a.Deuterium, b.Deuterium)));

    /// <summary>The same shares of each resource, scaled down to <paramref name="capacity"/> when it's more.</summary>
    public static ResourceSet Fit(ResourceSet cargo, long capacity)
    {
        var total = Total(cargo);
        if (total <= capacity)
            return cargo;
        var factor = (double)capacity / total;
        return new ResourceSet((long)(cargo.Metal * factor), (long)(cargo.Crystal * factor), (long)(cargo.Deuterium * factor));
    }

    public static long Total(ResourceSet resources) => resources.Metal + resources.Crystal + resources.Deuterium;
}

/// <summary>
/// Cargo amounts as a player would type them: two significant figures, at least whole thousands (2,345,678 → 2,300,000
/// or 2,400,000; 123,456 → 120,000 or 130,000; 800 → 0 or 1,000).
/// </summary>
public static class CargoRounding
{
    public const long MinStep = 1_000;

    /// <summary>The rounding step of <paramref name="amount"/>: a tenth of its leading power of ten, at least <see cref="MinStep"/>.</summary>
    public static long Step(long amount)
    {
        var step = 1L;
        for (var rest = amount; rest >= 100; rest /= 10)
            step *= 10;
        return Math.Max(MinStep, step);
    }

    public static long Down(long amount) => amount <= 0 ? 0 : amount / Step(amount) * Step(amount);

    public static long Up(long amount)
    {
        if (amount <= 0)
            return 0;
        var step = Step(amount);
        return (amount + step - 1) / step * step;
    }

    /// <summary>Rounded up when that stays within <paramref name="limit"/>, else down.</summary>
    public static long Within(long amount, long limit) => Up(amount) is var up && up <= limit ? up : Down(amount);

    public static ResourceSet Down(ResourceSet cargo) => new(Down(cargo.Metal), Down(cargo.Crystal), Down(cargo.Deuterium));

    /// <summary>Each resource rounded up when that stays within <paramref name="limit"/>'s, else down.</summary>
    public static ResourceSet Within(ResourceSet cargo, ResourceSet limit) =>
        new(Within(cargo.Metal, limit.Metal), Within(cargo.Crystal, limit.Crystal), Within(cargo.Deuterium, limit.Deuterium));
}
