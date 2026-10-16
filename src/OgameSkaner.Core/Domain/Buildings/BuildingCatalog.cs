namespace OgameSkaner.Core.Domain.Buildings;

/// <summary>Buildings the automation knows about. Adding one = a value here and an entry in <see cref="BuildingCatalog"/>.</summary>
public enum BuildingType
{
    MetalMine,
    CrystalMine,
    DeuteriumSynthesizer,
    SolarPlant,
    FusionReactor,
    MetalStorage,
    CrystalStorage,
    DeuteriumTank,
    RoboticsFactory,
    Shipyard,
    ResearchLab,
    NaniteFactory,
    University,
    Terraformer,
    MissileSilo
}

public enum BuildingCategory
{
    Resource,
    Facility
}

/// <param name="ServerId">The id OgameX uses for the building in page markup and requests.</param>
/// <param name="CostFactor">
/// Cost growth per level. Only used to estimate levels beyond the next one: the cost of the next level is
/// read from the game page, because OgameX does not follow the standard formulas for every building.
/// </param>
public sealed record BuildingDefinition(
    BuildingType Type,
    BuildingCategory Category,
    string DisplayName,
    string ServerId,
    ResourceSet BaseCost,
    double CostFactor)
{
    /// <summary>Cost of upgrading to <paramref name="level"/> (base·factor^(level−1)).</summary>
    public ResourceSet CostOf(int level) =>
        level < 1 ? ResourceSet.Zero : BaseCost.Scale(Math.Pow(CostFactor, level - 1));
}

public static class BuildingCatalog
{
    // Bases and factors checked against OgameX pages on 2026-10-16 where noted; see docs/ogamex-build-api.md.
    private static readonly Dictionary<BuildingType, BuildingDefinition> Definitions = new BuildingDefinition[]
    {
        // Mines: no exact formula found (growth slowly drops with level); the factors are measured over 4–11 levels.
        new(BuildingType.MetalMine, BuildingCategory.Resource, "Metal Mine", "METAL_MINE", new(60, 15, 0), 1.45),
        new(BuildingType.CrystalMine, BuildingCategory.Resource, "Crystal Mine", "CRYSTAL_MINE", new(48, 24, 0), 1.45),
        new(BuildingType.DeuteriumSynthesizer, BuildingCategory.Resource, "Deuterium Synthesizer", "DEUTERIUM_REFINERY", new(225, 75, 0), 1.48),
        new(BuildingType.SolarPlant, BuildingCategory.Resource, "Solar Plant", "SOLAR_POWER_PLANT", new(100, 40, 0), 1.5),
        new(BuildingType.FusionReactor, BuildingCategory.Resource, "Fusion Reactor", "FUSION_REACTOR", new(900, 360, 180), 1.8),
        new(BuildingType.MetalStorage, BuildingCategory.Resource, "Metal Storage", "METAL_STORAGE", new(20_000, 0, 0), 1.5),
        new(BuildingType.CrystalStorage, BuildingCategory.Resource, "Crystal Storage", "CRYSTAL_STORAGE", new(20_000, 10_000, 0), 1.5),
        new(BuildingType.DeuteriumTank, BuildingCategory.Resource, "Deuterium Tank", "DEUTERIUM_TANK", new(20_000, 20_000, 0), 1.5),
        new(BuildingType.RoboticsFactory, BuildingCategory.Facility, "Robotics Factory", "ROBOT_FACTORY", new(400, 120, 200), 2),
        new(BuildingType.Shipyard, BuildingCategory.Facility, "Shipyard", "SHIPYARD", new(400, 200, 100), 2),
        new(BuildingType.ResearchLab, BuildingCategory.Facility, "Research Lab", "RESEARCH_LAB", new(200, 400, 200), 2),
        new(BuildingType.NaniteFactory, BuildingCategory.Facility, "Nanite Factory", "NANITE_FACTORY", new(1_000_000, 500_000, 100_000), 2),
        // Not verified yet (locked on the test account).
        new(BuildingType.University, BuildingCategory.Facility, "University", "UNIVERSITY", new(100_000, 150_000, 50_000), 2),
        new(BuildingType.Terraformer, BuildingCategory.Facility, "Terraformer", "TERRAFORMER", new(0, 50_000, 100_000), 2),
        new(BuildingType.MissileSilo, BuildingCategory.Facility, "Missile Silo", "MISSILE_SILO", new(20_000, 20_000, 1_000), 2)
    }.ToDictionary(d => d.Type);

    private static readonly Dictionary<string, BuildingType> ByServerId =
        Definitions.Values.ToDictionary(d => d.ServerId, d => d.Type, StringComparer.Ordinal);

    /// <summary>All buildings in display order.</summary>
    public static IReadOnlyList<BuildingDefinition> All { get; } = Enum.GetValues<BuildingType>().Select(t => Definitions[t]).ToList();

    public static BuildingDefinition Get(BuildingType type) => Definitions[type];

    public static ResourceSet CostOf(BuildingType type, int level) => Definitions[type].CostOf(level);

    /// <summary>Maps an OgameX id such as <c>METAL_MINE</c>; false for buildings we don't automate (e.g. <c>MOON_BASE</c>).</summary>
    public static bool TryGetByServerId(string? serverId, out BuildingType type) =>
        ByServerId.TryGetValue(serverId ?? string.Empty, out type);
}

/// <summary>
/// Base energy formulas (no bonuses) of the buildings that matter for planning the solar plant. They are only used
/// for the difference a planned level makes; the starting point is always the game's own balance.
/// </summary>
public static class EnergyFormulas
{
    /// <summary>Energy used by a mine at <paramref name="level"/>; 0 for buildings that use no energy.</summary>
    public static long ConsumptionOf(BuildingType type, int level)
    {
        var factor = type switch
        {
            BuildingType.MetalMine or BuildingType.CrystalMine => 10,
            BuildingType.DeuteriumSynthesizer => 20,
            _ => 0
        };
        return level <= 0 || factor == 0 ? 0 : (long)Math.Ceiling(factor * level * Math.Pow(1.1, level));
    }

    public static long SolarProduction(int level) =>
        level <= 0 ? 0 : (long)Math.Floor(20 * level * Math.Pow(1.1, level));

    public static bool UsesEnergy(BuildingType type) => ConsumptionOf(type, 1) > 0;

    /// <summary>Net energy of a building at a level: solar production is positive, mine consumption negative.</summary>
    public static long NetOf(BuildingType type, int level) =>
        type == BuildingType.SolarPlant ? SolarProduction(level) : -ConsumptionOf(type, level);

    /// <summary>
    /// The game's current balance plus what the planned levels add or use compared with the built ones.
    /// Bonuses mostly raise production, so leaving them out of the solar plant's gain errs on the safe side
    /// (the solar plant is planned a little early rather than late).
    /// </summary>
    public static long Projected(long currentBalance, IReadOnlyDictionary<BuildingType, int> built, IReadOnlyDictionary<BuildingType, int> planned) =>
        currentBalance + planned.Sum(p => NetOf(p.Key, p.Value) - NetOf(p.Key, built.TryGetValue(p.Key, out var level) ? level : 0));
}
