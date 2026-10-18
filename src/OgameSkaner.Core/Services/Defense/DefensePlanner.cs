using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Services.Defense;

/// <summary>Where an escape goes.</summary>
/// <param name="Label">"its moon" or the planet's name, for messages.</param>
public sealed record EscapeDestination(Coordinates Target, PlanetType TargetType, string Label);

/// <summary>The planet a hostile fleet heads for, as far as the event list tells.</summary>
/// <param name="Planet">The own planet at the target coordinates; null when none is known there.</param>
/// <param name="OnMoon">
/// The target's name isn't the planet's, so it's the moon at those coordinates. The event list has no planet type,
/// so a moon named like its planet counts as the planet.
/// </param>
public sealed record ThreatTarget(OwnPlanet? Planet, bool OnMoon);

/// <summary>Pure rules of the attack protection: what to escape when, where to and with which cargo.</summary>
public static class DefensePlanner
{
    /// <summary>Planets tried after the first choice when the server refuses a destination.</summary>
    private const int PlanetChoices = 3;

    /// <summary>Whether the alert about this incoming fleet goes out (attacks and missiles always, spies by setting).</summary>
    public static bool ShouldNotify(FleetMovement movement, DefenseSettings settings) =>
        movement.IsIncomingHostile && (movement.Mission != MovementMission.Espionage || settings.NotifyEspionage);

    /// <summary>When the fleet of the attacked planet should leave.</summary>
    public static DateTimeOffset EscapeAt(FleetMovement movement, DefenseSettings settings) =>
        movement.ArrivesAt - TimeSpan.FromSeconds(Math.Max(0, settings.EscapeSecondsBeforeArrival));

    /// <summary>The incoming attacks whose escape is due at <paramref name="now"/>, skipping those already handled.</summary>
    public static IReadOnlyList<FleetMovement> DueEscapes(
        IEnumerable<FleetMovement> movements, IReadOnlySet<string> handled, DateTimeOffset now, DefenseSettings settings) =>
        settings.EscapeMode == FleetEscapeMode.Off
            ? []
            : movements.Where(m => m.IsIncomingHostile && m.IsAttack && !handled.Contains(m.Id) && EscapeAt(m, settings) <= now).ToList();

    /// <summary>
    /// How long to sleep: the check interval, or less when an escape falls due before then (never less than a second,
    /// so a due escape the server refused doesn't spin).
    /// </summary>
    public static TimeSpan NextWait(
        IEnumerable<FleetMovement> movements, IReadOnlySet<string> handled, DateTimeOffset now, DefenseSettings settings)
    {
        var wait = TimeSpan.FromSeconds(Math.Max(5, settings.CheckIntervalSeconds));
        if (settings.EscapeMode == FleetEscapeMode.Off)
            return wait;

        foreach (var movement in movements.Where(m => m.IsIncomingHostile && m.IsAttack && !handled.Contains(m.Id)))
        {
            var until = EscapeAt(movement, settings) - now;
            if (until < wait)
                wait = until;
        }

        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
    }

    public static ThreatTarget TargetOf(FleetMovement movement, IReadOnlyList<OwnPlanet> planets)
    {
        var planet = planets.FirstOrDefault(p => p.Coordinates == movement.Target);
        var onMoon = planet is not null
                     && movement.TargetName.Length > 0
                     && !string.Equals(movement.TargetName, planet.Name.Trim(), StringComparison.Ordinal);
        return new ThreatTarget(planet, onMoon);
    }

    /// <summary>The ships of the fleet page that can fly away.</summary>
    public static IReadOnlyList<ShipStack> EscapingShips(FleetSnapshot fleet) =>
        fleet.Ships.Where(s => s.Quantity > 0 && UnitCatalog.CanFly(s.ShipType)).ToList();

    /// <summary>Total cargo capacity of the ships (per-ship capacities from the fleet page).</summary>
    public static long CargoCapacity(IEnumerable<ShipStack> ships, FleetSnapshot fleet) =>
        ships.Sum(s => s.Quantity * fleet.CargoCapacityOf(s.ShipType));

    /// <summary>
    /// What the fleet takes along: deuterium first (minus the fuel the flight burns), then crystal, then metal, up to the
    /// cargo capacity. The dearest resources go first because they're what attackers are after.
    /// </summary>
    public static ResourceSet Cargo(ResourceSet available, long capacity, long fuel)
    {
        var left = Math.Max(0, capacity);
        long Take(long amount)
        {
            var taken = Math.Clamp(amount, 0, left);
            left -= taken;
            return taken;
        }

        var deuterium = Take(available.Deuterium - Math.Max(0, fuel));
        var crystal = Take(available.Crystal);
        var metal = Take(available.Metal);
        return new ResourceSet(metal, crystal, deuterium);
    }

    /// <summary>
    /// Where the fleet of <paramref name="planet"/> may go, best first: its moon (with <see cref="FleetEscapeMode.Moon"/>),
    /// then the nearest other planets (with <see cref="FleetEscapeMode.NearestPlanet"/>, or as the moon's fallback).
    /// Planets under attack themselves aren't destinations.
    /// </summary>
    public static IReadOnlyList<EscapeDestination> Destinations(
        OwnPlanet planet,
        IReadOnlyList<OwnPlanet> planets,
        IReadOnlyCollection<Coordinates> attacked,
        DefenseSettings settings,
        UniverseProfile universe)
    {
        var destinations = new List<EscapeDestination>();
        if (settings.EscapeMode == FleetEscapeMode.Off)
            return destinations;

        if (settings.EscapeMode == FleetEscapeMode.Moon)
            destinations.Add(new EscapeDestination(planet.Coordinates, PlanetType.Moon, "its moon"));

        if (settings.EscapeMode == FleetEscapeMode.NearestPlanet || settings.FallbackToNearestPlanet)
        {
            destinations.AddRange(planets
                .Where(p => p.Coordinates != planet.Coordinates && !attacked.Contains(p.Coordinates))
                .OrderBy(p => RaidPlanner.Distance(planet.Coordinates, p.Coordinates, universe))
                .Take(PlanetChoices)
                .Select(p => new EscapeDestination(p.Coordinates, PlanetType.Planet, $"{p.Name} [{p.Coordinates}]")));
        }

        return destinations;
    }
}
