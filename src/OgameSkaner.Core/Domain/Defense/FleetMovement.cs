namespace OgameSkaner.Core.Domain.Defense;

/// <summary>The mission of a row in the game's event list (<c>row-mission-type-…</c>).</summary>
public enum MovementMission
{
    Unknown,
    Expedition,
    Deploy,
    Collect,
    Transport,
    Colonize,
    Recycle,
    Espionage,
    Hold,
    Attack,
    AcsAttack,
    Destroy,
    MissileAttack,
    AsteroidMining
}

/// <summary>Whose fleet a row of the event list is: <c>row-hostile-mission</c>, <c>row-friendly-mission</c> or neither.</summary>
public enum MovementRelation
{
    Own,
    Friendly,
    Hostile
}

/// <summary>One fleet of the game's event list (<c>/home/fleetmovementlist</c>).</summary>
/// <param name="Id">The row's <c>data-fleet-id</c>, or a key made from the row when it has none.</param>
/// <param name="Returning">The fleet is on its way back (<c>row-fleet-return</c>).</param>
/// <param name="ArrivesAt">When the fleet reaches <paramref name="Target"/> (or its origin when returning).</param>
/// <param name="ShipCount">Ships in the fleet as the list shows them; 0 when it doesn't show a number.</param>
/// <param name="SenderName">The attacking player (<c>.fleet-sender-player</c>); null for own fleets.</param>
public sealed record FleetMovement(
    string Id,
    MovementMission Mission,
    MovementRelation Relation,
    bool Returning,
    DateTimeOffset ArrivesAt,
    Coordinates Origin,
    string OriginName,
    Coordinates Target,
    string TargetName,
    long ShipCount,
    string? SenderName = null)
{
    /// <summary>A mission that fights the fleet on the target (missiles only hit defenses).</summary>
    public bool IsAttack => Mission is MovementMission.Attack or MovementMission.AcsAttack or MovementMission.Destroy;

    /// <summary>A hostile fleet on its way to one of the player's planets or moons.</summary>
    public bool IsIncomingHostile => Relation == MovementRelation.Hostile && !Returning;
}
