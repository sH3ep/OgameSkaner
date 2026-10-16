using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>What one gene of a build order does: the next level of a building, or a batch of solar satellites.</summary>
public enum Gene : byte
{
    MetalMine,
    CrystalMine,
    DeuteriumSynthesizer,
    SolarPlant,
    MetalStorage,
    CrystalStorage,
    DeuteriumTank,
    RoboticsFactory,
    NaniteFactory,
    Shipyard,

    /// <summary>Order enough solar satellites to power the next mine level (built in the shipyard, in parallel).</summary>
    SolarSatellites
}

/// <summary>
/// The game's rules as the simulator sees them: costs, construction times, production, energy and storage capacity.
/// Calibrated on OgameX (genesis, 2026-10-16) with the test account's bonuses included; see
/// <c>docs/ogamex-build-api.md</c> ("Formulas for the optimizer"). Everything is precomputed per level, so the
/// simulator only does array lookups.
/// </summary>
public sealed class GameModel
{
    /// <summary>Changes whenever a formula changes, so results simulated with an older model aren't compared.</summary>
    public const string Version = "genesis-2026-10-16";

    public const int MaxLevel = 60;
    public const int BuildingCount = 10;

    /// <summary>Satellites need these levels (user: Robotics 3 and Shipyard 3).</summary>
    public const int SatelliteRoboticsLevel = 3;
    public const int SatelliteShipyardLevel = 3;
    public const int ShipyardRoboticsLevel = 2;
    public const int NaniteRoboticsLevel = 10;

    // Production per second of one "formula unit" (30·L·1.1^L metal, 20·L·1.1^L crystal, 10·L·1.1^L deuterium),
    // with the account's bonuses. Base income doesn't depend on mines or energy (metal 25/s, crystal 12.5/s).
    private const double MineUnitPerSecond = 6.5645833;
    private const double MetalBaseIncome = 25;
    private const double CrystalBaseIncome = 12.5;
    private const double DeuteriumEfficiency = 0.721488;
    private const double EnergyBonus = 1.464;
    private const double NaniteSpeedUp = 1.8;

    // Storage capacity by level (game's table, bonus included); later levels continue the growth of the differences.
    private static readonly double[] MeasuredCapacity =
    [
        11_000_000, 17_600_000, 24_062_500, 31_212_500, 39_496_875, 49_637_500, 62_794_531,
        80_824_218, 106_684_960, 145_075_390, 203_435_009, 293_510_400
    ];

    private static readonly BuildingType[] Types =
    [
        BuildingType.MetalMine, BuildingType.CrystalMine, BuildingType.DeuteriumSynthesizer, BuildingType.SolarPlant,
        BuildingType.MetalStorage, BuildingType.CrystalStorage, BuildingType.DeuteriumTank, BuildingType.RoboticsFactory,
        BuildingType.NaniteFactory, BuildingType.Shipyard
    ];

    private readonly double[][] _metal = new double[BuildingCount][];
    private readonly double[][] _crystal = new double[BuildingCount][];
    private readonly double[][] _deuterium = new double[BuildingCount][];
    private readonly double[][] _baseSeconds = new double[BuildingCount][];
    private readonly double[] _roboticsDivisor = new double[MaxLevel + 2];
    private readonly double[] _naniteDivisor = new double[MaxLevel + 2];

    public GameModel(int maxTemperature)
    {
        MaxTemperature = maxTemperature;
        for (var b = 0; b < BuildingCount; b++)
        {
            _metal[b] = new double[MaxLevel + 2];
            _crystal[b] = new double[MaxLevel + 2];
            _deuterium[b] = new double[MaxLevel + 2];
            _baseSeconds[b] = new double[MaxLevel + 2];
            for (var level = 1; level <= MaxLevel + 1; level++)
            {
                var (m, c, d) = Cost((Gene)b, level);
                _metal[b][level] = m;
                _crystal[b][level] = c;
                _deuterium[b][level] = d;
                var (h, k) = DurationConstants((Gene)b);
                _baseSeconds[b][level] = (m + c) * h * Math.Pow(k, level);
            }
        }

        for (var level = 0; level <= MaxLevel + 1; level++)
        {
            _roboticsDivisor[level] = (1 + level) * Math.Pow(1.05, level);
            _naniteDivisor[level] = Math.Pow(NaniteSpeedUp, level);
            MetalPerSecond[level] = MineUnitPerSecond * 30 * level * Math.Pow(1.1, level);
            CrystalPerSecond[level] = MineUnitPerSecond * 20 * level * Math.Pow(1.1, level);
            DeuteriumPerSecond[level] = MineUnitPerSecond * 10 * level * Math.Pow(1.1, level)
                                        * DeuteriumEfficiency * (1.44 - 0.004 * maxTemperature);
            MetalConsumption[level] = 10 * level * Math.Pow(1.1, level);
            CrystalConsumption[level] = 10 * level * Math.Pow(1.1, level);
            DeuteriumConsumption[level] = 20 * level * Math.Pow(1.1, level);
            SolarEnergy[level] = 20 * level * Math.Pow(1.1, level) * EnergyBonus;
            Capacity[level] = CapacityOf(level);
        }

        SatelliteEnergy = Math.Max(0, (maxTemperature + 140) / 6.0 * EnergyBonus);
    }

    public int MaxTemperature { get; }

    public static double BaseMetalIncome => MetalBaseIncome;

    public static double BaseCrystalIncome => CrystalBaseIncome;

    public double[] MetalPerSecond { get; } = new double[MaxLevel + 2];
    public double[] CrystalPerSecond { get; } = new double[MaxLevel + 2];
    public double[] DeuteriumPerSecond { get; } = new double[MaxLevel + 2];
    public double[] MetalConsumption { get; } = new double[MaxLevel + 2];
    public double[] CrystalConsumption { get; } = new double[MaxLevel + 2];
    public double[] DeuteriumConsumption { get; } = new double[MaxLevel + 2];
    public double[] SolarEnergy { get; } = new double[MaxLevel + 2];

    /// <summary>Capacity of each storage by its level (the same for metal, crystal and deuterium).</summary>
    public double[] Capacity { get; } = new double[MaxLevel + 2];

    public double SatelliteEnergy { get; }

    public static readonly (double Metal, double Crystal, double Deuterium) SatelliteCost = (0, 2_000, 500);

    public static BuildingType ToBuilding(Gene gene) =>
        gene == Gene.SolarSatellites ? throw new ArgumentOutOfRangeException(nameof(gene)) : Types[(int)gene];

    public static Gene ToGene(BuildingType building) => (Gene)Array.IndexOf(Types, building);

    public double MetalCost(Gene gene, int level) => _metal[(int)gene][level];
    public double CrystalCost(Gene gene, int level) => _crystal[(int)gene][level];
    public double DeuteriumCost(Gene gene, int level) => _deuterium[(int)gene][level];

    public ResourceSet CostOf(Gene gene, int level) =>
        new((long)_metal[(int)gene][level], (long)_crystal[(int)gene][level], (long)_deuterium[(int)gene][level]);

    /// <summary>Construction time in whole seconds, like the game shows it (at least one second).</summary>
    public double Seconds(Gene gene, int level, int robotics, int nanite) =>
        Math.Max(1, Math.Floor(_baseSeconds[(int)gene][level] / (_roboticsDivisor[robotics] * _naniteDivisor[nanite])));

    /// <summary>How much faster everything builds at a Robotics/Nanite level (1 at level 0).</summary>
    public double SpeedUp(int robotics, int nanite) => _roboticsDivisor[robotics] * _naniteDivisor[nanite];

    /// <summary>Time to build one satellite: 22.6 s at Shipyard 0, 11.3% less per level; Nanite doesn't help.</summary>
    public static double SatelliteSeconds(int shipyard) => Math.Max(1, Math.Floor(22.6 * Math.Pow(0.887, shipyard)));

    private static (double Metal, double Crystal, double Deuterium) Cost(Gene gene, int level)
    {
        // Mines: the base grows ×1.48 per level up to 20, ×1.46 for 21–30 and ×1.445 from 31 (measured up to 36;
        // bases are 4/3 of standard OGame). Solar plant: ×1.5 up to 20, ×1.49 from 21 (measured up to 28).
        // Storages ×1.5, factories ×2. Results are floored like the game.
        double Tiered(double upTo20, double upTo30, double above) =>
            Math.Pow(upTo20, Math.Min(level, 20) - 1)
            * Math.Pow(upTo30, Math.Clamp(level - 20, 0, 10))
            * Math.Pow(above, Math.Max(0, level - 30));

        var mine = Tiered(1.48, 1.46, 1.445);
        var (m, c, d, factor) = gene switch
        {
            Gene.MetalMine => (80.0, 20.0, 0.0, mine),
            Gene.CrystalMine => (64, 32, 0, mine),
            Gene.DeuteriumSynthesizer => (340, 100, 0, mine),
            Gene.SolarPlant => (100, 40, 0, Tiered(1.5, 1.49, 1.49)),
            Gene.MetalStorage => (20_000, 0, 0, Math.Pow(1.5, level - 1)),
            Gene.CrystalStorage => (20_000, 10_000, 0, Math.Pow(1.5, level - 1)),
            Gene.DeuteriumTank => (20_000, 20_000, 0, Math.Pow(1.5, level - 1)),
            Gene.RoboticsFactory => (400, 120, 200, Math.Pow(2, level - 1)),
            Gene.NaniteFactory => (1_000_000, 500_000, 100_000, Math.Pow(2, level - 1)),
            Gene.Shipyard => (400, 200, 100, Math.Pow(2, level - 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(gene))
        };
        return (Math.Floor(m * factor + 1e-6), Math.Floor(c * factor + 1e-6), Math.Floor(d * factor + 1e-6));
    }

    /// <summary>
    /// Seconds per resource unit (metal + crystal) at Robotics 0 / Nanite 0 and level 0, and the growth per level.
    /// Fitted to 120 durations read from the game (all within 0.1%); time = (m + c)·h·k^level / ((1+R)·1.05^R·1.8^N).
    /// </summary>
    private static (double H, double K) DurationConstants(Gene gene) => gene switch
    {
        Gene.MetalMine or Gene.CrystalMine or Gene.DeuteriumSynthesizer or Gene.SolarPlant => (0.0037086, 1.019810),
        Gene.MetalStorage or Gene.CrystalStorage or Gene.DeuteriumTank => (0.0036916, 1.05),
        Gene.RoboticsFactory => (0.0051155, 1.05),
        Gene.NaniteFactory => (0.003937, 1.05),
        Gene.Shipyard => (0.004305, 1.05),
        _ => throw new ArgumentOutOfRangeException(nameof(gene))
    };

    private static double CapacityOf(int level)
    {
        if (level < MeasuredCapacity.Length)
            return MeasuredCapacity[level];

        // The differences between levels grow ×1.48–1.54 at the end of the table; continue with ×1.55.
        var last = MeasuredCapacity.Length - 1;
        var capacity = MeasuredCapacity[last];
        var step = MeasuredCapacity[last] - MeasuredCapacity[last - 1];
        for (var l = last + 1; l <= level; l++)
        {
            step *= 1.55;
            capacity += step;
        }
        return capacity;
    }
}
