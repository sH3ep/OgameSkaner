using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Storage.Sqlite;

namespace OgameSkaner.Core.Storage;

/// <summary>The Asteroids page's settings, the asteroids seen, the miners sent and the mining reports, per universe.</summary>
public interface IAsteroidStore
{
    AsteroidSettings LoadSettings(string universe);

    void SaveSettings(string universe, AsteroidSettings settings);

    /// <summary>The asteroids that disappear after <paramref name="after"/>, soonest first.</summary>
    IReadOnlyList<Asteroid> LoadAsteroids(string universe, DateTimeOffset after);

    /// <summary>Adds the asteroids; one still there at the same coordinates gets the new disappearance time.</summary>
    void SaveAsteroids(string universe, IEnumerable<Asteroid> asteroids);

    /// <summary>The miners sent, oldest first.</summary>
    IReadOnlyList<AsteroidMission> LoadMissions(string universe);

    void AddMissions(string universe, IEnumerable<AsteroidMission> missions);

    /// <summary>Marks <paramref name="mission"/> as turned back by the player at <paramref name="recalledAt"/>.</summary>
    void MarkRecalled(string universe, AsteroidMission mission, DateTimeOffset recalledAt);

    /// <summary>The mining reports, oldest first.</summary>
    IReadOnlyList<AsteroidReport> LoadReports(string universe);

    /// <summary>Adds the reports not stored yet (by id).</summary>
    void AddReports(string universe, IEnumerable<AsteroidReport> reports);
}

/// <summary>
/// Asteroid data in the universe's SQLite database (all of it, the history); the settings in
/// <c>universes/{name}/asteroid-settings.json</c>.
/// </summary>
public sealed class SqliteAsteroidStore(UniverseDatabases databases, IAppPaths paths) : IAsteroidStore
{
    private readonly object _sync = new();

    public AsteroidSettings LoadSettings(string universe)
    {
        lock (_sync)
            return JsonFile.Read<AsteroidSettings>(SettingsPath(universe)) ?? AsteroidSettings.Default;
    }

    public void SaveSettings(string universe, AsteroidSettings settings)
    {
        lock (_sync)
            JsonFile.Write(SettingsPath(universe), settings);
    }

    public IReadOnlyList<Asteroid> LoadAsteroids(string universe, DateTimeOffset after)
    {
        using var connection = databases.Open(universe);
        return AsteroidTables.Asteroids(connection, after);
    }

    public void SaveAsteroids(string universe, IEnumerable<Asteroid> asteroids)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        foreach (var asteroid in asteroids)
            AsteroidTables.SaveAsteroid(connection, transaction, asteroid);
        transaction.Commit();
    }

    public IReadOnlyList<AsteroidMission> LoadMissions(string universe)
    {
        using var connection = databases.Open(universe);
        return AsteroidTables.Missions(connection);
    }

    public void AddMissions(string universe, IEnumerable<AsteroidMission> missions)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        AsteroidTables.AddMissions(connection, transaction, missions);
        transaction.Commit();
    }

    public void MarkRecalled(string universe, AsteroidMission mission, DateTimeOffset recalledAt)
    {
        using var connection = databases.Open(universe);
        AsteroidTables.MarkRecalled(connection, mission.Target, mission.SentAt, recalledAt);
    }

    public IReadOnlyList<AsteroidReport> LoadReports(string universe)
    {
        using var connection = databases.Open(universe);
        return AsteroidTables.Reports(connection);
    }

    public void AddReports(string universe, IEnumerable<AsteroidReport> reports)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        AsteroidTables.AddReports(connection, transaction, reports);
        transaction.Commit();
    }

    private string SettingsPath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "asteroid-settings.json");
}
