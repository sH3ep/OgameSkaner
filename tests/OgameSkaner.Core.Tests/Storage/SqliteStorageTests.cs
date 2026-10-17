using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Sqlite;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Storage;

public class SqliteStorageTests : IDisposable
{
    private static readonly SolarSystemAddress System11 = new(1, 1);

    private readonly TempDirectory _temp = new();
    private readonly AppPaths _paths;

    public SqliteStorageTests() => _paths = new AppPaths(_temp.Path);

    public void Dispose() => _temp.Dispose();

    /// <summary>A new instance per call, like a new run of the app.</summary>
    private UniverseDatabases Databases() => new(_paths, TimeProvider.System, NullLogger<UniverseDatabases>.Instance);

    private SqlitePlanetRepository Repository() => new(Databases(), _paths);

    private string UniverseFile(string name) => Path.Combine(_paths.UniverseDirectory("tron"), name);

    private string BackupDirectory => Path.Combine(_paths.BackupsDirectory, "tron");

    /// <summary>Scans <paramref name="planets"/> into system 1:1 and saves, as the scanner does.</summary>
    private static async Task ScanAsync(IPlanetRepository repository, DateTimeOffset at, params PlayerPlanet[] planets)
    {
        var database = await repository.GetAsync("tron");
        database.ReplaceSystem(System11, planets, at);
        await repository.SaveAsync(database);
    }

    [Fact]
    public async Task Planets_and_scanned_systems_round_trip()
    {
        var planets = new[] { Planet("Admin", 1, 1, 1) with { Status = PlayerStatus.Inactive, PlayerId = "a", Rank = 5 }, Planet("Bartolo", 1, 1, 2, moon: true) };
        var writer = Repository();
        await ScanAsync(writer, ScanTime, planets);
        var empty = await writer.GetAsync("tron");
        empty.ReplaceSystem(new SolarSystemAddress(1, 2), [], ScanTime);
        await writer.SaveAsync(empty);

        var loaded = await Repository().GetAsync("tron");

        Assert.Equal(planets, loaded.Snapshot());
        Assert.Equal([new ScannedSystem(System11, ScanTime), new ScannedSystem(new SolarSystemAddress(1, 2), ScanTime)], loaded.ScannedSystems());
        Assert.True(Repository().Exists("tron"));
        Assert.False(Repository().Exists("other"));
    }

    [Fact]
    public async Task Slot_history_records_only_changes()
    {
        var repository = Repository();
        var slot = new Coordinates(1, 1, 4);

        await ScanAsync(repository, ScanTime, Planet("A", 1, 1, 4, at: ScanTime) with { PlayerId = "a" });
        await ScanAsync(repository, ScanTime.AddDays(1), Planet("A", 1, 1, 4, at: ScanTime.AddDays(1)) with { PlayerId = "a" }); // unchanged
        await ScanAsync(repository, ScanTime.AddDays(2), Planet("A2", 1, 1, 4, at: ScanTime.AddDays(2)) with { PlayerId = "a" }); // renamed: same owner
        await ScanAsync(repository, ScanTime.AddDays(3), Planet("A2", 1, 1, 4, moon: true, at: ScanTime.AddDays(3)) with { PlayerId = "a" });
        await ScanAsync(repository, ScanTime.AddDays(4)); // abandoned
        await ScanAsync(repository, ScanTime.AddDays(5), Planet("B", 1, 1, 4, at: ScanTime.AddDays(5)) with { PlayerId = "b" });

        var history = await Repository().GetSlotHistoryAsync("tron", slot);

        Assert.Equal(
        [
            new SlotObservation(slot, ScanTime, "A", "a", false),
            new SlotObservation(slot, ScanTime.AddDays(3), "A2", "a", true),
            new SlotObservation(slot, ScanTime.AddDays(4), null, null, false),
            new SlotObservation(slot, ScanTime.AddDays(5), "B", "b", false)
        ], history);
        Assert.True(history[2].IsFree);
    }

    [Fact]
    public async Task Player_history_records_status_rank_and_name_changes()
    {
        var repository = Repository();
        PlayerPlanet Seen(int day, string name, PlayerStatus status, int? rank) =>
            Planet(name, 1, 1, 1, at: ScanTime.AddDays(day)) with { PlayerId = "p", Status = status, Rank = rank };

        await ScanAsync(repository, ScanTime, Seen(0, "Kowalski", PlayerStatus.None, 100));
        await ScanAsync(repository, ScanTime.AddDays(1), Seen(1, "Kowalski", PlayerStatus.None, 100)); // unchanged
        await ScanAsync(repository, ScanTime.AddDays(2), Seen(2, "Kowalski", PlayerStatus.Inactive, 100));
        await ScanAsync(repository, ScanTime.AddDays(3), Seen(3, "Kowalski", PlayerStatus.Inactive, null)); // rank unknown: kept
        await ScanAsync(repository, ScanTime.AddDays(4), Seen(4, "Nowak", PlayerStatus.Inactive, 120));

        var history = await Repository().GetPlayerHistoryAsync("tron", "p", "Nowak");

        Assert.Equal(
        [
            new PlayerObservation("p", "Kowalski", ScanTime, PlayerStatus.None, 100),
            new PlayerObservation("p", "Kowalski", ScanTime.AddDays(2), PlayerStatus.Inactive, 100),
            new PlayerObservation("p", "Nowak", ScanTime.AddDays(4), PlayerStatus.Inactive, 120)
        ], history);
    }

    [Fact]
    public async Task Player_history_includes_what_was_recorded_by_name_before_the_scans_had_ids()
    {
        var repository = Repository();
        await ScanAsync(repository, ScanTime, Planet("Kowalski", 1, 1, 1, at: ScanTime));
        await ScanAsync(repository, ScanTime.AddDays(1), Planet("Kowalski", 1, 1, 1, at: ScanTime.AddDays(1)) with { PlayerId = "p", Status = PlayerStatus.Inactive });

        var history = await Repository().GetPlayerHistoryAsync("tron", "p", "Kowalski");

        Assert.Equal([PlayerStatus.None, PlayerStatus.Inactive], history.Select(h => h.Status));
    }

    [Fact]
    public void Changed_systems_are_taken_once_and_can_be_marked_again()
    {
        var database = new PlanetDatabase("tron", [Planet("Loaded", 1, 5, 1)]);
        database.ReplaceSystem(System11, [Planet("A", 1, 1, 1)], ScanTime);

        var changed = database.TakeChangedSystems();
        Assert.Equal([System11], changed); // loaded systems aren't changes
        Assert.Empty(database.TakeChangedSystems());

        database.MarkChanged(changed); // e.g. after a failed save
        Assert.Equal([System11], database.TakeChangedSystems());
    }

    [Fact]
    public async Task Json_files_are_imported_once_backed_up_and_renamed()
    {
        Directory.CreateDirectory(_paths.UniverseDirectory("tron"));
        await File.WriteAllTextAsync(UniverseFile("planets.json"),
            """
            {"version":3,
             "planets":[{"player":"A","galaxy":1,"system":1,"position":2,"moon":true,"scannedAt":"2026-01-02T03:04:05+01:00","status":"Inactive, Strong","playerId":"a","rank":7}],
             "systems":[{"galaxy":1,"system":1,"scannedAt":"2026-01-02T03:04:05+01:00"},{"galaxy":1,"system":2,"scannedAt":"2026-01-02T03:04:05+01:00"}]}
            """);
        await File.WriteAllTextAsync(UniverseFile("espionage-reports.json"),
            """
            {"reports":[{"id":"r1","galaxy":1,"system":1,"position":2,"type":"Planet","time":"2026-01-02T03:04:05+01:00","player":"A","playerId":"a",
                         "status":"Inactive","metal":10,"crystal":20,"deuterium":30,"counterEspionage":0,"fleetPoints":0,"ships":{},"defensePoints":null,"defenses":null}]}
            """);
        await File.WriteAllTextAsync(UniverseFile("raids.json"),
            """
            {"raids":[{"galaxy":1,"system":1,"position":2,"type":"Planet","player":"A","mission":"Spy","originGalaxy":1,"originSystem":5,"originPosition":8,
                       "shipType":"SPY_PROBE","ships":5,"expectedLoot":0,"sentAt":"2026-01-02T03:04:05+01:00","arrivesAt":"2026-01-02T03:05:05+01:00","returnsAt":"2026-01-02T03:06:05+01:00"}]}
            """);

        var databases = Databases();
        var planets = await new SqlitePlanetRepository(databases, _paths).GetAsync("tron");
        var store = new SqliteRaidStore(databases, _paths);

        var planet = Assert.Single(planets.Snapshot());
        Assert.Equal(new PlayerPlanet("A", new Coordinates(1, 1, 2), true, ScanTime, PlayerStatus.Inactive | PlayerStatus.Strong, "a", 7), planet);
        Assert.Equal(2, planets.ScannedSystems().Count);
        Assert.Equal("r1", Assert.Single(store.LoadReports("tron")).Id);
        Assert.Equal(MissionType.Spy, Assert.Single(store.LoadRaids("tron")).Mission);
        Assert.Single(await new SqlitePlanetRepository(databases, _paths).GetSlotHistoryAsync("tron", planet.Coordinates)); // the baseline

        foreach (var name in new[] { "planets.json", "espionage-reports.json", "raids.json" })
        {
            Assert.False(File.Exists(UniverseFile(name)));
            Assert.True(File.Exists(UniverseFile(name + ".migrated")));
            Assert.True(File.Exists(Path.Combine(Assert.Single(Directory.GetDirectories(BackupDirectory, "json-*")), name)));
        }

        Assert.Single(Directory.GetFiles(BackupDirectory, "data-*-after-import.db"));

        // A copy of the JSON put back by hand isn't imported twice.
        File.Copy(UniverseFile("raids.json.migrated"), UniverseFile("raids.json"));
        Assert.Single(new SqliteRaidStore(Databases(), _paths).LoadRaids("tron"));
    }

    [Fact]
    public async Task An_older_database_is_backed_up_then_upgraded()
    {
        await ScanAsync(Repository(), ScanTime, Planet("A", 1, 1, 1));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_paths.UniverseDirectory("tron"), UniverseDatabases.FileName)}"))
        {
            // Back to version 1: the database as the first SQLite build left it.
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE rankings; PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var store = new SqliteRaidStore(Databases(), _paths);

        Assert.Equal(RankingSnapshot.Empty, store.LoadRanking("tron", RankingCategory.Defense)); // the table is back
        Assert.Single(Directory.GetFiles(BackupDirectory, "data-*-before-upgrade.db"));
        Assert.Single((await Repository().GetAsync("tron")).Snapshot()); // the data survived
    }

    [Fact]
    public async Task A_daily_backup_is_made_and_only_the_newest_are_kept()
    {
        await ScanAsync(Repository(), ScanTime, Planet("A", 1, 1, 1));
        Assert.False(Directory.Exists(BackupDirectory)); // a new database without imported data has nothing to back up yet

        Directory.CreateDirectory(BackupDirectory);
        for (var day = 1; day <= 9; day++)
        {
            var old = Path.Combine(BackupDirectory, $"data-old{day}-daily.db");
            File.WriteAllText(old, "");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-day - 1));
        }

        var loaded = await Repository().GetAsync("tron"); // next run: the newest backup is 2 days old
        Assert.Single(loaded.Snapshot());

        var backups = Directory.GetFiles(BackupDirectory, "data-*.db");
        Assert.Equal(UniverseDatabases.KeptBackups, backups.Length);
        Assert.Contains(backups, b => !Path.GetFileName(b).StartsWith("data-old", StringComparison.Ordinal));

        await Repository().GetAsync("tron"); // same day: no new backup
        Assert.Equal(UniverseDatabases.KeptBackups, Directory.GetFiles(BackupDirectory, "data-*.db").Length);
    }

    [Fact]
    public async Task The_backup_holds_the_data()
    {
        await ScanAsync(Repository(), ScanTime, Planet("A", 1, 1, 1));
        await Repository().GetAsync("tron"); // next run makes the daily backup

        var backup = Assert.Single(Directory.GetFiles(BackupDirectory, "data-*-daily.db"));
        File.Copy(backup, Path.Combine(_temp.Path, "restored.db"));
        var restoredPaths = new AppPaths(Path.Combine(_temp.Path, "restored"));
        Directory.CreateDirectory(restoredPaths.UniverseDirectory("tron"));
        File.Move(Path.Combine(_temp.Path, "restored.db"), Path.Combine(restoredPaths.UniverseDirectory("tron"), UniverseDatabases.FileName));

        var restored = await new SqlitePlanetRepository(new UniverseDatabases(restoredPaths, TimeProvider.System, NullLogger<UniverseDatabases>.Instance), restoredPaths).GetAsync("tron");

        Assert.Equal("A", Assert.Single(restored.Snapshot()).PlayerName);
    }
}
