using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Services.Optimizer;

/// <summary>
/// Simulates a build order on a fresh planet, event by event. One building is under construction at a time; the next
/// one starts when the previous one is finished <b>and</b> the planet has its resources (they are taken at the start,
/// like the game). Satellites are built in the shipyard in parallel and add their energy one by one. Mines produce
/// at <c>min(1, energy produced / energy used)</c>; resources stop at the storage capacity.
/// </summary>
/// <remarks>
/// Not thread-safe: the optimizer gives every worker its own simulator. Genes the setup doesn't allow (above a max
/// level, requirements missing, a satellite order with no energy shortage) are skipped; a storage is inserted when a
/// cost exceeds the capacity.
/// </remarks>
public sealed class BuildSimulator
{
    private const int MetalStorageIndex = (int)Gene.MetalStorage;
    private const int CrystalStorageIndex = (int)Gene.CrystalStorage;
    private const int DeuteriumTankIndex = (int)Gene.DeuteriumTank;

    private readonly int[] _levels = new int[GameModel.BuildingCount];
    private readonly int[] _maxLevels = new int[GameModel.BuildingCount];
    private readonly int[] _targets = new int[3];
    private double _metal, _crystal, _deuterium;
    private int _satellites;
    private int _satellitesPending;
    private double _nextSatelliteAt;
    private double _satelliteSeconds;
    private List<OptimizedStep>? _timeline;

    public BuildSimulator(GameModel model, OptimizerSetup setup)
    {
        Model = model;
        Setup = setup;
        for (var b = 0; b < GameModel.BuildingCount; b++)
            _maxLevels[b] = setup.MaxLevelOf((Gene)b);
        for (var m = 0; m < 3; m++)
            _targets[m] = Math.Min(GameModel.MaxLevel, setup.TargetOf((Gene)m));
        Reset();
    }

    public GameModel Model { get; }

    public OptimizerSetup Setup { get; }

    /// <summary>Game time in seconds since the start.</summary>
    public double Time { get; private set; }

    public int Satellites => _satellites + _satellitesPending;

    public int LevelOf(Gene gene) => _levels[(int)gene];

    public bool TargetsReached =>
        _levels[0] >= _targets[0] && _levels[1] >= _targets[1] && _levels[2] >= _targets[2];

    public void Reset()
    {
        Array.Clear(_levels);
        _metal = Setup.StartResources.Metal;
        _crystal = Setup.StartResources.Crystal;
        _deuterium = Setup.StartResources.Deuterium;
        _satellites = 0;
        _satellitesPending = 0;
        _nextSatelliteAt = 0;
        _satelliteSeconds = 0;
        _timeline = null;
        Time = 0;
    }

    /// <summary>Seconds until all targets are built with this order; +∞ when the order can't reach them.</summary>
    public double Evaluate(ReadOnlySpan<Gene> genes)
    {
        Reset();
        return Run(genes);
    }

    /// <summary>Simulates the order and records what was built when.</summary>
    public (double Seconds, List<OptimizedStep> Steps) Trace(ReadOnlySpan<Gene> genes)
    {
        Reset();
        var steps = new List<OptimizedStep>();
        _timeline = steps;
        var seconds = Run(genes);
        _timeline = null;
        return (seconds, steps);
    }

    private double Run(ReadOnlySpan<Gene> genes)
    {
        if (TargetsReached)
            return 0;

        for (var i = 0; i < genes.Length; i++)
        {
            if (!Apply(genes[i], genes[(i + 1)..]))
                return double.PositiveInfinity;
            if (TargetsReached)
                return Time;
        }
        return double.PositiveInfinity;
    }

    /// <summary>Applies one gene (skipping it when not allowed); false when the order gets stuck.</summary>
    /// <param name="ahead">The rest of the order; a satellite gene looks for the next mine in it.</param>
    public bool Apply(Gene gene, ReadOnlySpan<Gene> ahead)
    {
        if (gene == Gene.SolarSatellites)
            return OrderSatellites(NextMine(ahead));
        return !CanBuild(gene) || Build(gene, forced: false);
    }

    /// <summary>Whether the next level of the building is allowed (max level, requirements).</summary>
    public bool CanBuild(Gene gene)
    {
        var next = _levels[(int)gene] + 1;
        if (next > _maxLevels[(int)gene])
            return false;
        return gene switch
        {
            Gene.NaniteFactory => _levels[(int)Gene.RoboticsFactory] >= GameModel.NaniteRoboticsLevel,
            Gene.Shipyard => _levels[(int)Gene.RoboticsFactory] >= GameModel.ShipyardRoboticsLevel,
            _ => true
        };
    }

    public bool CanOrderSatellites =>
        Setup.UseSatellites
        && _levels[(int)Gene.Shipyard] >= GameModel.SatelliteShipyardLevel
        && _levels[(int)Gene.RoboticsFactory] >= GameModel.SatelliteRoboticsLevel;

    /// <summary>Energy produced (solar plant + satellites built so far) minus energy used by the mines.</summary>
    public double EnergyBalance => EnergyProduction(_satellites) - EnergyUse(_levels[0], _levels[1], _levels[2]);

    /// <summary>Energy balance once the next level of <paramref name="mine"/> is built and every ordered satellite flies.</summary>
    public double EnergyBalanceAfter(Gene mine)
    {
        var m = _levels[0] + (mine == Gene.MetalMine ? 1 : 0);
        var c = _levels[1] + (mine == Gene.CrystalMine ? 1 : 0);
        var d = _levels[2] + (mine == Gene.DeuteriumSynthesizer ? 1 : 0);
        return EnergyProduction(Satellites) - EnergyUse(m, c, d);
    }

    /// <summary>Seconds from now until the next level could be finished (waiting for resources + construction).</summary>
    public double SecondsToFinish(Gene gene)
    {
        var level = _levels[(int)gene] + 1;
        var wait = SecondsToAfford(Model.MetalCost(gene, level), Model.CrystalCost(gene, level), Model.DeuteriumCost(gene, level));
        return wait + Model.Seconds(gene, level, _levels[(int)Gene.RoboticsFactory], _levels[(int)Gene.NaniteFactory]);
    }

    private bool Build(Gene gene, bool forced)
    {
        var index = (int)gene;
        var level = _levels[index] + 1;
        var metal = Model.MetalCost(gene, level);
        var crystal = Model.CrystalCost(gene, level);
        var deuterium = Model.DeuteriumCost(gene, level);

        // A cost above the capacity can never be paid: raise that storage first.
        if (!EnsureCapacity(MetalStorageIndex, metal) || !EnsureCapacity(CrystalStorageIndex, crystal) || !EnsureCapacity(DeuteriumTankIndex, deuterium))
            return false;
        if (!WaitFor(metal, crystal, deuterium))
            return false;

        _metal -= metal;
        _crystal -= crystal;
        _deuterium -= deuterium;
        var start = Time;
        Advance(Model.Seconds(gene, level, _levels[(int)Gene.RoboticsFactory], _levels[(int)Gene.NaniteFactory]));
        _levels[index] = level;
        _timeline?.Add(new OptimizedStep(gene, level, start, Time, forced));
        return true;
    }

    private bool EnsureCapacity(int storage, double amount)
    {
        while (Model.Capacity[_levels[storage]] < amount)
        {
            if (_levels[storage] >= GameModel.MaxLevel || !Build((Gene)storage, forced: true))
                return false;
        }
        return true;
    }

    /// <summary>Orders enough satellites to power <paramref name="nextMine"/>'s next level; nothing when there is no shortage.</summary>
    private bool OrderSatellites(Gene? nextMine)
    {
        if (!CanOrderSatellites || nextMine is not { } mine || Model.SatelliteEnergy <= 0)
            return true;

        var shortage = -EnergyBalanceAfter(mine);
        if (shortage <= 0)
            return true;

        var count = (int)Math.Ceiling(shortage / Model.SatelliteEnergy);
        var (_, crystalEach, deuteriumEach) = GameModel.SatelliteCost;
        var crystal = count * crystalEach;
        var deuterium = count * deuteriumEach;
        if (!EnsureCapacity(CrystalStorageIndex, crystal) || !EnsureCapacity(DeuteriumTankIndex, deuterium))
            return false;
        if (!WaitFor(0, crystal, deuterium))
            return false;

        _crystal -= crystal;
        _deuterium -= deuterium;
        var seconds = Model.SatelliteSeconds(_levels[(int)Gene.Shipyard]);
        if (_satellitesPending == 0)
        {
            _satelliteSeconds = seconds;
            _nextSatelliteAt = Time + seconds;
        }
        _satellitesPending += count;
        var finish = _nextSatelliteAt + (_satellitesPending - 1) * _satelliteSeconds;
        _timeline?.Add(new OptimizedStep(Gene.SolarSatellites, count, Time, finish));
        return true;
    }

    private Gene? NextMine(ReadOnlySpan<Gene> ahead)
    {
        foreach (var gene in ahead)
        {
            if (gene <= Gene.DeuteriumSynthesizer && _levels[(int)gene] < _targets[(int)gene])
                return gene;
        }
        return null;
    }

    /// <summary>Lets time pass until the resources are there; false when they never come.</summary>
    private bool WaitFor(double metal, double crystal, double deuterium)
    {
        while (true)
        {
            Rates(out var rm, out var rc, out var rd);
            var wait = Math.Max(Need(metal - _metal, rm), Math.Max(Need(crystal - _crystal, rc), Need(deuterium - _deuterium, rd)));
            if (wait <= 0)
                return true;

            if (_satellitesPending > 0 && Time + wait > _nextSatelliteAt)
            {
                // Rates change when the next satellite is done.
                Advance(_nextSatelliteAt - Time);
                continue;
            }
            if (double.IsPositiveInfinity(wait))
                return false;

            Advance(wait);
            return true;
        }
    }

    private double SecondsToAfford(double metal, double crystal, double deuterium)
    {
        Rates(out var rm, out var rc, out var rd);
        return Math.Max(0, Math.Max(Need(metal - _metal, rm), Math.Max(Need(crystal - _crystal, rc), Need(deuterium - _deuterium, rd))));
    }

    private static double Need(double missing, double rate) =>
        missing <= 0 ? 0 : rate > 0 ? missing / rate : double.PositiveInfinity;

    /// <summary>Production while <paramref name="seconds"/> pass, split where a satellite is finished.</summary>
    private void Advance(double seconds)
    {
        while (seconds > 0)
        {
            var step = seconds;
            var satelliteDone = false;
            if (_satellitesPending > 0 && Time + step >= _nextSatelliteAt)
            {
                step = Math.Max(0, _nextSatelliteAt - Time);
                satelliteDone = true;
            }

            Produce(step);
            Time += step;
            seconds -= step;
            if (satelliteDone)
            {
                _satellites++;
                _satellitesPending--;
                _nextSatelliteAt += _satelliteSeconds;
            }
        }

        // Satellites finished exactly at the end of the interval.
        while (_satellitesPending > 0 && _nextSatelliteAt <= Time)
        {
            _satellites++;
            _satellitesPending--;
            _nextSatelliteAt += _satelliteSeconds;
        }
    }

    private void Produce(double seconds)
    {
        if (seconds <= 0)
            return;

        Rates(out var rm, out var rc, out var rd);
        _metal = Fill(_metal, rm * seconds, Model.Capacity[_levels[MetalStorageIndex]]);
        _crystal = Fill(_crystal, rc * seconds, Model.Capacity[_levels[CrystalStorageIndex]]);
        _deuterium = Fill(_deuterium, rd * seconds, Model.Capacity[_levels[DeuteriumTankIndex]]);
    }

    /// <summary>Production stops at the capacity; resources already above it (start resources) are kept.</summary>
    private static double Fill(double amount, double produced, double capacity) =>
        amount >= capacity ? amount : Math.Min(capacity, amount + produced);

    private void Rates(out double metal, out double crystal, out double deuterium)
    {
        var use = EnergyUse(_levels[0], _levels[1], _levels[2]);
        var factor = use <= 0 ? 1 : Math.Min(1, EnergyProduction(_satellites) / use);
        metal = Model.BaseMetalIncome + Model.MetalPerSecond[_levels[0]] * factor;
        crystal = Model.BaseCrystalIncome + Model.CrystalPerSecond[_levels[1]] * factor;
        deuterium = Model.DeuteriumPerSecond[_levels[2]] * factor;
    }

    private double EnergyProduction(int satellites) =>
        Model.SolarEnergy[_levels[(int)Gene.SolarPlant]] + satellites * Model.SatelliteEnergy;

    private double EnergyUse(int metal, int crystal, int deuterium) =>
        Model.MetalConsumption[metal] + Model.CrystalConsumption[crystal] + Model.DeuteriumConsumption[deuterium];
}
