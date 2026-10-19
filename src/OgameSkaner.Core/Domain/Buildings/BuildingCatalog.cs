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
    MissileSilo,

    // Moon only (the moon also has Robotics, Nanite and Shipyard).
    MoonBase,
    PhalanxSensor,
    JumpGate
}

public enum BuildingCategory
{
    Resource,
    Facility,

    /// <summary>Only on moons.</summary>
    Moon
}

/// <summary>
/// One stretch of a tiered cost formula: levels up to <paramref name="UpTo"/> cost
/// <paramref name="Metal"/>/<paramref name="Crystal"/>·<paramref name="Factor"/>^(level − <paramref name="From"/>).
/// </summary>
public sealed record CostTier(int From, int UpTo, double Metal, double Crystal, double Factor);

/// <param name="ServerId">The id OgameX uses for the building in page markup and requests.</param>
/// <param name="CostFactor">Cost growth per level (of the first tier for tiered buildings).</param>
/// <param name="Tiers">
/// The game's tiered formula (mines and solar plant: the growth drops every 10 levels), or null for base·factor^(level−1).
/// </param>
public sealed record BuildingDefinition(
    BuildingType Type,
    BuildingCategory Category,
    string DisplayName,
    string ServerId,
    ResourceSet BaseCost,
    double CostFactor,
    IReadOnlyList<CostTier>? Tiers = null)
{
    /// <summary>Cost of upgrading to <paramref name="level"/>, rounded like the game pages (solar plant 8 = 1,708.6 → 1,709).</summary>
    public ResourceSet CostOf(int level)
    {
        var (metal, crystal, deuterium) = ExactCost(level);
        return new ResourceSet(Round(metal), Round(crystal), Round(deuterium));
    }

    /// <summary>The formula's value before rounding (doubles: no overflow for very high levels).</summary>
    public (double Metal, double Crystal, double Deuterium) ExactCost(int level)
    {
        if (level < 1)
            return (0, 0, 0);
        if (Tiers is null)
        {
            var factor = Math.Pow(CostFactor, level - 1);
            return (BaseCost.Metal * factor, BaseCost.Crystal * factor, BaseCost.Deuterium * factor);
        }

        var tier = Tiers.FirstOrDefault(t => level <= t.UpTo) ?? Tiers[^1];
        var growth = Math.Pow(tier.Factor, level - tier.From);
        return (tier.Metal * growth, tier.Crystal * growth, 0);
    }

    /// <summary>Rounded like the game; a cost beyond <see cref="long"/> saturates.</summary>
    internal static long Round(double value) => value >= long.MaxValue ? long.MaxValue : (long)Math.Round(value, MidpointRounding.AwayFromZero);
}

public static class BuildingCatalog
{
    // The game's own formulas (/assets/js/app/pages/simulatorCostTable.js, "V2" universes such as genesis; read
    // 2026-10-19), see docs/ogamex-build-api.md. The pages round where that table truncates.
    private static readonly Dictionary<BuildingType, BuildingDefinition> Definitions = new BuildingDefinition[]
    {
        new(BuildingType.MetalMine, BuildingCategory.Resource, "Metal Mine", "METAL_MINE", new(80, 20, 0), 1.48, MineTiers(
            (80, 20), (137_424, 34_356), (6_047_713, 1_511_928), (240_032_257, 60_008_064), (8_582_819_573, 2_145_704_893),
            (266_576_038_331, 66_644_009_575), (1_383_232_265_343, 345_808_066_295), (6_675_717_445_001, 1_668_929_361_053),
            (29_934_140_242_686, 7_483_535_059_787), (124_573_307_057_205, 31_143_326_760_620))),
        new(BuildingType.CrystalMine, BuildingCategory.Resource, "Crystal Mine", "CRYSTAL_MINE", new(64, 32, 0), 1.48, MineTiers(
            (64, 32), (109_939, 54_970), (4_838_170, 2_419_085), (192_025_805, 96_012_903), (6_866_255_658, 3_433_127_829),
            (213_260_830_652, 106_630_415_326), (1_106_585_812_208, 553_292_906_104), (5_340_573_955_680, 2_670_286_977_840),
            (23_947_312_192_710, 11_973_656_096_355), (99_658_645_639_776, 49_829_322_819_888))),
        new(BuildingType.DeuteriumSynthesizer, BuildingCategory.Resource, "Deuterium Synthesizer", "DEUTERIUM_REFINERY", new(340, 100, 0), 1.48, MineTiers(
            (340, 100), (584_052, 171_780), (25_702_778, 7_559_641), (1_020_137_091, 300_040_321), (36_476_983_184, 10_728_524_466),
            (1_132_948_162_869, 333_220_047_906), (5_878_737_127_512, 1_729_040_331_638), (28_371_799_140_311, 8_344_646_806_055),
            (127_220_096_027_188, 37_417_675_302_478), (529_436_554_975_531, 155_716_633_817_847))),
        new(BuildingType.SolarPlant, BuildingCategory.Resource, "Solar Plant", "SOLAR_POWER_PLANT", new(100, 40, 0), 1.5,
        [
            new(1, 20, 100, 40, 1.5), new(20, 30, 221_684, 88_674, 1.49), new(30, 40, 11_956_306, 4_782_544, 1.48),
            new(40, 50, 602_856_816, 241_143_815, 1.47), new(50, 60, 28_404_524_468, 11_361_861_078, 1.46),
            new(60, int.MaxValue, 1_250_017_464_669, 500_009_243_057, 1.45)
        ]),
        new(BuildingType.FusionReactor, BuildingCategory.Resource, "Fusion Reactor", "FUSION_REACTOR", new(900, 360, 180), 2),
        new(BuildingType.MetalStorage, BuildingCategory.Resource, "Metal Storage", "METAL_STORAGE", new(20_000, 0, 0), 1.5),
        new(BuildingType.CrystalStorage, BuildingCategory.Resource, "Crystal Storage", "CRYSTAL_STORAGE", new(20_000, 10_000, 0), 1.5),
        new(BuildingType.DeuteriumTank, BuildingCategory.Resource, "Deuterium Tank", "DEUTERIUM_TANK", new(20_000, 20_000, 0), 1.5),
        new(BuildingType.RoboticsFactory, BuildingCategory.Facility, "Robotics Factory", "ROBOT_FACTORY", new(400, 120, 200), 2),
        new(BuildingType.Shipyard, BuildingCategory.Facility, "Shipyard", "SHIPYARD", new(400, 200, 100), 2),
        new(BuildingType.ResearchLab, BuildingCategory.Facility, "Research Lab", "RESEARCH_LAB", new(200, 400, 200), 2),
        new(BuildingType.NaniteFactory, BuildingCategory.Facility, "Nanite Factory", "NANITE_FACTORY", new(1_000_000, 500_000, 100_000), 2),
        new(BuildingType.University, BuildingCategory.Facility, "University", "UNIVERSITY", new(1_200_000, 800_000, 200_000), 2),
        new(BuildingType.Terraformer, BuildingCategory.Facility, "Terraformer", "TERRAFORMER", new(0, 500_000, 200_000), 2),
        new(BuildingType.MissileSilo, BuildingCategory.Facility, "Missile Silo", "MISSILE_SILO", new(20_000, 20_000, 1_000), 2),
        // Moon only; every level costs twice the one before.
        new(BuildingType.MoonBase, BuildingCategory.Moon, "Moon Base", "MOON_BASE", new(200_000, 400_000, 100_000), 2),
        new(BuildingType.PhalanxSensor, BuildingCategory.Moon, "Phalanx Sensor", "PHALANX_SENSOR", new(200_000, 400_000, 200_000), 2),
        new(BuildingType.JumpGate, BuildingCategory.Moon, "Jump Gate", "JUMP_GATE", new(2_000_000, 4_000_000, 2_000_000), 2)
    }.ToDictionary(d => d.Type);

    private static readonly Dictionary<string, BuildingType> ByServerId =
        Definitions.Values.ToDictionary(d => d.ServerId, d => d.Type, StringComparer.Ordinal);

    /// <summary>All buildings in display order.</summary>
    public static IReadOnlyList<BuildingDefinition> All { get; } = Enum.GetValues<BuildingType>().Select(t => Definitions[t]).ToList();

    /// <summary>The buildings of planets (everything but the moon-only ones), in display order.</summary>
    public static IReadOnlyList<BuildingDefinition> PlanetBuildings { get; } = All.Where(d => d.Category != BuildingCategory.Moon).ToList();

    /// <summary>The buildings of a moon's facility page, in the page's order.</summary>
    public static IReadOnlyList<BuildingType> MoonBuildings { get; } =
    [
        BuildingType.MoonBase, BuildingType.RoboticsFactory, BuildingType.NaniteFactory, BuildingType.Shipyard,
        BuildingType.PhalanxSensor, BuildingType.JumpGate
    ];

    /// <summary>What a moon building needs before it can be built (read from the moon's facility page).</summary>
    public static IReadOnlyDictionary<BuildingType, (BuildingType Building, int Level)> MoonRequirements { get; } =
        new Dictionary<BuildingType, (BuildingType, int)>
        {
            [BuildingType.NaniteFactory] = (BuildingType.RoboticsFactory, 10),
            [BuildingType.Shipyard] = (BuildingType.RoboticsFactory, 2),
            [BuildingType.PhalanxSensor] = (BuildingType.MoonBase, 3),
            [BuildingType.JumpGate] = (BuildingType.MoonBase, 5)
        };

    /// <summary>
    /// The mines' tiers: base·1.48^(level−1) up to 20, then from each anchor ×1.46 to 30, ×1.445 to 40, ×1.43 to 50,
    /// ×1.41 to 60, then 5 levels each at 1.39, 1.37, 1.35, 1.33 and 1.31 onwards.
    /// </summary>
    private static CostTier[] MineTiers(params (double Metal, double Crystal)[] anchors)
    {
        (int From, int UpTo, double Factor)[] stretches =
            [(1, 20, 1.48), (20, 30, 1.46), (30, 40, 1.445), (40, 50, 1.43), (50, 60, 1.41), (60, 65, 1.39), (65, 70, 1.37), (70, 75, 1.35), (75, 80, 1.33), (80, int.MaxValue, 1.31)];
        return stretches.Zip(anchors, (s, a) => new CostTier(s.From, s.UpTo, a.Metal, a.Crystal, s.Factor)).ToArray();
    }

    /// <summary>Fields each Moon Base level adds ("each level of this building makes 3 new fields").</summary>
    public const int FieldsPerMoonBaseLevel = 3;

    public static BuildingDefinition Get(BuildingType type) => Definitions[type];

    public static ResourceSet CostOf(BuildingType type, int level) => Definitions[type].CostOf(level);

    /// <summary>Maps an OgameX id such as <c>METAL_MINE</c>; false for buildings we don't know.</summary>
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
