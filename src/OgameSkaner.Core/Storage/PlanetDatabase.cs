using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>
/// In-memory, thread-safe set of known player planets for one universe, organised by solar system.
/// Also knows which systems were scanned, so an empty system differs from one never scanned.
/// Persisted through <see cref="IPlanetRepository"/>.
/// </summary>
public sealed class PlanetDatabase
{
    private readonly object _sync = new();
    private readonly Dictionary<SolarSystemAddress, IReadOnlyList<PlayerPlanet>> _systems = new();
    private readonly Dictionary<SolarSystemAddress, DateTimeOffset> _scanned = new();

    /// <param name="scannedSystems">
    /// Systems known to be scanned. Systems with planets count as scanned anyway (older files have no list).
    /// </param>
    public PlanetDatabase(string universe, IEnumerable<PlayerPlanet>? planets = null, IEnumerable<ScannedSystem>? scannedSystems = null)
    {
        Universe = universe;

        foreach (var system in planets?.GroupBy(p => p.Coordinates.SolarSystem) ?? [])
        {
            _systems[system.Key] = Normalize(system);
            _scanned[system.Key] = system.Max(p => p.ScannedAt);
        }

        foreach (var system in scannedSystems ?? [])
        {
            if (!_scanned.TryGetValue(system.Address, out var known) || known < system.ScannedAt)
                _scanned[system.Address] = system.ScannedAt;
        }
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

    /// <summary>
    /// Replaces everything known about a solar system with the result of a fresh scan and marks it as
    /// scanned at <paramref name="scannedAt"/> (default: the newest planet's scan time, or now).
    /// </summary>
    public void ReplaceSystem(SolarSystemAddress address, IEnumerable<PlayerPlanet> planets, DateTimeOffset? scannedAt = null)
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

            _scanned[address] = scannedAt
                ?? (normalized.Count > 0 ? normalized.Max(p => p.ScannedAt) : DateTimeOffset.UtcNow);
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

    /// <summary>All scanned systems ordered by address.</summary>
    public IReadOnlyList<ScannedSystem> ScannedSystems()
    {
        lock (_sync)
        {
            return _scanned
                .Select(s => new ScannedSystem(s.Key, s.Value))
                .OrderBy(s => s.Address.Galaxy)
                .ThenBy(s => s.Address.System)
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
