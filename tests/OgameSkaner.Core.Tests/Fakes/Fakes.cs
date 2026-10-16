using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
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

    public List<OwnPlanet> OwnPlanets { get; } = [];

    /// <summary>Current state per planet; queued items are appended (unstarted) by <see cref="EnqueueBuildingAsync"/>.</summary>
    public Dictionary<PlanetId, PlanetBuildState> BuildStates { get; } = new();

    /// <summary>Overrides the server answer; null means "behave like the server".</summary>
    public Func<PlanetId, BuildingType, EnqueueOutcome?> EnqueueAnswer { get; set; } = (_, _) => null;

    public List<(PlanetId Planet, BuildingType Building)> EnqueuedBuildings { get; } = [];

    public List<(PlanetId Planet, bool IncludeFacilities)> StateRequests { get; } = [];

    /// <summary>The planet selected in the game; reading a planet's state selects it, like on the server.</summary>
    public PlanetId? CurrentPlanet { get; set; }

    public List<PlanetId> SelectedPlanets { get; } = [];

    public Task<OwnPlanetsSnapshot> GetOwnPlanetsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OwnPlanetsSnapshot(OwnPlanets.ToList(), CurrentPlanet ?? OwnPlanets.FirstOrDefault()?.Id ?? default));

    public Dictionary<PlanetId, ShipyardState> Shipyards { get; } = new();

    public AccountBonuses Bonuses { get; set; } = AccountBonuses.Calibration;

    public int BonusReads { get; private set; }

    public Task<AccountBonuses> GetAccountBonusesAsync(CancellationToken cancellationToken = default)
    {
        BonusReads++;
        return Task.FromResult(Bonuses);
    }

    public List<(PlanetId Planet, string ShipType, int Quantity)> OrderedShips { get; } = [];

    public Dictionary<PlanetId, (int Min, int Max)> Temperatures { get; } = new();

    public int TemperatureReads { get; private set; }

    public Task<(int Min, int Max)> GetPlanetTemperatureAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        TemperatureReads++;
        CurrentPlanet = planet;
        return Task.FromResult(Temperatures.TryGetValue(planet, out var temperature) ? temperature : (28, 32));
    }

    public Task<ShipyardState> GetShipyardStateAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        CurrentPlanet = planet;
        return Task.FromResult(Shipyards.TryGetValue(planet, out var shipyard)
            ? shipyard
            : new ShipyardState(planet, 0, 0, ResourceSet.Zero, SatellitesLocked: true));
    }

    public Task<EnqueueOutcome> BuildShipsAsync(PlanetId planet, string shipType, int quantity, CancellationToken cancellationToken = default)
    {
        OrderedShips.Add((planet, shipType, quantity));
        return Task.FromResult(EnqueueOutcome.Queued);
    }

    public Task SelectPlanetAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        SelectedPlanets.Add(planet);
        CurrentPlanet = planet;
        return Task.CompletedTask;
    }

    public Task<PlanetBuildState> GetPlanetBuildStateAsync(PlanetId planet, bool includeFacilities = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StateRequests.Add((planet, includeFacilities));
        CurrentPlanet = planet;
        return Task.FromResult(BuildStates[planet]);
    }

    public Task<EnqueueOutcome> EnqueueBuildingAsync(PlanetId planet, BuildingType building, CancellationToken cancellationToken = default)
    {
        if (EnqueueAnswer(planet, building) is { } answer)
            return Task.FromResult(answer);

        // Like the server: checked against the actual resources (paid only when the item starts), and an
        // unaffordable item is silently ignored while the answer still says success.
        var state = BuildStates[planet];
        var level = state.Queue.Where(q => q.Building == building).Select(q => q.Level).DefaultIfEmpty(state.LevelOf(building)).Max() + 1;
        if (state.Queue.Count >= 15)
            return Task.FromResult(new EnqueueOutcome(EnqueueResult.QueueFull, "Construction queue is full."));
        if (!state.Resources.CanAfford(BuildingCatalog.CostOf(building, level)))
            return Task.FromResult(EnqueueOutcome.Queued);

        BuildStates[planet] = state with { Queue = [.. state.Queue, new QueuedBuilding(building, level, Started: false)] };
        EnqueuedBuildings.Add((planet, building));
        return Task.FromResult(EnqueueOutcome.Queued);
    }

    public void Dispose()
    {
    }
}

/// <summary>In-memory planet list store.</summary>
public sealed class FakeOwnPlanetStore : IOwnPlanetStore
{
    public Dictionary<string, OwnPlanetList> Saved { get; } = new();

    public OwnPlanetList Load(string universe) => Saved.GetValueOrDefault(universe) ?? OwnPlanetList.Empty;

    public void Save(string universe, OwnPlanetList planets) => Saved[universe] = planets;
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
