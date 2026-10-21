namespace OgameSkaner.Core.Domain.Raiding;

/// <summary>
/// A planet marked low resource: the raider doesn't spy on it until <see cref="Until"/> (or until the player lifts the
/// mark). Bans are kept as history, so a repeated one lasts longer.
/// </summary>
/// <param name="Nth">1 for the planet's first ban, 2 for the next one, and so on.</param>
/// <param name="Reason">What the statistics saw, e.g. "3 reports, best loot 120,000".</param>
/// <param name="LiftedAt">When the player lifted it before <see cref="Until"/>; null when it runs its course.</param>
public sealed record TargetBan(Coordinates Target, DateTimeOffset BannedAt, DateTimeOffset Until, int Nth, string Reason, DateTimeOffset? LiftedAt = null)
{
    /// <summary>When the ban stopped (or stops) counting.</summary>
    public DateTimeOffset EndsAt => LiftedAt is { } lifted && lifted < Until ? lifted : Until;

    public bool IsActive(DateTimeOffset now) => BannedAt <= now && now < EndsAt;
}
