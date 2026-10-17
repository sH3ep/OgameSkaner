using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Services.Optimizer;

namespace OgameSkaner.Core.Tests.Services;

public class BuildSimulatorTests
{
    private static readonly GameModel Model = new(AccountBonuses.Calibration, new PlanetConditions(8, 28, 32));

    private static BuildSimulator Simulator(OptimizerSetup setup) => new(Model, setup with { PlanetSlot = 8, MinTemperature = 28, MaxTemperature = 32 });

    private static OptimizerSetup Targets(int metal, int crystal = 0, int deuterium = 0) => new()
    {
        MetalMine = metal,
        CrystalMine = crystal,
        DeuteriumSynthesizer = deuterium,
        StartResources = ResourceSet.Zero
    };

    [Fact]
    public void Waits_for_resources_then_builds()
    {
        // Metal Mine 1 costs 80/20; base income is 25 metal and 12.5 crystal per second; it builds in 1 s.
        var seconds = Simulator(Targets(1)).Evaluate([Gene.MetalMine]);

        Assert.Equal(80 / 25.0 + 1, seconds, 6);
    }

    [Fact]
    public void Mines_without_energy_produce_only_the_base_income()
    {
        // After Metal Mine 1 (at 4.2 s, 25 metal left over from the build second) Metal Mine 2 needs 118 metal.
        var seconds = Simulator(Targets(2)).Evaluate([Gene.MetalMine, Gene.MetalMine]);

        Assert.Equal(4.2 + (118 - 25) / 25.0 + 1, seconds, 6);
    }

    [Fact]
    public void A_solar_plant_makes_the_mines_produce()
    {
        var withoutEnergy = Simulator(Targets(8)).Evaluate(Enumerable.Repeat(Gene.MetalMine, 8).ToArray());
        var withEnergy = Simulator(Targets(8)).Evaluate([Gene.SolarPlant, Gene.SolarPlant, .. Enumerable.Repeat(Gene.MetalMine, 8)]);

        Assert.True(withEnergy < withoutEnergy, $"{withEnergy} vs {withoutEnergy}");
    }

    [Fact]
    public void Robotics_shortens_the_following_constructions()
    {
        var setup = Targets(16) with { StartResources = new ResourceSet(10_000_000, 10_000_000, 1_000_000) };
        var (_, steps) = Simulator(setup).Trace([Gene.RoboticsFactory, Gene.RoboticsFactory, .. Enumerable.Repeat(Gene.MetalMine, 16)]);

        var last = steps[^1];
        Assert.Equal((Gene.MetalMine, 16), (last.Gene, last.Level));
        Assert.Equal(Model.Seconds(Gene.MetalMine, 16, robotics: 2, nanite: 0), last.FinishSeconds - last.StartSeconds, 6);
    }

    [Fact]
    public void A_cost_above_the_capacity_adds_a_storage_first()
    {
        // Metal Mine 33 costs 18.2M metal; the planet stores 11M without a metal storage.
        var setup = Targets(33) with { StartResources = new ResourceSet(10_000_000, 10_000_000, 0), SolarMaxLevel = 40 };
        var (seconds, steps) = Simulator(setup).Trace([.. Enumerable.Repeat(Gene.SolarPlant, 30), .. Enumerable.Repeat(Gene.MetalMine, 33)]);

        Assert.False(double.IsPositiveInfinity(seconds));
        var storage = steps.FindIndex(s => s.Gene == Gene.MetalStorage);
        Assert.True(storage >= 0 && steps[storage].Forced);
        Assert.True(storage < steps.FindIndex(s => s is { Gene: Gene.MetalMine, Level: 33 }));
    }

    [Fact]
    public void Satellites_need_robotics_and_shipyard_3_and_power_the_next_mine()
    {
        var setup = Targets(1) with { StartResources = new ResourceSet(100_000, 100_000, 100_000) };
        Gene[] tooEarly = [Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];
        Gene[] ready = [Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];

        var (_, skipped) = Simulator(setup).Trace(tooEarly);
        var (_, ordered) = Simulator(setup).Trace(ready);

        Assert.DoesNotContain(skipped, s => s.IsSatellites);
        var satellites = Assert.Single(ordered, s => s.IsSatellites);
        Assert.Equal(1, satellites.Level);
        Assert.Equal(Model.SatelliteSeconds(3), satellites.FinishSeconds - satellites.StartSeconds, 6);
    }

    [Fact]
    public void Satellites_are_not_ordered_without_an_energy_shortage()
    {
        var setup = Targets(1) with { StartResources = new ResourceSet(100_000, 100_000, 100_000) };
        Gene[] genes = [Gene.SolarPlant, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];

        var (_, steps) = Simulator(setup).Trace(genes);

        Assert.DoesNotContain(steps, s => s.IsSatellites);
    }

    [Fact]
    public void An_order_that_never_gets_the_resources_is_infinite()
    {
        // Robotics needs deuterium; there is none and no synthesizer.
        var seconds = Simulator(Targets(1)).Evaluate([Gene.RoboticsFactory, Gene.MetalMine]);

        Assert.True(double.IsPositiveInfinity(seconds));
    }

    [Fact]
    public void An_order_missing_target_levels_is_infinite()
    {
        Assert.True(double.IsPositiveInfinity(Simulator(Targets(3)).Evaluate([Gene.MetalMine, Gene.MetalMine])));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, true)] // the Builder's rules change which mine a gene builds and insert levels; resuming must still match
    public void Resuming_from_a_checkpoint_gives_exactly_the_full_result(int mineBalancePercent, bool builderRules)
    {
        // Satellites and factories in the order, so look-ahead (satellites size their order for the next mine) matters.
        var setup = new OptimizerSetup
        {
            MetalMine = 30, CrystalMine = 27, DeuteriumSynthesizer = 25, SolarMaxLevel = 12, RoboticsMaxLevel = 12,
            NaniteMaxLevel = 2, ShipyardMaxLevel = 8, PlanetSlot = 8, MinTemperature = 28, MaxTemperature = 32,
            StartResources = new ResourceSet(50_000, 50_000, 50_000),
            MineBalancePercent = mineBalancePercent,
            EarlyRoboticsLevel = builderRules ? 6 : 0,
            KeepMinesPowered = builderRules
        };
        // A weak account (no bonuses, slow economy): resources and energy limit the mines, so every satellite counts.
        var simulator = new BuildSimulator(new GameModel(new AccountBonuses { EconomySpeed = 5 }, setup.Planet), setup);
        var random = new Random(42);
        Gene[] pool = [Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer, Gene.SolarPlant, Gene.RoboticsFactory,
            Gene.NaniteFactory, Gene.Shipyard, Gene.SolarSatellites, Gene.SolarSatellites, Gene.MetalStorage];
        var genes = new List<Gene>();
        foreach (var (mine, count) in new[] { (Gene.MetalMine, 30), (Gene.CrystalMine, 27), (Gene.DeuteriumSynthesizer, 25) })
            for (var i = 0; i < count; i++)
                genes.Insert(random.Next(genes.Count + 1), mine);
        for (var i = 0; i < 40; i++)
            genes.Insert(random.Next(genes.Count + 1), Gene.SolarSatellites);
        genes.InsertRange(0, [.. Enumerable.Repeat(Gene.RoboticsFactory, 4), .. Enumerable.Repeat(Gene.Shipyard, 4)]);

        var current = genes.ToArray();
        var currentTrace = new EvaluationTrace();
        var scratch = new EvaluationTrace();
        Assert.Equal(simulator.Evaluate(current), simulator.Evaluate(current, 0, scratch, currentTrace));
        var (seconds, steps) = simulator.Trace(current);
        Assert.False(double.IsPositiveInfinity(seconds));
        Assert.True(steps.Count(s => s.IsSatellites) > 10, "the order should use satellites, so look-ahead matters");

        var results = new HashSet<double>();
        for (var round = 0; round < 3000; round++)
        {
            var candidate = current.ToList();
            int changed;
            switch (random.Next(3))
            {
                case 0: // move
                    var from = random.Next(candidate.Count);
                    var to = random.Next(candidate.Count);
                    var gene = candidate[from];
                    candidate.RemoveAt(from);
                    candidate.Insert(to, gene);
                    changed = Math.Min(from, to);
                    break;
                case 1: // insert a helper
                    changed = random.Next(candidate.Count + 1);
                    candidate.Insert(changed, pool[random.Next(3, pool.Length)]);
                    break;
                default: // remove a helper
                    changed = candidate.FindIndex(random.Next(candidate.Count), g => g > Gene.DeuteriumSynthesizer);
                    if (changed < 0)
                        continue;
                    candidate.RemoveAt(changed);
                    break;
            }

            var genesNow = candidate.ToArray();
            var full = simulator.Evaluate(genesNow);
            var resumed = simulator.Evaluate(genesNow, changed, currentTrace, scratch);
            Assert.Equal(full, resumed);
            results.Add(full);

            if (random.Next(2) == 0)
            {
                current = genesNow;
                (currentTrace, scratch) = (scratch, currentTrace);
            }
        }
        Assert.True(results.Count > 100, "the mutations should change the result");
    }

    [Fact]
    public void Genes_above_the_max_level_are_skipped()
    {
        var setup = Targets(1) with { RoboticsMaxLevel = 1, StartResources = new ResourceSet(10_000, 10_000, 10_000) };

        var (_, steps) = Simulator(setup).Trace([Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.MetalMine]);

        Assert.Equal([Gene.RoboticsFactory, Gene.MetalMine], steps.Select(s => s.Gene));
    }

    private static OptimizerSetup FromPlanet(OptimizerSetup setup, PlanetStart start) => setup with { Start = start };

    /// <summary>Metal 50, Crystal 35, Deuterium 40 with the Builder's targets for reference level 50 (+2 / 0 / −6).</summary>
    private static OptimizerSetup Unbalanced(int mineBalancePercent) => FromPlanet(
        Targets(52, 50, 44) with { MineBalancePercent = mineBalancePercent, SolarMaxLevel = 60, RoboticsMaxLevel = 0, UseSatellites = false },
        new PlanetStart
        {
            Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 50, [Gene.CrystalMine] = 35, [Gene.DeuteriumSynthesizer] = 40, [Gene.SolarPlant] = 45 },
            Resources = new ResourceSet(1_000_000_000_000, 1_000_000_000_000, 1_000_000_000_000)
        });

    /// <summary>Metal genes first, then crystal, then deuterium: the order the balance has to correct.</summary>
    private static Gene[] MetalFirst(OptimizerSetup setup) =>
    [
        .. Enumerable.Repeat(Gene.MetalMine, setup.MetalMine - setup.Start!.Levels[Gene.MetalMine]),
        .. Enumerable.Repeat(Gene.CrystalMine, setup.CrystalMine - setup.Start.Levels[Gene.CrystalMine]),
        .. Enumerable.Repeat(Gene.DeuteriumSynthesizer, setup.DeuteriumSynthesizer - setup.Start.Levels[Gene.DeuteriumSynthesizer])
    ];

    private static double NextMineCost(Gene mine, int level) =>
        Model.MetalCost(mine, level + 1) + Model.CrystalCost(mine, level + 1) + Model.DeuteriumCost(mine, level + 1);

    [Fact]
    public void The_storage_rule_doesnt_raise_storages_its_resource_refills_while_they_are_built()
    {
        // High mines, empty storages: every resource fills its storage long before the next one (each slower than the
        // last) is done. Raising them one after another used to run up to the model's highest level.
        var setup = Targets(33, 31, 26) with
        {
            SolarMaxLevel = 40, StorageFillPercent = 70,
            Start = new PlanetStart
            {
                Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 30, [Gene.CrystalMine] = 28, [Gene.DeuteriumSynthesizer] = 23, [Gene.SolarPlant] = 35 }
            }
        };
        Gene[] genes = [Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer, Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer,
            Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer];

        var (withRule, steps) = Simulator(setup).Trace(genes);
        var withoutRule = Simulator(setup with { StorageFillPercent = 0 }).Evaluate(genes);

        Assert.True(withRule <= withoutRule * 1.5, $"{withRule:N0} s with the rule, {withoutRule:N0} s without");
        Assert.All(steps.Where(s => s.Gene is Gene.MetalStorage or Gene.CrystalStorage or Gene.DeuteriumTank), s => Assert.True(s.Level <= 15, $"{s}"));
    }

    [Fact]
    public void Robotics_gets_its_early_level_before_a_mine_passes_the_mine_level()
    {
        var setup = Targets(12, 11, 8) with { EarlyRoboticsLevel = 6, EarlyRoboticsMineLevel = 10, StartResources = new ResourceSet(10_000_000, 10_000_000, 10_000_000) };
        Gene[] mines =
        [
            .. Enumerable.Repeat(Gene.MetalMine, 12), .. Enumerable.Repeat(Gene.CrystalMine, 11), .. Enumerable.Repeat(Gene.DeuteriumSynthesizer, 8)
        ];

        var (_, steps) = Simulator(setup).Trace(mines);

        var robotics6 = steps.Single(s => s.Gene == Gene.RoboticsFactory && s.Level == 6);
        Assert.True(robotics6.Forced);
        Assert.All(steps.Where(s => s.Gene <= Gene.DeuteriumSynthesizer && s.Level > 10), s => Assert.True(s.StartSeconds >= robotics6.FinishSeconds));
        Assert.Contains(steps, s => s.Gene == Gene.MetalMine && s.Level == 10 && s.FinishSeconds <= robotics6.StartSeconds); // mines may reach 10 first
        Assert.DoesNotContain(steps, s => s.Gene == Gene.RoboticsFactory && s.Level > 6); // only what the rule needs
    }

    [Fact]
    public void Kept_powered_every_mine_starts_with_enough_energy()
    {
        // Solar plant up to 5, then satellites: the order has no energy genes at all.
        var setup = Targets(16, 14, 10) with
        {
            KeepMinesPowered = true, SolarMaxLevel = 5, RoboticsMaxLevel = 3, ShipyardMaxLevel = 3, UseSatellites = true,
            StartResources = new ResourceSet(10_000_000, 10_000_000, 10_000_000)
        };
        Gene[] mines =
        [
            .. Enumerable.Repeat(Gene.MetalMine, 16), .. Enumerable.Repeat(Gene.CrystalMine, 14), .. Enumerable.Repeat(Gene.DeuteriumSynthesizer, 10)
        ];

        var (seconds, steps) = Simulator(setup).Trace(mines);

        Assert.False(double.IsPositiveInfinity(seconds));
        int solar = 0, satellites = 0, metal = 0, crystal = 0, deuterium = 0;
        foreach (var step in steps)
        {
            switch (step.Gene)
            {
                case Gene.SolarPlant: solar = step.Level; break;
                case Gene.SolarSatellites: satellites += step.Level; break;
                case Gene.MetalMine: metal = step.Level; break;
                case Gene.CrystalMine: crystal = step.Level; break;
                case Gene.DeuteriumSynthesizer: deuterium = step.Level; break;
                default: continue;
            }
            if (step.Gene > Gene.DeuteriumSynthesizer)
                continue;

            // Ordered satellites count from the order on, like the game's energy once they fly.
            var energy = Model.SolarEnergy[solar] + satellites * Model.SatelliteEnergy
                         - Model.MetalConsumption[metal] - Model.CrystalConsumption[crystal] - Model.DeuteriumConsumption[deuterium];
            Assert.True(energy >= 0, $"{step} started with energy {energy:N0}");
        }

        Assert.Equal(5, steps.Where(s => s.Gene == Gene.SolarPlant).Max(s => s.Level));
        Assert.True(steps.FindIndex(s => s.Gene == Gene.SolarPlant && s.Level == 5) < steps.FindIndex(s => s.IsSatellites), "the solar plant goes first");
        Assert.Contains(steps, s => s.Gene == Gene.Shipyard && s.Level == 3 && s.Forced);
    }

    [Fact]
    public void Satellites_are_ordered_at_least_the_minimum_at_a_time()
    {
        var setup = Targets(16, 14, 10) with
        {
            KeepMinesPowered = true, SolarMaxLevel = 5, RoboticsMaxLevel = 3, ShipyardMaxLevel = 3, UseSatellites = true,
            StartResources = new ResourceSet(10_000_000, 10_000_000, 10_000_000)
        };
        Gene[] mines =
        [
            .. Enumerable.Repeat(Gene.MetalMine, 16), .. Enumerable.Repeat(Gene.CrystalMine, 14), .. Enumerable.Repeat(Gene.DeuteriumSynthesizer, 10)
        ];

        var (_, exact) = Simulator(setup).Trace(mines);
        var (_, batched) = Simulator(setup with { MinSatelliteOrder = 30 }).Trace(mines);

        Assert.Contains(exact, s => s.IsSatellites && s.Level < 30); // without the minimum: small orders
        Assert.NotEmpty(batched.Where(s => s.IsSatellites));
        Assert.All(batched.Where(s => s.IsSatellites), s => Assert.True(s.Level >= 30, $"{s}"));
        Assert.True(batched.Count(s => s.IsSatellites) < exact.Count(s => s.IsSatellites));
    }

    [Fact]
    public void Without_the_mine_balance_genes_build_their_own_mine()
    {
        var setup = Unbalanced(0);

        var (_, steps) = Simulator(setup).Trace(MetalFirst(setup));

        Assert.Equal([Gene.MetalMine, Gene.MetalMine, Gene.CrystalMine], steps.Where(s => !s.Forced).Take(3).Select(s => s.Gene));
    }

    [Fact]
    public void The_lowest_mine_goes_first_while_it_costs_under_the_share_of_the_dearest()
    {
        var setup = Unbalanced(30);

        var (seconds, steps) = Simulator(setup).Trace(MetalFirst(setup));

        Assert.False(double.IsPositiveInfinity(seconds)); // every target is still reached
        var levels = setup.Start!.Levels.ToDictionary(l => l.Key, l => l.Value);
        var targets = new Dictionary<Gene, int> { [Gene.MetalMine] = 52, [Gene.CrystalMine] = 50, [Gene.DeuteriumSynthesizer] = 44 };
        foreach (var step in steps.Where(s => s.Gene <= Gene.DeuteriumSynthesizer))
        {
            // Before every mine step: if the lowest mine below its target was cheap enough, this step built it.
            var open = targets.Keys.Where(m => levels[m] < targets[m]).ToList();
            var lowest = open.OrderBy(m => levels[m]).ThenBy(m => NextMineCost(m, levels[m])).First();
            var dearest = open.Max(m => NextMineCost(m, levels[m]));
            if (NextMineCost(lowest, levels[lowest]) < 0.3 * dearest)
                Assert.Equal(lowest, step.Gene);
            levels[step.Gene] = step.Level;
        }

        Assert.Equal(targets, levels.Where(l => l.Key <= Gene.DeuteriumSynthesizer).ToDictionary(l => l.Key, l => l.Value));
        Assert.Equal(Gene.CrystalMine, steps.First(s => s.Gene <= Gene.DeuteriumSynthesizer).Gene); // Crystal 35 catches up first
    }

    [Fact]
    public void A_real_planet_finishes_its_queue_first()
    {
        var start = new PlanetStart
        {
            Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 5 },
            Queue = [new StartQueueItem(Gene.MetalMine, 6, ResourceSet.Zero, Seconds: 100)]
        };

        var simulator = Simulator(FromPlanet(Targets(6), start));

        Assert.True(simulator.TargetsReached);
        Assert.Equal(100, simulator.Evaluate([]), 6);
    }

    [Fact]
    public void Waiting_queue_items_are_paid_when_they_start()
    {
        // Running Metal Mine 6 has 10 s left (250 metal, 125 crystal of base income by then); Metal Mine 7 waits for its cost.
        var start = new PlanetStart
        {
            Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 5 },
            Queue =
            [
                new StartQueueItem(Gene.MetalMine, 6, ResourceSet.Zero, Seconds: 10),
                new StartQueueItem(Gene.MetalMine, 7, ResourceSet.Zero, Seconds: null)
            ]
        };
        var (metal, crystal, _) = (Model.MetalCost(Gene.MetalMine, 7), Model.CrystalCost(Gene.MetalMine, 7), 0);
        var wait = Math.Max((metal - 250) / 25.0, (crystal - 125) / 12.5);

        var seconds = Simulator(FromPlanet(Targets(7), start)).Evaluate([]);

        Assert.Equal(10 + wait + Model.Seconds(Gene.MetalMine, 7, 0, 0), seconds, 6);
    }

    [Fact]
    public void Planet_corrections_and_extra_energy_change_production()
    {
        PlanetStart Start(double correction, double extraEnergy) => new()
        {
            Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 15 },
            Resources = new ResourceSet(0, 1_000_000, 0), // only metal limits the next level
            ExtraEnergy = extraEnergy,
            MetalCorrection = correction
        };

        var withoutEnergy = Simulator(FromPlanet(Targets(16), Start(1, 0))).Evaluate([Gene.MetalMine]);
        var powered = Simulator(FromPlanet(Targets(16), Start(1, 100_000))).Evaluate([Gene.MetalMine]);
        var doubled = Simulator(FromPlanet(Targets(16), Start(2, 100_000))).Evaluate([Gene.MetalMine]);

        Assert.True(powered < withoutEnergy, $"{powered} vs {withoutEnergy}");
        Assert.True(doubled < powered, $"{doubled} vs {powered}");
    }

    [Fact]
    public void Factory_targets_must_be_reached_too()
    {
        var setup = Targets(1) with { RoboticsTarget = 2, StartResources = new ResourceSet(100_000, 100_000, 100_000) };

        Assert.True(double.IsPositiveInfinity(Simulator(setup).Evaluate([Gene.MetalMine, Gene.RoboticsFactory])));
        Assert.False(double.IsPositiveInfinity(Simulator(setup).Evaluate([Gene.MetalMine, Gene.RoboticsFactory, Gene.RoboticsFactory])));
    }

    [Fact]
    public void Covering_energy_makes_the_order_finish_with_powered_mines()
    {
        var setup = Targets(5) with { CoverEnergy = true, UseSatellites = false, StartResources = new ResourceSet(100_000, 100_000, 0) };
        Gene[] unpowered = [.. Enumerable.Repeat(Gene.MetalMine, 5)];
        Gene[] powered = [.. unpowered, Gene.SolarPlant, Gene.SolarPlant, Gene.SolarPlant];

        Assert.True(double.IsPositiveInfinity(Simulator(setup).Evaluate(unpowered)));
        var (seconds, steps) = Simulator(setup).Trace(powered);
        Assert.False(double.IsPositiveInfinity(seconds));
        Assert.Equal(steps[^1].FinishSeconds, seconds);
        // Without the requirement the mines alone are enough.
        Assert.False(double.IsPositiveInfinity(Simulator(setup with { CoverEnergy = false }).Evaluate(unpowered)));
    }

    [Fact]
    public void A_final_satellite_gene_covers_the_mines_and_its_time_counts()
    {
        var setup = Targets(5) with
        {
            CoverEnergy = true, SolarMaxLevel = 0, RoboticsMaxLevel = 3, StartResources = new ResourceSet(1_000_000, 1_000_000, 1_000_000)
        };
        Gene[] genes = [Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.Shipyard,
            .. Enumerable.Repeat(Gene.MetalMine, 5), Gene.SolarSatellites];

        var (seconds, steps) = Simulator(setup).Trace(genes);

        var satellites = Assert.Single(steps, s => s.IsSatellites);
        Assert.Equal(satellites.FinishSeconds, seconds, 6);
        Assert.True(satellites.Level * Model.SatelliteEnergy >= Model.MetalConsumption[5]);
    }

    [Fact]
    public void A_storage_goes_first_when_an_item_would_leave_its_resource_nearly_full()
    {
        // 8M of 11M metal (73%); Metal Mine 1 costs 80 metal, so the metal storage comes first at 70% but not at 80%.
        var setup = Targets(1) with { StartResources = new ResourceSet(8_000_000, 0, 0) };

        var (_, at70) = Simulator(setup with { StorageFillPercent = 70 }).Trace([Gene.MetalMine]);
        var (_, at80) = Simulator(setup with { StorageFillPercent = 80 }).Trace([Gene.MetalMine]);

        Assert.Equal([(Gene.MetalStorage, 1, true), (Gene.MetalMine, 1, false)], at70.Select(s => (s.Gene, s.Level, s.Forced)));
        Assert.Equal([Gene.MetalMine], at80.Select(s => s.Gene));
    }

    [Fact]
    public void The_storage_rule_doesnt_change_the_real_queue()
    {
        var start = new PlanetStart
        {
            Resources = new ResourceSet(10_000_000, 0, 0),
            Queue = [new StartQueueItem(Gene.MetalMine, 1, ResourceSet.Zero, Seconds: 5)]
        };
        var setup = Targets(2) with { StorageFillPercent = 70, Start = start };

        var (_, steps) = Simulator(setup).Trace([Gene.MetalMine]);

        // The queue finished without a storage; the plan's first item gets one.
        Assert.Equal([Gene.MetalStorage, Gene.MetalMine], steps.Select(s => s.Gene));
        Assert.Equal(2, steps[^1].Level);
    }

    private static PlanetStart ShortOfEnergy(ResourceSet resources, params StartQueueItem[] queue) => new()
    {
        Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 10, [Gene.RoboticsFactory] = 3, [Gene.Shipyard] = 3 },
        Resources = resources,
        Queue = queue
    };

    [Fact]
    public void Satellites_for_the_real_queue_are_ordered_at_once()
    {
        // No solar plant: Metal Mine 10 is short now, the queued Metal Mine 11 even more.
        var start = ShortOfEnergy(new ResourceSet(0, 1_000_000, 1_000_000), new StartQueueItem(Gene.MetalMine, 11, ResourceSet.Zero, Seconds: 5));
        var setup = Targets(12) with { CoverEnergy = true, Start = start };

        var (_, steps) = Simulator(setup).Trace([Gene.MetalMine]);

        var first = steps[0];
        Assert.True(first.IsSatellites);
        Assert.Equal(0, first.StartSeconds);
        Assert.Equal((int)Math.Ceiling(Model.MetalConsumption[11] / Model.SatelliteEnergy), first.Level);
    }

    [Fact]
    public void Satellites_for_the_queue_dont_take_what_waiting_items_need()
    {
        // 30,000 crystal, but the waiting Robotics Factory needs 20,000 of it: 5 satellites (10,000) are too many.
        var robotics = new StartQueueItem(Gene.RoboticsFactory, 4, new ResourceSet(0, 20_000, 0), Seconds: null);
        var start = ShortOfEnergy(new ResourceSet(0, 30_000, 1_000_000), new StartQueueItem(Gene.MetalMine, 11, ResourceSet.Zero, Seconds: 5), robotics);
        var setup = Targets(12) with { CoverEnergy = true, Start = start };

        var (_, steps) = Simulator(setup).Trace([Gene.MetalMine]);

        Assert.False(steps[0].IsSatellites && steps[0].StartSeconds == 0);
    }
}
