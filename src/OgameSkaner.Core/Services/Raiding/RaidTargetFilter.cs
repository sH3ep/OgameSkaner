namespace OgameSkaner.Core.Services.Raiding;

/// <summary>
/// What the Raids grid shows: a galaxy and system range and at most this many defense points in the ranking. A null
/// limit doesn't filter; with a defense limit, players not in the ranking (not read yet, no id) are hidden too.
/// </summary>
public sealed record RaidTargetFilter(
    int? GalaxyFrom = null,
    int? GalaxyTo = null,
    int? SystemFrom = null,
    int? SystemTo = null,
    long? MaxRankingDefensePoints = null)
{
    public static RaidTargetFilter None { get; } = new();

    public bool Matches(RaidTarget target)
    {
        var at = target.Planet.Coordinates;
        return (GalaxyFrom is null || at.Galaxy >= GalaxyFrom)
               && (GalaxyTo is null || at.Galaxy <= GalaxyTo)
               && (SystemFrom is null || at.System >= SystemFrom)
               && (SystemTo is null || at.System <= SystemTo)
               && (MaxRankingDefensePoints is null || target.RankingDefensePoints <= MaxRankingDefensePoints);
    }
}
