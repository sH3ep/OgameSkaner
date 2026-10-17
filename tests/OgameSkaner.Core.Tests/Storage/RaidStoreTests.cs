using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Sqlite;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class RaidStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 22, 0, 0, TimeSpan.FromHours(2));

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SqliteRaidStore Store()
    {
        var paths = new AppPaths(_temp.Path);
        return new SqliteRaidStore(new UniverseDatabases(paths, TimeProvider.System, NullLogger<UniverseDatabases>.Instance), paths);
    }

    [Fact]
    public void Settings_default_until_saved_then_round_trip()
    {
        Assert.Equal(RaidSettings.Default, Store().LoadSettings("tron"));

        var settings = RaidSettings.Default with { MaxDefensePoints = 50, CargoShipType = "LIGHT_CARGO", UseNearestPlanet = true };
        Store().SaveSettings("tron", settings);

        Assert.Equal(settings, Store().LoadSettings("tron"));
        Assert.Equal(RaidSettings.Default, Store().LoadSettings("other"));
    }

    [Fact]
    public void Reports_round_trip_and_keep_the_newest_per_target()
    {
        var target = new Coordinates(5, 288, 3);
        var older = Report("a", target, Now.AddHours(-1));
        var newer = Report("b", target, Now) with
        {
            Ships = new UnitSection(0, []),
            Defenses = new UnitSection(2_000, [new UnitCount("MISSILE_LAUNCHER", 1_000)])
        };
        var hidden = Report("c", new Coordinates(8, 250, 10), Now);

        Store().AddReports("tron", [older, newer, hidden]);
        var loaded = Store().LoadReports("tron");

        Assert.Equal(2, loaded.Count);
        var full = loaded.Single(r => r.Target == target);
        Assert.Equal("b", full.Id);
        Assert.Equal(2_000, full.DefensePoints);
        Assert.Equal(0, full.FleetPoints);
        Assert.Equal([new UnitCount("MISSILE_LAUNCHER", 1_000)], full.Defenses!.Units);
        Assert.Equal(newer.Resources, full.Resources);
        Assert.Equal(PlayerStatus.Inactive, full.PlayerStatus);
        Assert.Null(loaded.Single(r => r.Id == "c").Defenses);
        Assert.Equal(["a", "b", "c"], Store().LoadReportIds("tron").Order()); // older reports stay as history
        Assert.Equal(["b"], Store().LoadReportIds("tron", withDetailsOnly: true));
    }

    [Fact]
    public void A_report_read_again_without_details_keeps_its_ships_and_defenses()
    {
        var target = new Coordinates(5, 288, 3);
        var detailed = Report("a", target, Now) with { Defenses = new UnitSection(2_000, [new UnitCount("MISSILE_LAUNCHER", 1_000)]) };

        Store().AddReports("tron", [detailed]);
        Store().AddReports("tron", [Report("a", target, Now)]);

        Assert.Equal(2_000, Assert.Single(Store().LoadReports("tron")).DefensePoints);
    }

    [Fact]
    public void A_ranking_read_replaces_the_previous_one()
    {
        Assert.Equal(RankingSnapshot.Empty, Store().LoadRanking("tron", RankingCategory.Defense));

        Store().SaveRanking("tron", RankingCategory.Defense, [new RankingEntry(1, "a", "A", 500, PlayerStatus.None), new RankingEntry(2, "b", "B", 0, PlayerStatus.Inactive)], Now);
        Store().SaveRanking("tron", RankingCategory.Defense, [new RankingEntry(1, "b", "B", 7, PlayerStatus.Inactive)], Now.AddHours(1));

        var ranking = Store().LoadRanking("tron", RankingCategory.Defense);
        Assert.Equal(Now.AddHours(1), ranking.ReadAt);
        Assert.Equal(new Dictionary<string, long> { ["b"] = 7 }, ranking.PointsByPlayerId);
        Assert.Null(ranking.PointsOf("a"));
    }

    [Fact]
    public void Raids_round_trip()
    {
        var raid = new RaidRecord(
            new Coordinates(5, 288, 3), PlanetType.Planet, "Kowalski", MissionType.Attack, new Coordinates(8, 260, 3),
            "HEAVY_CARGO", 120, 3_000_000_000, Now, Now.AddMinutes(10), Now.AddMinutes(20));

        var later = raid with { SentAt = Now.AddHours(1) };

        Store().AddRaids("tron", [later]);
        Store().AddRaids("tron", [raid]);

        Assert.Equal([raid, later], Store().LoadRaids("tron")); // oldest first

    }

    [Fact]
    public async Task Planet_database_loads_version_1_files_and_saves_status_and_rank()
    {
        var path = Path.Combine(_temp.Path, "universes", "tron", "planets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {"version":1,"planets":[{"player":"Old","galaxy":1,"system":2,"position":3,"moon":true,"scannedAt":"2026-01-02T03:04:05+01:00"}]}
            """);

        var repository = new JsonPlanetRepository(new AppPaths(_temp.Path));
        var database = await repository.GetAsync("tron");
        var old = Assert.Single(database.Snapshot());
        Assert.Equal(new PlayerPlanet("Old", new Coordinates(1, 2, 3), true, TestData.ScanTime), old);

        database.ReplaceSystem(new SolarSystemAddress(1, 2), [old with { Status = PlayerStatus.Inactive | PlayerStatus.Strong, PlayerId = "p", Rank = 72 }]);
        await repository.SaveAsync(database);
        var reloaded = await new JsonPlanetRepository(new AppPaths(_temp.Path)).GetAsync("tron");

        var planet = Assert.Single(reloaded.Snapshot());
        Assert.Equal(PlayerStatus.Inactive | PlayerStatus.Strong, planet.Status);
        Assert.Equal("p", planet.PlayerId);
        Assert.Equal(72, planet.Rank);
    }

    private static EspionageReport Report(string id, Coordinates target, DateTimeOffset time) =>
        new(id, target, PlanetType.Planet, time, "Kowalski", "p2", PlayerStatus.Inactive, new ResourceSet(4_000, 1_000, 500), 3);
}
