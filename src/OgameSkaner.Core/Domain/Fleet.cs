using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain;

public sealed record ShipStack(string ShipType, long Quantity);

/// <summary>Fleet slots of the account ("Fleets: 1/11").</summary>
public readonly record struct FleetSlots(int Used, int Max)
{
    /// <summary>
    /// Fleet slots no automation may take, so the player (or an attack escape) always has one: the client refuses a send
    /// that would leave fewer free (<see cref="FleetMission.MayUseLastSlot"/> excepted).
    /// </summary>
    public const int KeptForEmergencies = 1;

    public int Free => Math.Max(0, Max - Used);
}

/// <summary>Ships available on the current planet and that planet's coordinates.</summary>
/// <param name="CargoCapacities">Cargo capacity of one ship per ship type (technology included), from the ship tooltips.</param>
/// <param name="ServerClock">The clock in the page header, in the time zone the game shows all dates in.</param>
/// <param name="Resources">The planet's resources (the page's <c>var resources</c> script).</param>
/// <param name="ExpeditionSlots">The account's expedition slots ("Expeditions: 0/12").</param>
public sealed record FleetSnapshot(
    IReadOnlyList<ShipStack> Ships,
    Coordinates CurrentPlanet,
    FleetSlots? Slots = null,
    IReadOnlyDictionary<string, long>? CargoCapacities = null,
    DateTime? ServerClock = null,
    ResourceSet? Resources = null,
    FleetSlots? ExpeditionSlots = null)
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

    Attack = 8,

    /// <summary>Asteroid Miners collect an asteroid at position 17.</summary>
    AsteroidMining = 12
}

/// <param name="Cargo">Resources the fleet takes along; none by default.</param>
/// <param name="HoldMinutes">An expedition's stay at position 16 (one of <see cref="FleetBriefing.ExpeditionDurations"/>).</param>
public sealed record FleetMission(
    Coordinates Target,
    PlanetType TargetType,
    MissionType Mission,
    IReadOnlyList<ShipStack> Ships,
    int SpeedPercentage = 100,
    ResourceSet Cargo = default,
    int? HoldMinutes = null)
{
    /// <summary>
    /// Whether the fleet may take the slots kept for emergencies (<see cref="FleetSlots.KeptForEmergencies"/>): only
    /// attack escapes.
    /// </summary>
    public bool MayUseLastSlot { get; init; }
}

/// <summary>Answer of <c>/fleet/submitfleet</c>.</summary>
/// <param name="NoFleetSlot">Not sent because the slots left are kept for emergencies (nothing was submitted).</param>
public sealed record FleetSendOutcome(bool Sent, string? Message = null, bool NoFleetSlot = false);

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
    string? Warning)
{
    /// <summary>The expedition durations offered, in minutes (<c>ExpeditionKeepValues</c>); empty for other targets.</summary>
    public IReadOnlyList<int> ExpeditionDurations { get; init; } = [];
}
