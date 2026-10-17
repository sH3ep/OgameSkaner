using System.Runtime.CompilerServices;
using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Services.Optimizer;

/// <summary>
/// Simulates a build order on a fresh planet (or a real one, <see cref="OptimizerSetup.Start"/>), event by event. One building is under construction at a time; the next
/// one starts when the previous one is finished <b>and</b> the planet has its resources (they are taken at the start,
/// like the game). Satellites are built in the shipyard in parallel and add their energy one by one. Mines produce
/// at <c>min(1, energy produced / energy used)</c>; resources stop at the storage capacity.
/// </summary>
/// <remarks>
/// A real planet starts with its levels, resources and satellites; its build queue is finished first (in order, the
/// waiting items paid when they start), so the order's genes come after it and the time counts from now.
/// <para>
/// Not thread-safe: the optimizer gives every worker its own simulator. Genes the setup doesn't allow (above a max
/// level, requirements missing, a satellite order with no energy shortage) are skipped; a storage is inserted when a
/// cost exceeds the capacity. With <see cref="OptimizerSetup.MineBalancePercent"/> a mine gene may build another mine
/// (<see cref="MineFor"/>).
/// </para>
/// <para>
/// The whole state is one value type (<see cref="SimState"/>), so it can be saved every
/// <see cref="EvaluationTrace.Interval"/> genes and restored: <see cref="Evaluate(ReadOnlySpan{Gene}, int, EvaluationTrace, EvaluationTrace)"/>
/// re-simulates only from the first gene a mutation changed, with exactly the same result as a full run.
/// </para>
/// </remarks>
public sealed class BuildSimulator
{
    private const int MetalStorageIndex = (int)Gene.MetalStorage;
    private const int CrystalStorageIndex = (int)Gene.CrystalStorage;
    private const int DeuteriumTankIndex = (int)Gene.DeuteriumTank;

    private readonly int[] _maxLevels = new int[GameModel.BuildingCount];
    private readonly int[] _targets = new int[GameModel.BuildingCount];
    private readonly int[] _targetGenes;
    private readonly double _metalCorrection = 1, _crystalCorrection = 1, _deuteriumCorrection = 1, _extraEnergy;
    private readonly double _storageFill;
    private readonly double _mineBalance;
    private readonly int _earlyRobotics, _earlyRoboticsMineLevel;
    private bool _finishingQueue;
    private SimState _start;

    // Satellites ordered at the start for the real queue's energy (CoverEnergy); the first step of every trace.
    private readonly List<OptimizedStep> _startSteps = [];
    private SimState _s;
    private List<OptimizedStep>? _timeline;

    // Production per second and whether energy covers the mines; recomputed only after a level or satellite change.
    private bool _ratesValid;
    private double _metalRate, _crystalRate, _deuteriumRate;
    private bool _energyCovered;

    public BuildSimulator(GameModel model, OptimizerSetup setup)
    {
        Model = model;
        Setup = setup;
        for (var b = 0; b < GameModel.BuildingCount; b++)
            _maxLevels[b] = setup.MaxLevelOf((Gene)b);
        for (var b = 0; b < GameModel.BuildingCount; b++)
            _targets[b] = Math.Min(GameModel.MaxLevel, setup.TargetOf((Gene)b));
        _targetGenes = Enumerable.Range(0, GameModel.BuildingCount).Where(b => _targets[b] > 0).ToArray();
        _storageFill = Math.Clamp(setup.StorageFillPercent, 0, 100) / 100.0;
        _mineBalance = Math.Clamp(setup.MineBalancePercent, 0, 100) / 100.0;
        _earlyRobotics = Math.Clamp(setup.EarlyRoboticsLevel, 0, GameModel.MaxLevel);
        _earlyRoboticsMineLevel = setup.EarlyRoboticsMineLevel;

        _start.Metal = setup.StartResources.Metal;
        _start.Crystal = setup.StartResources.Crystal;
        _start.Deuterium = setup.StartResources.Deuterium;
        if (setup.Start is { } planet)
        {
            _metalCorrection = planet.MetalCorrection;
            _crystalCorrection = planet.CrystalCorrection;
            _deuteriumCorrection = planet.DeuteriumCorrection;
            _extraEnergy = planet.ExtraEnergy;
            _start = StartFrom(planet);
        }
        Reset();
    }

    public GameModel Model { get; }

    public OptimizerSetup Setup { get; }

    /// <summary>Game time in seconds since the start.</summary>
    public double Time => _s.Time;

    public int Satellites => _s.Satellites + _s.SatellitesPending;

    public int LevelOf(Gene gene) => _s.Levels[(int)gene];

    public bool TargetsReached
    {
        get
        {
            foreach (var gene in _targetGenes)
            {
                if (_s.Levels[gene] < _targets[gene])
                    return false;
            }
            return EnergyDone();
        }
    }

    /// <summary><see cref="OptimizerSetup.CoverEnergy"/>: energy (with the ordered satellites) covers the mines, or can't.</summary>
    private bool EnergyDone() =>
        !Setup.CoverEnergy
        || EnergyProduction(_s.Satellites + _s.SatellitesPending) >= EnergyUse(_s.Levels[0], _s.Levels[1], _s.Levels[2])
        || (!Setup.UseSatellites && !CanBuild(Gene.SolarPlant));

    /// <summary>When the order is done: now, or with <see cref="OptimizerSetup.CoverEnergy"/> when the last ordered satellite is.</summary>
    private double FinishTime =>
        Setup.CoverEnergy && _s.SatellitesPending > 0
            ? Math.Max(_s.Time, _s.NextSatelliteAt + (_s.SatellitesPending - 1) * _s.SatelliteSeconds)
            : _s.Time;

    /// <summary>Back to the start: a fresh planet, or the real planet with its queue finished.</summary>
    public void Reset()
    {
        _s = _start;
        _timeline = null;
        _ratesValid = false;
    }

    /// <summary>The real planet's state once its build queue is finished (time 0 = now).</summary>
    private SimState StartFrom(PlanetStart planet)
    {
        _finishingQueue = true; // the real queue gets no storages the game doesn't have
        _s = default;
        foreach (var (gene, level) in planet.Levels)
        {
            if (gene < Gene.SolarSatellites)
                _s.Levels[(int)gene] = Math.Min(level, GameModel.MaxLevel);
        }
        _s.Metal = planet.Resources.Metal;
        _s.Crystal = planet.Resources.Crystal;
        _s.Deuterium = planet.Resources.Deuterium;
        _s.Satellites = planet.Satellites;
        if (planet.SatellitesPending > 0)
        {
            _s.SatellitesPending = planet.SatellitesPending;
            _s.SatelliteSeconds = Model.SatelliteSeconds(_s.Levels[(int)Gene.Shipyard]);
            _s.NextSatelliteAt = _s.SatelliteSeconds;
        }
        _ratesValid = false;
        CoverQueueEnergy(planet);

        for (var i = 0; i < planet.Queue.Count; i++)
        {
            var item = planet.Queue[i];
            var known = item.Gene is { } g && g < Gene.SolarSatellites && item.Level <= GameModel.MaxLevel;
            if (i == 0 && item.Seconds is { } remaining)
            {
                // The running item: paid already, done when its time is up.
                Advance(remaining);
            }
            else if (known && item.Seconds is null && item.Level == _s.Levels[(int)item.Gene!.Value] + 1)
            {
                // Built like a gene: resources taken when it starts, time with the factories it finds.
                Build(item.Gene!.Value, forced: false);
                continue;
            }
            else
            {
                if (!WaitFor(item.Cost.Metal, item.Cost.Crystal, item.Cost.Deuterium))
                    break; // never affordable: the game drops it
                _s.Metal -= item.Cost.Metal;
                _s.Crystal -= item.Cost.Crystal;
                _s.Deuterium -= item.Cost.Deuterium;
                Advance(item.Seconds ?? 0);
            }

            if (known)
            {
                _s.Levels[(int)item.Gene!.Value] = Math.Max(_s.Levels[(int)item.Gene!.Value], item.Level);
                _ratesValid = false;
            }
        }
        _finishingQueue = false;
        return _s;
    }

    /// <summary>Seconds until all targets are built with this order; +∞ when the order can't reach them.</summary>
    public double Evaluate(ReadOnlySpan<Gene> genes)
    {
        Reset();
        return Run(genes, 0, null);
    }

    /// <summary>
    /// Like <see cref="Evaluate(ReadOnlySpan{Gene})"/>, but resumes from <paramref name="baseline"/> (the trace of an order
    /// that equals <paramref name="genes"/> before <paramref name="firstChanged"/>) and records the trace of this order
    /// into <paramref name="record"/>.
    /// </summary>
    public double Evaluate(ReadOnlySpan<Gene> genes, int firstChanged, EvaluationTrace baseline, EvaluationTrace record)
    {
        record.Prepare(genes.Length);
        var start = baseline.ResumePoint(genes, firstChanged, out var checkpoint);
        if (start <= 0)
            Reset();
        else
        {
            _s = baseline.Checkpoints[checkpoint];
            _timeline = null;
            _ratesValid = false;
            record.CopyPrefix(baseline, checkpoint, start);
        }
        return Run(genes, start, record);
    }

    /// <summary>Simulates the order and records what was built when.</summary>
    public (double Seconds, List<OptimizedStep> Steps) Trace(ReadOnlySpan<Gene> genes)
    {
        Reset();
        var steps = new List<OptimizedStep>(_startSteps);
        _timeline = steps;
        var seconds = Run(genes, 0, null);
        _timeline = null;
        return (seconds, steps);
    }

    private double Run(ReadOnlySpan<Gene> genes, int start, EvaluationTrace? record)
    {
        if (TargetsReached)
            return Done(FinishTime); // 0, or the time the real planet's queue still needs

        for (var i = start; i < genes.Length; i++)
        {
            record?.Save(i, ref _s);
            var reach = i;
            var gene = genes[i];
            bool ok;
            if (gene == Gene.SolarSatellites)
            {
                var next = NextMine(genes, i + 1);
                reach = next < 0 ? genes.Length : next;
                ok = OrderSatellites(next < 0 ? null : MineFor(genes[next]));
            }
            else if (CanBuild(gene = MineFor(gene)))
                ok = Build(gene, forced: false);
            else
            {
                reach = -1; // skipped
                ok = true;
            }
            record?.SetReach(i, reach);

            if (!ok)
                return Done(double.PositiveInfinity);
            if (TargetsReached)
                return Done(FinishTime);
        }
        return Done(double.PositiveInfinity);

        double Done(double seconds)
        {
            record?.Finish(seconds);
            return seconds;
        }
    }

    /// <summary>Applies one gene (skipping it when not allowed); false when the order gets stuck.</summary>
    /// <param name="ahead">The rest of the order; a satellite gene looks for the next mine in it.</param>
    public bool Apply(Gene gene, ReadOnlySpan<Gene> ahead)
    {
        if (gene == Gene.SolarSatellites)
        {
            var next = NextMine(ahead, 0);
            return OrderSatellites(next < 0 ? null : MineFor(ahead[next]));
        }
        gene = MineFor(gene);
        return !CanBuild(gene) || Build(gene, forced: false);
    }

    /// <summary>
    /// The mine a gene builds. With <see cref="OptimizerSetup.MineBalancePercent"/>: the lowest mine still below its target
    /// while its next level costs less than that share of the dearest next mine level; otherwise the gene's own mine, or,
    /// once that one is at its target, the cheapest mine still below its target (so every mine gene still builds a level
    /// and the order keeps reaching all targets). Other genes and the rule turned off: the gene itself.
    /// </summary>
    private Gene MineFor(Gene gene)
    {
        if (_mineBalance <= 0 || gene > Gene.DeuteriumSynthesizer)
            return gene;

        int lowest = -1, cheapest = -1;
        double lowestCost = 0, cheapestCost = double.PositiveInfinity, dearest = 0;
        for (var mine = 0; mine <= (int)Gene.DeuteriumSynthesizer; mine++)
        {
            var level = _s.Levels[mine];
            if (level >= _targets[mine])
                continue;

            var cost = Model.MetalCost((Gene)mine, level + 1) + Model.CrystalCost((Gene)mine, level + 1) + Model.DeuteriumCost((Gene)mine, level + 1);
            dearest = Math.Max(dearest, cost);
            if (cost < cheapestCost)
                (cheapest, cheapestCost) = (mine, cost);
            if (lowest < 0 || level < _s.Levels[lowest] || (level == _s.Levels[lowest] && cost < lowestCost))
                (lowest, lowestCost) = (mine, cost);
        }

        if (lowest < 0)
            return gene;
        if (lowestCost < _mineBalance * dearest)
            return (Gene)lowest;
        return _s.Levels[(int)gene] < _targets[(int)gene] ? gene : (Gene)cheapest;
    }

    /// <summary>
    /// <see cref="OptimizerSetup.CoverEnergy"/> for the real queue: when energy is short now or would be after any queued
    /// item, satellites are ordered at once (the shipyard doesn't wait for the building queue), paid only from what the
    /// waiting items don't need. Without this the plan could only add energy after the whole queue.
    /// </summary>
    private void CoverQueueEnergy(PlanetStart planet)
    {
        if (!Setup.CoverEnergy || !CanOrderSatellites || Model.SatelliteEnergy <= 0)
            return;

        var levels = _s.Levels;
        var lowest = Balance(ref levels);
        double reservedCrystal = 0, reservedDeuterium = 0;
        for (var i = 0; i < planet.Queue.Count; i++)
        {
            var item = planet.Queue[i];
            if (i > 0 || item.Seconds is null)
            {
                reservedCrystal += item.Cost.Crystal;
                reservedDeuterium += item.Cost.Deuterium;
            }
            if (item.Gene is { } gene && gene < Gene.SolarSatellites && item.Level <= GameModel.MaxLevel)
            {
                levels[(int)gene] = Math.Max(levels[(int)gene], item.Level);
                lowest = Math.Min(lowest, Balance(ref levels));
            }
        }
        if (lowest >= 0)
            return;

        var count = Math.Max(Setup.MinSatelliteOrder, (int)Math.Ceiling(-lowest / Model.SatelliteEnergy));
        var (_, crystalEach, deuteriumEach) = GameModel.SatelliteCost;
        if (_s.Crystal - reservedCrystal < count * crystalEach || _s.Deuterium - reservedDeuterium < count * deuteriumEach)
            return; // the game couldn't take the order now either; the plan adds energy later

        _s.Crystal -= count * crystalEach;
        _s.Deuterium -= count * deuteriumEach;
        var seconds = Model.SatelliteSeconds(_s.Levels[(int)Gene.Shipyard]);
        if (_s.SatellitesPending == 0)
        {
            _s.SatelliteSeconds = seconds;
            _s.NextSatelliteAt = _s.Time + seconds;
        }
        _s.SatellitesPending += count;
        _startSteps.Add(new OptimizedStep(Gene.SolarSatellites, count, _s.Time, _s.NextSatelliteAt + (_s.SatellitesPending - 1) * _s.SatelliteSeconds));
        _ratesValid = false;

        double Balance(ref LevelArray l) =>
            Model.SolarEnergy[l[(int)Gene.SolarPlant]] + Satellites * Model.SatelliteEnergy + _extraEnergy
            - (Model.MetalConsumption[l[0]] + Model.CrystalConsumption[l[1]] + Model.DeuteriumConsumption[l[2]]);
    }

    /// <summary>Whether the next level of the building is allowed (max level, requirements).</summary>
    public bool CanBuild(Gene gene)
    {
        var next = _s.Levels[(int)gene] + 1;
        if (next > _maxLevels[(int)gene])
            return false;
        return gene switch
        {
            Gene.NaniteFactory => _s.Levels[(int)Gene.RoboticsFactory] >= GameModel.NaniteRoboticsLevel,
            Gene.Shipyard => _s.Levels[(int)Gene.RoboticsFactory] >= GameModel.ShipyardRoboticsLevel,
            _ => true
        };
    }

    public bool CanOrderSatellites =>
        Setup.UseSatellites
        && _s.Levels[(int)Gene.Shipyard] >= GameModel.SatelliteShipyardLevel
        && _s.Levels[(int)Gene.RoboticsFactory] >= GameModel.SatelliteRoboticsLevel;

    /// <summary>Energy produced (solar plant + satellites built so far) minus energy used by the mines.</summary>
    public double EnergyBalance => EnergyProduction(_s.Satellites) - EnergyUse(_s.Levels[0], _s.Levels[1], _s.Levels[2]);

    /// <summary>Energy balance once the next level of <paramref name="mine"/> is built and every ordered satellite flies.</summary>
    public double EnergyBalanceAfter(Gene mine)
    {
        var m = _s.Levels[0] + (mine == Gene.MetalMine ? 1 : 0);
        var c = _s.Levels[1] + (mine == Gene.CrystalMine ? 1 : 0);
        var d = _s.Levels[2] + (mine == Gene.DeuteriumSynthesizer ? 1 : 0);
        return EnergyProduction(Satellites) - EnergyUse(m, c, d);
    }

    /// <summary>Seconds from now until the next level could be finished (waiting for resources + construction).</summary>
    public double SecondsToFinish(Gene gene)
    {
        var level = _s.Levels[(int)gene] + 1;
        var wait = SecondsToAfford(Model.MetalCost(gene, level), Model.CrystalCost(gene, level), Model.DeuteriumCost(gene, level));
        return wait + Model.Seconds(gene, level, _s.Levels[(int)Gene.RoboticsFactory], _s.Levels[(int)Gene.NaniteFactory]);
    }

    private bool Build(Gene gene, bool forced)
    {
        var index = (int)gene;
        var level = _s.Levels[index] + 1;

        // Robotics first when this mine level would pass the mine level it must be there by (OptimizerSetup.EarlyRoboticsLevel).
        if (!forced && !_finishingQueue && index <= (int)Gene.DeuteriumSynthesizer && level > _earlyRoboticsMineLevel)
        {
            while (_s.Levels[(int)Gene.RoboticsFactory] < _earlyRobotics && CanBuild(Gene.RoboticsFactory))
            {
                if (!Build(Gene.RoboticsFactory, forced: true))
                    return false;
            }
        }

        if (!forced && !_finishingQueue && index <= (int)Gene.DeuteriumSynthesizer && Setup.KeepMinesPowered && !PowerFor(gene))
            return false;

        var metal = Model.MetalCost(gene, level);
        var crystal = Model.CrystalCost(gene, level);
        var deuterium = Model.DeuteriumCost(gene, level);

        // A cost above the capacity can never be paid: raise that storage first.
        if (!EnsureCapacity(MetalStorageIndex, metal) || !EnsureCapacity(CrystalStorageIndex, crystal) || !EnsureCapacity(DeuteriumTankIndex, deuterium))
            return false;
        if (!WaitFor(metal, crystal, deuterium))
            return false;

        // Storages first when this item would leave a resource (nearly) full (OptimizerSetup.StorageFillPercent).
        if (!forced && _storageFill > 0 && !_finishingQueue)
        {
            for (var guard = 0; guard < 6; guard++)
            {
                var storage = FullStorage(metal, crystal, deuterium);
                if (storage < 0)
                    break;
                if (!Build((Gene)storage, forced: true) || !WaitFor(metal, crystal, deuterium))
                    return false;
            }
        }

        _s.Metal -= metal;
        _s.Crystal -= crystal;
        _s.Deuterium -= deuterium;
        var start = _s.Time;
        Advance(Model.Seconds(gene, level, _s.Levels[(int)Gene.RoboticsFactory], _s.Levels[(int)Gene.NaniteFactory]));
        _s.Levels[index] = level;
        if (index <= (int)Gene.SolarPlant)
            _ratesValid = false; // mines and the solar plant change production
        _timeline?.Add(new OptimizedStep(gene, level, start, _s.Time, forced));
        return true;
    }

    /// <summary>
    /// <see cref="OptimizerSetup.KeepMinesPowered"/>: energy for the mine's next level before it starts. Solar Plant levels
    /// while allowed, then satellites (after the Robotics and Shipyard levels they need); when neither is possible the mine
    /// is built short of energy. False when the plan gets stuck.
    /// </summary>
    private bool PowerFor(Gene mine)
    {
        for (var guard = 0; guard < 2 * GameModel.MaxLevel && EnergyBalanceAfter(mine) < 0; guard++)
        {
            if (CanBuild(Gene.SolarPlant))
            {
                if (!Build(Gene.SolarPlant, forced: true))
                    return false;
            }
            else if (!Setup.UseSatellites || Model.SatelliteEnergy <= 0)
                return true;
            else if (_s.Levels[(int)Gene.RoboticsFactory] < GameModel.SatelliteRoboticsLevel)
            {
                if (!CanBuild(Gene.RoboticsFactory))
                    return true;
                if (!Build(Gene.RoboticsFactory, forced: true))
                    return false;
            }
            else if (_s.Levels[(int)Gene.Shipyard] < GameModel.SatelliteShipyardLevel)
            {
                if (!CanBuild(Gene.Shipyard))
                    return true;
                if (!Build(Gene.Shipyard, forced: true))
                    return false;
            }
            else
                return OrderSatellites(mine);
        }
        return true;
    }

    /// <summary>
    /// The storage whose resource would stay fullest at or above the fill share once the item is paid; −1 if none. A
    /// storage that its resource would fill to the share of the new capacity again while it's being built doesn't count:
    /// it can't help, and raising it again and again (each level slower than the last) only delays the plan.
    /// </summary>
    private int FullStorage(double metal, double crystal, double deuterium)
    {
        var storage = -1;
        var fullest = _storageFill;
        UpdateRates();
        Check(MetalStorageIndex, _s.Metal - metal);
        Check(CrystalStorageIndex, _s.Crystal - crystal);
        Check(DeuteriumTankIndex, _s.Deuterium - deuterium);
        return storage;

        void Check(int index, double left)
        {
            var level = _s.Levels[index];
            var share = left / Model.Capacity[level];
            if (share >= fullest && level < _maxLevels[index] && level < GameModel.MaxLevel && RefillsWhileBuilt(index, left) < _storageFill)
            {
                storage = index;
                fullest = share;
            }
        }
    }

    /// <summary>The share of the next capacity its resource would have once the storage's next level is built.</summary>
    private double RefillsWhileBuilt(int storage, double left)
    {
        var level = _s.Levels[storage] + 1;
        var rate = storage switch
        {
            MetalStorageIndex => _metalRate,
            CrystalStorageIndex => _crystalRate,
            _ => _deuteriumRate
        };
        var seconds = Model.Seconds((Gene)storage, level, _s.Levels[(int)Gene.RoboticsFactory], _s.Levels[(int)Gene.NaniteFactory]);
        return Math.Min(Model.Capacity[level - 1], left + rate * seconds) / Model.Capacity[level];
    }

    private bool EnsureCapacity(int storage, double amount)
    {
        while (Model.Capacity[_s.Levels[storage]] < amount)
        {
            if (_s.Levels[storage] >= GameModel.MaxLevel || !Build((Gene)storage, forced: true))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Orders enough satellites to power <paramref name="nextMine"/>'s next level, or (no mine left, with
    /// <see cref="OptimizerSetup.CoverEnergy"/>) the mines as they are; nothing when there is no shortage.
    /// </summary>
    private bool OrderSatellites(Gene? nextMine)
    {
        if (!CanOrderSatellites || Model.SatelliteEnergy <= 0 || (nextMine is null && !Setup.CoverEnergy))
            return true;

        var shortage = nextMine is { } mine
            ? -EnergyBalanceAfter(mine)
            : EnergyUse(_s.Levels[0], _s.Levels[1], _s.Levels[2]) - EnergyProduction(Satellites);
        if (shortage <= 0)
            return true;

        var count = Math.Max(Setup.MinSatelliteOrder, (int)Math.Ceiling(shortage / Model.SatelliteEnergy));
        var (_, crystalEach, deuteriumEach) = GameModel.SatelliteCost;
        var crystal = count * crystalEach;
        var deuterium = count * deuteriumEach;
        if (!EnsureCapacity(CrystalStorageIndex, crystal) || !EnsureCapacity(DeuteriumTankIndex, deuterium))
            return false;
        if (!WaitFor(0, crystal, deuterium))
            return false;

        _s.Crystal -= crystal;
        _s.Deuterium -= deuterium;
        var seconds = Model.SatelliteSeconds(_s.Levels[(int)Gene.Shipyard]);
        if (_s.SatellitesPending == 0)
        {
            _s.SatelliteSeconds = seconds;
            _s.NextSatelliteAt = _s.Time + seconds;
        }
        _s.SatellitesPending += count;
        var finish = _s.NextSatelliteAt + (_s.SatellitesPending - 1) * _s.SatelliteSeconds;
        _timeline?.Add(new OptimizedStep(Gene.SolarSatellites, count, _s.Time, finish));
        return true;
    }

    /// <summary>Index of the next mine in <paramref name="genes"/> from <paramref name="from"/> that is still below its target; −1 if none.</summary>
    private int NextMine(ReadOnlySpan<Gene> genes, int from)
    {
        for (var i = from; i < genes.Length; i++)
        {
            var gene = genes[i];
            if (gene <= Gene.DeuteriumSynthesizer && _s.Levels[(int)gene] < _targets[(int)gene])
                return i;
        }
        return -1;
    }

    /// <summary>Lets time pass until the resources are there; false when they never come.</summary>
    private bool WaitFor(double metal, double crystal, double deuterium)
    {
        while (true)
        {
            UpdateRates();
            var wait = Math.Max(Need(metal - _s.Metal, _metalRate), Math.Max(Need(crystal - _s.Crystal, _crystalRate), Need(deuterium - _s.Deuterium, _deuteriumRate)));
            if (wait <= 0)
                return true;

            if (_s.SatellitesPending > 0 && _s.Time + wait > _s.NextSatelliteAt && !_energyCovered)
            {
                // Mines are short of energy: rates change when the next satellite is done.
                Advance(_s.NextSatelliteAt - _s.Time);
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
        UpdateRates();
        return Math.Max(0, Math.Max(Need(metal - _s.Metal, _metalRate), Math.Max(Need(crystal - _s.Crystal, _crystalRate), Need(deuterium - _s.Deuterium, _deuteriumRate))));
    }

    private static double Need(double missing, double rate) =>
        missing <= 0 ? 0 : rate > 0 ? missing / rate : double.PositiveInfinity;

    /// <summary>
    /// Production while <paramref name="seconds"/> pass. While the mines are short of energy, the interval is split
    /// where a satellite is finished (each one raises production); once energy covers the mines, satellites don't
    /// change production any more, so all the ones due in the interval are counted at once.
    /// </summary>
    private void Advance(double seconds)
    {
        while (seconds > 0)
        {
            UpdateRates();
            if (_s.SatellitesPending > 0 && _energyCovered)
            {
                Produce(seconds);
                _s.Time += seconds;
                CompleteSatellitesUntil(_s.Time);
                return;
            }

            var step = seconds;
            var satelliteDone = false;
            if (_s.SatellitesPending > 0 && _s.Time + step >= _s.NextSatelliteAt)
            {
                step = Math.Max(0, _s.NextSatelliteAt - _s.Time);
                satelliteDone = true;
            }

            Produce(step);
            _s.Time += step;
            seconds -= step;
            if (satelliteDone)
            {
                _s.Satellites++;
                _s.SatellitesPending--;
                _s.NextSatelliteAt += _s.SatelliteSeconds;
                _ratesValid = false;
            }
        }

        // Satellites finished exactly at the end of the interval.
        CompleteSatellitesUntil(_s.Time);
    }

    /// <summary>Counts every ordered satellite finished by <paramref name="time"/> (O(1), no matter how many).</summary>
    private void CompleteSatellitesUntil(double time)
    {
        if (_s.SatellitesPending == 0 || _s.NextSatelliteAt > time)
            return;

        var done = (int)Math.Min(_s.SatellitesPending, Math.Floor((time - _s.NextSatelliteAt) / _s.SatelliteSeconds) + 1);
        _s.Satellites += done;
        _s.SatellitesPending -= done;
        _s.NextSatelliteAt += done * _s.SatelliteSeconds;
        _ratesValid = false;
    }

    /// <summary>Uses the cached rates (the caller made sure they are current).</summary>
    private void Produce(double seconds)
    {
        if (seconds <= 0)
            return;

        _s.Metal = Fill(_s.Metal, _metalRate * seconds, Model.Capacity[_s.Levels[MetalStorageIndex]]);
        _s.Crystal = Fill(_s.Crystal, _crystalRate * seconds, Model.Capacity[_s.Levels[CrystalStorageIndex]]);
        _s.Deuterium = Fill(_s.Deuterium, _deuteriumRate * seconds, Model.Capacity[_s.Levels[DeuteriumTankIndex]]);
    }

    /// <summary>Production stops at the capacity; resources already above it (start resources) are kept.</summary>
    private static double Fill(double amount, double produced, double capacity) =>
        amount >= capacity ? amount : Math.Min(capacity, amount + produced);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateRates()
    {
        if (_ratesValid)
            return;

        var use = EnergyUse(_s.Levels[0], _s.Levels[1], _s.Levels[2]);
        var production = EnergyProduction(_s.Satellites);
        var factor = use <= 0 ? 1 : Math.Min(1, production / use);
        _energyCovered = production >= use;
        _metalRate = Model.BaseMetalIncome + Model.MetalPerSecond[_s.Levels[0]] * factor * _metalCorrection;
        _crystalRate = Model.BaseCrystalIncome + Model.CrystalPerSecond[_s.Levels[1]] * factor * _crystalCorrection;
        _deuteriumRate = Model.DeuteriumPerSecond[_s.Levels[2]] * factor * _deuteriumCorrection;
        _ratesValid = true;
    }

    private double EnergyProduction(int satellites) =>
        Model.SolarEnergy[_s.Levels[(int)Gene.SolarPlant]] + satellites * Model.SatelliteEnergy + _extraEnergy;

    private double EnergyUse(int metal, int crystal, int deuterium) =>
        Model.MetalConsumption[metal] + Model.CrystalConsumption[crystal] + Model.DeuteriumConsumption[deuterium];
}

/// <summary>Building levels by <see cref="Gene"/> index, stored inline so the whole state copies as one value.</summary>
[InlineArray(GameModel.BuildingCount)]
public struct LevelArray
{
    private int _first;
}

/// <summary>Everything that changes while an order is simulated.</summary>
public struct SimState
{
    public LevelArray Levels;
    public double Metal, Crystal, Deuterium, Time;
    public int Satellites, SatellitesPending;
    public double NextSatelliteAt, SatelliteSeconds;
}

/// <summary>
/// What the simulation of one order leaves behind for the next candidate: the state before every
/// <see cref="Interval"/>-th gene, and for every gene how far ahead it looked (a satellite gene sizes its order for
/// the next mine in the order, so changing that mine changes the satellite gene's result too).
/// </summary>
public sealed class EvaluationTrace
{
    public const int Interval = 8;

    private SimState[] _checkpoints = new SimState[64];
    private int[] _reach = new int[512];

    /// <summary>Genes simulated before the result was known (targets reached or stuck).</summary>
    public int Length { get; private set; }

    public double Seconds { get; private set; } = double.PositiveInfinity;

    public bool IsValid { get; private set; }

    internal SimState[] Checkpoints => _checkpoints;

    public void Invalidate() => IsValid = false;

    internal void Prepare(int genes)
    {
        if (_reach.Length < genes)
            Array.Resize(ref _reach, Math.Max(genes, _reach.Length * 2));
        var checkpoints = genes / Interval + 1;
        if (_checkpoints.Length < checkpoints)
            Array.Resize(ref _checkpoints, Math.Max(checkpoints, _checkpoints.Length * 2));
        Length = 0;
        IsValid = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Save(int gene, ref SimState state)
    {
        if (gene % Interval == 0)
            _checkpoints[gene / Interval] = state;
    }

    internal void SetReach(int gene, int reach)
    {
        _reach[gene] = reach;
        Length = gene + 1;
    }

    internal void Finish(double seconds)
    {
        Seconds = seconds;
        IsValid = true;
    }

    /// <summary>
    /// Where to resume an order that equals this trace's order before <paramref name="firstChanged"/>: the last
    /// checkpoint before the first gene whose result can differ. That is <paramref name="firstChanged"/> itself, or an
    /// earlier satellite gene that looked ahead to it or beyond. Returns 0 (start over) when there is nothing to reuse.
    /// </summary>
    internal int ResumePoint(ReadOnlySpan<Gene> genes, int firstChanged, out int checkpoint)
    {
        checkpoint = 0;
        if (!IsValid)
            return 0;

        var start = Math.Min(firstChanged, Length);
        for (var j = start - 1; j >= 0; j--)
        {
            var reach = _reach[j];
            if (reach >= firstChanged)
                start = j;
            else if (reach == j && genes[j] <= Gene.DeuteriumSynthesizer)
                break; // an earlier satellite gene looks at this mine or before
        }

        // A checkpoint exists for every simulated gene index that is a multiple of the interval.
        checkpoint = Math.Min(start, Math.Max(0, Length - 1)) / Interval;
        return checkpoint * Interval;
    }

    /// <summary>Takes over the unchanged beginning of <paramref name="baseline"/>.</summary>
    internal void CopyPrefix(EvaluationTrace baseline, int checkpoints, int genes)
    {
        Array.Copy(baseline._checkpoints, _checkpoints, checkpoints + 1);
        Array.Copy(baseline._reach, _reach, genes);
    }
}
