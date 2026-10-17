namespace OgameSkaner.Core.Domain;

/// <summary>
/// What a scan saw at a slot when it differed from the scan before: a planet (owner, moon) or, with no
/// <see cref="PlayerName"/>, a slot that became free.
/// </summary>
public sealed record SlotObservation(
    Coordinates Coordinates,
    DateTimeOffset ObservedAt,
    string? PlayerName,
    string? PlayerId,
    bool HasMoon)
{
    public bool IsFree => PlayerName is null;
}

/// <summary>A player's name, status and rank when a scan saw them differ from before.</summary>
public sealed record PlayerObservation(
    string? PlayerId,
    string PlayerName,
    DateTimeOffset ObservedAt,
    PlayerStatus Status,
    int? Rank);
