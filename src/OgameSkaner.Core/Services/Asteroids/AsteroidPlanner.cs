using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Services.Asteroids;

/// <summary>Pure decisions of the asteroid runner: where to look and how many miners to send.</summary>
public static class AsteroidPlanner
{
    /// <summary>
    /// The hint rows no known asteroid accounts for, in the order to search them. Each row stands for one asteroid
    /// (rows overlap and repeat), so every row is matched to its own asteroid: by the row's end, each taking the
    /// first asteroid left in its range, which matches as many rows as possible. <paramref name="known"/> are the
    /// asteroids still there or already being mined.
    /// </summary>
    public static IReadOnlyList<AsteroidHint> Uncovered(IReadOnlyList<AsteroidHint> hints, IEnumerable<Coordinates> known)
    {
        var free = known.OrderBy(c => c.Galaxy).ThenBy(c => c.System).ToList();
        var uncovered = new List<AsteroidHint>();
        foreach (var hint in OrderForMatching(hints))
        {
            var index = free.FindIndex(hint.Contains);
            if (index >= 0)
                free.RemoveAt(index);
            else
                uncovered.Add(hint);
        }

        return uncovered;
    }

    /// <summary>The order rows take asteroids in: by the end of their range.</summary>
    public static IEnumerable<AsteroidHint> OrderForMatching(IEnumerable<AsteroidHint> hints) =>
        hints.OrderBy(h => h.Galaxy).ThenBy(h => h.ToSystem).ThenBy(h => h.FromSystem);

    /// <summary>
    /// The systems of <paramref name="searches"/> to scan, nearest to one of <paramref name="from"/> first (each once;
    /// ties and an empty <paramref name="from"/> keep the rows' order).
    /// </summary>
    public static IReadOnlyList<SolarSystemAddress> ScanOrder(
        IEnumerable<AsteroidHint> searches, IReadOnlyCollection<Coordinates> from, UniverseProfile universe) =>
        searches
            .SelectMany(s => Enumerable.Range(s.FromSystem, Math.Max(0, s.ToSystem - s.FromSystem + 1)).Select(system => new SolarSystemAddress(s.Galaxy, system)))
            .Distinct()
            .OrderBy(a => from.Count == 0 ? 0 : from.Min(c => RaidPlanner.Distance(c, new Coordinates(a.Galaxy, a.System, 17), universe)))
            .ToList();

    /// <summary>
    /// Miners for the estimated resources plus the margin; <see cref="AsteroidSettings.MinersWithoutEstimate"/> without
    /// an estimate or a known cargo capacity; never more than <see cref="AsteroidSettings.MaxMiners"/>.
    /// </summary>
    public static long MinersNeeded(long? estimate, long cargoPerMiner, AsteroidSettings settings)
    {
        long needed;
        if (estimate is not { } resources || cargoPerMiner <= 0)
            needed = Math.Max(settings.MinMiners, settings.MinersWithoutEstimate);
        else
        {
            var cargo = resources * (1 + Math.Max(0, settings.MarginPercent) / 100.0);
            needed = Math.Max(Math.Max(1, settings.MinMiners), (long)Math.Ceiling(cargo / cargoPerMiner));
        }

        return settings.MaxMiners is > 0 and var max ? Math.Min(needed, max) : needed;
    }

    /// <summary>
    /// Whether <paramref name="miners"/> carry at least <see cref="AsteroidSettings.MinCoveragePercent"/> of the
    /// estimate; true without an estimate or a known cargo capacity.
    /// </summary>
    public static bool CoversEnough(long miners, long? estimate, long cargoPerMiner, AsteroidSettings settings) =>
        estimate is not > 0 || cargoPerMiner <= 0 || settings.MinCoveragePercent <= 0
        || (double)miners * cargoPerMiner >= estimate.Value * Math.Min(100, settings.MinCoveragePercent) / 100.0;

    /// <summary>Fleet slots mining fleets may still fill: <see cref="AsteroidSettings.FleetSlotUsePercent"/> of all, minus those in use.</summary>
    public static int SlotBudget(FleetSlots slots, AsteroidSettings settings) =>
        Math.Max(0, (int)Math.Floor(slots.Max * Math.Clamp(settings.FleetSlotUsePercent, 0, 100) / 100.0) - slots.Used);

    /// <summary>Whether miners flying <paramref name="flight"/> from now arrive before the asteroid is gone (with the safety margin).</summary>
    public static bool ArrivesInTime(Asteroid asteroid, TimeSpan flight, DateTimeOffset now, AsteroidSettings settings) =>
        now + flight + TimeSpan.FromSeconds(Math.Max(0, settings.ArrivalSafetySeconds)) <= asteroid.DisappearsAt;
}
