using System.Globalization;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>
/// What to optimize: reach these mine levels on a fresh planet as fast as possible. The solar plant, storages,
/// Robotics/Nanite Factory, Shipyard and solar satellites are placed by the search within the limits below.
/// </summary>
public sealed record OptimizerSetup
{
    public int MetalMine { get; init; } = 20;
    public int CrystalMine { get; init; } = 17;
    public int DeuteriumSynthesizer { get; init; } = 12;

    /// <summary>Planet's maximum temperature: deuterium production and satellite energy depend on it.</summary>
    public int MaxTemperature { get; init; } = 84;

    public ResourceSet StartResources { get; init; } = new(500, 500, 0);

    public int SolarMaxLevel { get; init; } = 40;
    public int RoboticsMaxLevel { get; init; } = 10;

    /// <summary>0 = no Nanite Factory (it needs Robotics 10 and Computer Technology 10).</summary>
    public int NaniteMaxLevel { get; init; }

    /// <summary>Solar satellites (and the Shipyard they need) may be used for energy.</summary>
    public bool UseSatellites { get; init; } = true;

    public int ShipyardMaxLevel { get; init; } = 8;

    /// <summary>Identifies the setup (and the model version) so the best order can be kept per setup.</summary>
    public string Key => FormattableString.Invariant(
        $"M{MetalMine} C{CrystalMine} D{DeuteriumSynthesizer} | T{MaxTemperature} | start {StartResources.Metal}/{StartResources.Crystal}/{StartResources.Deuterium} | solar≤{SolarMaxLevel} robotics≤{RoboticsMaxLevel} nanite≤{NaniteMaxLevel} ") +
        (UseSatellites ? $"satellites shipyard≤{ShipyardMaxLevel}" : "no satellites") + " | " + GameModel.Version;

    public string ShortName => $"M{MetalMine} C{CrystalMine} D{DeuteriumSynthesizer}";

    public int TargetOf(Gene gene) => gene switch
    {
        Gene.MetalMine => MetalMine,
        Gene.CrystalMine => CrystalMine,
        Gene.DeuteriumSynthesizer => DeuteriumSynthesizer,
        _ => 0
    };

    /// <summary>Highest level the search may build of a helper building; mines go exactly to their target.</summary>
    public int MaxLevelOf(Gene gene) => Math.Min(GameModel.MaxLevel, gene switch
    {
        Gene.MetalMine or Gene.CrystalMine or Gene.DeuteriumSynthesizer => TargetOf(gene),
        Gene.SolarPlant => SolarMaxLevel,
        Gene.RoboticsFactory => RoboticsMaxLevel,
        Gene.NaniteFactory => NaniteMaxLevel,
        Gene.Shipyard => UseSatellites ? ShipyardMaxLevel : 0,
        _ => GameModel.MaxLevel
    });
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

    public int Runs { get; set; }

    public long Evaluations { get; set; }

    public DateTimeOffset FoundAt { get; set; }

    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);

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
