using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Services.Moons;

namespace OgameSkaner.Core.Tests.Services;

public class MoonPlannerTests
{
    private static readonly HashSet<BuildingType> SpeedAndFields = [BuildingType.MoonBase, BuildingType.RoboticsFactory, BuildingType.NaniteFactory];
    private static readonly HashSet<BuildingType> Others = [BuildingType.Shipyard, BuildingType.PhalanxSensor, BuildingType.JumpGate];

    /// <summary>The moon at 8:260:3 as its facility page showed it on 2026-10-19: nothing built, 1 free field.</summary>
    internal static PlanetBuildState FreshMoon(ResourceSet resources = default, IReadOnlyList<QueuedBuilding>? queue = null, string id = "moon") => new(
        new OwnPlanet(new PlanetId(id), "Moon", new Coordinates(8, 260, 3)),
        BuildingCatalog.MoonBuildings.ToDictionary(b => b, _ => 0),
        resources,
        queue ?? [])
    {
        FreeFields = 1,
        NextLevelCosts = new Dictionary<BuildingType, ResourceSet>
        {
            [BuildingType.MoonBase] = new(200_000, 400_000, 100_000),
            [BuildingType.RoboticsFactory] = new(400, 120, 200)
        },
        NextLevelDurations = new Dictionary<BuildingType, TimeSpan>
        {
            [BuildingType.MoonBase] = new(0, 31, 6),
            [BuildingType.RoboticsFactory] = TimeSpan.FromSeconds(1),
            [BuildingType.NaniteFactory] = new(1, 11, 6),
            [BuildingType.Shipyard] = TimeSpan.FromSeconds(1),
            [BuildingType.PhalanxSensor] = new(0, 35, 33),
            [BuildingType.JumpGate] = new(5, 55, 33)
        },
        Locked = new HashSet<BuildingType> { BuildingType.NaniteFactory, BuildingType.Shipyard, BuildingType.PhalanxSensor, BuildingType.JumpGate }
    };

    [Fact]
    public void Simulation_builds_mostly_moon_base_robots_and_nanite_first()
    {
        var steps = MoonPlanner.Simulate(FreshMoon(), MoonSettings.Default).Steps.ToList(); // Moon Base 8, Robot 10, Nanite 2, Shipyard 4, Phalanx 4, Jump Gate 1

        Assert.Equal(BuildingType.MoonBase, steps[0].Building); // one field: the Moon Base makes room first
        Assert.True(steps.Take(12).Count(s => SpeedAndFields.Contains(s.Building)) >= 10, string.Join(", ", steps.Take(12)));
        var lastRobot = steps.FindLastIndex(s => s.Building == BuildingType.RoboticsFactory);
        var firstOther = steps.FindIndex(s => Others.Contains(s.Building));
        Assert.True(lastRobot < firstOther, "every Robot Factory level comes before the Shipyard, Phalanx and Jump Gate");
        var robot10 = steps.FindIndex(s => s is { Building: BuildingType.RoboticsFactory, Level: 10 });
        var nanite1 = steps.FindIndex(s => s is { Building: BuildingType.NaniteFactory, Level: 1 });
        Assert.InRange(nanite1 - robot10, 1, 2); // right after (a Moon Base level may come between for a field)
        Assert.True(nanite1 < firstOther);
    }

    [Fact]
    public void Simulation_reaches_every_target_within_the_fields_and_requirements()
    {
        var plan = MoonPlanner.Simulate(FreshMoon(), MoonSettings.Default);

        Assert.Null(plan.Blocked);
        var levels = BuildingCatalog.MoonBuildings.ToDictionary(b => b, _ => 0);
        var fields = 1;
        foreach (var step in plan.Steps)
        {
            Assert.Equal(levels[step.Building] + 1, step.Level);
            if (BuildingCatalog.MoonRequirements.TryGetValue(step.Building, out var requirement))
                Assert.True(levels[requirement.Building] >= requirement.Level, $"{step} before {requirement.Building} {requirement.Level}");
            Assert.True(fields >= 1, $"{step} without a free field");
            fields += step.Building == BuildingType.MoonBase ? BuildingCatalog.FieldsPerMoonBaseLevel - 1 : -1;
            levels[step.Building] = step.Level;
        }

        Assert.All(MoonSettings.DefaultTargets, t => Assert.True(levels[t.Building] >= t.Level, $"{t} not reached"));
        Assert.True(fields >= 0);
        Assert.Equal(plan.Steps.Zip(plan.Steps.Skip(1)).Select(p => p.First.EndsIn), plan.Steps.Skip(1).Select(s => s.StartsIn)); // one after another
    }

    [Fact]
    public void Simulation_is_faster_than_building_the_targets_without_the_factories_first()
    {
        var simulated = MoonPlanner.Simulate(FreshMoon(), MoonSettings.Default);
        var naive = MoonPlanner.FromSchema(FreshMoon(),
        [
            new(BuildingType.MoonBase, 10), new(BuildingType.PhalanxSensor, 4), new(BuildingType.JumpGate, 1),
            new(BuildingType.RoboticsFactory, 10), new(BuildingType.Shipyard, 4), new(BuildingType.NaniteFactory, 2)
        ]);

        Assert.Null(naive.Blocked);
        Assert.True(simulated.Total * 2 < naive.Total, $"{simulated.Total} vs {naive.Total}");
    }

    [Fact]
    public void A_requirement_of_a_target_is_built_even_without_its_own_target()
    {
        var settings = MoonSettings.Default with { Targets = [new(BuildingType.NaniteFactory, 1)] };

        var steps = MoonPlanner.Simulate(FreshMoon(), settings).Steps;

        Assert.Equal(10, steps.Count(s => s.Building == BuildingType.RoboticsFactory));
        Assert.Equal(BuildingType.NaniteFactory, steps.Last().Building);
    }

    [Fact]
    public void Each_moon_base_level_costs_twice_the_one_before()
    {
        var steps = MoonPlanner.Simulate(FreshMoon(), MoonSettings.Default).Steps.Where(s => s.Building == BuildingType.MoonBase).ToList();

        Assert.Equal(new ResourceSet(200_000, 400_000, 100_000), steps[0].Cost);
        Assert.All(steps.Zip(steps.Skip(1)), p => Assert.Equal(p.First.Cost.Scale(2), p.Second.Cost));
        Assert.Equal(new ResourceSet(400_000, 800_000, 200_000), BuildingCatalog.CostOf(BuildingType.MoonBase, 2));
    }

    [Fact]
    public void The_queue_runs_first_and_the_plan_continues_after_it()
    {
        var moon = FreshMoon(queue: [new QueuedBuilding(BuildingType.MoonBase, 1, Started: true)]) with { RunningRemaining = TimeSpan.FromMinutes(10) };

        var plan = MoonPlanner.Simulate(moon, MoonSettings.Default);

        Assert.Equal(TimeSpan.FromMinutes(10), Assert.Single(plan.Queue).Duration);
        Assert.Equal(TimeSpan.FromMinutes(10), plan.Steps[0].StartsIn);
        Assert.DoesNotContain(plan.Steps, s => s is { Building: BuildingType.MoonBase, Level: 1 });
    }

    [Fact]
    public void A_schema_runs_in_its_order_and_stops_at_a_missing_requirement()
    {
        var plan = MoonPlanner.FromSchema(FreshMoon(),
            [new(BuildingType.MoonBase, 1), new(BuildingType.RoboticsFactory, 2), new(BuildingType.NaniteFactory, 1), new(BuildingType.Shipyard, 1)]);

        Assert.Equal(["Moon Base 1", "Robotics Factory 1", "Robotics Factory 2"], plan.Steps.Select(s => s.ToString()));
        Assert.Equal("Nanite Factory 1 needs Robotics Factory 10", plan.Blocked);
    }

    [Fact]
    public void A_schema_stops_when_no_field_is_left()
    {
        var plan = MoonPlanner.FromSchema(FreshMoon(), [new(BuildingType.RoboticsFactory, 2)]);

        Assert.Equal(["Robotics Factory 1"], plan.Steps.Select(s => s.ToString()));
        Assert.Contains("no free field", plan.Blocked);
    }

    [Fact]
    public void Only_what_the_moon_can_pay_on_top_of_the_waiting_items_is_queued()
    {
        var moon = FreshMoon(new ResourceSet(1_000_000, 1_000_000, 1_000_000));
        var plan = MoonPlanner.Simulate(moon, MoonSettings.Default);

        var push = MoonPlanner.ToQueue(plan, moon, maxDepth: 3);

        // Moon Base 1 (200k/400k/100k), Robot 1 and 2, then Moon Base 2 (400k/800k) doesn't fit any more.
        Assert.Equal(["Moon Base 1", "Robotics Factory 1", "Robotics Factory 2"], push.Select(s => s.ToString()));
        Assert.Single(MoonPlanner.ToQueue(plan, moon, maxDepth: 1));
    }

    [Fact]
    public void A_level_whose_requirement_is_only_queued_waits()
    {
        var moon = FreshMoon(new ResourceSet(10_000_000_000, 10_000_000_000, 10_000_000_000));
        var plan = MoonPlanner.FromSchema(moon, [new(BuildingType.MoonBase, 1), new(BuildingType.RoboticsFactory, 2), new(BuildingType.Shipyard, 1)]);

        var push = MoonPlanner.ToQueue(plan, moon, maxDepth: 10);

        Assert.Equal(["Moon Base 1", "Robotics Factory 1", "Robotics Factory 2"], push.Select(s => s.ToString())); // the Shipyard needs Robot 2 built
    }

    [Fact]
    public void A_shipment_covers_only_the_next_levels_minus_what_is_there_or_on_its_way()
    {
        var moon = FreshMoon(new ResourceSet(100_000, 0, 0));
        var plan = MoonPlanner.Simulate(moon, MoonSettings.Default);
        var next3 = plan.Steps.Take(3).Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cost);

        var shipment = MoonPlanner.Shipment(plan, moon, inFlight: new ResourceSet(0, 50_000, 0), TimeSpan.FromHours(6), levels: 3);

        Assert.Equal(next3 - new ResourceSet(100_000, 50_000, 0), shipment);
        Assert.Equal(ResourceSet.Zero, MoonPlanner.Shipment(plan, moon, inFlight: next3, TimeSpan.FromHours(6), levels: 3)); // covered
    }

    [Fact]
    public void A_shipment_stays_small_although_the_nanite_factory_makes_hours_of_the_plan_cost_hundreds_of_millions()
    {
        // The targets of the account where every cycle shipped what the planets had (2026-10-19).
        var settings = MoonSettings.Default with
        {
            Targets = [new(BuildingType.MoonBase, 20), new(BuildingType.RoboticsFactory, 20), new(BuildingType.NaniteFactory, 10), new(BuildingType.JumpGate, 1)]
        };
        var moon = FreshMoon();
        var plan = MoonPlanner.Simulate(moon, settings);
        var sixHours = plan.Steps.Where(s => s.StartsIn < TimeSpan.FromHours(6)).Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cost);

        var shipment = MoonPlanner.Shipment(plan, moon, ResourceSet.Zero, TimeSpan.FromHours(6), levels: 3);

        Assert.True(sixHours.Metal > 100_000_000, sixHours.ToString());
        Assert.Equal(plan.Steps.Take(3).Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cost), shipment); // Moon Base 1, Robot 1 and 2
    }

    [Fact]
    public void Moon_cost_and_graviton_follow_the_games_formulas()
    {
        Assert.Equal(17_311_270, MoonFormation.MetalCost(1_000));
        Assert.True(MoonFormation.MetalCost(2_000) > MoonFormation.MetalCost(1_000) * 10);
        Assert.Equal(0, MoonFormation.GravitonNeeded(1_799));
        Assert.Equal(1, MoonFormation.GravitonNeeded(1_800));
        Assert.Equal(4, MoonFormation.GravitonNeeded(4_199));
        Assert.Equal(5, MoonFormation.GravitonNeeded(4_200));
        Assert.Equal(4_199, MoonFormation.MaxDiameterFor(4));
        Assert.Equal(9_999, MoonFormation.MaxDiameterFor(20));
        Assert.Equal(4_199, MoonFormation.Clamp(8_000, 4));
        Assert.Equal(1_000, MoonFormation.Clamp(10, 4));
    }
}
