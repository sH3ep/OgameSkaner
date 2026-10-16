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
/// Formulas measured on OgameX (genesis, 2026-10-16); the account's bonuses (<see cref="AccountBonuses"/>, read from
/// the game before every simulation) and the planet (slot, temperature) are parameters. See
/// <c>docs/ogamex-build-api.md</c> ("Formulas for the optimizer"). Everything is precomputed per level, so the
/// simulator only does array lookups.
/// </summary>
public sealed class GameModel
{
    /// <summary>Changes whenever a formula changes, so results simulated with an older model aren't compared.</summary>
    public const string Version = "genesis-2026-10-16b";

    public const int MaxLevel = 60;
    public const int BuildingCount = 10;

    /// <summary>Satellites need these levels (user: Robotics 3 and Shipyard 3).</summary>
    public const int SatelliteRoboticsLevel = 3;
    public const int SatelliteShipyardLevel = 3;
    public const int ShipyardRoboticsLevel = 2;
    public const int NaniteRoboticsLevel = 10;

    /// <summary>"Each level … increases the production speed of buildings … by 80%" (Nanite Factory description).</summary>
    private const double NaniteSpeedUp = 1.8;

    /// <summary>OgameX's solar plant gives 1.2 × the standard 20·L·1.1^L (Resources page, before bonuses).</summary>
    private const double SolarBase = 24;

    /// <summary>Satellite energy before bonuses: 0.26158·(min temperature + 140) (Home 55.97 at 74 °C, Colony 54.67 at 69 °C).</summary>
    private const double SatelliteEnergyPerDegree = 0.26158;

    /// <summary>One satellite at Shipyard 0, measured with +10% ship production speed.</summary>
    private const double MeasuredSatelliteSeconds = 22.6;

    // Storage capacity by level as the game showed it with +10% storage capacity; later levels continue the growth.
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
    private readonly double _satelliteSeconds;

    public GameModel(AccountBonuses bonuses, PlanetConditions planet)
    {
        Bonuses = bonuses;
        Planet = planet;
        var calibration = AccountBonuses.Calibration;

        // Durations were measured with the calibration account's construction speed; speeds divide the time.
        var construction = (1 + bonuses.ConstructionSpeed) / (1 + calibration.ConstructionSpeed);
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
                _baseSeconds[b][level] = (m + c) * h * Math.Pow(k, level) / construction;
            }
        }

        // Per second: (basic + mine · energy factor · (1 + Σ increases)) · (1 + class bonus). The mine's base is
        // economy speed · formula · (1 + planet slot bonus); deuterium also depends on the average temperature.
        var more = 1 + bonuses.ClassBonus;
        var perSecond = bonuses.EconomySpeed / 3600;
        BaseMetalIncome = 30 * perSecond * more;
        BaseCrystalIncome = 15 * perSecond * more;
        var metal = perSecond * (1 + planet.MetalBonus) * (1 + bonuses.Metal.Total) * more;
        var crystal = perSecond * (1 + planet.CrystalBonus) * (1 + bonuses.Crystal.Total) * more;
        var deuterium = perSecond * (1 + planet.DeuteriumBonus) * (1 + bonuses.Deuterium.Total) * more
                        * Math.Max(0, 1.202 - 0.004 * planet.AverageTemperature);
        var energy = 1 + bonuses.Energy.Total;
        for (var level = 0; level <= MaxLevel + 1; level++)
        {
            var growth = level * Math.Pow(1.1, level);
            _roboticsDivisor[level] = (1 + level) * Math.Pow(1.05, level);
            _naniteDivisor[level] = Math.Pow(NaniteSpeedUp, level);
            MetalPerSecond[level] = 30 * growth * metal;
            CrystalPerSecond[level] = 20 * growth * crystal;
            DeuteriumPerSecond[level] = 10 * growth * deuterium;
            MetalConsumption[level] = 10 * growth;
            CrystalConsumption[level] = 10 * growth;
            DeuteriumConsumption[level] = 20 * growth;
            SolarEnergy[level] = SolarBase * growth * energy;
            Capacity[level] = CapacityOf(level) / (1 + calibration.StorageIncrease) * (1 + bonuses.StorageIncrease);
        }

        SatelliteEnergy = Math.Max(0, SatelliteEnergyPerDegree * (planet.MinTemperature + 140) * energy);
        _satelliteSeconds = MeasuredSatelliteSeconds * (1 + calibration.ShipyardSpeed) / (1 + bonuses.ShipyardSpeed);
    }

    public AccountBonuses Bonuses { get; }

    public PlanetConditions Planet { get; }

    /// <summary>Basic income per second (no mine or energy needed); only the class bonus applies to it.</summary>
    public double BaseMetalIncome { get; }

    public double BaseCrystalIncome { get; }

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

    /// <summary>Time to build one satellite: 11.3% less per Shipyard level; Nanite doesn't help (Home: 6 s at Shipyard 11, Nanite 1).</summary>
    public double SatelliteSeconds(int shipyard) => Math.Max(1, Math.Floor(_satelliteSeconds * Math.Pow(0.887, shipyard)));

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
    /// Seconds per resource unit (metal + crystal) at Robotics 0 / Nanite 0 and level 0, and the growth per level,
    /// with the calibration account's +80% construction speed. Fitted to 120 durations read from the game (all within
    /// 0.1%); time = (m + c)·h·k^level / ((1+R)·1.05^R·1.8^N) · 1.8 / (1 + construction speed).
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
