namespace OgameSkaner.Core.Domain;

/// <summary>A planet owned by a player, as seen during a galaxy scan.</summary>
public sealed record PlayerPlanet(string PlayerName, Coordinates Coordinates, bool HasMoon, DateTimeOffset ScannedAt);
