using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Building;

namespace OgameSkaner.Core.Storage;

/// <summary>The build automation page as the user left it: strategy plus the last run options.</summary>
public sealed class BuildSettings
{
    public int ReferenceLevel { get; set; } = 10;

    public int SolarMaxLevel { get; set; } = 15;

    /// <summary>Queue a storage when its resource reaches this share of capacity; 0 turns it off.</summary>
    public int StorageFillPercent { get; set; } = 70;

    /// <summary>Simulated mode: the lowest mine goes first while its next level costs less than this share of the dearest one; 0 turns it off.</summary>
    public int MineBalancePercent { get; set; } = BuildStrategy.DefaultMineBalancePercent;

    /// <summary>Simulated mode: Robotics level built before any mine passes <see cref="EarlyRoboticsMineLevel"/>; 0 turns it off.</summary>
    public int EarlyRoboticsLevel { get; set; } = BuildStrategy.DefaultEarlyRoboticsLevel;

    public int EarlyRoboticsMineLevel { get; set; } = BuildStrategy.DefaultEarlyRoboticsMineLevel;

    public List<BuildingRule> Rules { get; set; } = [];

    /// <summary>Simulated mode: orders simulated per plan of one planet.</summary>
    public int SimulationsPerPlan { get; set; } = SimulatedBuildOptions.DefaultSimulations;

    /// <summary>Simulated mode: solar satellites may provide energy.</summary>
    public bool UseSolarSatellites { get; set; } = true;

    public SimulatedBuildOptions ToSimulatedOptions() => new(Math.Max(10_000, SimulationsPerPlan), UseSolarSatellites);

    /// <summary>
    /// Planets the user unticked. Storing the excluded ones (not the selected ones) means a new colony takes part
    /// in automation without having to tick it first.
    /// </summary>
    public List<string> ExcludedPlanetIds { get; set; } = [];

    public BuildStrategy ToStrategy() => new(ReferenceLevel, Rules, SolarMaxLevel, StorageFillPercent, MineBalancePercent, EarlyRoboticsLevel, EarlyRoboticsMineLevel);

    public static BuildSettings CreateDefault() => new() { Rules = BuildingCatalog.All.Select(d => DefaultRule(d.Type)).OfType<BuildingRule>().ToList() };

    /// <summary>
    /// Ensures exactly one rule per catalog building (except the automatic ones: solar plant and storages), in
    /// catalog order, so buildings added to the catalog later show up in old settings files.
    /// </summary>
    public BuildSettings Normalize()
    {
        var existing = Rules.GroupBy(r => r.Building).ToDictionary(g => g.Key, g => g.First());
        Rules = BuildingCatalog.All
            .Where(d => !BuildStrategy.IsAutomatic(d.Type))
            .Select(d => existing.GetValueOrDefault(d.Type) ?? DefaultRule(d.Type)!)
            .ToList();
        return this;
    }

    private static BuildingRule? DefaultRule(BuildingType building) => building switch
    {
        _ when BuildStrategy.IsAutomatic(building) => null,
        BuildingType.MetalMine => new(building, true, RuleMode.Relative, 2),
        BuildingType.CrystalMine => new(building, true, RuleMode.Relative, 0),
        BuildingType.DeuteriumSynthesizer => new(building, true, RuleMode.Relative, -2),
        _ => new(building, false, RuleMode.Absolute, 0)
    };
}

public interface IBuildSettingsStore
{
    /// <summary>Returns the saved settings of a universe, or defaults when none were saved.</summary>
    BuildSettings Load(string universe);

    void Save(string universe, BuildSettings settings);
}

/// <summary>Stores the settings in <c>universes/{name}/build-settings.json</c>.</summary>
public sealed class JsonBuildSettingsStore(IAppPaths paths) : IBuildSettingsStore
{
    private readonly object _sync = new();

    public BuildSettings Load(string universe)
    {
        lock (_sync)
            return (JsonFile.Read<BuildSettings>(FilePath(universe)) ?? BuildSettings.CreateDefault()).Normalize();
    }

    public void Save(string universe, BuildSettings settings)
    {
        lock (_sync)
            JsonFile.Write(FilePath(universe), settings);
    }

    private string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "build-settings.json");
}
