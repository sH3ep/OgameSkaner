using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Services.Building;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class SimulatedPlannerTests
{
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));
    private static readonly SimulatedBuildOptions Options = new();
    private static readonly (int, int) Temperature = (28, 32);

    private static BuildingRule Relative(BuildingType building, int offset) => new(building, true, RuleMode.Relative, offset);

    private static BuildingRule Absolute(BuildingType building, int level) => new(building, true, RuleMode.Absolute, level);

    private static OptimizerSetup Setup(PlanetBuildState state, BuildStrategy strategy, int satellites = 0, int pending = 0) =>
        SimulatedPlanner.SetupFor(state, strategy, Options, AccountBonuses.Calibration, Temperature, satellites, pending);

    [Fact]
    public void The_strategy_targets_and_the_planet_become_the_setup()
    {
        var strategy = Strategy(10, solarMax: 20,
            Relative(BuildingType.MetalMine, 2), Relative(BuildingType.CrystalMine, 0), Relative(BuildingType.DeuteriumSynthesizer, -2),
            Absolute(BuildingType.RoboticsFactory, 3), Absolute(BuildingType.ResearchLab, 5),
            new BuildingRule(BuildingType.Shipyard, false, RuleMode.Absolute, 6));
        var state = State(
            levels: new() { [BuildingType.MetalMine] = 9, [BuildingType.SolarPlant] = 8, [BuildingType.ResearchLab] = 2 },
            queue: [new QueuedBuilding(BuildingType.MetalMine, 10, Started: true), new QueuedBuilding(BuildingType.ResearchLab, 3, Started: false)],
            planet: Colony) with { RunningRemaining = TimeSpan.FromSeconds(42) };

        var setup = Setup(state, strategy, satellites: 7, pending: 3);

        Assert.Equal((12, 10, 8), (setup.MetalMine, setup.CrystalMine, setup.DeuteriumSynthesizer));
        Assert.Equal((3, 0, 0), (setup.RoboticsTarget, setup.NaniteTarget, setup.ShipyardTarget));
        Assert.Equal((8, 28, 32), (setup.PlanetSlot, setup.MinTemperature, setup.MaxTemperature));
        Assert.Equal(20, setup.SolarMaxLevel);
        Assert.Equal(strategy.StorageFillPercent, setup.StorageFillPercent);
        Assert.True(setup.CoverEnergy);
        Assert.Equal(2, setup.NaniteMaxLevel); // not locked: the search may add two levels
        var start = setup.Start!;
        Assert.Equal(9, start.LevelOf(Gene.MetalMine));
        Assert.Equal((7, 3), (start.Satellites, start.SatellitesPending));
        Assert.Equal(new StartQueueItem(Gene.MetalMine, 10, start.Queue[0].Cost, 42), start.Queue[0]);
        Assert.Null(start.Queue[1].Gene); // Research Lab: not simulated, only time and resources
        Assert.Equal(3, start.Queue[1].Level);
    }

    [Fact]
    public void Nanite_levels_are_only_added_when_the_game_offers_them()
    {
        var strategy = Strategy(10, rules: Relative(BuildingType.MetalMine, 0));
        var unlocked = State(levels: new() { [BuildingType.NaniteFactory] = 1 }, planet: Colony);
        var locked = State(planet: Colony, locked: BuildingType.NaniteFactory);

        Assert.Equal(3, Setup(unlocked, strategy).NaniteMaxLevel);
        Assert.Equal(0, Setup(locked, strategy).NaniteMaxLevel);
    }

    [Fact]
    public void The_model_is_corrected_to_the_page()
    {
        var model = new GameModel(AccountBonuses.Calibration, new PlanetConditions(8, 28, 32));
        // Metal 20 and Crystal 18 at full energy: the page shows 10% more metal, 20% less crystal and 500 more energy.
        var production = model.SolarEnergy[22];
        var use = model.MetalConsumption[20] + model.CrystalConsumption[18];
        Assert.True(production > use);
        var page = new ResourceSet(
            (long)((model.BaseMetalIncome + model.MetalPerSecond[20] * 1.1) * 3600),
            (long)((model.BaseCrystalIncome + model.CrystalPerSecond[18] * 0.8) * 3600),
            0);
        var state = State(
            levels: new() { [BuildingType.MetalMine] = 20, [BuildingType.CrystalMine] = 18, [BuildingType.SolarPlant] = 22 },
            energy: (long)Math.Floor(production - use + 500),
            planet: Colony) with { ProductionPerHour = page };

        var start = Setup(state, Strategy(25, rules: Relative(BuildingType.MetalMine, 0))).Start!;

        Assert.InRange(start.ExtraEnergy, 499, 501);
        Assert.Equal(1.1, start.MetalCorrection, 3);
        Assert.Equal(0.8, start.CrystalCorrection, 3);
        Assert.Equal(1, start.DeuteriumCorrection); // no deuterium production on either side
    }

    [Fact]
    public void Pushes_the_plan_while_resources_cover_it_and_every_waiting_item()
    {
        var costs = new Dictionary<BuildingType, ResourceSet> { [BuildingType.MetalMine] = new(1_000, 0, 0), [BuildingType.CrystalMine] = new(500, 0, 0) };
        var state = State(resources: new ResourceSet(2_000, 1_000, 0), pageCosts: costs, planet: Colony);
        OptimizedStep[] steps =
        [
            new(Gene.MetalMine, 1, 0, 10),
            new(Gene.CrystalMine, 1, 10, 20),
            new(Gene.MetalMine, 2, 600, 700)
        ];

        var push = SimulatedPlanner.Push(state, steps, shipyard: null, queueCapacity: 15);

        Assert.Equal(
            [new SimulatedAction.Upgrade(BuildingType.MetalMine, 1, new(1_000, 0, 0)), new SimulatedAction.Upgrade(BuildingType.CrystalMine, 1, new(500, 0, 0))],
            push.Actions);
        Assert.Equal(2, push.StepsPushed);
        Assert.StartsWith("Metal Mine 2 needs more", push.Waiting);
        Assert.Equal(TimeSpan.FromSeconds(600), push.NextIn);
    }

    [Fact]
    public void Satellites_wait_for_a_shipyard_and_are_paid_at_once()
    {
        var state = State(resources: new ResourceSet(0, 10_000, 10_000), planet: Colony);
        OptimizedStep[] steps = [new(Gene.SolarSatellites, 4, 0, 40)];
        var shipyard = new ShipyardState(Colony.Id, 0, 50, new ResourceSet(0, 2_000, 500), SatellitesLocked: false);

        Assert.Empty(SimulatedPlanner.Push(state, steps, shipyard: null, 15).Actions);
        var push = SimulatedPlanner.Push(state, steps, shipyard, 15);

        Assert.Equal([new SimulatedAction.Satellites(4, new ResourceSet(0, 8_000, 2_000))], push.Actions);
        Assert.Null(push.NextIn);
    }

    [Fact]
    public void Stops_when_the_plan_no_longer_fits_the_planet()
    {
        var state = State(levels: new() { [BuildingType.MetalMine] = 3 }, planet: Colony);
        OptimizedStep[] steps = [new(Gene.MetalMine, 3, 0, 10)];

        var push = SimulatedPlanner.Push(state, steps, null, 15);

        Assert.Empty(push.Actions);
        Assert.Contains("planning again", push.Waiting);
    }

    [Fact]
    public void Respects_the_queue_capacity()
    {
        var queue = Enumerable.Range(1, 15).Select(l => new QueuedBuilding(BuildingType.SolarPlant, l, Started: l == 1)).ToList();
        var state = State(queue: queue, planet: Colony);

        var push = SimulatedPlanner.Push(state, [new OptimizedStep(Gene.MetalMine, 1, 0, 10)], null, 15);

        Assert.Empty(push.Actions);
        Assert.Equal("the build queue is full", push.Waiting);
    }
}
