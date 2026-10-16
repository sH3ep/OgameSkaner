using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Storage;

/// <summary>The Optimizer page as the user left it, plus the best order found per setup.</summary>
public sealed class OptimizerData
{
    public OptimizerSetup LastSetup { get; set; } = new();

    public int Runs { get; set; } = 200;

    public int IterationsPerRun { get; set; } = 20_000;

    public List<OptimizedOrder> Results { get; set; } = [];
}

public interface IOptimizerResultStore
{
    OptimizerData Load();

    void SaveSettings(OptimizerSetup setup, int runs, int iterationsPerRun);

    /// <summary>Keeps <paramref name="order"/> only when its setup has no result yet or it is faster; returns the best one.</summary>
    OptimizedOrder SaveIfBetter(OptimizedOrder order);
}

/// <summary>
/// Stored in <c>optimizer-results.json</c> next to the schema library (the model is the same for every universe;
/// its version is part of the setup key).
/// </summary>
public sealed class JsonOptimizerResultStore(IAppPaths paths) : IOptimizerResultStore
{
    private readonly object _sync = new();

    private string FilePath => Path.Combine(paths.Root, "optimizer-results.json");

    public OptimizerData Load()
    {
        lock (_sync)
            return JsonFile.Read<OptimizerData>(FilePath) ?? new OptimizerData();
    }

    public void SaveSettings(OptimizerSetup setup, int runs, int iterationsPerRun)
    {
        lock (_sync)
        {
            var data = Load();
            data.LastSetup = setup;
            data.Runs = runs;
            data.IterationsPerRun = iterationsPerRun;
            JsonFile.Write(FilePath, data);
        }
    }

    public OptimizedOrder SaveIfBetter(OptimizedOrder order)
    {
        lock (_sync)
        {
            var data = Load();
            var existing = data.Results.FirstOrDefault(r => r.SetupKey == order.SetupKey);
            if (existing is not null && existing.TotalSeconds <= order.TotalSeconds)
            {
                // Count the work anyway, so the saved result shows how much search is behind it.
                existing.Runs += order.Runs;
                existing.Evaluations += order.Evaluations;
                JsonFile.Write(FilePath, data);
                return existing;
            }

            if (existing is not null)
            {
                order.Runs += existing.Runs;
                order.Evaluations += existing.Evaluations;
                data.Results.Remove(existing);
            }
            data.Results.Add(order);
            JsonFile.Write(FilePath, data);
            return order;
        }
    }
}
