using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.Core.Services.Moons;

/// <summary>One level of a moon's plan (or its game queue), with when it starts counted from now.</summary>
public sealed record MoonStep(BuildingType Building, int Level, ResourceSet Cost, TimeSpan Duration, TimeSpan StartsIn)
{
    public TimeSpan EndsIn => StartsIn + Duration;

    public override string ToString() => $"{BuildingCatalog.Get(Building).DisplayName} {Level}";
}

/// <param name="Queue">The game's queue as it will run: the running item first.</param>
/// <param name="Steps">The levels to queue next, in order, after the game's queue.</param>
/// <param name="Blocked">Why the plan ends before the targets (a missing requirement or field), or null.</param>
public sealed record MoonPlan(IReadOnlyList<MoonStep> Queue, IReadOnlyList<MoonStep> Steps, string? Blocked)
{
    /// <summary>When the last step (or queue item) is done.</summary>
    public TimeSpan Total => Steps.Count > 0 ? Steps[^1].EndsIn : Queue.Count > 0 ? Queue[^1].EndsIn : TimeSpan.Zero;
}

/// <summary>
/// Pure planning of a moon's facilities. Moons produce nothing, so the resources come by transport and only the time to
/// reach the targets counts: <see cref="Simulate"/> builds Robot Factory and Nanite Factory levels as early as they save
/// more time than they take, the Moon Base whenever fields run out, and otherwise the quickest level. Costs and durations
/// are anchored on the moon's page (next level after the queue) and extrapolated: every moon building costs twice its
/// previous level, and construction time follows (metal + crystal)·1.05^level / ((1+R)·1.05^R·1.8^N).
/// </summary>
public static class MoonPlanner
{
    /// <summary>Seconds per metal + crystal unit (and per 1.05^level) at Robot 0 / Nanite 0: Moon Base 1 took 31:06.</summary>
    internal const double DefaultSecondsPerUnit = 1_866 / (600_000 * 1.05);

    /// <summary>Page durations shorter than this are rounded too much to calibrate on (Robot Factory 1 shows 00:01).</summary>
    private static readonly TimeSpan MinCalibration = TimeSpan.FromMinutes(1);

    private const int MaxSteps = 1_000;

    /// <summary>The order that reaches <see cref="MoonSettings.Targets"/> soonest (a requirement's level counts as a target too).</summary>
    public static MoonPlan Simulate(PlanetBuildState moon, MoonSettings settings)
    {
        var sim = new Sim(moon);
        var targets = BuildingCatalog.MoonBuildings.ToDictionary(b => b, settings.TargetOf);
        foreach (var (building, (required, level)) in BuildingCatalog.MoonRequirements)
        {
            if (targets[building] > 0)
                targets[required] = Math.Max(targets[required], level);
        }

        string? blocked = null;
        while (sim.Steps.Count < MaxSteps)
        {
            var left = targets.Where(t => sim.Level(t.Key) < t.Value).Select(t => t.Key).ToList();
            if (left.Count == 0)
                break;

            var levelsLeft = left.Sum(b => targets[b] - sim.Level(b));
            var options = left.Where(sim.RequirementMet).Where(b => sim.FieldsAllow(b, levelsLeft)).ToList();
            BuildingType next;
            if (options.Count == 0)
            {
                // Out of fields (the last one is kept for the Moon Base): one more Moon Base level, past its target if need be.
                if (sim.FieldsAllow(BuildingType.MoonBase, levelsLeft))
                {
                    next = BuildingType.MoonBase;
                }
                else
                {
                    blocked = sim.FreeFields <= 0 ? "no free field left" : $"{string.Join(", ", left.Select(Name))} need(s) a requirement first";
                    break;
                }
            }
            else
            {
                next = BestSpeedUp(sim, options, targets) ?? options.MinBy(b => sim.Duration(b, sim.Level(b) + 1));
            }

            sim.Add(next);
        }

        return new MoonPlan(sim.Queue, sim.Steps, blocked);
    }

    /// <summary>The schema's steps in order from where the moon stands; a step whose requirement or field is missing ends it.</summary>
    public static MoonPlan FromSchema(PlanetBuildState moon, IReadOnlyList<SchemaStep> steps)
    {
        var sim = new Sim(moon);
        string? blocked = null;
        foreach (var step in steps.Where(s => BuildingCatalog.MoonBuildings.Contains(s.Building)))
        {
            while (sim.Level(step.Building) < step.Level && blocked is null)
            {
                if (!sim.RequirementMet(step.Building))
                {
                    var (required, level) = BuildingCatalog.MoonRequirements[step.Building];
                    blocked = $"{step} needs {Name(required)} {level}";
                }
                else if (sim.FreeFields <= 0)
                {
                    blocked = $"{step}: no free field (add a Moon Base level before it)";
                }
                else
                {
                    sim.Add(step.Building);
                }
            }

            if (blocked is not null)
                break;
        }

        return new MoonPlan(sim.Queue, sim.Steps, blocked);
    }

    /// <summary>
    /// The plan's next steps to put into the game's queue now: in order, while the moon's resources pay for them on top of
    /// the waiting items (the game drops an item it can't pay when its turn comes), the queue stays within
    /// <paramref name="maxDepth"/> and each step's requirement is already built (not just queued).
    /// </summary>
    public static IReadOnlyList<MoonStep> ToQueue(MoonPlan plan, PlanetBuildState moon, int maxDepth)
    {
        var available = moon.Resources - plan.Queue.Skip(moon.Queue.Count > 0 && moon.Queue[0].Started ? 1 : 0).Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cost);
        var depth = moon.Queue.Count;
        var push = new List<MoonStep>();
        foreach (var step in plan.Steps)
        {
            if (depth >= Math.Max(1, maxDepth) || !available.CanAfford(step.Cost))
                break;
            if (BuildingCatalog.MoonRequirements.TryGetValue(step.Building, out var requirement) && moon.LevelOf(requirement.Building) < requirement.Level)
                break;

            push.Add(step);
            available -= step.Cost;
            depth++;
        }

        return push;
    }

    /// <summary>
    /// What a transport should bring: the waiting queue items and the plan's first <paramref name="levels"/> steps (at
    /// least one) that start within <paramref name="horizon"/>, minus what the moon has and what's flying there. Never
    /// more (moons are unguarded).
    /// </summary>
    /// <remarks>
    /// The plan's start times assume every level is paid when it can start, so with the Nanite Factory a few hours of it
    /// cost hundreds of millions: the number of levels, not the horizon, keeps a shipment to what the moon spends next.
    /// </remarks>
    public static ResourceSet Shipment(MoonPlan plan, PlanetBuildState moon, ResourceSet inFlight, TimeSpan horizon, int levels)
    {
        var waiting = plan.Queue.Skip(moon.Queue.Count > 0 && moon.Queue[0].Started ? 1 : 0);
        var next = plan.Steps.Where(s => s.StartsIn < horizon).Take(Math.Max(1, levels));
        var need = waiting.Concat(next).Aggregate(ResourceSet.Zero, (sum, s) => sum + s.Cost);
        var have = moon.Resources + inFlight;
        return new ResourceSet(
            Math.Max(0, need.Metal - have.Metal), Math.Max(0, need.Crystal - have.Crystal), Math.Max(0, need.Deuterium - have.Deuterium));
    }

    /// <summary>
    /// The Robot or Nanite level that saves more time on the rest than it takes, most net gain first; Robot levels count
    /// towards unlocking the Nanite Factory (Robot 10) when a Nanite level is a target.
    /// </summary>
    private static BuildingType? BestSpeedUp(Sim sim, IReadOnlyList<BuildingType> options, IReadOnlyDictionary<BuildingType, int> targets)
    {
        BuildingType? best = null;
        var bestGain = 0.0;
        foreach (var speedUp in new[] { BuildingType.RoboticsFactory, BuildingType.NaniteFactory }.Where(options.Contains))
        {
            var chains = new List<List<BuildingType>> { new() { speedUp } };
            var naniteRequirement = BuildingCatalog.MoonRequirements[BuildingType.NaniteFactory].Level;
            if (speedUp == BuildingType.RoboticsFactory && targets[BuildingType.NaniteFactory] > sim.Level(BuildingType.NaniteFactory)
                && sim.Level(BuildingType.RoboticsFactory) < naniteRequirement)
            {
                chains.Add(Enumerable.Repeat(BuildingType.RoboticsFactory, naniteRequirement - sim.Level(BuildingType.RoboticsFactory))
                    .Append(BuildingType.NaniteFactory).ToList());
            }

            foreach (var chain in chains)
            {
                var gain = sim.Gain(chain, targets);
                if (gain > bestGain)
                    (best, bestGain) = (speedUp, gain);
            }
        }

        return best;
    }

    private static string Name(BuildingType building) => BuildingCatalog.Get(building).DisplayName;

    /// <summary>A moon being planned: levels with the queue and the steps so far, free fields and the time.</summary>
    private sealed class Sim
    {
        private readonly Dictionary<BuildingType, int> _levels;
        private readonly Dictionary<BuildingType, int> _anchorLevel = [];
        private readonly Dictionary<BuildingType, ResourceSet> _anchorCost = [];
        private readonly Dictionary<BuildingType, double> _secondsPerUnit = [];
        private TimeSpan _time;

        public Sim(PlanetBuildState moon)
        {
            _levels = BuildingCatalog.MoonBuildings.ToDictionary(b => b, moon.LevelOf);
            FreeFields = moon.FreeFields ?? 1;

            // The page's next level (after the queue) anchors cost and duration; its duration is for the built factories.
            int builtRobotics = moon.LevelOf(BuildingType.RoboticsFactory), builtNanite = moon.LevelOf(BuildingType.NaniteFactory);
            var calibrated = new List<double>();
            foreach (var building in BuildingCatalog.MoonBuildings)
            {
                var next = Math.Max(moon.LevelOf(building), moon.Queue.Where(q => q.Building == building).Select(q => q.Level).DefaultIfEmpty(0).Max()) + 1;
                _anchorLevel[building] = next;
                _anchorCost[building] = moon.NextLevelCosts.TryGetValue(building, out var cost) ? cost : BuildingCatalog.CostOf(building, next);
                if (moon.NextLevelDurations.TryGetValue(building, out var duration) && duration >= MinCalibration)
                {
                    var units = Units(_anchorCost[building]) * Math.Pow(1.05, next);
                    _secondsPerUnit[building] = duration.TotalSeconds * GameModel.ConstructionSpeedUp(builtRobotics, builtNanite) / units;
                    calibrated.Add(_secondsPerUnit[building]);
                }
            }

            var fallback = calibrated.Count > 0 ? calibrated.Average() : DefaultSecondsPerUnit;
            foreach (var building in BuildingCatalog.MoonBuildings)
                _secondsPerUnit.TryAdd(building, fallback);

            // The game's queue runs first: the running item for its remaining time, the rest as estimated.
            var levels = new Dictionary<BuildingType, int>(_levels);
            foreach (var item in moon.Queue.Where(q => BuildingCatalog.MoonBuildings.Contains(q.Building)))
            {
                var seconds = item.Started && moon.RunningRemaining is { } remaining
                    ? remaining
                    : DurationAt(item.Building, item.Level, levels);
                Queue.Add(new MoonStep(item.Building, item.Level, Cost(item.Building, item.Level), seconds, _time));
                _time += seconds;
                levels[item.Building] = Math.Max(levels[item.Building], item.Level);
            }

            foreach (var (building, level) in levels)
                _levels[building] = level;
        }

        public List<MoonStep> Queue { get; } = [];

        public List<MoonStep> Steps { get; } = [];

        /// <summary>Free fields with the queue counted (like the planets' "queue included" number).</summary>
        public int FreeFields { get; private set; }

        public int Level(BuildingType building) => _levels[building];

        public bool RequirementMet(BuildingType building) =>
            !BuildingCatalog.MoonRequirements.TryGetValue(building, out var requirement) || Level(requirement.Building) >= requirement.Level;

        /// <summary>
        /// Every level takes a field and a Moon Base level adds <see cref="BuildingCatalog.FieldsPerMoonBaseLevel"/>. The
        /// last free field is kept for the Moon Base unless this is the very last level to build.
        /// </summary>
        public bool FieldsAllow(BuildingType building, int levelsLeft) =>
            building == BuildingType.MoonBase ? FreeFields >= 1 : FreeFields >= 2 || (FreeFields == 1 && levelsLeft == 1);

        public TimeSpan Duration(BuildingType building, int level) => DurationAt(building, level, _levels);

        public void Add(BuildingType building)
        {
            var level = Level(building) + 1;
            var duration = Duration(building, level);
            Steps.Add(new MoonStep(building, level, Cost(building, level), duration, _time));
            _time += duration;
            _levels[building] = level;
            FreeFields += building == BuildingType.MoonBase ? BuildingCatalog.FieldsPerMoonBaseLevel - 1 : -1;
        }

        /// <summary>
        /// Time the rest of the targets saves once <paramref name="chain"/> is built, minus the chain's own time (it runs
        /// at the speed reached so far, the rest at the speed after it).
        /// </summary>
        public double Gain(IReadOnlyList<BuildingType> chain, IReadOnlyDictionary<BuildingType, int> targets)
        {
            var levels = new Dictionary<BuildingType, int>(_levels);
            var chainSeconds = 0.0;
            foreach (var building in chain)
            {
                levels[building]++;
                chainSeconds += DurationAt(building, levels[building], Lower(levels, building)).TotalSeconds;
            }

            double rest = 0, restAfter = 0;
            foreach (var (building, target) in targets)
            {
                for (var level = levels[building] + 1; level <= target; level++)
                {
                    rest += DurationAt(building, level, _levels).TotalSeconds;
                    restAfter += DurationAt(building, level, levels).TotalSeconds;
                }
            }

            return rest - restAfter - chainSeconds;
        }

        /// <summary><paramref name="levels"/> with <paramref name="building"/> one lower: a level is built at the speed before it.</summary>
        private static Dictionary<BuildingType, int> Lower(Dictionary<BuildingType, int> levels, BuildingType building) =>
            new(levels) { [building] = levels[building] - 1 };

        private TimeSpan DurationAt(BuildingType building, int level, IReadOnlyDictionary<BuildingType, int> levels)
        {
            var seconds = Units(Cost(building, level)) * Math.Pow(1.05, level) * _secondsPerUnit[building]
                          / GameModel.ConstructionSpeedUp(levels[BuildingType.RoboticsFactory], levels[BuildingType.NaniteFactory]);
            return TimeSpan.FromSeconds(Math.Max(1, Math.Floor(seconds)));
        }

        private ResourceSet Cost(BuildingType building, int level) =>
            level == _anchorLevel[building] ? _anchorCost[building] : _anchorCost[building].Scale(Math.Pow(2, level - _anchorLevel[building]));

        private static double Units(ResourceSet cost) => cost.Metal + cost.Crystal;
    }
}
