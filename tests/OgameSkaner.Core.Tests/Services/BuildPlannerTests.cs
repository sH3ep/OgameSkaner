using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Building;

namespace OgameSkaner.Core.Tests.Services;

public class BuildPlannerTests
{
    private static readonly OwnPlanet Planet = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly ResourceSet Rich = new(10_000_000, 10_000_000, 10_000_000);

    internal static PlanetBuildState State(
        Dictionary<BuildingType, int>? levels = null,
        ResourceSet? resources = null,
        IReadOnlyList<QueuedBuilding>? queue = null,
        long energy = 1_000_000,
        OwnPlanet? planet = null,
        Dictionary<BuildingType, ResourceSet>? pageCosts = null,
        params BuildingType[] locked) =>
        new(planet ?? Planet, levels ?? new(), resources ?? Rich, queue ?? [], energy)
        {
            NextLevelCosts = pageCosts ?? new(),
            Locked = locked.ToHashSet()
        };

    internal static BuildStrategy Strategy(int reference, int solarMax = 30, params BuildingRule[] rules) => new(reference, rules, solarMax);

    private static BuildingRule Relative(BuildingType building, int offset) => new(building, true, RuleMode.Relative, offset);

    private static readonly BuildStrategy MetalAndCrystal =
        Strategy(5, rules: [Relative(BuildingType.MetalMine, 0), Relative(BuildingType.CrystalMine, 0)]);

    [Fact]
    public void A_storage_target_is_planned_like_any_other_target()
    {
        var strategy = Strategy(0, rules: [new BuildingRule(BuildingType.MetalStorage, true, RuleMode.Absolute, 2)]) with { StorageFillPercent = 0 };

        var plan = BuildPlanner.Plan(State(), strategy, maxItems: 5, requireAffordable: false);

        Assert.Equal([(BuildingType.MetalStorage, 1), (BuildingType.MetalStorage, 2)], plan.Upgrades.Select(u => (u.Building, u.Level)));
        Assert.Equal(BuildStopReason.TargetsReached, plan.Reason);
    }

    [Fact]
    public void Picks_the_cheapest_upgrade_below_target_by_weighted_value()
    {
        // metal 100 + 10×1.5 = 115, crystal 60 + 40×1.5 = 120 (raw totals 110 vs 100 would pick crystal)
        var state = State(pageCosts: new() { [BuildingType.MetalMine] = new(100, 10, 0), [BuildingType.CrystalMine] = new(60, 40, 0) });

        var decision = BuildPlanner.NextUpgrade(state, MetalAndCrystal, requireAffordable: false);

        Assert.Equal(new PlannedUpgrade(BuildingType.MetalMine, 1, new(100, 10, 0)), decision.Upgrade);
    }

    [Fact]
    public void Deuterium_weighs_three_times_metal()
    {
        // robotics 400/120/200: raw 720, weighted 1180; metal 683/170/0: raw 853, weighted 938
        var strategy = Strategy(7, rules: [Relative(BuildingType.MetalMine, 0), new BuildingRule(BuildingType.RoboticsFactory, true, RuleMode.Absolute, 1)]);
        var state = State(new() { [BuildingType.MetalMine] = 6 },
            pageCosts: new() { [BuildingType.MetalMine] = new(683, 170, 0), [BuildingType.RoboticsFactory] = new(400, 120, 200) });

        var decision = BuildPlanner.NextUpgrade(state, strategy, requireAffordable: false);

        Assert.Equal(new PlannedUpgrade(BuildingType.MetalMine, 7, new(683, 170, 0)), decision.Upgrade);
    }

    [Fact]
    public void Disabled_rules_and_reached_targets_are_skipped()
    {
        var strategy = Strategy(5, rules: [Relative(BuildingType.MetalMine, 0), new BuildingRule(BuildingType.CrystalMine, false, RuleMode.Absolute, 20)]);

        var decision = BuildPlanner.NextUpgrade(State(new() { [BuildingType.MetalMine] = 5 }), strategy, requireAffordable: false);

        Assert.Null(decision.Upgrade);
        Assert.Equal(BuildStopReason.TargetsReached, decision.Reason);
    }

    [Fact]
    public void Locked_buildings_are_skipped()
    {
        var decision = BuildPlanner.NextUpgrade(State(locked: BuildingType.MetalMine), MetalAndCrystal, requireAffordable: false);

        Assert.Equal(BuildingType.CrystalMine, decision.Upgrade?.Building);
    }

    [Fact]
    public void Queued_levels_count_as_built()
    {
        var state = State(new() { [BuildingType.MetalMine] = 3 }, queue: [new QueuedBuilding(BuildingType.MetalMine, 4, Started: true)]);

        var decision = BuildPlanner.NextUpgrade(state, Strategy(10, rules: [Relative(BuildingType.MetalMine, 0)]), requireAffordable: false);

        Assert.Equal(5, decision.Upgrade?.Level);
    }

    [Fact]
    public void Uses_the_page_cost_for_the_next_level_and_grows_it_by_the_catalog_factor()
    {
        var state = State(new() { [BuildingType.MetalMine] = 24 }, pageCosts: new() { [BuildingType.MetalMine] = new(911_647, 227_912, 0) });

        var plan = BuildPlanner.Plan(state, Strategy(26, rules: [Relative(BuildingType.MetalMine, 0)]), maxItems: 14, requireAffordable: false);

        Assert.Equal(
            [new PlannedUpgrade(BuildingType.MetalMine, 25, new(911_647, 227_912, 0)), new PlannedUpgrade(BuildingType.MetalMine, 26, new(1_321_888, 330_472, 0))],
            plan.Upgrades);
    }

    [Fact]
    public void Builds_the_solar_plant_first_when_the_next_mine_lacks_energy()
    {
        var decision = BuildPlanner.NextUpgrade(State(energy: 0), Strategy(5, rules: [Relative(BuildingType.MetalMine, 0)]), requireAffordable: false);

        Assert.Equal(new PlannedUpgrade(BuildingType.SolarPlant, 1, new(100, 40, 0)), decision.Upgrade);
    }

    [Fact]
    public void Does_not_build_the_solar_plant_while_energy_is_sufficient()
    {
        var state = State(new() { [BuildingType.SolarPlant] = 1 }, energy: 22);

        var decision = BuildPlanner.NextUpgrade(state, Strategy(5, rules: [Relative(BuildingType.MetalMine, 0)]), requireAffordable: false);

        Assert.Equal(BuildingType.MetalMine, decision.Upgrade?.Building);
    }

    [Fact]
    public void Solar_plant_stops_at_its_max_level_and_the_mine_is_flagged()
    {
        var state = State(new() { [BuildingType.SolarPlant] = 1, [BuildingType.MetalMine] = 1 }, energy: 11);

        var decision = BuildPlanner.NextUpgrade(state, Strategy(5, solarMax: 1, rules: [Relative(BuildingType.MetalMine, 0)]), requireAffordable: false);

        Assert.Equal(BuildingType.MetalMine, decision.Upgrade?.Building);
        Assert.True(decision.Upgrade?.EnergyDeficit);
    }

    [Fact]
    public void Energy_check_starts_from_the_games_balance_not_the_formulas()
    {
        // metal mine 24 → 25 uses 345 more energy (formula difference); bonuses are already in the game's balance
        var strategy = Strategy(25, rules: [Relative(BuildingType.MetalMine, 0)]);
        var levels = new Dictionary<BuildingType, int> { [BuildingType.MetalMine] = 24, [BuildingType.SolarPlant] = 7 };

        var enough = BuildPlanner.NextUpgrade(State(levels, energy: 345), strategy, requireAffordable: false);
        var short1 = BuildPlanner.NextUpgrade(State(levels, energy: 344), strategy, requireAffordable: false);

        Assert.Equal(BuildingType.MetalMine, enough.Upgrade?.Building);
        Assert.Equal(new PlannedUpgrade(BuildingType.SolarPlant, 8, BuildingCatalog.CostOf(BuildingType.SolarPlant, 8)), short1.Upgrade);
    }

    [Fact]
    public void Queued_mines_count_against_energy()
    {
        // 1 → 2 uses 14 more (queued), 2 → 3 uses 15 more; with 20 spare, metal 3 needs a solar plant first
        var state = State(new() { [BuildingType.MetalMine] = 1, [BuildingType.SolarPlant] = 1 }, energy: 20,
            queue: [new QueuedBuilding(BuildingType.MetalMine, 2, Started: false)]);

        var decision = BuildPlanner.NextUpgrade(state, Strategy(5, rules: [Relative(BuildingType.MetalMine, 0)]), requireAffordable: false);

        Assert.Equal(BuildingType.SolarPlant, decision.Upgrade?.Building);
    }

    [Fact]
    public void Solar_plant_goes_first_when_energy_is_already_short_even_if_something_else_is_cheaper()
    {
        var strategy = Strategy(10, rules: [Relative(BuildingType.MetalMine, 0), new BuildingRule(BuildingType.Shipyard, true, RuleMode.Absolute, 5)]);
        var state = State(new() { [BuildingType.MetalMine] = 5, [BuildingType.SolarPlant] = 3 }, energy: -10,
            pageCosts: new() { [BuildingType.Shipyard] = new(10, 0, 0) });

        var decision = BuildPlanner.NextUpgrade(state, strategy, requireAffordable: false);

        Assert.Equal(BuildingType.SolarPlant, decision.Upgrade?.Building);
        Assert.Equal(4, decision.Upgrade?.Level);
    }

    [Fact]
    public void Short_of_energy_and_no_money_for_the_solar_plant_builds_nothing_that_uses_energy()
    {
        var strategy = Strategy(10, rules: [Relative(BuildingType.MetalMine, 0), new BuildingRule(BuildingType.Shipyard, true, RuleMode.Absolute, 5)]);
        var state = State(new() { [BuildingType.MetalMine] = 5, [BuildingType.SolarPlant] = 3 }, energy: -10, resources: new(50, 50, 0),
            pageCosts: new()
            {
                [BuildingType.SolarPlant] = new(1_000, 400, 0),
                [BuildingType.MetalMine] = new(40, 10, 0),
                [BuildingType.Shipyard] = new(45, 0, 0)
            });

        var decision = BuildPlanner.NextUpgrade(state, strategy, requireAffordable: true);

        // the mine is affordable but would make things worse; the shipyard uses no energy
        Assert.Equal(BuildingType.Shipyard, decision.Upgrade?.Building);
    }

    private static readonly ResourceSet Capacity = new(1_000_000, 1_000_000, 1_000_000);

    [Fact]
    public void Storage_goes_first_when_its_resource_is_at_the_fill_threshold()
    {
        var state = State(new() { [BuildingType.MetalStorage] = 3 }, resources: new(700_000, 100_000, 100_000)) with { StorageCapacity = Capacity };

        var decision = BuildPlanner.NextUpgrade(state, MetalAndCrystal, requireAffordable: true);

        Assert.Equal(BuildingType.MetalStorage, decision.Upgrade?.Building);
        Assert.Equal(4, decision.Upgrade?.Level);
    }

    [Fact]
    public void No_storage_below_the_threshold_or_when_turned_off()
    {
        var state = State(resources: new(699_999, 100_000, 100_000)) with { StorageCapacity = Capacity };
        var full = State(resources: new(1_000_000, 100_000, 100_000)) with { StorageCapacity = Capacity };

        Assert.Equal(BuildingType.MetalMine, BuildPlanner.NextUpgrade(state, MetalAndCrystal, requireAffordable: true).Upgrade?.Building);
        Assert.Equal(BuildingType.MetalMine, BuildPlanner.NextUpgrade(full, MetalAndCrystal with { StorageFillPercent = 0 }, requireAffordable: true).Upgrade?.Building);
    }

    [Fact]
    public void Only_one_level_of_a_storage_while_one_is_queued()
    {
        var state = State(new() { [BuildingType.MetalStorage] = 3 }, resources: new(900_000, 100_000, 100_000),
            queue: [new QueuedBuilding(BuildingType.MetalStorage, 4, Started: true)]) with { StorageCapacity = Capacity };

        Assert.Equal(BuildingType.MetalMine, BuildPlanner.NextUpgrade(state, MetalAndCrystal, requireAffordable: true).Upgrade?.Building);
    }

    [Fact]
    public void Plan_takes_each_full_storage_once_fullest_first()
    {
        var state = State(resources: new(800_000, 950_000, 100_000)) with { StorageCapacity = Capacity };

        var plan = BuildPlanner.Plan(state, MetalAndCrystal, maxItems: 4, requireAffordable: false);

        Assert.Equal(
            [BuildingType.CrystalStorage, BuildingType.MetalStorage, BuildingType.MetalMine, BuildingType.CrystalMine],
            plan.Upgrades.Select(u => u.Building));
    }

    [Fact]
    public void Energy_shortage_still_goes_before_storages()
    {
        var state = State(new() { [BuildingType.MetalMine] = 5, [BuildingType.SolarPlant] = 3 }, energy: -10,
            resources: new(900_000, 100_000, 100_000)) with { StorageCapacity = Capacity };

        var plan = BuildPlanner.Plan(state, MetalAndCrystal, maxItems: 2, requireAffordable: false);

        Assert.Equal([BuildingType.SolarPlant, BuildingType.MetalStorage], plan.Upgrades.Select(u => u.Building));
    }

    [Fact]
    public void Plan_stops_at_the_free_planet_fields()
    {
        var plan = BuildPlanner.Plan(State() with { FreeFields = 2 }, Strategy(20, rules: [Relative(BuildingType.MetalMine, 0)]), maxItems: 14, requireAffordable: false);
        var none = BuildPlanner.NextUpgrade(State() with { FreeFields = 0 }, MetalAndCrystal, requireAffordable: true);

        Assert.Equal([1, 2], plan.Upgrades.Select(u => u.Level));
        Assert.Equal(BuildStopReason.NoFreeFields, plan.Reason);
        Assert.Equal(BuildStopReason.NoFreeFields, none.Reason);
    }

    [Fact]
    public void Affordable_mode_skips_to_the_cheapest_upgrade_the_planet_can_pay()
    {
        // metal 1 (60/15, worth 82.5) is cheaper but needs more metal than the planet has; crystal 1 (48/24, worth 84) fits
        var costs = new Dictionary<BuildingType, ResourceSet> { [BuildingType.MetalMine] = new(60, 15, 0), [BuildingType.CrystalMine] = new(48, 24, 0) };

        var affordable = BuildPlanner.NextUpgrade(State(resources: new(50, 100, 0), pageCosts: costs), MetalAndCrystal, requireAffordable: true);
        var plain = BuildPlanner.NextUpgrade(State(resources: new(50, 100, 0), pageCosts: costs), MetalAndCrystal, requireAffordable: false);

        Assert.Equal(BuildingType.CrystalMine, affordable.Upgrade?.Building);
        Assert.Equal(BuildingType.MetalMine, plain.Upgrade?.Building);
    }

    [Fact]
    public void Affordable_mode_reports_missing_resources_when_nothing_fits()
    {
        var decision = BuildPlanner.NextUpgrade(State(resources: new(40, 100, 0)), MetalAndCrystal, requireAffordable: true);

        Assert.Equal(BuildStopReason.NotEnoughResources, decision.Reason);
    }

    [Fact]
    public void Affordability_checks_current_resources_regardless_of_queued_items()
    {
        var strategy = Strategy(10, rules: [Relative(BuildingType.MetalMine, 0)]);
        // metal 1 queued (not paid yet); the page shows the cost of metal 2
        var queue = new[] { new QueuedBuilding(BuildingType.MetalMine, 1, Started: false) };
        var costs = new Dictionary<BuildingType, ResourceSet> { [BuildingType.MetalMine] = new(90, 22, 0) };

        var enough = BuildPlanner.NextUpgrade(State(resources: new(90, 22, 0), queue: queue, pageCosts: costs), strategy, requireAffordable: true);
        var tooLittle = BuildPlanner.NextUpgrade(State(resources: new(89, 22, 0), queue: queue, pageCosts: costs), strategy, requireAffordable: true);

        Assert.Equal(new PlannedUpgrade(BuildingType.MetalMine, 2, new(90, 22, 0)), enough.Upgrade);
        Assert.Equal(BuildStopReason.NotEnoughResources, tooLittle.Reason);
    }

    [Fact]
    public void Plan_simulates_upgrades_in_order_until_targets_are_reached()
    {
        var plan = BuildPlanner.Plan(State(energy: 0), Strategy(1, rules: [Relative(BuildingType.MetalMine, 0), Relative(BuildingType.CrystalMine, 0)]), maxItems: 14, requireAffordable: false);

        Assert.Equal(
            [(BuildingType.SolarPlant, 1), (BuildingType.MetalMine, 1), (BuildingType.CrystalMine, 1)],
            plan.Upgrades.Select(p => (p.Building, p.Level)));
        Assert.Equal(BuildStopReason.TargetsReached, plan.Reason);
    }

    [Fact]
    public void Plan_is_limited_to_max_items()
    {
        var plan = BuildPlanner.Plan(State(), Strategy(20, rules: [Relative(BuildingType.MetalMine, 0)]), maxItems: 4, requireAffordable: false);

        Assert.Equal([1, 2, 3, 4], plan.Upgrades.Select(p => p.Level));
        Assert.Equal(BuildStopReason.QueueFull, plan.Reason);
    }

    [Fact]
    public void Affordable_plan_stops_when_the_next_upgrade_is_too_expensive()
    {
        // metal 1 = 60/15, 2 = 87/22, 3 = 126/32
        var plan = BuildPlanner.Plan(State(resources: new(100, 30, 0)), Strategy(10, rules: [Relative(BuildingType.MetalMine, 0)]), maxItems: 14, requireAffordable: true);

        Assert.Equal([1, 2], plan.Upgrades.Select(p => p.Level));
        Assert.Equal(BuildStopReason.NotEnoughResources, plan.Reason);
    }
}
