using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Services;

public static class PlanetSearch
{
    /// <summary>
    /// Filters planets by player name (case-insensitive). With <paramref name="exactMatch"/> the whole
    /// name must match, otherwise any part of it. An empty query returns everything.
    /// Results are ordered by coordinates.
    /// </summary>
    public static IReadOnlyList<PlayerPlanet> ByPlayerName(IEnumerable<PlayerPlanet> planets, string? query, bool exactMatch)
    {
        var term = query?.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? planets
            : planets.Where(p => exactMatch
                ? string.Equals(p.PlayerName, term, StringComparison.OrdinalIgnoreCase)
                : p.PlayerName.Contains(term, StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderBy(p => p.Coordinates.Galaxy)
            .ThenBy(p => p.Coordinates.System)
            .ThenBy(p => p.Coordinates.Position)
            .ToList();
    }
}
