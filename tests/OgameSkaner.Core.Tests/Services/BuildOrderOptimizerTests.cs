using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Services.Optimizer;

namespace OgameSkaner.Core.Tests.Services;

public class BuildOrderOptimizerTests
{
    // Mines only, so every possible order can be listed.
    private static readonly OptimizerSetup Small = new()
    {
        MetalMine = 4,
        CrystalMine = 3,
        DeuteriumSynthesizer = 2,
        SolarMaxLevel = 0,
        RoboticsMaxLevel = 0,
        UseSatellites = false
    };

    [Fact]
    public async Task Finds_the_same_best_time_as_trying_every_order()
    {
        var simulator = new BuildSimulator(new GameModel(Small.Bonuses, Small.Planet), Small);
        var exhaustive = Orders(4, 3, 2).Min(order => simulator.Evaluate(order));

        var result = await new BuildOrderOptimizer().RunAsync(Small, new OptimizerOptions(Runs: 8, IterationsPerRun: 3_000, Threads: 4));

        Assert.NotNull(result);
        Assert.Equal(exhaustive, result.TotalSeconds, 6);
        Assert.Equal(9, result.Steps.Count(s => s.Gene <= Gene.DeuteriumSynthesizer));
    }

    [Fact]
    public async Task Storage_targets_alone_make_an_order_of_only_those_storages()
    {
        // The Builder with every rule unticked except two storages: mines stay where they are.
        var setup = new OptimizerSetup
        {
            MetalMine = 0, CrystalMine = 0, DeuteriumSynthesizer = 0, MetalStorageTarget = 3, DeuteriumTankTarget = 2,
            StartResources = new ResourceSet(1_000_000, 1_000_000, 0)
        };

        var result = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 2, IterationsPerRun: 500, Threads: 2));

        Assert.NotNull(result);
        Assert.Equal(
            [(Gene.MetalStorage, 1), (Gene.MetalStorage, 2), (Gene.MetalStorage, 3), (Gene.DeuteriumTank, 1), (Gene.DeuteriumTank, 2)],
            result.Steps.Select(s => (s.Gene, s.Level)).OrderBy(s => s.Gene).ThenBy(s => s.Level));
    }

    [Fact]
    public async Task Same_seed_gives_the_same_order()
    {
        var setup = new OptimizerSetup { MetalMine = 10, CrystalMine = 8, DeuteriumSynthesizer = 5 };
        var options = new OptimizerOptions(Runs: 6, IterationsPerRun: 1_000, Seed: 7);

        var first = await new BuildOrderOptimizer().RunAsync(setup, options);
        var second = await new BuildOrderOptimizer().RunAsync(setup, options);

        Assert.NotNull(first);
        Assert.Equal(first.TotalSeconds, second!.TotalSeconds);
        Assert.Equal(first.Steps, second.Steps);
    }

    [Fact]
    public async Task Uses_helpers_and_beats_its_own_greedy_start()
    {
        var setup = new OptimizerSetup { MetalMine = 22, CrystalMine = 19, DeuteriumSynthesizer = 14 };
        var simulator = new BuildSimulator(new GameModel(setup.Bonuses, setup.Planet), setup);
        var greedy = new BuildOrderOptimizer.RunSearch(simulator, new Random(1)).Greedy();
        var greedySeconds = simulator.Evaluate(greedy);

        var result = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 4, IterationsPerRun: 5_000));

        Assert.NotNull(result);
        Assert.True(result.TotalSeconds <= greedySeconds);
        Assert.Contains(result.Steps, s => s.Gene == Gene.RoboticsFactory);
        Assert.Contains(result.Steps, s => s.Gene == Gene.SolarPlant || s.IsSatellites);
        Assert.Equal((22, 19, 14), (Last(Gene.MetalMine), Last(Gene.CrystalMine), Last(Gene.DeuteriumSynthesizer)));

        int Last(Gene mine) => result.Steps.Where(s => s.Gene == mine).Max(s => s.Level);
    }

    [Fact]
    public async Task Stopping_keeps_the_best_order_so_far()
    {
        var setup = new OptimizerSetup { MetalMine = 25, CrystalMine = 22, DeuteriumSynthesizer = 18 };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 10_000, IterationsPerRun: 1_000_000), cancellationToken: cancel.Token);

        Assert.NotNull(result);
        Assert.True(result.Runs < 10_000);
    }

    [Fact]
    public void A_result_becomes_a_schema_with_absolute_steps()
    {
        var order = new OptimizedOrder
        {
            Setup = new OptimizerSetup { UseSatellites = true },
            Steps =
            [
                new(Gene.MetalMine, 1, 0, 4),
                new(Gene.SolarPlant, 1, 4, 5),
                new(Gene.SolarSatellites, 3, 5, 50),
                new(Gene.MetalMine, 2, 5, 9)
            ]
        };

        var schema = order.ToSchema("M1");

        Assert.Equal(["Metal Mine 1", "Solar Plant 1", "Metal Mine 2"], schema.Steps.Select(s => s.ToString()));
        Assert.Equal((0, 0, true), (schema.Rules.RoboticsMaxLevel, schema.Rules.NaniteMaxLevel, schema.Rules.UseSolarSatellites));
    }

    [Fact]
    public void Default_uses_90_percent_of_the_cores()
    {
        var expected = Math.Max(1, (int)Math.Floor(Environment.ProcessorCount * 0.9));

        Assert.Equal(expected, new OptimizerOptions().ThreadCount);
        Assert.Equal(1, OptimizerOptions.ThreadsFor(1));
        Assert.Equal(Environment.ProcessorCount, OptimizerOptions.ThreadsFor(100));
        Assert.Equal(3, new OptimizerOptions(Threads: 3).ThreadCount);
    }

    /// <summary>Every distinct order of the given numbers of mine levels.</summary>
    private static IEnumerable<Gene[]> Orders(int metal, int crystal, int deuterium)
    {
        var total = metal + crystal + deuterium;
        var order = new Gene[total];
        var left = new[] { metal, crystal, deuterium };

        IEnumerable<Gene[]> Fill(int position)
        {
            if (position == total)
            {
                yield return (Gene[])order.Clone();
                yield break;
            }
            for (var mine = 0; mine < 3; mine++)
            {
                if (left[mine] == 0)
                    continue;
                left[mine]--;
                order[position] = (Gene)mine;
                foreach (var result in Fill(position + 1))
                    yield return result;
                left[mine]++;
            }
        }

        return Fill(0);
    }

    [Fact]
    public async Task A_planet_that_has_its_targets_gets_an_empty_order()
    {
        var setup = new OptimizerSetup
        {
            MetalMine = 3, CrystalMine = 0, DeuteriumSynthesizer = 0,
            Start = new PlanetStart { Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 5 } }
        };

        var order = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 4, IterationsPerRun: 2_000, Threads: 2));

        Assert.NotNull(order);
        Assert.Empty(order.Steps);
        Assert.Equal(0, order.TotalSeconds);
    }

    [Fact]
    public async Task High_targets_from_reference_level_70_are_planned_to_the_end()
    {
        // Reference level 70: Metal +2, Crystal 0, Deuterium −6. Used to index past the model's levels (max 60).
        var setup = new OptimizerSetup
        {
            MetalMine = 72, CrystalMine = 70, DeuteriumSynthesizer = 64,
            Start = new PlanetStart
            {
                Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = 69, [Gene.CrystalMine] = 67, [Gene.DeuteriumSynthesizer] = 62, [Gene.SolarPlant] = 40 }
            }
        };

        var order = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 2, IterationsPerRun: 200, Threads: 2));

        Assert.NotNull(order);
        Assert.Equal(72, order.Steps.Where(s => s.Gene == Gene.MetalMine).Max(s => s.Level));
        Assert.Equal(70, order.Steps.Where(s => s.Gene == Gene.CrystalMine).Max(s => s.Level));
        Assert.Equal(64, order.Steps.Where(s => s.Gene == Gene.DeuteriumSynthesizer).Max(s => s.Level));
    }

    [Fact]
    public async Task Targets_above_the_models_levels_are_capped()
    {
        var setup = new OptimizerSetup
        {
            MetalMine = GameModel.MaxLevel + 20, CrystalMine = 0, DeuteriumSynthesizer = 0,
            Start = new PlanetStart { Levels = new Dictionary<Gene, int> { [Gene.MetalMine] = GameModel.MaxLevel - 1, [Gene.SolarPlant] = 40 } }
        };

        Assert.Equal(GameModel.MaxLevel, setup.TargetOf(Gene.MetalMine));
        var order = await new BuildOrderOptimizer().RunAsync(setup, new OptimizerOptions(Runs: 2, IterationsPerRun: 100, Threads: 2));

        Assert.NotNull(order);
        Assert.Equal(GameModel.MaxLevel, order.Steps.Where(s => s.Gene == Gene.MetalMine).Max(s => s.Level));
    }
}
