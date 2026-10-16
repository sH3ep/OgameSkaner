namespace OgameSkaner.Core.Domain.Buildings;

/// <summary>The server's id of one of the player's own planets (used to switch the active planet).</summary>
public readonly record struct PlanetId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>One of the player's own planets.</summary>
public sealed record OwnPlanet(PlanetId Id, string Name, Coordinates Coordinates)
{
    public override string ToString() => $"{Name} [{Coordinates}]";
}

/// <summary>Solar satellites on a planet, from the shipyard page.</summary>
/// <param name="SatellitesLocked">The shipyard is missing (needs level 1), so no satellites can be built.</param>
/// <param name="SatelliteDuration">Production time of one satellite.</param>
public sealed record ShipyardState(
    PlanetId Planet,
    int Satellites,
    double EnergyPerSatellite,
    ResourceSet SatelliteCost,
    bool SatellitesLocked,
    TimeSpan? SatelliteDuration = null);

/// <summary>The player's planets and the one that is currently selected in the game.</summary>
public sealed record OwnPlanetsSnapshot(IReadOnlyList<OwnPlanet> Planets, PlanetId CurrentPlanet);

/// <summary>An entry of a planet's build queue; <paramref name="Level"/> is the level it upgrades to.</summary>
public sealed record QueuedBuilding(BuildingType Building, int Level, bool Started);

/// <summary>Everything the planner needs to know about a planet.</summary>
/// <param name="Levels">Current levels, without what is still in the queue.</param>
/// <param name="Queue">The running item first (Started), then the waiting ones in order.</param>
/// <param name="EnergyBalance">
/// Energy production minus consumption as reported by the game for the built levels (queue not included). It
/// includes every bonus (research, officers, class), which the formulas don't know about.
/// </param>
public sealed record PlanetBuildState(
    OwnPlanet Planet,
    IReadOnlyDictionary<BuildingType, int> Levels,
    ResourceSet Resources,
    IReadOnlyList<QueuedBuilding> Queue,
    long EnergyBalance = 0)
{
    /// <summary>
    /// Cost of the next level the game would queue for each building (after what is already queued), as shown on
    /// the game page. Buildings missing here fall back to the catalog formula.
    /// </summary>
    public IReadOnlyDictionary<BuildingType, ResourceSet> NextLevelCosts { get; init; } = new Dictionary<BuildingType, ResourceSet>();

    /// <summary>Construction time of each building's next level (after the queue), as shown on the game page.</summary>
    public IReadOnlyDictionary<BuildingType, TimeSpan> NextLevelDurations { get; init; } = new Dictionary<BuildingType, TimeSpan>();

    /// <summary>Resource production per hour; null when unknown.</summary>
    public ResourceSet? ProductionPerHour { get; init; }

    /// <summary>Storage capacity per resource; null when unknown (then storages are not planned).</summary>
    public ResourceSet? StorageCapacity { get; init; }

    /// <summary>Free planet fields with the queue already counted; every new level takes one. Null when unknown.</summary>
    public int? FreeFields { get; init; }

    /// <summary>The player is in vacation mode, which blocks building.</summary>
    public bool VacationMode { get; init; }

    /// <summary>Buildings whose requirements are not met yet; they cannot be queued.</summary>
    public IReadOnlySet<BuildingType> Locked { get; init; } = new HashSet<BuildingType>();

    public int LevelOf(BuildingType building) => Levels.TryGetValue(building, out var level) ? level : 0;

    /// <summary>True when <paramref name="level"/> of the building is built or in the queue.</summary>
    public bool HasReached(BuildingType building, int level) =>
        LevelOf(building) >= level || Queue.Any(q => q.Building == building && q.Level >= level);
}

/// <summary>What the server answered to adding a building to the queue.</summary>
public enum EnqueueResult
{
    Queued,
    NotEnoughResources,
    QueueFull,
    Rejected
}

/// <param name="Message">The server's message, if any (e.g. why it was rejected).</param>
public sealed record EnqueueOutcome(EnqueueResult Result, string? Message = null)
{
    public static EnqueueOutcome Queued { get; } = new(EnqueueResult.Queued);
}
