using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>An item of the planet's build queue when the simulation starts.</summary>
/// <param name="Gene">The building, or null for one the model doesn't know (it only takes time and resources).</param>
/// <param name="Cost">What a waiting item will take when it starts (the running item is paid already).</param>
/// <param name="Seconds">
/// Construction time: the time left for the running item; for a waiting item of a known building null means "from the
/// model" (with the factories it will find).
/// </param>
public sealed record StartQueueItem(Gene? Gene, int Level, ResourceSet Cost, double? Seconds);

/// <summary>
/// A real planet as the simulation's starting point instead of a fresh one: built levels, resources, satellites,
/// the build queue (it finishes first, in order) and corrections that make the model match what the game shows.
/// </summary>
public sealed record PlanetStart
{
    /// <summary>Built levels by gene, without the queue.</summary>
    public IReadOnlyDictionary<Gene, int> Levels { get; init; } = new Dictionary<Gene, int>();

    public ResourceSet Resources { get; init; }

    public int Satellites { get; init; }

    /// <summary>Satellites ordered and not finished yet; they come one by one from now on.</summary>
    public int SatellitesPending { get; init; }

    /// <summary>The running item first, then the waiting ones in order.</summary>
    public IReadOnlyList<StartQueueItem> Queue { get; init; } = [];

    /// <summary>
    /// Energy the game shows beyond the model (e.g. a Fusion Reactor or a premium the model doesn't know); added to the
    /// production.
    /// </summary>
    public double ExtraEnergy { get; init; }

    /// <summary>Game production ÷ model production of each mine (1 = the model matches the page).</summary>
    public double MetalCorrection { get; init; } = 1;

    public double CrystalCorrection { get; init; } = 1;

    public double DeuteriumCorrection { get; init; } = 1;

    public int LevelOf(Gene gene) => Levels.TryGetValue(gene, out var level) ? level : 0;
}
