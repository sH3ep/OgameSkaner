namespace OgameSkaner.Core.Domain.Raiding;

/// <summary>A fleet the raider sent (an attack or a spy mission).</summary>
public sealed record RaidRecord(
    Coordinates Target,
    PlanetType TargetType,
    string? PlayerName,
    MissionType Mission,
    Coordinates Origin,
    string ShipType,
    long Ships,
    long ExpectedLoot,
    DateTimeOffset SentAt,
    DateTimeOffset ArrivesAt,
    DateTimeOffset ReturnsAt);
