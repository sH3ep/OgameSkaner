using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>
/// The SQLite database of each universe, <c>universes/{name}/data.db</c>. The first open in a run creates or upgrades
/// the tables, imports the former JSON data files once and makes the daily backup.
/// </summary>
/// <remarks>
/// Backups go to <c>backups/{name}/</c>: <c>data-{time}.db</c> at most once a day and before a schema upgrade (the newest
/// <see cref="KeptBackups"/> are kept), and the JSON files as they were before the import in <c>json-{time}/</c>. After the
/// import the JSON files are renamed to <c>*.migrated</c>.
/// </remarks>
public sealed class UniverseDatabases(IAppPaths paths, TimeProvider timeProvider, ILogger<UniverseDatabases> logger)
{
    public const string FileName = "data.db";
    public const int KeptBackups = 7;
    public static readonly TimeSpan BackupInterval = TimeSpan.FromDays(1);

    private const string ImportedKey = "json_imported_at";
    private static readonly string[] JsonDataFiles = [JsonPlanetRepository.FileName, JsonRaidStore.ReportsFileName, JsonRaidStore.RaidsFileName];

    private readonly object _sync = new();
    private readonly HashSet<string> _ready = new();

    public string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), FileName);

    public string BackupDirectory(string universe) => Path.Combine(paths.BackupsDirectory, universe);

    public bool Exists(string universe) => File.Exists(FilePath(universe));

    /// <summary>An open connection to the universe's database; dispose it after use (connections are pooled).</summary>
    public SqliteConnection Open(string universe)
    {
        lock (_sync)
        {
            if (!_ready.Contains(universe))
            {
                Initialize(universe);
                _ready.Add(universe);
            }
        }

        return Connect(universe);
    }

    private SqliteConnection Connect(string universe)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath(universe), Pooling = true }.ToString());
        connection.Open();
        return connection;
    }

    private void Initialize(string universe)
    {
        Directory.CreateDirectory(paths.UniverseDirectory(universe));
        using var connection = Connect(universe);

        var version = Schema.CurrentVersion(connection);
        if (version == 0)
        {
            // WAL lets the pages read while a scan writes; the setting stays with the file.
            Sql.Execute(connection, null, "PRAGMA journal_mode = WAL");
        }
        else if (version < Schema.Version)
        {
            Backup(connection, universe, "before-upgrade");
        }

        if (version < Schema.Version)
        {
            Schema.Upgrade(connection, version);
            logger.LogInformation("Database of {Universe} upgraded from version {From} to {To}", universe, version, Schema.Version);
        }

        if (Sql.Scalar(connection, $"SELECT value FROM meta WHERE key = '{ImportedKey}'") is null && JsonFilesExist(universe))
            ImportJson(connection, universe);
        else if (version == Schema.Version)
            BackupIfDue(connection, universe);
    }

    private bool JsonFilesExist(string universe) =>
        JsonDataFiles.Any(name => File.Exists(Path.Combine(paths.UniverseDirectory(universe), name)));

    /// <summary>Copies the JSON files aside, imports them in one transaction and renames them once that's committed.</summary>
    private void ImportJson(SqliteConnection connection, string universe)
    {
        var directory = paths.UniverseDirectory(universe);
        var backup = Path.Combine(BackupDirectory(universe), $"json-{Stamp()}");
        Directory.CreateDirectory(backup);
        foreach (var name in JsonDataFiles.Where(n => File.Exists(Path.Combine(directory, n))))
            File.Copy(Path.Combine(directory, name), Path.Combine(backup, name), overwrite: true);

        var planets = new JsonPlanetRepository(paths).GetAsync(universe).GetAwaiter().GetResult();
        var raidStore = new JsonRaidStore(paths);
        var reports = raidStore.LoadReports(universe);
        var raids = raidStore.LoadRaids(universe);

        using (var transaction = connection.BeginTransaction())
        {
            PlanetTables.Write(connection, transaction, planets, planets.ScannedSystems().Select(s => s.Address));
            RaidTables.AddReports(connection, transaction, reports);
            RaidTables.AddRaids(connection, transaction, raids);
            using var imported = Sql.Prepare(connection, transaction, "INSERT INTO meta (key, value) VALUES ($key, $value)");
            imported.Set("$key", ImportedKey).Set("$value", timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)).ExecuteNonQuery();
            transaction.Commit();
        }

        logger.LogInformation(
            "Imported {Planets} planets, {Reports} reports and {Raids} fleets of {Universe} into {Database}; the JSON files are saved in {Backup}",
            planets.Count, reports.Count, raids.Count, universe, FilePath(universe), backup);

        foreach (var name in JsonDataFiles)
        {
            var path = Path.Combine(directory, name);
            try
            {
                if (File.Exists(path))
                    File.Move(path, path + ".migrated", overwrite: true);
            }
            catch (IOException ex)
            {
                // Harmless: the import is recorded in the database and won't run again.
                logger.LogWarning(ex, "Could not rename {File} after the import", path);
            }
        }

        Backup(connection, universe, "after-import");
    }

    private void BackupIfDue(SqliteConnection connection, string universe)
    {
        var newest = Backups(universe).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (timeProvider.GetUtcNow().UtcDateTime - newest >= BackupInterval)
            Backup(connection, universe, "daily");
    }

    /// <summary>A consistent copy through SQLite's backup API (safe while other connections write).</summary>
    private void Backup(SqliteConnection connection, string universe, string reason)
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory(universe));
            var path = Path.Combine(BackupDirectory(universe), $"data-{Stamp()}-{reason}.db");
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                target.Open();
                connection.BackupDatabase(target);
            }

            foreach (var old in Backups(universe).OrderByDescending(File.GetLastWriteTimeUtc).Skip(KeptBackups))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogWarning(ex, "Backup of the {Universe} database failed", universe);
        }
    }

    private IEnumerable<string> Backups(string universe) =>
        Directory.Exists(BackupDirectory(universe)) ? Directory.EnumerateFiles(BackupDirectory(universe), "data-*.db") : [];

    private string Stamp() => timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
