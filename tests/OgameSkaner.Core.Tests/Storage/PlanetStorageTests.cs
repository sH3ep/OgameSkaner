using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Storage;

public class PlanetStorageTests
{
    [Fact]
    public void ReplaceSystem_replaces_only_that_system()
    {
        var database = new PlanetDatabase("tron", [Planet("A", 1, 1, 1), Planet("B", 1, 1, 2), Planet("C", 1, 2, 5)]);

        database.ReplaceSystem(new SolarSystemAddress(1, 1), [Planet("D", 1, 1, 3)]);

        Assert.Equal(["D", "C"], database.Snapshot().Select(p => p.PlayerName));
    }

    [Fact]
    public void ReplaceSystem_with_no_planets_clears_the_system()
    {
        var database = new PlanetDatabase("tron", [Planet("A", 1, 1, 1)]);

        database.ReplaceSystem(new SolarSystemAddress(1, 1), []);

        Assert.Equal(0, database.Count);
    }

    [Fact]
    public void ReplaceSystem_rejects_planets_from_other_systems()
    {
        var database = new PlanetDatabase("tron");

        Assert.Throws<ArgumentException>(() => database.ReplaceSystem(new SolarSystemAddress(1, 1), [Planet("A", 1, 2, 1)]));
    }

    [Fact]
    public void Duplicate_positions_keep_the_newest_scan()
    {
        var database = new PlanetDatabase("tron",
        [
            Planet("Old", 1, 1, 1, at: ScanTime),
            Planet("New", 1, 1, 1, at: ScanTime.AddDays(1))
        ]);

        Assert.Equal("New", Assert.Single(database.Snapshot()).PlayerName);
    }

    [Fact]
    public async Task Json_repository_round_trips_planets()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        var planets = new[] { Planet("Bartolo", 1, 1, 2, moon: true), Planet("Admin", 1, 1, 1) };

        var writer = new JsonPlanetRepository(paths);
        var database = await writer.GetAsync("tron");
        database.ReplaceSystem(new SolarSystemAddress(1, 1), planets);
        await writer.SaveAsync(database);

        var reader = new JsonPlanetRepository(paths);
        var loaded = await reader.GetAsync("tron");

        Assert.True(reader.Exists("tron"));
        Assert.Equal(planets.OrderBy(p => p.Coordinates.Position), loaded.Snapshot());
    }

    [Fact]
    public async Task Json_repository_returns_same_instance_per_universe()
    {
        using var temp = new TempDirectory();
        var repository = new JsonPlanetRepository(new AppPaths(temp.Path));

        Assert.Same(await repository.GetAsync("tron"), await repository.GetAsync("tron"));
        Assert.NotSame(await repository.GetAsync("tron"), await repository.GetAsync("orion"));
    }

    [Fact]
    public void Profile_store_persists_profiles_and_selection()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);

        var store = new JsonProfileStore(paths);
        Assert.False(store.Exists);
        store.Save(new UniverseProfile("tron"));
        store.Save(new UniverseProfile("orion") { Galaxies = 5 });
        store.SelectedName = "orion";

        var reloaded = new JsonProfileStore(paths);
        Assert.True(reloaded.Exists);
        Assert.Equal(["orion", "tron"], reloaded.GetAll().Select(p => p.Name));
        Assert.Equal(5, reloaded.Get("orion")!.Galaxies);
        Assert.Equal("orion", reloaded.SelectedName);

        reloaded.Delete("orion");
        Assert.Equal("tron", reloaded.SelectedName);
    }

    [Fact]
    public void Session_store_protects_secrets_on_disk()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        var store = new JsonSessionStore(paths, new FakeSecretProtector());

        store.Save("tron", new OgamexSession("secret-token", "secret-session"));

        var json = File.ReadAllText(paths.SessionsFile);
        Assert.Contains("protected:secret-token", json);
        Assert.Equal(new OgamexSession("secret-token", "secret-session"), new JsonSessionStore(paths, new FakeSecretProtector()).Get("tron"));

        store.Delete("tron");
        Assert.Null(store.Get("tron"));
    }
}
