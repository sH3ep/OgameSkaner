namespace OgameSkaner.Core.Domain;

/// <summary>A planet owned by a player, as seen during a galaxy scan.</summary>
/// <param name="Rank">The player's ranking (1 = most points); null when unknown (older scans).</param>
public sealed record PlayerPlanet(
    string PlayerName,
    Coordinates Coordinates,
    bool HasMoon,
    DateTimeOffset ScannedAt,
    PlayerStatus Status = PlayerStatus.None,
    string? PlayerId = null,
    int? Rank = null);
