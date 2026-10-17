using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Raiding;

/// <summary>Units of one type in a report. <paramref name="Unit"/> is the catalog id, or the text shown when the unit isn't known.</summary>
public sealed record UnitCount(string Unit, long Quantity);

/// <summary>A section of a report's detail page the probes saw: the points the game shows in its header and the units listed.</summary>
public sealed record UnitSection(long Points, IReadOnlyList<UnitCount> Units);

/// <summary>An espionage report: the list entry plus, once read, the ships and defenses of the detail page.</summary>
/// <param name="Id">The message id (also the detail page's report id).</param>
/// <param name="Ships">Null when the probes didn't see the ships (or the detail wasn't read).</param>
/// <param name="Defenses">Null when the probes didn't see the defenses (or the detail wasn't read).</param>
public sealed record EspionageReport(
    string Id,
    Coordinates Target,
    PlanetType TargetType,
    DateTimeOffset Time,
    string? PlayerName,
    string? PlayerId,
    PlayerStatus PlayerStatus,
    ResourceSet Resources,
    int? CounterEspionagePercent,
    UnitSection? Ships = null,
    UnitSection? Defenses = null)
{
    /// <summary>The ships' points as the game shows them (all ships, satellites included); null when unseen.</summary>
    public long? FleetPoints => Ships?.Points;

    /// <summary>The defenses' points as the game shows them (missiles included); null when unseen.</summary>
    public long? DefensePoints => Defenses?.Points;

    public long TotalResources => Resources.Metal + Resources.Crystal + Resources.Deuterium;
}
