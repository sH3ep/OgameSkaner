namespace OgameSkaner.Core.Domain;

public sealed record ShipStack(string ShipType, long Quantity);

/// <summary>Fleet slots of the account ("Fleets: 1/11").</summary>
public readonly record struct FleetSlots(int Used, int Max)
{
    public int Free => Math.Max(0, Max - Used);
}

/// <summary>Ships available on the current planet and that planet's coordinates.</summary>
/// <param name="CargoCapacities">Cargo capacity of one ship per ship type (technology included), from the ship tooltips.</param>
/// <param name="ServerClock">The clock in the page header, in the time zone the game shows all dates in.</param>
public sealed record FleetSnapshot(
    IReadOnlyList<ShipStack> Ships,
    Coordinates CurrentPlanet,
    FleetSlots? Slots = null,
    IReadOnlyDictionary<string, long>? CargoCapacities = null,
    DateTime? ServerClock = null)
{
    public long QuantityOf(string shipType) => Ships.FirstOrDefault(s => s.ShipType == shipType)?.Quantity ?? 0;

    public long CargoCapacityOf(string shipType) => CargoCapacities?.GetValueOrDefault(shipType) ?? 0;
}

/// <summary>Mission ids used by the OgameX fleet dispatch API (<c>data-mission-type</c> on the fleet page).</summary>
public enum MissionType
{
    Expedition = 1,
    Colonize = 2,
    Recycle = 3,
    Transport = 4,

    /// <summary>Stays at a planet or moon of the own empire; used by the fleet escape utility.</summary>
    Deploy = 5,

    Spy = 6,

    /// <summary>Defends an allied player's planet or moon.</summary>
    Hold = 7,

    Attack = 8
}

public sealed record FleetMission(
    Coordinates Target,
    PlanetType TargetType,
    MissionType Mission,
    IReadOnlyList<ShipStack> Ships,
    int SpeedPercentage = 100);

/// <summary>Answer of <c>/fleet/submitfleet</c>.</summary>
public sealed record FleetSendOutcome(bool Sent, string? Message = null);

/// <summary>
/// What the game's fleet page shows before sending (<c>/fleet/fleetpage3data</c>): flight time, the missions possible
/// against the target and the target player's current state. Nothing is sent.
/// </summary>
public sealed record FleetBriefing(
    IReadOnlyList<MissionType> AvailableMissions,
    TimeSpan OneWayFlight,
    long DeuteriumConsumption,
    long Distance,
    string? PlayerId,
    string? PlayerName,
    int? PlayerRank,
    long PlayerPoints,
    long PlayerFleetPoints,
    PlayerStatus PlayerStatus,
    string? Warning);
