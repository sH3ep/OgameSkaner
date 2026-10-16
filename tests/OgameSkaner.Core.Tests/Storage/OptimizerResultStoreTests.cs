using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class OptimizerResultStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private JsonOptimizerResultStore Store() => new(new AppPaths(_temp.Path));

    private static OptimizedOrder Order(OptimizerSetup setup, double seconds) => new()
    {
        Setup = setup,
        SetupKey = setup.Key,
        TotalSeconds = seconds,
        Runs = 200,
        Evaluations = 1_000,
        Steps = [new OptimizedStep(Gene.MetalMine, 1, 0, seconds)]
    };

    [Fact]
    public void Keeps_only_the_fastest_order_per_setup()
    {
        var setup = new OptimizerSetup { MetalMine = 1, CrystalMine = 0, DeuteriumSynthesizer = 0 };

        Store().SaveIfBetter(Order(setup, 100));
        var kept = Store().SaveIfBetter(Order(setup, 120));
        Store().SaveIfBetter(Order(setup with { MaxTemperature = 20 }, 130));

        Assert.Equal(100, kept.TotalSeconds);
        Assert.Equal(400, kept.Runs);
        var results = Store().Load().Results;
        Assert.Equal(2, results.Count);
        Assert.Equal(100, results.Single(r => r.SetupKey == setup.Key).TotalSeconds);

        var faster = Store().SaveIfBetter(Order(setup, 90));
        Assert.Equal((90, 600), (faster.TotalSeconds, faster.Runs));
        Assert.Equal(90, Store().Load().Results.Single(r => r.SetupKey == setup.Key).TotalSeconds);
    }

    [Fact]
    public void Settings_and_orders_round_trip()
    {
        var setup = new OptimizerSetup { MetalMine = 25, NaniteMaxLevel = 2, UseSatellites = false };
        Store().SaveSettings(setup, runs: 500, iterationsPerRun: 30_000);
        Store().SaveIfBetter(Order(setup, 42));

        var data = Store().Load();

        Assert.Equal(setup, data.LastSetup);
        Assert.Equal((500, 30_000), (data.Runs, data.IterationsPerRun));
        var order = Assert.Single(data.Results);
        Assert.Equal(setup, order.Setup);
        Assert.Equal([new OptimizedStep(Gene.MetalMine, 1, 0, 42)], order.Steps);
    }
}
