using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Services.Building;

/// <param name="EnergyDeficit">The upgrade leaves the planet with negative energy because the solar plant is at its maximum.</param>
public sealed record PlannedUpgrade(BuildingType Building, int Level, ResourceSet Cost, bool EnergyDeficit = false);

/// <summary>Why a planet gets no (more) upgrades.</summary>
public enum BuildStopReason
{
    None,
    TargetsReached,
    NotEnoughResources,
    QueueFull,
    Rejected,
    NoFreeFields,
    VacationMode
}

/// <summary>The next upgrade for a planet, or why there is none.</summary>
public sealed record BuildDecision(PlannedUpgrade? Upgrade, BuildStopReason Reason)
{
    public static BuildDecision Stop(BuildStopReason reason) => new(null, reason);
}

/// <summary>Upgrades in build order and why the plan ends there.</summary>
public sealed record BuildPlan(IReadOnlyList<PlannedUpgrade> Upgrades, BuildStopReason Reason);

/// <summary>
/// Decides what to build next on a planet. Already queued items count as built, so a level is never
/// queued twice. Upgrades below their target are compared by <see cref="ResourceSet.Value"/> (metal 1,
/// crystal 1.5, deuterium 3) and the cheapest wins. Locked buildings are skipped.
/// </summary>
/// <remarks>
/// A storage goes ahead of the price order when its resource is at <see cref="BuildStrategy.StorageFillPercent"/>
/// of capacity or more and no level of that storage is queued yet (the next state read brings the new capacity).
/// The solar plant is not chosen by price but by energy: it goes first whenever the planned levels are already
/// short of energy, and it takes a mine's place when that mine would make them short. Energy starts from the
/// game's real balance (bonuses included) and only the planned differences come from formulas
/// (<see cref="EnergyFormulas.Projected"/>).
/// </remarks>
/// <remarks>
/// Affordability mirrors the server: every new queue item is checked against the planet's current resources,
/// which are only paid when the item starts, so queued items do not reduce what is available. The server
/// silently ignores an unaffordable item, which is why the planner never proposes one when asked for
/// affordable upgrades.
/// </remarks>
public static class BuildPlanner
{
    /// <param name="requireAffordable">When true, the cheapest upgrade the planet's current resources cover.</param>
    public static BuildDecision NextUpgrade(PlanetBuildState state, BuildStrategy strategy, bool requireAffordable)
    {
        var plan = Plan(state, strategy, maxItems: 1, requireAffordable);
        return plan.Upgrades.Count > 0
            ? new BuildDecision(plan.Upgrades[0], BuildStopReason.None)
            : BuildDecision.Stop(plan.Reason);
    }

    /// <summary>The next <paramref name="maxItems"/> upgrades in build order.</summary>
    public static BuildPlan Plan(PlanetBuildState state, BuildStrategy strategy, int maxItems, bool requireAffordable)
    {
        var levels = PlanetLevels.Planned(state);
        var costs = new CostModel(state);
        var plan = new List<PlannedUpgrade>();

        while (plan.Count < maxItems)
        {
            if (state.FreeFields is { } fields && plan.Count >= fields)
                return new BuildPlan(plan, BuildStopReason.NoFreeFields);

            var options = Candidates(levels, strategy, state, costs).ToList();
            if (options.Count == 0)
                return new BuildPlan(plan, BuildStopReason.TargetsReached);

            var next = requireAffordable ? options.FirstOrDefault(u => state.Resources.CanAfford(u.Cost)) : options[0];
            if (next is null)
                return new BuildPlan(plan, BuildStopReason.NotEnoughResources);

            plan.Add(next);
            levels[next.Building] = next.Level;
        }

        return new BuildPlan(plan, BuildStopReason.QueueFull);
    }

    /// <summary>
    /// Possible next upgrades in order: the solar plant first when energy is already short, then the others
    /// cheapest first, with the solar plant taking the place of a mine that would run out of energy.
    /// </summary>
    private static IEnumerable<PlannedUpgrade> Candidates(Dictionary<BuildingType, int> levels, BuildStrategy strategy, PlanetBuildState state, CostModel costs)
    {
        var solar = NextSolar(levels, strategy, state, costs);
        var byPrice = strategy.Rules
            .Where(r => r.Enabled && r.Building != BuildingType.SolarPlant && !state.Locked.Contains(r.Building)) // storages: their target
            .Where(r => PlanetLevels.Of(levels, r.Building) < r.TargetFor(strategy.ReferenceLevel))
            .Select(r => costs.Upgrade(r.Building, PlanetLevels.Of(levels, r.Building) + 1))
            .OrderBy(u => u.Cost.Value)
            .Select(u => WithEnergy(u, levels, state, solar))
            .ToList();

        // Storages for (nearly) full resources go before the price order...
        byPrice.InsertRange(0, StorageRule.Needed(levels, strategy.StorageFillPercent, state, costs).Where(u => strategy.FillsStorage(u.Building)));

        // ...and only an energy shortage (e.g. mines finished before the solar plant) goes before them.
        if (solar is not null && byPrice.Count > 0 && EnergyFormulas.Projected(state.EnergyBalance, state.Levels, levels) < 0)
            byPrice.Insert(0, solar);

        return byPrice.Distinct();
    }

    private static PlannedUpgrade WithEnergy(PlannedUpgrade candidate, Dictionary<BuildingType, int> levels, PlanetBuildState state, PlannedUpgrade? solar)
    {
        if (!EnergyFormulas.UsesEnergy(candidate.Building))
            return candidate;

        var after = new Dictionary<BuildingType, int>(levels) { [candidate.Building] = candidate.Level };
        if (EnergyFormulas.Projected(state.EnergyBalance, state.Levels, after) >= 0)
            return candidate;

        return solar ?? candidate with { EnergyDeficit = true };
    }

    /// <summary>The next solar plant level, or null when it is at its maximum or locked.</summary>
    private static PlannedUpgrade? NextSolar(Dictionary<BuildingType, int> levels, BuildStrategy strategy, PlanetBuildState state, CostModel costs)
    {
        var level = PlanetLevels.Of(levels, BuildingType.SolarPlant);
        return level < strategy.SolarMaxLevel && !state.Locked.Contains(BuildingType.SolarPlant)
            ? costs.Upgrade(BuildingType.SolarPlant, level + 1)
            : null;
    }
}
