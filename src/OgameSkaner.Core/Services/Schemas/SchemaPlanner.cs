using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Services.Building;

namespace OgameSkaner.Core.Services.Schemas;

/// <summary>
/// Decides the next action for a planet that follows a <see cref="BuildSchema"/>. Steps are absolute levels in strict
/// order; a step already built or queued is skipped. Before the next step, rules may add: the solar plant (or solar
/// satellites once it is at its maximum) when energy would run out, a storage when a resource is nearly full, and a
/// Robotics/Nanite Factory level when it pays back in construction time.
/// </summary>
/// <remarks>
/// The game drops a queued item whose resources are missing when its turn comes. So an upgrade is only proposed when
/// the planet's resources cover it <b>plus</b> every waiting item already in the queue, and the queue is kept shallow
/// (<see cref="SchemaRules.MaxServerQueueDepth"/>).
/// </remarks>
public static class SchemaPlanner
{
    /// <summary>How many upcoming steps count for the Robotics/Nanite payback.</summary>
    public const int PaybackHorizonSteps = 15;

    /// <param name="shipyard">Needed only for solar satellites; without it no satellites are proposed.</param>
    /// <param name="pendingSatellites">Satellites ordered but not built yet (their energy is counted as coming).</param>
    public static SchemaAction Next(
        PlanetBuildState state,
        BuildSchema schema,
        ShipyardState? shipyard = null,
        int pendingSatellites = 0,
        int queueCapacity = 15)
    {
        if (state.VacationMode)
            return new SchemaAction.Wait("vacation mode");

        var remaining = schema.Steps.Where(s => !state.HasReached(s.Building, s.Level)).ToList();
        if (remaining.Count == 0)
            return new SchemaAction.Done();

        var rules = schema.Rules;
        var depth = Math.Clamp(rules.MaxServerQueueDepth, 1, queueCapacity);
        if (state.Queue.Count >= depth)
            return new SchemaAction.Wait($"the game's queue has {state.Queue.Count} items (limit {depth})");
        if (state.FreeFields is <= 0)
            return new SchemaAction.Wait("no free planet fields");

        var step = remaining[0];
        if (state.Locked.Contains(step.Building))
            return new SchemaAction.Wait($"{step}: requirements not met");

        var levels = PlanetLevels.Planned(state);
        var costs = new CostModel(state);
        var reserved = state.Queue.Where(q => !q.Started)
            .Aggregate(ResourceSet.Zero, (sum, q) => sum + costs.CostOf(q.Building, q.Level));
        var available = state.Resources - reserved;
        var stepUpgrade = costs.Upgrade(step.Building, PlanetLevels.Of(levels, step.Building) + 1);

        return EnergyAction(state, rules, shipyard, pendingSatellites, levels, costs, stepUpgrade, available)
               ?? StorageRule.Needed(levels, rules.StorageFillPercent, state, costs)
                   .Select(s => Afford(s, available, $"storage: resource at {rules.StorageFillPercent}%+ of capacity"))
                   .FirstOrDefault()
               ?? FactoryAction(state, rules, remaining, levels, costs, available)
               ?? Afford(stepUpgrade, available, $"schema step {step}");
    }

    private static SchemaAction? EnergyAction(
        PlanetBuildState state, SchemaRules rules, ShipyardState? shipyard, int pendingSatellites,
        Dictionary<BuildingType, int> levels, CostModel costs, PlannedUpgrade stepUpgrade, ResourceSet available)
    {
        var coming = pendingSatellites * (shipyard?.EnergyPerSatellite ?? 0);
        var now = EnergyFormulas.Projected(state.EnergyBalance, state.Levels, levels) + coming;
        var after = EnergyFormulas.UsesEnergy(stepUpgrade.Building)
            ? EnergyFormulas.Projected(state.EnergyBalance, state.Levels, new Dictionary<BuildingType, int>(levels) { [stepUpgrade.Building] = stepUpgrade.Level }) + coming
            : now;
        var lowest = Math.Min(now, after);
        if (lowest >= 0 || stepUpgrade.Building == BuildingType.SolarPlant)
            return null;

        var solarLevel = PlanetLevels.Of(levels, BuildingType.SolarPlant);
        if (solarLevel < rules.SolarMaxLevel && !state.Locked.Contains(BuildingType.SolarPlant))
            return Afford(costs.Upgrade(BuildingType.SolarPlant, solarLevel + 1), available, $"energy would drop to {lowest:0}");

        if (rules.UseSolarSatellites && shipyard is { SatellitesLocked: false, EnergyPerSatellite: > 0 } yard)
        {
            var count = (int)Math.Ceiling(-lowest / yard.EnergyPerSatellite);
            var cost = yard.SatelliteCost.Scale(count);
            return available.CanAfford(cost)
                ? new SchemaAction.Satellites(count, cost, $"energy would drop to {lowest:0}, solar plant at its maximum")
                : new SchemaAction.Wait($"{count} solar satellites need {Missing(cost, available)}");
        }

        return null; // build on with an energy deficit
    }

    /// <summary>
    /// Robotics R→R+1 divides the build time of what follows by (R+2)/(R+1), i.e. saves W/(R+2) of the remaining work W;
    /// Nanite halves it, saving W/2. It pays back when that saving exceeds its own construction time plus its cost
    /// expressed as production time. Nanite is preferred when both qualify.
    /// </summary>
    private static SchemaAction? FactoryAction(
        PlanetBuildState state, SchemaRules rules, List<SchemaStep> remaining,
        Dictionary<BuildingType, int> levels, CostModel costs, ResourceSet available)
    {
        var work = RemainingWork(remaining, levels, costs, out var steps);
        if (work <= TimeSpan.Zero || state.ProductionPerHour is not { Value: > 0 } production)
            return null;

        PlannedUpgrade? Candidate(BuildingType factory, int maxLevel, Func<int, TimeSpan> saving, out string reason)
        {
            reason = string.Empty;
            var level = PlanetLevels.Of(levels, factory);
            if (level >= maxLevel || state.Locked.Contains(factory) || costs.DurationOf(factory, level + 1) is not { } build)
                return null;

            var upgrade = costs.Upgrade(factory, level + 1);
            var resources = TimeSpan.FromHours(upgrade.Cost.Value / production.Value);
            var saved = saving(level);
            reason = $"{BuildingCatalog.Get(factory).DisplayName} {level + 1} saves {Format(saved)} of the next {steps} steps, " +
                     $"costs {Format(build + resources)} ({Format(build)} build + {Format(resources)} of production)";
            return saved > build + resources ? upgrade : null;
        }

        var nanite = Candidate(BuildingType.NaniteFactory, rules.NaniteMaxLevel, _ => work / 2, out var naniteReason);
        if (nanite is not null)
            return Afford(nanite, available, naniteReason);

        var robotics = Candidate(BuildingType.RoboticsFactory, rules.RoboticsMaxLevel, level => work / (level + 2), out var roboticsReason);
        return robotics is not null ? Afford(robotics, available, roboticsReason) : null;
    }

    /// <summary>Estimated construction time of the next <see cref="PaybackHorizonSteps"/> steps with today's factories.</summary>
    private static TimeSpan RemainingWork(List<SchemaStep> remaining, Dictionary<BuildingType, int> levels, CostModel costs, out int steps)
    {
        var simulated = new Dictionary<BuildingType, int>(levels);
        var horizon = remaining.Take(PaybackHorizonSteps).ToList();
        steps = horizon.Count;

        var total = TimeSpan.Zero;
        foreach (var step in horizon)
        {
            for (var level = PlanetLevels.Of(simulated, step.Building) + 1; level <= step.Level; level++)
                total += costs.DurationOf(step.Building, level) ?? TimeSpan.Zero;
            simulated[step.Building] = Math.Max(PlanetLevels.Of(simulated, step.Building), step.Level);
        }

        return total;
    }

    private static SchemaAction Afford(PlannedUpgrade upgrade, ResourceSet available, string reason) =>
        available.CanAfford(upgrade.Cost)
            ? new SchemaAction.Upgrade(upgrade.Building, upgrade.Level, upgrade.Cost, reason)
            : new SchemaAction.Wait($"{BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level} ({reason}) needs {Missing(upgrade.Cost, available)}");

    private static string Missing(ResourceSet cost, ResourceSet available)
    {
        var parts = new List<string>();
        if (cost.Metal > available.Metal) parts.Add($"{cost.Metal - available.Metal:N0} metal");
        if (cost.Crystal > available.Crystal) parts.Add($"{cost.Crystal - available.Crystal:N0} crystal");
        if (cost.Deuterium > available.Deuterium) parts.Add($"{cost.Deuterium - available.Deuterium:N0} deuterium");
        return parts.Count == 0 ? "nothing" : "more " + string.Join(", ", parts) + " (queued items reserved)";
    }

    public static string Format(TimeSpan time) => time.TotalSeconds switch
    {
        < 60 => $"{time.TotalSeconds:0}s",
        < 3600 => $"{(int)time.TotalMinutes}m{time.Seconds:00}s",
        < 86400 => $"{(int)time.TotalHours}h{time.Minutes:00}m",
        _ => $"{(int)time.TotalDays}d{time.Hours:00}h"
    };
}
