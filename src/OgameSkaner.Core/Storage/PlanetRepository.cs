using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>Loads and saves the <see cref="PlanetDatabase"/> of each universe.</summary>
public interface IPlanetRepository
{
    /// <summary>Returns the database for a universe. The same instance is returned for repeated calls.</summary>
    Task<PlanetDatabase> GetAsync(string universe, CancellationToken cancellationToken = default);

    Task SaveAsync(PlanetDatabase database, CancellationToken cancellationToken = default);

    bool Exists(string universe);
}

/// <summary>Stores each universe in <c>universes/{name}/planets.json</c>.</summary>
public sealed class JsonPlanetRepository(IAppPaths paths) : IPlanetRepository
{
    private const int CurrentVersion = 1;

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<string, PlanetDatabase> _cache = new();

    public bool Exists(string universe) => File.Exists(FilePath(universe));

    public async Task<PlanetDatabase> GetAsync(string universe, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(universe, out var cached))
                return cached;

            var document = await JsonFile.ReadAsync<PlanetsDocument>(FilePath(universe), cancellationToken).ConfigureAwait(false);
            var planets = document?.Planets.Select(r => r.ToDomain()) ?? [];
            var database = new PlanetDatabase(universe, planets);
            _cache[universe] = database;
            return database;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SaveAsync(PlanetDatabase database, CancellationToken cancellationToken = default)
    {
        var document = new PlanetsDocument
        {
            Version = CurrentVersion,
            Planets = database.Snapshot().Select(PlanetRecord.FromDomain).ToList()
        };

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonFile.WriteAsync(FilePath(database.Universe), document, cancellationToken, JsonFile.CompactOptions).ConfigureAwait(false);
        }
        finally
        {
            _sync.Release();
        }
    }

    private string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "planets.json");

    private sealed class PlanetsDocument
    {
        public int Version { get; set; }
        public List<PlanetRecord> Planets { get; set; } = [];
    }

    /// <summary>Flat on-disk shape, decoupled from the domain model.</summary>
    private sealed record PlanetRecord(string Player, int Galaxy, int System, int Position, bool Moon, DateTimeOffset ScannedAt)
    {
        public PlayerPlanet ToDomain() => new(Player, new Coordinates(Galaxy, System, Position), Moon, ScannedAt);

        public static PlanetRecord FromDomain(PlayerPlanet p) =>
            new(p.PlayerName, p.Coordinates.Galaxy, p.Coordinates.System, p.Coordinates.Position, p.HasMoon, p.ScannedAt);
    }
}
