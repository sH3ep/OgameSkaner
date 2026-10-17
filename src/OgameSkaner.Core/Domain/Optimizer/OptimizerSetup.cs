using System.Globalization;
using System.Text.Json.Serialization;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>
/// What to optimize: reach these mine levels on a fresh planet (or from <see cref="Start"/>) as fast as possible. The
/// solar plant, storages, Robotics/Nanite Factory, Shipyard and solar satellites are placed by the search within the
/// limits below; the factory targets must be reached too.
/// </summary>
public sealed record OptimizerSetup
{
    public int MetalMine { get; init; } = 20;
    public int CrystalMine { get; init; } = 17;
    public int DeuteriumSynthesizer { get; init; } = 12;

    /// <summary>Planet slot 1–15: production bonuses (OgameX wiki) and the temperature range.</summary>
    public int PlanetSlot { get; init; } = 8;

    /// <summary>Planet temperature: deuterium depends on the average, satellite energy on the minimum.</summary>
    public int MinTemperature { get; init; } = 28;

    public int MaxTemperature { get; init; } = 32;

    public ResourceSet StartResources { get; init; } = new(500, 500, 0);

    /// <summary>The account's bonuses read from the game (research, officers, academy, premium, ranking, class).</summary>
    public AccountBonuses Bonuses { get; init; } = AccountBonuses.Calibration;

    public PlanetConditions Planet => new(PlanetSlot, MinTemperature, MaxTemperature);

    public int SolarMaxLevel { get; init; } = 40;
    public int RoboticsMaxLevel { get; init; } = 10;

    /// <summary>0 = no Nanite Factory (it needs Robotics 10 and Computer Technology 10).</summary>
    public int NaniteMaxLevel { get; init; }

    /// <summary>Solar satellites (and the Shipyard they need) may be used for energy.</summary>
    public bool UseSatellites { get; init; } = true;

    public int ShipyardMaxLevel { get; init; } = 8;

    /// <summary>Factory levels the order must reach as well (0 = only a helper).</summary>
    public int RoboticsTarget { get; init; }

    public int NaniteTarget { get; init; }

    public int ShipyardTarget { get; init; }

    /// <summary>Storage levels the order must reach as well (0 = built only as needed, see <see cref="StorageFillPercent"/>).</summary>
    public int MetalStorageTarget { get; init; }

    public int CrystalStorageTarget { get; init; }

    public int DeuteriumTankTarget { get; init; }

    /// <summary>
    /// The order is only done when energy covers the mines (satellites ordered count, the time includes building them),
    /// unless the solar plant is at its maximum and satellites aren't allowed. Without it, a server where construction
    /// time is the limit leaves the mines without power: energy wouldn't make the targets come sooner.
    /// </summary>
    public bool CoverEnergy { get; init; }

    /// <summary>
    /// Before an item starts, a storage level goes first when the item would leave its resource at this share of the
    /// capacity or more, so production isn't lost while long constructions run (0 = only when a cost needs the room).
    /// </summary>
    public int StorageFillPercent { get; init; }

    /// <summary>The storages <see cref="StorageFillPercent"/> applies to; the others are built only when a cost needs the room.</summary>
    public StorageKinds FillStorages { get; init; } = StorageKinds.All;

    /// <summary>
    /// Keeps the mines level: while the lowest mine still below its target has a next level that costs (metal + crystal +
    /// deuterium) less than this share of the dearest next mine level, every mine step builds that mine instead
    /// (0 = off). Avoids e.g. Metal 50 with Crystal 35.
    /// </summary>
    public int MineBalancePercent { get; init; }

    /// <summary>
    /// Robotics Factory level that must be built before any mine goes above <see cref="EarlyRoboticsMineLevel"/>; the
    /// simulator inserts the missing levels (0 = off). The search alone builds Robotics late: early mines take seconds and
    /// the factory's resources only come back slowly.
    /// </summary>
    public int EarlyRoboticsLevel { get; init; }

    /// <summary>
    /// Every mine level is powered when it starts: before a level that would leave energy short, the simulator inserts
    /// Solar Plant levels (up to <see cref="SolarMaxLevel"/>), then satellites (with the Robotics and Shipyard levels they
    /// need). Without it the fastest order can leave the mines unpowered until the end when construction time, not
    /// production, limits the plan.
    /// </summary>
    public bool KeepMinesPowered { get; init; }

    /// <summary>Satellites are ordered at least this many at a time (0 = exactly what the shortage needs).</summary>
    public int MinSatelliteOrder { get; init; }

    /// <summary>The mine level from which <see cref="EarlyRoboticsLevel"/> must be there (mines may reach it, not pass it).</summary>
    public int EarlyRoboticsMineLevel { get; init; } = 10;

    /// <summary>A real planet to start from; null = a fresh planet with <see cref="StartResources"/>.</summary>
    [JsonIgnore]
    public PlanetStart? Start { get; init; }

    /// <summary>
    /// Identifies the setup, the account's bonuses and the model version, so the best order is kept per setup and
    /// orders simulated with other bonuses aren't compared.
    /// </summary>
    public string Key => FormattableString.Invariant(
        $"M{MetalMine} C{CrystalMine} D{DeuteriumSynthesizer} | slot {PlanetSlot} {MinTemperature}..{MaxTemperature}°C | start {StartResources.Metal}/{StartResources.Crystal}/{StartResources.Deuterium} | solar≤{SolarMaxLevel} robotics≤{RoboticsMaxLevel} nanite≤{NaniteMaxLevel} ") +
        (UseSatellites ? $"satellites shipyard≤{ShipyardMaxLevel}" : "no satellites") + FactoryTargetsKey + " | " + Bonuses.Signature + " | " + GameModel.Version;

    /// <summary>The same setup without the bonuses: finds earlier results for it after the bonuses changed.</summary>
    public string TargetKey => FormattableString.Invariant(
        $"M{MetalMine} C{CrystalMine} D{DeuteriumSynthesizer} | slot {PlanetSlot} {MinTemperature}..{MaxTemperature}°C | start {StartResources.Metal}/{StartResources.Crystal}/{StartResources.Deuterium} | solar≤{SolarMaxLevel} robotics≤{RoboticsMaxLevel} nanite≤{NaniteMaxLevel} ") +
        (UseSatellites ? $"satellites shipyard≤{ShipyardMaxLevel}" : "no satellites") + FactoryTargetsKey;

    private string FactoryTargetsKey => (RoboticsTarget + NaniteTarget + ShipyardTarget == 0
        ? string.Empty
        : FormattableString.Invariant($" | robotics {RoboticsTarget} nanite {NaniteTarget} shipyard {ShipyardTarget}"))
        + (MetalStorageTarget + CrystalStorageTarget + DeuteriumTankTarget == 0
            ? string.Empty
            : FormattableString.Invariant($" | storages {MetalStorageTarget}/{CrystalStorageTarget}/{DeuteriumTankTarget}"))
        + (CoverEnergy ? " | energy covered" : string.Empty)
        + (StorageFillPercent > 0 ? FormattableString.Invariant($" | storage at {StorageFillPercent}%") : string.Empty)
        + (StorageFillPercent > 0 && FillStorages != StorageKinds.All ? $" ({FillStorages})" : string.Empty)
        + (MineBalancePercent > 0 ? FormattableString.Invariant($" | mines within {MineBalancePercent}%") : string.Empty)
        + (EarlyRoboticsLevel > 0 ? FormattableString.Invariant($" | robotics {EarlyRoboticsLevel} by mines {EarlyRoboticsMineLevel}") : string.Empty)
        + (KeepMinesPowered ? " | mines powered" : string.Empty)
        + (MinSatelliteOrder > 0 ? FormattableString.Invariant($" | satellites ≥{MinSatelliteOrder}") : string.Empty);

    public string ShortName => $"M{MetalMine} C{CrystalMine} D{DeuteriumSynthesizer}";

    /// <summary>The level the order must reach, at most <see cref="GameModel.MaxLevel"/>.</summary>
    public int TargetOf(Gene gene) => Math.Min(GameModel.MaxLevel, gene switch
    {
        Gene.MetalMine => MetalMine,
        Gene.CrystalMine => CrystalMine,
        Gene.DeuteriumSynthesizer => DeuteriumSynthesizer,
        Gene.RoboticsFactory => RoboticsTarget,
        Gene.NaniteFactory => NaniteTarget,
        Gene.Shipyard => ShipyardTarget,
        Gene.MetalStorage => MetalStorageTarget,
        Gene.CrystalStorage => CrystalStorageTarget,
        Gene.DeuteriumTank => DeuteriumTankTarget,
        _ => 0
    });

    /// <summary>Highest level the search may build of a helper building; mines go exactly to their target.</summary>
    public int MaxLevelOf(Gene gene) => Math.Min(GameModel.MaxLevel, gene switch
    {
        Gene.MetalMine or Gene.CrystalMine or Gene.DeuteriumSynthesizer => TargetOf(gene),
        Gene.SolarPlant => SolarMaxLevel,
        Gene.RoboticsFactory => Math.Max(Math.Max(RoboticsMaxLevel, RoboticsTarget), EarlyRoboticsLevel),
        Gene.NaniteFactory => Math.Max(NaniteMaxLevel, NaniteTarget),
        Gene.Shipyard => Math.Max(UseSatellites ? ShipyardMaxLevel : 0, ShipyardTarget),
        _ => GameModel.MaxLevel
    });
}

/// <summary>A set of the three storages.</summary>
[Flags]
public enum StorageKinds
{
    None = 0,
    Metal = 1,
    Crystal = 2,
    Deuterium = 4,
    All = Metal | Crystal | Deuterium
}

/// <summary>One item of a simulated order: a building level, or satellites ordered in the shipyard.</summary>
/// <param name="Level">The level built, or the number of satellites ordered.</param>
/// <param name="Forced">Added by the simulator (a storage needed because a cost exceeded the capacity).</param>
public sealed record OptimizedStep(Gene Gene, int Level, double StartSeconds, double FinishSeconds, bool Forced = false)
{
    public bool IsSatellites => Gene == Gene.SolarSatellites;

    public override string ToString() => IsSatellites
        ? $"{Level} solar satellites"
        : $"{BuildingCatalog.Get(GameModel.ToBuilding(Gene)).DisplayName} {Level}";
}

/// <summary>The best order found for a setup.</summary>
public sealed class OptimizedOrder
{
    public OptimizerSetup Setup { get; set; } = new();

    public string SetupKey { get; set; } = string.Empty;

    /// <summary>Game time from the start until the last target level is finished.</summary>
    public double TotalSeconds { get; set; }

    public List<OptimizedStep> Steps { get; set; } = [];

    /// <summary>The order as the search found it (to continue from); not saved.</summary>
    [JsonIgnore]
    public Gene[] Genes { get; set; } = [];

    public int Runs { get; set; }

    public long Evaluations { get; set; }

    public DateTimeOffset FoundAt { get; set; }

    [JsonIgnore]
    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);

    [JsonIgnore]
    public string TotalText => FormatDuration(TotalSeconds);

    /// <summary>
    /// The order as a schema: every building level as a step (satellites are left to the schema's satellite rule).
    /// The factories are already in the order, so the schema's Robotics/Nanite rules are off; the solar and storage
    /// rules stay on as a safety net for a planet that differs from the model.
    /// </summary>
    public BuildSchema ToSchema(string name)
    {
        var schema = new BuildSchema
        {
            Name = name,
            Rules = new SchemaRules
            {
                SolarMaxLevel = Math.Max(Setup.SolarMaxLevel, Steps.Where(s => s.Gene == Gene.SolarPlant).Select(s => s.Level).DefaultIfEmpty().Max()),
                UseSolarSatellites = Setup.UseSatellites,
                RoboticsMaxLevel = 0,
                NaniteMaxLevel = 0
            }
        };
        schema.Steps.AddRange(Steps.Where(s => !s.IsSatellites).Select(s => new SchemaStep(GameModel.ToBuilding(s.Gene), s.Level)));
        return schema;
    }

    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds / 2)
            return "never";
        var time = TimeSpan.FromSeconds(Math.Round(seconds));
        return time.TotalDays >= 1
            ? $"{(int)time.TotalDays}d {time:hh\\:mm\\:ss}"
            : time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
