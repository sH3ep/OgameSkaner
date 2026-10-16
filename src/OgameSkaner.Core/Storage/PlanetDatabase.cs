using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>
/// In-memory, thread-safe set of known player planets for one universe, organised by solar system.
/// Persisted through <see cref="IPlanetRepository"/>.
/// </summary>
public sealed class PlanetDatabase
{
    private readonly object _sync = new();
    private readonly Dictionary<SolarSystemAddress, IReadOnlyList<PlayerPlanet>> _systems = new();

    public PlanetDatabase(string universe, IEnumerable<PlayerPlanet>? planets = null)
    {
        Universe = universe;
        if (planets is null)
            return;

        foreach (var system in planets.GroupBy(p => p.Coordinates.SolarSystem))
            _systems[system.Key] = Normalize(system);
    }

    public string Universe { get; }

    /// <summary>Raised (possibly on a background thread) after the content changed.</summary>
    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_sync)
                return _systems.Values.Sum(s => s.Count);
        }
    }

    /// <summary>Replaces everything known about a solar system with the result of a fresh scan.</summary>
    public void ReplaceSystem(SolarSystemAddress address, IEnumerable<PlayerPlanet> planets)
    {
        var normalized = Normalize(planets);
        if (normalized.Any(p => p.Coordinates.SolarSystem != address))
            throw new ArgumentException($"All planets must belong to {address}.", nameof(planets));

        lock (_sync)
        {
            if (normalized.Count == 0)
                _systems.Remove(address);
            else
                _systems[address] = normalized;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>All planets ordered by coordinates.</summary>
    public IReadOnlyList<PlayerPlanet> Snapshot()
    {
        lock (_sync)
        {
            return _systems.Values
                .SelectMany(s => s)
                .OrderBy(p => p.Coordinates.Galaxy)
                .ThenBy(p => p.Coordinates.System)
                .ThenBy(p => p.Coordinates.Position)
                .ToList();
        }
    }

    private static IReadOnlyList<PlayerPlanet> Normalize(IEnumerable<PlayerPlanet> planets) =>
        planets
            .GroupBy(p => p.Coordinates.Position)
            .Select(g => g.MaxBy(p => p.ScannedAt)!)
            .OrderBy(p => p.Coordinates.Position)
            .ToList();
}
