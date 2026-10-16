using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Services.Schemas;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class SchemaPlannerTests
{
    private static readonly PlanetId PlanetId = new("1");

    internal static BuildSchema Schema(params (BuildingType Building, int Level)[] steps) => Schema(new SchemaRules { MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 0 }, steps);

    internal static BuildSchema Schema(SchemaRules rules, params (BuildingType Building, int Level)[] steps) => new()
    {
        Name = "test",
        Steps = steps.Select(s => new SchemaStep(s.Building, s.Level)).ToList(),
        Rules = rules
    };

    private static SchemaAction.Upgrade AssertUpgrade(SchemaAction action, BuildingType building, int level)
    {
        var upgrade = Assert.IsType<SchemaAction.Upgrade>(action);
        Assert.Equal((building, level), (upgrade.Building, upgrade.Level));
        return upgrade;
    }

    [Fact]
    public void Appending_a_building_adds_its_next_level()
    {
        var schema = new BuildSchema();
        schema.AppendNextLevel(BuildingType.MetalMine);
        schema.AppendNextLevel(BuildingType.CrystalMine);
        schema.AppendNextLevel(BuildingType.MetalMine);

        Assert.Equal([new(BuildingType.MetalMine, 1), new(BuildingType.CrystalMine, 1), new SchemaStep(BuildingType.MetalMine, 2)], schema.Steps);
    }

    [Fact]
    public void Skips_steps_that_are_built_or_queued_and_finishes()
    {
        var schema = Schema((BuildingType.MetalMine, 1), (BuildingType.CrystalMine, 1), (BuildingType.MetalMine, 2));
        var state = State(new() { [BuildingType.MetalMine] = 1 }, queue: [new QueuedBuilding(BuildingType.CrystalMine, 1, Started: true)]);

        AssertUpgrade(SchemaPlanner.Next(state, schema), BuildingType.MetalMine, 2);
        Assert.IsType<SchemaAction.Done>(SchemaPlanner.Next(State(new() { [BuildingType.MetalMine] = 2, [BuildingType.CrystalMine] = 1 }), schema));
    }

    [Fact]
    public void A_step_further_ahead_is_reached_one_level_at_a_time()
    {
        var action = SchemaPlanner.Next(State(new() { [BuildingType.MetalMine] = 2 }), Schema((BuildingType.MetalMine, 5)));

        AssertUpgrade(action, BuildingType.MetalMine, 3);
    }

    [Fact]
    public void Strict_order_waits_for_the_next_step_even_when_a_later_one_is_affordable()
    {
        // metal 1 needs 60 metal, crystal 1 only 48
        var schema = Schema((BuildingType.MetalMine, 1), (BuildingType.CrystalMine, 1));

        var action = SchemaPlanner.Next(State(resources: new(50, 100, 0)), schema);

        var wait = Assert.IsType<SchemaAction.Wait>(action);
        Assert.Contains("Metal Mine 1", wait.Reason, StringComparison.Ordinal);
        Assert.Contains("10 metal", wait.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Waiting_queue_items_are_reserved_because_the_game_drops_them_when_unpaid()
    {
        // crystal 1 = 48/24; the waiting metal 1 (60/15) will be paid first
        var schema = Schema((BuildingType.MetalMine, 1), (BuildingType.CrystalMine, 1));
        var waiting = State(resources: new(100, 50, 0), queue: [new QueuedBuilding(BuildingType.MetalMine, 1, Started: false)]);
        var started = State(new() { [BuildingType.MetalMine] = 0 }, resources: new(100, 50, 0), queue: [new QueuedBuilding(BuildingType.MetalMine, 1, Started: true)]);

        Assert.IsType<SchemaAction.Wait>(SchemaPlanner.Next(waiting, schema));
        AssertUpgrade(SchemaPlanner.Next(started, schema), BuildingType.CrystalMine, 1);
    }

    [Fact]
    public void Keeps_the_games_queue_shallow()
    {
        var schema = Schema(new SchemaRules { MaxServerQueueDepth = 2, StorageFillPercent = 0, RoboticsMaxLevel = 0 }, (BuildingType.MetalMine, 5));
        var state = State(queue: [new QueuedBuilding(BuildingType.MetalMine, 1, true), new QueuedBuilding(BuildingType.MetalMine, 2, false)]);

        Assert.Contains("limit 2", Assert.IsType<SchemaAction.Wait>(SchemaPlanner.Next(state, schema)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Locked_step_and_vacation_mode_wait()
    {
        var schema = Schema((BuildingType.NaniteFactory, 1));

        Assert.Contains("requirements", Assert.IsType<SchemaAction.Wait>(SchemaPlanner.Next(State(locked: BuildingType.NaniteFactory), schema)).Reason, StringComparison.Ordinal);
        Assert.Equal("vacation mode", Assert.IsType<SchemaAction.Wait>(SchemaPlanner.Next(State() with { VacationMode = true }, schema)).Reason);
    }

    [Fact]
    public void Solar_plant_goes_first_when_the_next_step_would_run_out_of_energy()
    {
        var action = SchemaPlanner.Next(State(energy: 0), Schema((BuildingType.MetalMine, 1)));

        AssertUpgrade(action, BuildingType.SolarPlant, 1);
    }

    [Fact]
    public void Solar_satellites_when_the_solar_plant_is_at_its_maximum()
    {
        var rules = new SchemaRules { SolarMaxLevel = 0, MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 0 };
        var shipyard = new ShipyardState(PlanetId, 10, 50, new ResourceSet(0, 2_000, 500), SatellitesLocked: false);

        // −100 now, metal mine 1 uses 11 more → 111 short → 3 satellites of 50
        var action = SchemaPlanner.Next(State(energy: -100), Schema(rules, (BuildingType.MetalMine, 1)), shipyard);

        Assert.Equal(new SchemaAction.Satellites(3, new ResourceSet(0, 6_000, 1_500), "energy would drop to -111, solar plant at its maximum"), action);
    }

    [Fact]
    public void Ordered_satellites_count_as_coming_and_locked_satellites_build_on_with_a_deficit()
    {
        var rules = new SchemaRules { SolarMaxLevel = 0, MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 0 };
        var shipyard = new ShipyardState(PlanetId, 10, 50, new ResourceSet(0, 2_000, 500), SatellitesLocked: false);
        var schema = Schema(rules, (BuildingType.MetalMine, 1));

        AssertUpgrade(SchemaPlanner.Next(State(energy: -100), schema, shipyard, pendingSatellites: 3), BuildingType.MetalMine, 1);
        AssertUpgrade(SchemaPlanner.Next(State(energy: -100), schema, shipyard with { SatellitesLocked = true }), BuildingType.MetalMine, 1);
    }

    [Fact]
    public void Storage_goes_before_the_step_when_a_resource_is_nearly_full()
    {
        var rules = new SchemaRules { MaxServerQueueDepth = 15, StorageFillPercent = 70, RoboticsMaxLevel = 0 };
        var state = State(resources: new(800_000, 10, 10)) with { StorageCapacity = new ResourceSet(1_000_000, 1_000_000, 1_000_000) };

        AssertUpgrade(SchemaPlanner.Next(state, Schema(rules, (BuildingType.CrystalMine, 1))), BuildingType.MetalStorage, 1);
    }

    private static PlanetBuildState FactoryState(int robotics, long productionPerHour, params BuildingType[] locked) =>
        State(new() { [BuildingType.RoboticsFactory] = robotics, [BuildingType.MetalMine] = 10 }, locked: locked) with
        {
            ProductionPerHour = new ResourceSet(productionPerHour, 0, 0),
            NextLevelDurations = new Dictionary<BuildingType, TimeSpan>
            {
                [BuildingType.MetalMine] = TimeSpan.FromHours(1),
                [BuildingType.RoboticsFactory] = TimeSpan.FromMinutes(10),
                [BuildingType.NaniteFactory] = TimeSpan.FromMinutes(30)
            },
            NextLevelCosts = new Dictionary<BuildingType, ResourceSet>
            {
                [BuildingType.RoboticsFactory] = new(100_000, 0, 0),
                [BuildingType.NaniteFactory] = new(1_000_000, 0, 0)
            }
        };

    private static readonly (BuildingType, int)[] TenMetalLevels = Enumerable.Range(11, 10).Select(l => (BuildingType.MetalMine, l)).ToArray();

    [Fact]
    public void Robotics_factory_when_it_pays_back_in_construction_time()
    {
        // 10 metal levels from 1h each (growing) ≫ robotics 3: saves W/4, costs 10m + 100k / 1M per hour = 16m
        var rules = new SchemaRules { MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 5 };

        var upgrade = AssertUpgrade(SchemaPlanner.Next(FactoryState(2, 1_000_000), Schema(rules, TenMetalLevels)), BuildingType.RoboticsFactory, 3);
        Assert.Contains("saves", upgrade.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void No_robotics_factory_when_it_does_not_pay_back_or_is_at_its_maximum()
    {
        var rules = new SchemaRules { MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 5 };

        // 100k at 100 per hour = 1000h of production: never worth it
        AssertUpgrade(SchemaPlanner.Next(FactoryState(2, 100), Schema(rules, TenMetalLevels)), BuildingType.MetalMine, 11);
        AssertUpgrade(SchemaPlanner.Next(FactoryState(5, 1_000_000), Schema(rules, TenMetalLevels)), BuildingType.MetalMine, 11);
    }

    [Fact]
    public void Nanite_factory_is_preferred_and_skipped_while_locked()
    {
        var rules = new SchemaRules { MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 5, NaniteMaxLevel = 2 };

        AssertUpgrade(SchemaPlanner.Next(FactoryState(2, 10_000_000), Schema(rules, TenMetalLevels)), BuildingType.NaniteFactory, 1);
        AssertUpgrade(SchemaPlanner.Next(FactoryState(2, 10_000_000, BuildingType.NaniteFactory), Schema(rules, TenMetalLevels)), BuildingType.RoboticsFactory, 3);
    }
}
