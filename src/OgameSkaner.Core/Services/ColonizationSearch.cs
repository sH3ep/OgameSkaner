using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Services;

/// <summary>Which free slots to look for: the systems of <see cref="Systems"/>, positions from..to.</summary>
public sealed record ColonizationFilter(ScanRange Systems, int PositionFrom, int PositionTo)
{
    /// <summary>Planet positions; 16 is the expedition slot and can't be colonized.</summary>
    public const int MaxPosition = 15;

    public IReadOnlyList<string> Validate(UniverseProfile universe)
    {
        var errors = Systems.Validate(universe).ToList();

        if (PositionFrom < 1 || PositionTo > MaxPosition)
            errors.Add($"Slot must be between 1 and {MaxPosition}.");
        if (PositionFrom > PositionTo)
            errors.Add("Start slot must not be greater than end slot.");

        return errors;
    }
}

/// <summary>A position without a planet in a scanned system.</summary>
/// <param name="PlanetsInSystem">How many planets the system has, i.e. how crowded it is.</param>
public sealed record FreeSlot(Coordinates Coordinates, DateTimeOffset ScannedAt, int PlanetsInSystem);

/// <param name="SystemsScanned">How many of the <paramref name="SystemsInRange"/> were ever scanned.</param>
public sealed record ColonizationResult(IReadOnlyList<FreeSlot> Slots, int SystemsInRange, int SystemsScanned);

public static class ColonizationSearch
{
    /// <summary>
    /// Free slots in the filter's range, ordered by coordinates. A slot is free when its system was
    /// scanned and no planet was seen at that position; systems never scanned are skipped.
    /// </summary>
    public static ColonizationResult Find(
        IEnumerable<PlayerPlanet> planets,
        IEnumerable<ScannedSystem> scannedSystems,
        ColonizationFilter filter)
    {
        var range = filter.Systems;
        bool InRange(SolarSystemAddress a) =>
            a.Galaxy >= range.StartGalaxy && a.Galaxy <= range.EndGalaxy
            && a.System >= range.StartSystem && a.System <= range.EndSystem;

        var occupied = planets
            .Where(p => InRange(p.Coordinates.SolarSystem))
            .GroupBy(p => p.Coordinates.SolarSystem)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Coordinates.Position).ToHashSet());

        var slots = new List<FreeSlot>();
        var scanned = 0;
        foreach (var system in scannedSystems
                     .Where(s => InRange(s.Address))
                     .OrderBy(s => s.Address.Galaxy)
                     .ThenBy(s => s.Address.System))
        {
            scanned++;
            var taken = occupied.GetValueOrDefault(system.Address) ?? [];
            for (var position = filter.PositionFrom; position <= filter.PositionTo; position++)
            {
                if (!taken.Contains(position))
                    slots.Add(new FreeSlot(new Coordinates(system.Address.Galaxy, system.Address.System, position), system.ScannedAt, taken.Count));
            }
        }

        return new ColonizationResult(slots, range.Count, scanned);
    }
}
