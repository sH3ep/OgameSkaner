using System.Diagnostics;
using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Services.Optimizer;

/// <param name="Runs">Independent searches; each starts from its own randomized greedy order.</param>
/// <param name="IterationsPerRun">Simulated orders per run (simulated annealing steps).</param>
/// <param name="Seed">Base seed: the same seed and options give the same result.</param>
/// <param name="Threads">Parallel runs; null = <see cref="DefaultCpuPercent"/> of the cores.</param>
public sealed record OptimizerOptions(int Runs = 200, int IterationsPerRun = 20_000, int Seed = 1, int? Threads = null)
{
    /// <summary>Leaves some cores free so the computer stays usable during a long search.</summary>
    public const int DefaultCpuPercent = 90;

    public int ThreadCount => Math.Max(1, Threads ?? ThreadsFor(DefaultCpuPercent));

    /// <summary>Number of worker threads for a share of the logical cores (at least one; 24 cores at 90% = 21).</summary>
    public static int ThreadsFor(int cpuPercent) =>
        Math.Clamp((int)Math.Floor(Environment.ProcessorCount * Math.Clamp(cpuPercent, 1, 100) / 100.0), 1, Environment.ProcessorCount);
}

public sealed record OptimizerProgress(int RunsDone, int Runs, double BestSeconds, long Evaluations, TimeSpan Elapsed);

/// <summary>
/// Brute-force search for the fastest build order of a setup: many independent runs spread over the cores (worker
/// threads at below-normal priority, so the desktop and the game stay responsive). A run
/// builds a randomized greedy order (cheapest mine next, solar plant or satellites when energy runs out, a factory
/// when it pays back with a random threshold), then improves it by simulated annealing: moving and swapping genes,
/// adding and removing helper levels. Every candidate is scored by <see cref="BuildSimulator"/>.
/// </summary>
public sealed class BuildOrderOptimizer
{
    /// <summary>Runs the search; when cancelled it returns the best order found so far (null when none finished).</summary>
    /// <param name="initial">
    /// An order to improve (e.g. the previous plan of the same planet): every second run starts from it instead of a
    /// greedy order, when it reaches the targets.
    /// </param>
    public Task<OptimizedOrder?> RunAsync(
        OptimizerSetup setup,
        OptimizerOptions options,
        IProgress<OptimizerProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<Gene>? initial = null) =>
        Task.Run(() => Run(setup, options, progress, cancellationToken, initial?.ToArray()), CancellationToken.None);

    private static OptimizedOrder? Run(OptimizerSetup setup, OptimizerOptions options, IProgress<OptimizerProgress>? progress, CancellationToken cancellationToken, Gene[]? initial)
    {
        var model = new GameModel(setup.Bonuses, setup.Planet);
        var watch = Stopwatch.StartNew();
        var sync = new object();
        Gene[]? best = null;
        var bestSeconds = double.PositiveInfinity;
        var bestRun = int.MaxValue;
        var runsDone = 0;
        long evaluations = 0;

        // Dedicated threads (not the thread pool) so they can run below normal priority; each takes the next run.
        var nextRun = -1;
        void Work()
        {
            int run;
            while (!cancellationToken.IsCancellationRequested && (run = Interlocked.Increment(ref nextRun)) < options.Runs)
            {
                var search = new RunSearch(new BuildSimulator(model, setup), new Random(unchecked(options.Seed * 1_000_003 + run * 7919)));
                var (genes, seconds) = search.Run(options.IterationsPerRun, cancellationToken, run % 2 == 0 ? initial : null);
                lock (sync)
                {
                    evaluations += search.Evaluations;
                    runsDone++;
                    // Ties go to the lower run number, so the result doesn't depend on thread timing.
                    if (seconds < bestSeconds || (seconds == bestSeconds && run < bestRun))
                    {
                        best = genes;
                        bestSeconds = seconds;
                        bestRun = run;
                    }
                    progress?.Report(new OptimizerProgress(runsDone, options.Runs, bestSeconds, evaluations, watch.Elapsed));
                }
            }
        }

        var threads = Enumerable.Range(0, Math.Min(options.ThreadCount, Math.Max(1, options.Runs)))
            .Select(i => new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"Optimizer {i + 1}" })
            .ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        if (best is null || double.IsPositiveInfinity(bestSeconds))
            return null;

        var (total, steps) = new BuildSimulator(model, setup).Trace(best);
        return new OptimizedOrder
        {
            Genes = best,
            Setup = setup,
            SetupKey = setup.Key,
            TotalSeconds = total,
            Steps = steps,
            Runs = runsDone,
            Evaluations = evaluations,
            FoundAt = DateTimeOffset.Now
        };
    }

    /// <summary>One run: a greedy start and simulated annealing, single-threaded.</summary>
    internal sealed class RunSearch(BuildSimulator simulator, Random random)
    {
        private static readonly Gene[] Mines = [Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer];

        private readonly OptimizerSetup _setup = simulator.Setup;
        private readonly GameModel _model = simulator.Model;
        private readonly Gene[] _helpers = HelpersOf(simulator.Setup);

        /// <summary>Index of the first gene the last mutation changed (the genes before it are the same as before).</summary>
        private int _changedAt;

        public long Evaluations { get; private set; }

        public (Gene[] Genes, double Seconds) Run(int iterations, CancellationToken cancellationToken, Gene[]? initial = null)
        {
            // The current order's trace lets a candidate resume where the mutation changed it instead of from the start.
            var currentTrace = new EvaluationTrace();
            var candidateTrace = new EvaluationTrace();
            var current = Greedy();
            Evaluations++;
            var currentSeconds = simulator.Evaluate(current, 0, candidateTrace, currentTrace);
            if (initial is { Length: > 0 })
            {
                Evaluations++;
                var initialSeconds = simulator.Evaluate(initial, 0, currentTrace, candidateTrace);
                if (initialSeconds < currentSeconds)
                {
                    current = initial;
                    currentSeconds = initialSeconds;
                    (currentTrace, candidateTrace) = (candidateTrace, currentTrace);
                }
            }
            var best = current;
            var bestSeconds = currentSeconds;
            var startTemperature = double.IsPositiveInfinity(currentSeconds) ? 1 : currentSeconds * 0.003;

            for (var i = 0; i < iterations; i++)
            {
                if ((i & 1023) == 0 && cancellationToken.IsCancellationRequested)
                    break;

                var candidate = Mutate(current);
                if (ReferenceEquals(candidate, current))
                    continue; // the mutation found nothing to change

                Evaluations++;
                var seconds = simulator.Evaluate(candidate, _changedAt, currentTrace, candidateTrace);
                var progress = (double)i / iterations;
                var temperature = startTemperature * (1 - progress) * (1 - progress);
                if (seconds <= currentSeconds
                    || (temperature > 0 && !double.IsPositiveInfinity(seconds) && random.NextDouble() < Math.Exp((currentSeconds - seconds) / temperature)))
                {
                    current = candidate;
                    currentSeconds = seconds;
                    (currentTrace, candidateTrace) = (candidateTrace, currentTrace);
                    if (seconds < bestSeconds || (seconds == bestSeconds && candidate.Length < best.Length))
                    {
                        best = candidate;
                        bestSeconds = seconds;
                    }
                }
            }
            return (best, bestSeconds);
        }

        /// <summary>
        /// A randomized greedy order: the mine that would be finished soonest (with noise), energy first when the
        /// mine would cause a shortage, and a factory level when its saving on the remaining mines beats its own
        /// time by a random threshold.
        /// </summary>
        public Gene[] Greedy()
        {
            simulator.Reset();
            var genes = new List<Gene>();
            var factoryThreshold = 0.3 + random.NextDouble() * 2.5;
            var preferSatellites = _setup.UseSatellites && random.NextDouble() < 0.5;
            var noise = 0.1 + random.NextDouble() * 0.5;

            void Add(Gene gene)
            {
                genes.Add(gene);
                simulator.Apply(gene, [Gene.MetalMine, Gene.CrystalMine, Gene.DeuteriumSynthesizer]);
            }

            for (var guard = 0; guard < 2000 && !simulator.TargetsReached && MinesLeft(); guard++)
            {
                var mine = PickMine(noise);
                if (FactoryPaysBack(Gene.NaniteFactory, factoryThreshold))
                {
                    Add(Gene.NaniteFactory);
                    continue;
                }
                if (FactoryPaysBack(Gene.RoboticsFactory, factoryThreshold))
                {
                    Add(Gene.RoboticsFactory);
                    continue;
                }

                if (simulator.EnergyBalanceAfter(mine) < 0)
                {
                    var solarAllowed = simulator.CanBuild(Gene.SolarPlant);
                    if (preferSatellites || !solarAllowed)
                    {
                        if (simulator.CanOrderSatellites)
                        {
                            genes.Add(Gene.SolarSatellites);
                            simulator.Apply(Gene.SolarSatellites, [mine]);
                        }
                        else if (_setup.UseSatellites && simulator.LevelOf(Gene.RoboticsFactory) < GameModel.SatelliteRoboticsLevel && Reachable(Gene.RoboticsFactory))
                            Add(Gene.RoboticsFactory);
                        else if (_setup.UseSatellites && simulator.LevelOf(Gene.Shipyard) < GameModel.SatelliteShipyardLevel && Reachable(Gene.Shipyard))
                            Add(Gene.Shipyard);
                        else if (solarAllowed)
                            Add(Gene.SolarPlant);
                        else
                            Add(mine);
                        continue;
                    }
                    Add(Gene.SolarPlant);
                    continue;
                }
                Add(mine);
            }

            // Factory targets the mines didn't need.
            foreach (var factory in (ReadOnlySpan<Gene>)[Gene.RoboticsFactory, Gene.NaniteFactory, Gene.Shipyard])
            {
                for (var guard = 0; guard < GameModel.MaxLevel && simulator.LevelOf(factory) < _setup.TargetOf(factory) && Reachable(factory); guard++)
                    Add(factory);
            }

            // Energy for the mines as they end up (CoverEnergy), when the mines' own energy steps fell short.
            for (var guard = 0; guard < 100 && !simulator.TargetsReached; guard++)
            {
                var solarAllowed = simulator.CanBuild(Gene.SolarPlant);
                if ((preferSatellites || !solarAllowed) && simulator.CanOrderSatellites)
                {
                    genes.Add(Gene.SolarSatellites);
                    simulator.Apply(Gene.SolarSatellites, []);
                }
                else if ((preferSatellites || !solarAllowed) && _setup.UseSatellites && simulator.LevelOf(Gene.RoboticsFactory) < GameModel.SatelliteRoboticsLevel && Reachable(Gene.RoboticsFactory))
                    Add(Gene.RoboticsFactory);
                else if ((preferSatellites || !solarAllowed) && _setup.UseSatellites && simulator.LevelOf(Gene.Shipyard) < GameModel.SatelliteShipyardLevel && Reachable(Gene.Shipyard))
                    Add(Gene.Shipyard);
                else if (solarAllowed)
                    Add(Gene.SolarPlant);
                else
                    break;
            }
            return [.. genes];
        }

        private bool MinesLeft()
        {
            foreach (var mine in Mines)
            {
                if (simulator.LevelOf(mine) < _setup.TargetOf(mine))
                    return true;
            }
            return false;
        }

        /// <summary>Allowed now and payable some day (factories need deuterium, which a fresh planet doesn't have yet).</summary>
        private bool Reachable(Gene gene) => simulator.CanBuild(gene) && !double.IsPositiveInfinity(simulator.SecondsToFinish(gene));

        private Gene PickMine(double noise)
        {
            var best = Gene.MetalMine;
            var bestScore = double.PositiveInfinity;
            foreach (var mine in Mines)
            {
                if (simulator.LevelOf(mine) >= _setup.TargetOf(mine))
                    continue;
                var score = simulator.SecondsToFinish(mine) * (1 + noise * (random.NextDouble() * 2 - 1));
                if (score < bestScore)
                {
                    best = mine;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>Saved construction time on the remaining mine levels vs. the factory's own time (and wait).</summary>
        private bool FactoryPaysBack(Gene factory, double threshold)
        {
            if (!Reachable(factory))
                return false;

            var robotics = simulator.LevelOf(Gene.RoboticsFactory);
            var nanite = simulator.LevelOf(Gene.NaniteFactory);
            var now = _model.SpeedUp(robotics, nanite);
            var after = factory == Gene.RoboticsFactory ? _model.SpeedUp(robotics + 1, nanite) : _model.SpeedUp(robotics, nanite + 1);
            var remaining = 0.0;
            foreach (var mine in Mines)
            {
                for (var level = simulator.LevelOf(mine) + 1; level <= _setup.TargetOf(mine); level++)
                    remaining += _model.Seconds(mine, level, robotics, nanite);
            }
            var saving = remaining * (1 - now / after);
            return saving > threshold * simulator.SecondsToFinish(factory);
        }

        private Gene[] Mutate(Gene[] genes)
        {
            var roll = random.NextDouble();
            if (genes.Length == 0)
                roll = 0; // nothing to move or remove (e.g. the planet already has its targets)
            if (roll < 0.15 && _helpers.Length > 0)
            {
                var index = random.Next(genes.Length + 1);
                _changedAt = index;
                return Insert(genes, _helpers[random.Next(_helpers.Length)], index);
            }
            if (roll < 0.30)
                return RemoveHelper(genes) ?? Move(genes);
            if (roll < 0.55)
                return SwapNeighbours(genes);
            return Move(genes);
        }

        private Gene[] Move(Gene[] genes)
        {
            if (genes.Length < 2)
                return genes;
            var from = random.Next(genes.Length);
            // Mostly short moves, sometimes far.
            var distance = random.NextDouble() < 0.7 ? random.Next(1, 6) : random.Next(1, genes.Length);
            var to = Math.Clamp(from + (random.Next(2) == 0 ? -distance : distance), 0, genes.Length - 1);
            if (to == from)
                return genes;

            _changedAt = Math.Min(from, to);
            var result = (Gene[])genes.Clone();
            var gene = result[from];
            if (from < to)
                Array.Copy(result, from + 1, result, from, to - from);
            else
                Array.Copy(result, to, result, to + 1, from - to);
            result[to] = gene;
            return result;
        }

        private Gene[] SwapNeighbours(Gene[] genes)
        {
            if (genes.Length < 2)
                return genes;
            var i = random.Next(genes.Length - 1);
            if (genes[i] == genes[i + 1])
                return Move(genes);
            _changedAt = i;
            var result = (Gene[])genes.Clone();
            (result[i], result[i + 1]) = (result[i + 1], result[i]);
            return result;
        }

        private static Gene[] Insert(Gene[] genes, Gene gene, int index)
        {
            var result = new Gene[genes.Length + 1];
            Array.Copy(genes, result, index);
            result[index] = gene;
            Array.Copy(genes, index, result, index + 1, genes.Length - index);
            return result;
        }

        private Gene[]? RemoveHelper(Gene[] genes)
        {
            // A few random probes are enough; mines are never removed (the order must keep its targets).
            if (genes.Length == 0)
                return null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var index = random.Next(genes.Length);
                if (genes[index] > Gene.DeuteriumSynthesizer)
                {
                    _changedAt = index;
                    var result = new Gene[genes.Length - 1];
                    Array.Copy(genes, result, index);
                    Array.Copy(genes, index + 1, result, index, genes.Length - index - 1);
                    return result;
                }
            }
            return null;
        }

        private static Gene[] HelpersOf(OptimizerSetup setup)
        {
            var helpers = new List<Gene> { Gene.MetalStorage, Gene.CrystalStorage, Gene.DeuteriumTank };
            if (setup.SolarMaxLevel > 0)
                helpers.AddRange([Gene.SolarPlant, Gene.SolarPlant]);
            if (setup.MaxLevelOf(Gene.RoboticsFactory) > 0)
                helpers.AddRange([Gene.RoboticsFactory, Gene.RoboticsFactory]);
            if (setup.MaxLevelOf(Gene.NaniteFactory) > 0)
                helpers.Add(Gene.NaniteFactory);
            if (setup.UseSatellites)
                helpers.AddRange([Gene.Shipyard, Gene.SolarSatellites, Gene.SolarSatellites]);
            else if (setup.ShipyardTarget > 0)
                helpers.Add(Gene.Shipyard);
            return [.. helpers];
        }
    }
}
