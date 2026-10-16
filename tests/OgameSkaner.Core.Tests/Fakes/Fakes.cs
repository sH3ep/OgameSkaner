using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Tests.Fakes;

/// <summary>A temporary directory deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("ogameskaner-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Marks protected text instead of encrypting it, so tests can inspect stored values.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => "protected:" + plainText;

    public string Unprotect(string protectedText) => protectedText["protected:".Length..];
}

/// <summary>Returns planets from a callback instead of calling the server.</summary>
public sealed class FakeOgamexClient(UniverseProfile universe) : IOgamexClient
{
    public Func<SolarSystemAddress, IReadOnlyList<PlayerPlanet>> SolarSystems { get; set; } = _ => [];

    public List<SolarSystemAddress> RequestedSystems { get; } = [];

    public FleetSnapshot Fleet { get; set; } = new([], new Coordinates(1, 1, 1));

    public List<FleetMission> SentMissions { get; } = [];

    public UniverseProfile Universe { get; } = universe;

    public Task<IReadOnlyList<PlayerPlanet>> GetSolarSystemAsync(SolarSystemAddress address, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedSystems.Add(address);
        return Task.FromResult(SolarSystems(address));
    }

    public Task SendSpyAsync(Coordinates target, PlanetType targetType, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FleetSnapshot> GetFleetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Fleet);

    public Task SendFleetAsync(FleetMission mission, CancellationToken cancellationToken = default)
    {
        SentMissions.Add(mission);
        return Task.CompletedTask;
    }

    public Task<bool> CheckSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public void Dispose()
    {
    }
}

/// <summary>In-memory repository that counts saves.</summary>
public sealed class FakePlanetRepository : IPlanetRepository
{
    private readonly Dictionary<string, PlanetDatabase> _databases = new();

    public int SaveCount { get; private set; }

    public Task<PlanetDatabase> GetAsync(string universe, CancellationToken cancellationToken = default)
    {
        if (!_databases.TryGetValue(universe, out var database))
            _databases[universe] = database = new PlanetDatabase(universe);
        return Task.FromResult(database);
    }

    public Task SaveAsync(PlanetDatabase database, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public bool Exists(string universe) => _databases.ContainsKey(universe);
}

public static class TestData
{
    public static readonly DateTimeOffset ScanTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));

    public static PlayerPlanet Planet(string name, int galaxy, int system, int position, bool moon = false, DateTimeOffset? at = null) =>
        new(name, new Coordinates(galaxy, system, position), moon, at ?? ScanTime);
}
