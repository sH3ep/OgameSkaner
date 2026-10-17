using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>Loads and saves the <see cref="PlanetDatabase"/> of each universe.</summary>
public interface IPlanetRepository
{
    /// <summary>Returns the database for a universe. The same instance is returned for repeated calls.</summary>
    Task<PlanetDatabase> GetAsync(string universe, CancellationToken cancellationToken = default);

    Task SaveAsync(PlanetDatabase database, CancellationToken cancellationToken = default);

    bool Exists(string universe);

    /// <summary>The changes seen at a slot, oldest first; empty when the repository keeps no history.</summary>
    Task<IReadOnlyList<SlotObservation>> GetSlotHistoryAsync(string universe, Coordinates coordinates, CancellationToken cancellationToken = default);

    /// <summary>
    /// The changes of a player's name, status and rank, oldest first; by <paramref name="playerId"/> when known,
    /// otherwise by name. Empty when the repository keeps no history.
    /// </summary>
    Task<IReadOnlyList<PlayerObservation>> GetPlayerHistoryAsync(string universe, string? playerId, string playerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// The former storage, <c>universes/{name}/planets.json</c>; now only read to migrate it into the SQLite database
/// (<see cref="Sqlite.SqlitePlanetRepository"/>). Keeps no history.
/// </summary>
public sealed class JsonPlanetRepository(IAppPaths paths) : IPlanetRepository
{
    public const string FileName = "planets.json";

    /// <summary>
    /// 2 added the player status, id and rank; version 1 files load with none.
    /// 3 added the scanned systems, so empty systems are known; older files know only systems with planets.
    /// </summary>
    private const int CurrentVersion = 3;

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
            var systems = document?.Systems.Select(r => r.ToDomain()) ?? [];
            var database = new PlanetDatabase(universe, planets, systems);
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
            Planets = database.Snapshot().Select(PlanetRecord.FromDomain).ToList(),
            Systems = database.ScannedSystems().Select(SystemRecord.FromDomain).ToList()
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

    public Task<IReadOnlyList<SlotObservation>> GetSlotHistoryAsync(string universe, Coordinates coordinates, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SlotObservation>>([]);

    public Task<IReadOnlyList<PlayerObservation>> GetPlayerHistoryAsync(string universe, string? playerId, string playerName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerObservation>>([]);

    private string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), FileName);

    private sealed class PlanetsDocument
    {
        public int Version { get; set; }
        public List<PlanetRecord> Planets { get; set; } = [];
        public List<SystemRecord> Systems { get; set; } = [];
    }

    private sealed record SystemRecord(int Galaxy, int System, DateTimeOffset ScannedAt)
    {
        public ScannedSystem ToDomain() => new(new SolarSystemAddress(Galaxy, System), ScannedAt);

        public static SystemRecord FromDomain(ScannedSystem s) => new(s.Address.Galaxy, s.Address.System, s.ScannedAt);
    }

    /// <summary>Flat on-disk shape, decoupled from the domain model.</summary>
    private sealed record PlanetRecord(
        string Player,
        int Galaxy,
        int System,
        int Position,
        bool Moon,
        DateTimeOffset ScannedAt,
        PlayerStatus Status = PlayerStatus.None,
        string? PlayerId = null,
        int? Rank = null)
    {
        public PlayerPlanet ToDomain() => new(Player, new Coordinates(Galaxy, System, Position), Moon, ScannedAt, Status, PlayerId, Rank);

        public static PlanetRecord FromDomain(PlayerPlanet p) =>
            new(p.PlayerName, p.Coordinates.Galaxy, p.Coordinates.System, p.Coordinates.Position, p.HasMoon, p.ScannedAt, p.Status, p.PlayerId, p.Rank);
    }
}
