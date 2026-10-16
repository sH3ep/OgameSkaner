namespace OgameSkaner.Core.Domain;

public sealed record ShipStack(string ShipType, long Quantity);

/// <summary>Ships available on the current planet and that planet's coordinates.</summary>
public sealed record FleetSnapshot(IReadOnlyList<ShipStack> Ships, Coordinates CurrentPlanet);

/// <summary>Mission ids used by the OgameX fleet dispatch API.</summary>
public enum MissionType
{
    /// <summary>Mission 5 – the mission used by the fleet escape utility.</summary>
    Hold = 5
}

public sealed record FleetMission(
    Coordinates Target,
    PlanetType TargetType,
    MissionType Mission,
    IReadOnlyList<ShipStack> Ships,
    int SpeedPercentage = 100);
