using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Services.Expeditions;

/// <summary>
/// The rules of the Expeditions page: each origin keeps its configured number of expeditions in the air, every
/// expedition flies to position 16 of the origin's system, and the ticked ships at home are shared by the expeditions
/// the origin still has to send.
/// </summary>
public static class ExpeditionPlanner
{
    /// <summary>The expedition position of every system.</summary>
    public const int Position = 16;

    /// <summary>The check after a return waits this long, so the fleet page shows the ships home again.</summary>
    public static readonly TimeSpan ReturnMargin = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MinWait = TimeSpan.FromSeconds(30);

    public static Coordinates Target(Coordinates origin) => new(origin.Galaxy, origin.System, Position);

    /// <summary>
    /// The own expeditions in the event list that left from <paramref name="place"/>, once per fleet. A planet and its
    /// moon share their coordinates, so with <paramref name="sharedCoordinates"/> the place's name tells them apart.
    /// </summary>
    public static int InFlight(IEnumerable<FleetMovement> movements, OwnPlanet place, bool sharedCoordinates) =>
        OwnExpeditions(movements)
            .Where(m => m.Origin == place.Coordinates && (!sharedCoordinates || m.OriginName == place.Name))
            .Select(m => m.Id)
            .Distinct()
            .Count();

    /// <summary>The own expeditions of the event list.</summary>
    public static IEnumerable<FleetMovement> OwnExpeditions(IEnumerable<FleetMovement> movements) =>
        movements.Where(m => m.Relation == MovementRelation.Own && m.Mission == MovementMission.Expedition);

    /// <summary>
    /// Expeditions that may leave now: the free expedition slots, and the free fleet slots minus the ones kept for the
    /// other pages (an expedition takes one of each).
    /// </summary>
    public static int Budget(FleetSlots fleets, FleetSlots? expeditions, int keepFleetSlotsFree)
    {
        var byFleets = fleets.Free - Math.Max(0, keepFleetSlotsFree);
        return Math.Max(0, expeditions is { } e ? Math.Min(e.Free, byFleets) : byFleets);
    }

    /// <summary>
    /// The next expedition's ships: of each ticked type at home, the share of the <paramref name="remaining"/>
    /// expeditions the origin still sends (rounded up, so the last one takes the rest). Empty when none is home.
    /// </summary>
    public static IReadOnlyList<ShipStack> Fleet(FleetSnapshot fleet, IReadOnlyCollection<string> shipTypes, int remaining)
    {
        var share = Math.Max(1, remaining);
        return fleet.Ships
            .Where(s => s.Quantity > 0 && shipTypes.Contains(s.ShipType) && UnitCatalog.CanFly(s.ShipType))
            .Select(s => new ShipStack(s.ShipType, (s.Quantity + share - 1) / share))
            .ToList();
    }

    /// <summary>The offered duration nearest to <paramref name="wanted"/> (the shorter one on a tie); the wish without offers.</summary>
    public static int Hold(IReadOnlyList<int> offered, int wanted) =>
        offered.Count == 0 ? wanted : offered.OrderBy(v => Math.Abs(v - wanted)).ThenBy(v => v).First();

    /// <summary>Until the next expedition changes state (plus <see cref="ReturnMargin"/>), at least 30 s and at most the interval.</summary>
    public static TimeSpan NextWait(IEnumerable<DateTimeOffset> changes, DateTimeOffset now, TimeSpan interval)
    {
        var next = changes
            .Select(t => t + ReturnMargin - now)
            .Where(t => t > TimeSpan.Zero)
            .Append(interval)
            .Min();
        return TimeSpan.FromTicks(Math.Clamp(next.Ticks, Math.Min(MinWait.Ticks, interval.Ticks), interval.Ticks));
    }
}
