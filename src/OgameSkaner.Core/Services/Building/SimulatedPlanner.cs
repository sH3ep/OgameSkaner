using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Services.Optimizer;

namespace OgameSkaner.Core.Services.Building;

/// <summary>How the Builder decides what to build.</summary>
public enum BuilderMode
{
    /// <summary>Plan each planet with the build simulation: the fastest order to the targets.</summary>
    Simulated,

    /// <summary>The cheapest next level of the ticked buildings first (metal 1, crystal 1.5, deuterium 3), until every target is reached.</summary>
    ByPrice
}

/// <summary>Settings of the Builder (most only matter in the Simulated mode).</summary>
/// <param name="SimulationsPerPlan">Orders the optimizer simulates for one plan of one planet.</param>
/// <param name="UseSolarSatellites">Satellites (and the Shipyard levels they need) may provide energy.</param>
/// <param name="MaxCheckInterval">Longest wait between two checks of the planets.</param>
public sealed record SimulatedBuildOptions(
    int SimulationsPerPlan = SimulatedBuildOptions.DefaultSimulations,
    bool UseSolarSatellites = true,
    int CpuPercent = OptimizerOptions.DefaultCpuPercent,
    TimeSpan? MaxCheckInterval = null,
    BuilderMode Mode = BuilderMode.Simulated)
{
    public const int DefaultSimulations = 2_000_000;

    /// <summary>Shortest wait between checks; requests are throttled anyway.</summary>
    public static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(10);

    public TimeSpan CheckInterval => MaxCheckInterval ?? TimeSpan.FromMinutes(5);

    /// <summary>Runs × iterations for <see cref="SimulationsPerPlan"/>: a few runs per thread.</summary>
    public OptimizerOptions OptimizerOptions(int seed)
    {
        var threads = Optimizer.OptimizerOptions.ThreadsFor(CpuPercent);
        var runs = threads * 4;
        return new OptimizerOptions(runs, Math.Max(1_000, SimulationsPerPlan / runs), seed, threads);
    }
}

/// <summary>Something to send to the game now.</summary>
public abstract record SimulatedAction
{
    public sealed record Upgrade(BuildingType Building, int Level, ResourceSet Cost) : SimulatedAction;

    public sealed record Satellites(int Count, ResourceSet Cost) : SimulatedAction;
}

/// <summary>What to push now and when the next item of the plan can follow.</summary>
/// <param name="NextIn">Time until the first item not pushed is due (null when everything is pushed).</param>
public sealed record PushPlan(IReadOnlyList<SimulatedAction> Actions, int StepsPushed, string Waiting, TimeSpan? NextIn);

/// <summary>
/// The pure part of the Builder's Simulated mode. <see cref="SetupFor"/> turns a real planet and the Builder strategy
/// into an optimizer setup that starts from the planet (levels, resources, satellites, queue) with the strategy's
/// targets; <see cref="Push"/> picks the beginning of the optimized order that can go to the game now.
/// </summary>
/// <remarks>
/// Only the buildings the simulation knows are planned this way (mines, solar plant, storages, Robotics, Nanite,
/// Shipyard, satellites); targets of other buildings (Research Lab, Fusion Reactor, …) are built afterwards by price.
/// The model is corrected per planet so it matches the page: the energy the game shows beyond the formulas (e.g. a
/// Fusion Reactor) is added, and each mine's production is scaled to the page's production.
/// </remarks>
public static class SimulatedPlanner
{
    /// <summary>Default upper limits of the factories the search may add as helpers (a target can go above).</summary>
    public const int RoboticsHelperLevel = 10;
    public const int ShipyardHelperLevel = 8;

    /// <summary>Satellites are ordered at least this many at a time, not one or two per mine level.</summary>
    public const int MinSatelliteOrder = 30;

    /// <summary>A correction outside this range means the page and the model disagree on something else; it's ignored.</summary>
    private const double MinCorrection = 0.5, MaxCorrection = 2;

    public static bool IsSimulated(BuildingType building) =>
        building is BuildingType.MetalMine or BuildingType.CrystalMine or BuildingType.DeuteriumSynthesizer or BuildingType.SolarPlant
            or BuildingType.MetalStorage or BuildingType.CrystalStorage or BuildingType.DeuteriumTank
            or BuildingType.RoboticsFactory or BuildingType.NaniteFactory or BuildingType.Shipyard;

    /// <param name="satellites">Satellites on the planet (0 without a shipyard).</param>
    /// <param name="pendingSatellites">Satellites ordered and not finished yet.</param>
    public static OptimizerSetup SetupFor(
        PlanetBuildState state,
        BuildStrategy strategy,
        SimulatedBuildOptions options,
        AccountBonuses bonuses,
        (int Min, int Max) temperature,
        int satellites,
        int pendingSatellites)
    {
        int Target(BuildingType building) =>
            strategy.Rules.FirstOrDefault(r => r.Building == building) is { Enabled: true } rule ? rule.TargetFor(strategy.ReferenceLevel) : 0;

        var planned = PlanetLevels.Planned(state);
        int Planned(BuildingType building) => PlanetLevels.Of(planned, building);

        // Nanite needs Computer Technology 10 too, which the model doesn't know: only add levels the game offers now.
        var nanite = Planned(BuildingType.NaniteFactory);
        var naniteHelper = state.Locked.Contains(BuildingType.NaniteFactory) ? nanite : nanite + 2;
        var conditions = new PlanetConditions(Math.Clamp(state.Planet.Coordinates.Position, 1, PlanetConditions.SlotCount), temperature.Min, temperature.Max);
        var setup = new OptimizerSetup
        {
            MetalMine = Target(BuildingType.MetalMine),
            CrystalMine = Target(BuildingType.CrystalMine),
            DeuteriumSynthesizer = Target(BuildingType.DeuteriumSynthesizer),
            RoboticsTarget = Target(BuildingType.RoboticsFactory),
            NaniteTarget = Target(BuildingType.NaniteFactory),
            ShipyardTarget = Target(BuildingType.Shipyard),
            MetalStorageTarget = Target(BuildingType.MetalStorage),
            CrystalStorageTarget = Target(BuildingType.CrystalStorage),
            DeuteriumTankTarget = Target(BuildingType.DeuteriumTank),
            PlanetSlot = conditions.Slot,
            MinTemperature = conditions.MinTemperature,
            MaxTemperature = conditions.MaxTemperature,
            StartResources = state.Resources,
            Bonuses = bonuses,
            SolarMaxLevel = Math.Max(strategy.SolarMaxLevel, Planned(BuildingType.SolarPlant)),
            RoboticsMaxLevel = Math.Max(RoboticsHelperLevel, Planned(BuildingType.RoboticsFactory) + 2),
            NaniteMaxLevel = naniteHelper,
            UseSatellites = options.UseSolarSatellites,
            CoverEnergy = true,
            KeepMinesPowered = true,
            MinSatelliteOrder = MinSatelliteOrder,
            StorageFillPercent = strategy.StorageFillPercent,
            FillStorages = strategy.FillStorages,
            MineBalancePercent = strategy.MineBalancePercent,
            EarlyRoboticsLevel = strategy.EarlyRoboticsLevel,
            EarlyRoboticsMineLevel = strategy.EarlyRoboticsMineLevel,
            ShipyardMaxLevel = Math.Max(ShipyardHelperLevel, Planned(BuildingType.Shipyard))
        };

        var model = new GameModel(bonuses, conditions);
        return setup with { Start = Calibrate(model, StartOf(state, model, satellites, pendingSatellites), state) };
    }

    /// <summary>The planet as the simulation's start: built levels, resources, satellites and the queue.</summary>
    internal static PlanetStart StartOf(PlanetBuildState state, GameModel model, int satellites, int pendingSatellites)
    {
        var costs = new CostModel(state);
        var levels = state.Levels.Where(l => IsSimulated(l.Key)).ToDictionary(l => GameModel.ToGene(l.Key), l => l.Value);
        var queue = new List<StartQueueItem>();
        var built = new Dictionary<BuildingType, int>(state.Levels);
        foreach (var item in state.Queue)
        {
            var known = IsSimulated(item.Building);
            double? seconds;
            if (item.Started)
            {
                seconds = state.RunningRemaining?.TotalSeconds
                          ?? (known
                              ? model.Seconds(GameModel.ToGene(item.Building), item.Level, PlanetLevels.Of(built, BuildingType.RoboticsFactory), PlanetLevels.Of(built, BuildingType.NaniteFactory))
                              : costs.DurationOf(item.Building, item.Level)?.TotalSeconds ?? 0);
            }
            else
                seconds = known ? null : costs.DurationOf(item.Building, item.Level)?.TotalSeconds ?? 0;

            queue.Add(new StartQueueItem(known ? GameModel.ToGene(item.Building) : null, item.Level, costs.CostOf(item.Building, item.Level), seconds));
            built[item.Building] = item.Level;
        }

        return new PlanetStart
        {
            Levels = levels,
            Resources = state.Resources,
            Satellites = satellites,
            SatellitesPending = pendingSatellites,
            Queue = queue
        };
    }

    /// <summary>
    /// Makes the model agree with the page for the built levels: extra energy = the game's balance − the model's, and
    /// each mine's production scaled to the page's (both include every bonus).
    /// </summary>
    internal static PlanetStart Calibrate(GameModel model, PlanetStart start, PlanetBuildState state)
    {
        int Level(Gene gene) => Math.Min(start.LevelOf(gene), GameModel.MaxLevel);
        int metal = Level(Gene.MetalMine), crystal = Level(Gene.CrystalMine), deuterium = Level(Gene.DeuteriumSynthesizer);

        var production = model.SolarEnergy[Level(Gene.SolarPlant)] + start.Satellites * model.SatelliteEnergy;
        var use = model.MetalConsumption[metal] + model.CrystalConsumption[crystal] + model.DeuteriumConsumption[deuterium];
        var extra = state.EnergyBalance - (production - use);
        // The page's balance is rounded down; only a real difference is an extra source (or drain).
        if (Math.Abs(extra) < 2)
            extra = 0;
        var factor = use <= 0 ? 1 : Math.Clamp((production + extra) / use, 0, 1);

        if (state.ProductionPerHour is not { } page)
            return start with { ExtraEnergy = extra };

        static double Correction(double pagePerSecond, double modelPerSecond)
        {
            if (pagePerSecond <= 0 || modelPerSecond <= 0)
                return 1;
            var ratio = pagePerSecond / modelPerSecond;
            return ratio is >= MinCorrection and <= MaxCorrection ? ratio : 1;
        }

        return start with
        {
            ExtraEnergy = extra,
            MetalCorrection = Correction(page.Metal / 3600.0 - model.BaseMetalIncome, model.MetalPerSecond[metal] * factor),
            CrystalCorrection = Correction(page.Crystal / 3600.0 - model.BaseCrystalIncome, model.CrystalPerSecond[crystal] * factor),
            DeuteriumCorrection = Correction(page.Deuterium / 3600.0, model.DeuteriumPerSecond[deuterium] * factor)
        };
    }

    /// <summary>
    /// The beginning of <paramref name="upgrades"/> (the price planner's order for the buildings the simulation doesn't
    /// know) that can go to the game now: as in <see cref="Push"/>, each must be covered by the resources left after
    /// every waiting queue item and the upgrades before it.
    /// </summary>
    public static IReadOnlyList<PlannedUpgrade> AffordablePrefix(PlanetBuildState state, IEnumerable<PlannedUpgrade> upgrades)
    {
        var available = Unreserved(state);
        var result = new List<PlannedUpgrade>();
        foreach (var upgrade in upgrades)
        {
            if (!available.CanAfford(upgrade.Cost))
                break;
            result.Add(upgrade);
            available -= upgrade.Cost;
        }
        return result;
    }

    /// <summary>The planet's resources minus what the waiting queue items will take when they start.</summary>
    private static ResourceSet Unreserved(PlanetBuildState state)
    {
        var costs = new CostModel(state);
        return state.Resources - state.Queue.Where(q => !q.Started).Aggregate(ResourceSet.Zero, (sum, q) => sum + costs.CostOf(q.Building, q.Level));
    }

    /// <summary>
    /// The beginning of <paramref name="steps"/> (an optimized order from this state) that can go to the game now, in
    /// order: an item is pushed only when the planet's resources cover it plus every waiting queue item (the game takes
    /// the resources when an item starts and drops it when they are missing), satellites are paid at once. Stops at the
    /// first item that has to wait, when the queue is full or the planet has no free fields.
    /// </summary>
    /// <param name="shipyard">The shipyard page; null when the planet has no shipyard (no satellites now).</param>
    public static PushPlan Push(PlanetBuildState state, IReadOnlyList<OptimizedStep> steps, ShipyardState? shipyard, int queueCapacity)
    {
        var costs = new CostModel(state);
        var levels = PlanetLevels.Planned(state);
        var available = Unreserved(state);
        var queued = state.Queue.Count;
        var fields = state.FreeFields;
        var actions = new List<SimulatedAction>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            PushPlan Wait(string reason) => new(actions, i, reason, TimeSpan.FromSeconds(Math.Max(0, step.StartSeconds)));

            if (step.IsSatellites)
            {
                if (shipyard is null || shipyard.SatellitesLocked)
                    return Wait($"{step.Level} solar satellites: the shipyard can't build them yet");
                var cost = shipyard.SatelliteCost.Scale(step.Level);
                if (!available.CanAfford(cost))
                    return Wait($"{step} needs {Missing(cost, available)}");
                actions.Add(new SimulatedAction.Satellites(step.Level, cost));
                available -= cost;
                continue;
            }

            var building = GameModel.ToBuilding(step.Gene);
            var next = PlanetLevels.Of(levels, building) + 1;
            if (step.Level != next)
                return Wait($"{step} doesn't follow level {next - 1} (the planet changed; planning again)");
            if (state.Locked.Contains(building))
                return Wait($"{step} is locked until its requirements are built");
            if (queued >= queueCapacity)
                return Wait("the build queue is full");
            if (fields is <= 0)
                return Wait("no free planet fields");

            var upgrade = costs.CostOf(building, step.Level);
            if (!available.CanAfford(upgrade))
                return Wait($"{step} needs {Missing(upgrade, available)}");

            actions.Add(new SimulatedAction.Upgrade(building, step.Level, upgrade));
            available -= upgrade;
            levels[building] = step.Level;
            queued++;
            fields--;
        }

        return new PushPlan(actions, steps.Count, "every planned item is queued", null);
    }

    private static string Missing(ResourceSet cost, ResourceSet available)
    {
        var parts = new List<string>();
        if (cost.Metal > available.Metal) parts.Add($"{cost.Metal - available.Metal:N0} metal");
        if (cost.Crystal > available.Crystal) parts.Add($"{cost.Crystal - available.Crystal:N0} crystal");
        if (cost.Deuterium > available.Deuterium) parts.Add($"{cost.Deuterium - available.Deuterium:N0} deuterium");
        return parts.Count == 0 ? "nothing" : "more " + string.Join(", ", parts) + " (queued items reserved)";
    }
}
