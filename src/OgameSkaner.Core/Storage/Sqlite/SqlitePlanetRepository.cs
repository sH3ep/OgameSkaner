using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>
/// Keeps each universe's <see cref="PlanetDatabase"/> in its SQLite database. A save writes only the systems replaced
/// since the last one and records what changed in the slot and player history.
/// </summary>
public sealed class SqlitePlanetRepository(UniverseDatabases databases, IAppPaths paths) : IPlanetRepository
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<string, PlanetDatabase> _cache = new();

    public bool Exists(string universe) =>
        databases.Exists(universe) || File.Exists(Path.Combine(paths.UniverseDirectory(universe), JsonPlanetRepository.FileName));

    public async Task<PlanetDatabase> GetAsync(string universe, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(universe, out var cached))
                return cached;

            var database = await Task.Run(() =>
            {
                using var connection = databases.Open(universe);
                return PlanetTables.Load(connection, universe);
            }, cancellationToken).ConfigureAwait(false);
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
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = database.TakeChangedSystems();
        try
        {
            if (changed.Count == 0)
                return;

            await Task.Run(() =>
            {
                using var connection = databases.Open(database.Universe);
                using var transaction = connection.BeginTransaction();
                PlanetTables.Write(connection, transaction, database, changed);
                transaction.Commit();
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            database.MarkChanged(changed);
            throw;
        }
        finally
        {
            _sync.Release();
        }
    }

    public Task<IReadOnlyList<SlotObservation>> GetSlotHistoryAsync(string universe, Coordinates coordinates, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = databases.Open(universe);
            return PlanetTables.SlotHistory(connection, coordinates);
        }, cancellationToken);

    public Task<IReadOnlyList<PlayerObservation>> GetPlayerHistoryAsync(string universe, string? playerId, string playerName, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = databases.Open(universe);
            return PlanetTables.PlayerHistory(connection, playerId, playerName);
        }, cancellationToken);
}
