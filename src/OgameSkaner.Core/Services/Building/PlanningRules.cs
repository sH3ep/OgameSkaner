using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Services.Building;

/// <summary>Helpers shared by the planners (<see cref="BuildPlanner"/> and the schema planner).</summary>
internal static class PlanetLevels
{
    /// <summary>Current levels with everything in the queue counted as built.</summary>
    public static Dictionary<BuildingType, int> Planned(PlanetBuildState state)
    {
        var levels = new Dictionary<BuildingType, int>(state.Levels);
        foreach (var queued in state.Queue)
            levels[queued.Building] = Math.Max(Of(levels, queued.Building), queued.Level);
        return levels;
    }

    public static int Of(IReadOnlyDictionary<BuildingType, int> levels, BuildingType building) =>
        levels.TryGetValue(building, out var level) ? level : 0;
}

/// <summary>
/// Costs and construction times anchored on the game page: the next level (after the queue) costs and takes what
/// the page says, other levels are scaled by the catalog's growth from there. Without page values the catalog
/// formula is used for costs and the time is unknown.
/// </summary>
internal sealed class CostModel(PlanetBuildState state)
{
    // The page shows the level right after everything already queued.
    private readonly Dictionary<BuildingType, int> _plannedLevels = PlanetLevels.Planned(state);

    public PlannedUpgrade Upgrade(BuildingType building, int level) => new(building, level, CostOf(building, level));

    public ResourceSet CostOf(BuildingType building, int level) =>
        state.NextLevelCosts.TryGetValue(building, out var pageCost)
            ? pageCost.Scale(Growth(building, level))
            : BuildingCatalog.CostOf(building, level);

    /// <summary>Estimated construction time with today's factories; null when the page gave none.</summary>
    public TimeSpan? DurationOf(BuildingType building, int level) =>
        state.NextLevelDurations.TryGetValue(building, out var pageDuration)
            ? TimeSpan.FromSeconds(pageDuration.TotalSeconds * Growth(building, level))
            : null;

    /// <summary>How much <paramref name="level"/> costs compared to the page's level, by the game's formula (tiers included).</summary>
    private double Growth(BuildingType building, int level)
    {
        var shown = PlanetLevels.Of(_plannedLevels, building) + 1;
        if (level == shown)
            return 1;

        static double Units(ResourceSet cost) => (double)cost.Metal + cost.Crystal + cost.Deuterium;
        var from = Units(BuildingCatalog.CostOf(building, shown));
        return from > 0
            ? Units(BuildingCatalog.CostOf(building, level)) / from
            : Math.Pow(BuildingCatalog.Get(building).CostFactor, level - shown);
    }
}

/// <summary>Queues a storage when its resource is (nearly) full.</summary>
internal static class StorageRule
{
    private static readonly (BuildingType Storage, Func<ResourceSet, long> Amount)[] Storages =
    [
        (BuildingType.MetalStorage, r => r.Metal),
        (BuildingType.CrystalStorage, r => r.Crystal),
        (BuildingType.DeuteriumTank, r => r.Deuterium)
    ];

    /// <summary>
    /// The next level of each storage whose resource is at <paramref name="fillPercent"/> of capacity or above,
    /// unless a level of that storage is already planned (queued or earlier in the plan); fullest first.
    /// </summary>
    public static IEnumerable<PlannedUpgrade> Needed(IReadOnlyDictionary<BuildingType, int> levels, int fillPercent, PlanetBuildState state, CostModel costs)
    {
        if (fillPercent <= 0 || state.StorageCapacity is not { } capacity)
            return [];

        return Storages
            .Where(s => !state.Locked.Contains(s.Storage) && PlanetLevels.Of(levels, s.Storage) == state.LevelOf(s.Storage))
            .Select(s => (s.Storage, Fill: s.Amount(capacity) > 0 ? (double)s.Amount(state.Resources) / s.Amount(capacity) : 0))
            .Where(s => s.Fill * 100 >= fillPercent)
            .OrderByDescending(s => s.Fill)
            .Select(s => costs.Upgrade(s.Storage, PlanetLevels.Of(levels, s.Storage) + 1));
    }
}
