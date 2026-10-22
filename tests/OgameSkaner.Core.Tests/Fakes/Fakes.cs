using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Tests.Fakes;

/// <summary>A temporary directory deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("ogameskaner-tests-").FullName;

    public void Dispose()
    {
        // Pooled SQLite connections keep the database files open.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    /// <summary>The asteroid at position 17 per system (none by default).</summary>
    public Dictionary<SolarSystemAddress, Asteroid> Asteroids { get; } = new();

    public async Task<GalaxySystemView> GetGalaxySystemAsync(SolarSystemAddress address, CancellationToken cancellationToken = default) =>
        new(await GetSolarSystemAsync(address, cancellationToken), Asteroids.GetValueOrDefault(address));

    public List<AsteroidHint> AsteroidHints { get; } = [];

    public Task<IReadOnlyList<AsteroidHint>> GetAsteroidHintsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AsteroidHint>>(AsteroidHints.ToList());

    /// <summary>The mining report list, newest first; one page holds them all.</summary>
    public List<AsteroidReport> AsteroidReports { get; } = [];

    public Task<IReadOnlyList<AsteroidReport>> GetAsteroidReportsAsync(int page = 1, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AsteroidReport>>(page == 1 ? AsteroidReports.ToList() : []);

    public Task SendSpyAsync(Coordinates target, PlanetType targetType, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Fleets of the planets read with an id; <see cref="Fleet"/> for the others.</summary>
    public Dictionary<PlanetId, FleetSnapshot> Fleets { get; } = new();

    public List<PlanetId?> FleetReads { get; } = [];

    /// <summary>When set, answers the reads with an id instead of <see cref="Fleets"/> (e.g. fleets that change).</summary>
    public Func<PlanetId, FleetSnapshot>? FleetOf { get; set; }

    public int EmpireReads { get; private set; }

    /// <summary>
    /// Overrides the empire page; by default it shows <see cref="OwnPlanets"/> and <see cref="OwnMoons"/> with the ships of
    /// their fleets (as the fleet reads would answer), the levels and stock of their <see cref="BuildStates"/> (else the
    /// fleet's stock) and the satellites of <see cref="Shipyards"/>.
    /// </summary>
    public Func<EmpirePage>? Empire { get; set; }

    public Task<EmpirePage> GetEmpireAsync(CancellationToken cancellationToken = default)
    {
        EmpireReads++;
        return Task.FromResult(Empire?.Invoke() ?? new EmpirePage(OwnPlanets.Select(EmpireColumn).ToList(), OwnMoons.Select(EmpireColumn).ToList(), new Dictionary<string, int>()));
    }

    private EmpirePlanet EmpireColumn(OwnPlanet planet)
    {
        var fleet = FleetOf?.Invoke(planet.Id) ?? Fleets.GetValueOrDefault(planet.Id) ?? Fleet;
        var state = BuildStates.GetValueOrDefault(planet.Id);
        var ships = fleet.Ships.ToDictionary(s => s.ShipType, s => s.Quantity, StringComparer.Ordinal);
        if (Shipyards.TryGetValue(planet.Id, out var shipyard))
            ships[HangarPageParser.SolarSatellite] = shipyard.Satellites;
        return new EmpirePlanet(
            planet.Name, planet.Coordinates, 0, 0, 0, 0, 0, null,
            state?.Resources ?? fleet.Resources ?? ResourceSet.Zero, ResourceSet.Zero, state?.StorageCapacity ?? ResourceSet.Zero,
            state?.Levels ?? new Dictionary<BuildingType, int>(), ships, new Dictionary<string, long>());
    }

    public Task<FleetSnapshot> GetFleetAsync(PlanetId? planet = null, CancellationToken cancellationToken = default)
    {
        FleetReads.Add(planet);
        if (planet is { } selected)
            CurrentPlanet = selected;
        if (planet is { } requested && FleetOf is not null)
            return Task.FromResult(FleetOf(requested));
        return Task.FromResult(planet is { } id && Fleets.TryGetValue(id, out var fleet) ? fleet : Fleet);
    }

    public Func<FleetMission, FleetBriefing> Briefing { get; set; } = mission => new FleetBriefing(
        [MissionType.Transport, MissionType.Attack, MissionType.Spy], TimeSpan.FromMinutes(5), 10, 3650,
        "player", "Target", 10, 1_000_000, 0, PlayerStatus.Inactive, null);

    /// <summary>The origin each briefing was asked for (null: the current planet).</summary>
    public List<PlanetId?> BriefingOrigins { get; } = [];

    public Task<FleetBriefing> GetFleetBriefingAsync(FleetMission mission, PlanetId? origin = null, CancellationToken cancellationToken = default)
    {
        BriefingOrigins.Add(origin);
        if (origin is { } id)
            CurrentPlanet = id;
        return Task.FromResult(Briefing(mission));
    }

    public Func<FleetMission, FleetSendOutcome> SendAnswer { get; set; } = _ => new FleetSendOutcome(true);

    /// <summary>The origin of each mission in <see cref="SentMissions"/>.</summary>
    public List<PlanetId?> SentOrigins { get; } = [];

    public Task<FleetSendOutcome> SendFleetAsync(FleetMission mission, PlanetId? origin = null, CancellationToken cancellationToken = default)
    {
        if (origin is { } from)
            CurrentPlanet = from;
        var outcome = SendAnswer(mission);
        if (outcome.Sent)
        {
            SentMissions.Add(mission);
            SentOrigins.Add(origin);
        }
        return Task.FromResult(outcome);
    }

    /// <summary>The event list; <see cref="MovementReads"/> counts the reads.</summary>
    public List<FleetMovement> FleetMovements { get; } = [];

    public int MovementReads { get; private set; }

    /// <summary>Thrown by every event list read when set.</summary>
    public Exception? MovementsError { get; set; }

    public Task<IReadOnlyList<FleetMovement>> GetFleetMovementsAsync(CancellationToken cancellationToken = default)
    {
        MovementReads++;
        if (MovementsError is not null)
            return Task.FromException<IReadOnlyList<FleetMovement>>(MovementsError);
        return Task.FromResult<IReadOnlyList<FleetMovement>>(FleetMovements.ToList());
    }

    /// <summary>The fleets turned back: id and the return button's timestamp.</summary>
    public List<(string FleetId, long Timestamp)> ReturnedFleets { get; } = [];

    public Func<string, FleetSendOutcome> ReturnAnswer { get; set; } = _ => new FleetSendOutcome(true);

    public Task<FleetSendOutcome> ReturnFleetAsync(string fleetId, long timestamp, CancellationToken cancellationToken = default)
    {
        var outcome = ReturnAnswer(fleetId);
        if (outcome.Sent)
            ReturnedFleets.Add((fleetId, timestamp));
        return Task.FromResult(outcome);
    }

    /// <summary>The espionage report list, newest first; one page holds them all.</summary>
    public List<EspionageReport> EspionageReports { get; } = [];

    /// <summary>Details by report id; a report without one comes back unchanged.</summary>
    public Dictionary<string, EspionageReport> ReportDetails { get; } = new();

    public List<string> DetailReads { get; } = [];

    /// <summary>The espionage reports (newest first) are cut into pages of this size.</summary>
    public int ReportPageSize { get; set; } = int.MaxValue;

    public List<int> ReportPageReads { get; } = [];

    public Task<IReadOnlyList<EspionageReport>> GetEspionageReportsAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        ReportPageReads.Add(page);
        return Task.FromResult<IReadOnlyList<EspionageReport>>(
            EspionageReports.Skip((int)Math.Min(int.MaxValue, (long)(page - 1) * ReportPageSize)).Take(ReportPageSize).ToList());
    }

    public Task<EspionageReport> GetEspionageReportDetailAsync(EspionageReport report, CancellationToken cancellationToken = default)
    {
        DetailReads.Add(report.Id);
        return Task.FromResult(ReportDetails.GetValueOrDefault(report.Id) ?? report);
    }

    /// <summary>The defense ranking, cut into pages of <see cref="RankingPageSize"/>.</summary>
    public List<RankingEntry> DefenseRanking { get; } = [];

    public int RankingPageSize { get; set; } = 100;

    public List<int> RankingPageReads { get; } = [];

    public Task<RankingPage> GetRankingPageAsync(RankingCategory category, int page, CancellationToken cancellationToken = default)
    {
        RankingPageReads.Add(page);
        var pages = Math.Max(1, (DefenseRanking.Count + RankingPageSize - 1) / RankingPageSize);
        return Task.FromResult(new RankingPage(DefenseRanking.Skip((page - 1) * RankingPageSize).Take(RankingPageSize).ToList(), pages));
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

    public List<OwnPlanet> OwnMoons { get; } = [];

    public Task<OwnPlanetsSnapshot> GetOwnPlanetsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new OwnPlanetsSnapshot(OwnPlanets.ToList(), CurrentPlanet ?? OwnPlanets.FirstOrDefault()?.Id ?? default) { Moons = OwnMoons.ToList() });

    public Dictionary<PlanetId, ShipyardState> Shipyards { get; } = new();

    public AccountBonuses Bonuses { get; set; } = AccountBonuses.Calibration;

    public int BonusReads { get; private set; }

    public Task<AccountBonuses> GetAccountBonusesAsync(CancellationToken cancellationToken = default)
    {
        BonusReads++;
        return Task.FromResult(Bonuses);
    }

    public List<(PlanetId Planet, string ShipType, int Quantity)> OrderedShips { get; } = [];

    public List<(PlanetId Planet, string DefenseType, int Quantity)> OrderedDefenses { get; } = [];

    /// <summary>The answer to a ship or defense order; queued by default.</summary>
    public Func<PlanetId, EnqueueOutcome> UnitOrderAnswer { get; set; } = _ => EnqueueOutcome.Queued;

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
        var outcome = UnitOrderAnswer(planet);
        if (outcome.Result == EnqueueResult.Queued)
            OrderedShips.Add((planet, shipType, quantity));
        return Task.FromResult(outcome);
    }

    public Task<EnqueueOutcome> BuildDefensesAsync(PlanetId planet, string defenseType, int quantity, CancellationToken cancellationToken = default)
    {
        var outcome = UnitOrderAnswer(planet);
        if (outcome.Result == EnqueueResult.Queued)
            OrderedDefenses.Add((planet, defenseType, quantity));
        return Task.FromResult(outcome);
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

    public Task<PlanetBuildState> GetMoonBuildStateAsync(PlanetId moon, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StateRequests.Add((moon, true));
        CurrentPlanet = moon;
        return Task.FromResult(BuildStates[moon]);
    }

    /// <summary>Formation pages per planet; a planet missing here has no moon, Graviton 4 and plenty of metal.</summary>
    public Dictionary<PlanetId, MoonFormationInfo> MoonFormations { get; } = new();

    /// <summary>Overrides the answer to a moon creation: a message makes the game refuse it.</summary>
    public Func<PlanetId, string?> CreateMoonRefusal { get; set; } = _ => null;

    public List<(PlanetId Planet, int Diameter)> CreatedMoons { get; } = [];

    public Task<MoonFormationInfo> GetMoonFormationAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        CurrentPlanet = planet;
        return Task.FromResult(MoonFormations.TryGetValue(planet, out var info)
            ? info
            : new MoonFormationInfo(planet, 4, 0, MoonFormation.MinDiameter, MoonFormation.MaxDiameter, 1_000_000_000));
    }

    public Task<PlanetId> CreateMoonAsync(PlanetId planet, int diameter, CancellationToken cancellationToken = default)
    {
        CurrentPlanet = planet;
        if (CreateMoonRefusal(planet) is { } refusal)
            throw new OgamexException(refusal);
        CreatedMoons.Add((planet, diameter));
        return Task.FromResult(new PlanetId($"moon-{planet.Value}"));
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

/// <summary>In-memory raid store.</summary>
public sealed class FakeRaidStore : IRaidStore
{
    public RaidSettings Settings { get; set; } = RaidSettings.Default;

    public List<EspionageReport> Reports { get; set; } = [];

    public List<RaidRecord> Raids { get; set; } = [];

    public RaidSettings LoadSettings(string universe) => Settings;

    public void SaveSettings(string universe, RaidSettings settings) => Settings = settings;

    /// <summary>All reports, like the database keeps them; <see cref="LoadReports"/> returns the newest per target.</summary>
    public IReadOnlyList<EspionageReport> LoadReports(string universe) =>
        Reports.GroupBy(r => (r.Target, r.TargetType)).Select(g => g.MaxBy(r => r.Time)!).ToList();

    public IReadOnlyList<EspionageReport> LoadReports(string universe, DateTimeOffset since) =>
        Reports.Where(r => r.Time >= since).OrderBy(r => r.Time).ToList();

    public List<TargetBan> Bans { get; set; } = [];

    public IReadOnlyList<TargetBan> LoadBans(string universe) => Bans.ToList();

    public void AddBans(string universe, IEnumerable<TargetBan> bans) => Bans.AddRange(bans);

    public void LiftBans(string universe, Coordinates target, DateTimeOffset at) =>
        Bans = Bans.Select(b => b.Target == target && b.LiftedAt is null && b.Until > at ? b with { LiftedAt = at } : b).ToList();

    public IReadOnlySet<string> LoadReportIds(string universe, bool withDetailsOnly = false) =>
        Reports.Where(r => !withDetailsOnly || r.Ships is not null || r.Defenses is not null).Select(r => r.Id).ToHashSet();

    public void AddReports(string universe, IEnumerable<EspionageReport> reports)
    {
        foreach (var report in reports)
        {
            Reports.RemoveAll(r => r.Id == report.Id);
            Reports.Add(report);
        }
    }

    public IReadOnlyList<RaidRecord> LoadRaids(string universe) => Raids.ToList();

    public void AddRaids(string universe, IEnumerable<RaidRecord> raids) => Raids.AddRange(raids);

    public RankingSnapshot DefenseRanking { get; set; } = RankingSnapshot.Empty;

    public RankingSnapshot LoadRanking(string universe, RankingCategory category) => DefenseRanking;

    public void SaveRanking(string universe, RankingCategory category, IReadOnlyList<RankingEntry> entries, DateTimeOffset readAt) =>
        DefenseRanking = new RankingSnapshot(readAt, entries.ToDictionary(e => e.PlayerId, e => e.Points));
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

    public Task<IReadOnlyList<SlotObservation>> GetSlotHistoryAsync(string universe, Coordinates coordinates, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SlotObservation>>([]);

    public Task<IReadOnlyList<PlayerObservation>> GetPlayerHistoryAsync(string universe, string? playerId, string playerName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlayerObservation>>([]);
}

public static class TestData
{
    public static readonly DateTimeOffset ScanTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));

    public static PlayerPlanet Planet(string name, int galaxy, int system, int position, bool moon = false, DateTimeOffset? at = null) =>
        new(name, new Coordinates(galaxy, system, position), moon, at ?? ScanTime);
}

public sealed class FakeAsteroidStore : IAsteroidStore
{
    public AsteroidSettings Settings { get; set; } = AsteroidSettings.Default;

    public List<Asteroid> Asteroids { get; } = [];

    public List<AsteroidMission> Missions { get; } = [];

    public List<AsteroidReport> Reports { get; } = [];

    public AsteroidSettings LoadSettings(string universe) => Settings;

    public void SaveSettings(string universe, AsteroidSettings settings) => Settings = settings;

    public IReadOnlyList<Asteroid> LoadAsteroids(string universe, DateTimeOffset after) =>
        Asteroids.Where(a => a.DisappearsAt > after).OrderBy(a => a.DisappearsAt).ToList();

    public void SaveAsteroids(string universe, IEnumerable<Asteroid> asteroids)
    {
        foreach (var asteroid in asteroids)
        {
            var index = Asteroids.FindIndex(a => a.Coordinates == asteroid.Coordinates && a.DisappearsAt > asteroid.SeenAt);
            if (index >= 0)
                Asteroids[index] = Asteroids[index] with { DisappearsAt = asteroid.DisappearsAt };
            else
                Asteroids.Add(asteroid);
        }
    }

    public IReadOnlyList<AsteroidMission> LoadMissions(string universe) => Missions.ToList();

    public void AddMissions(string universe, IEnumerable<AsteroidMission> missions) => Missions.AddRange(missions);

    public void MarkRecalled(string universe, AsteroidMission mission, DateTimeOffset recalledAt)
    {
        for (var i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].Target == mission.Target && Missions[i].SentAt == mission.SentAt)
                Missions[i] = Missions[i] with { RecalledAt = recalledAt };
        }
    }

    public IReadOnlyList<AsteroidReport> LoadReports(string universe) => Reports.OrderBy(r => r.Time).ToList();

    public void AddReports(string universe, IEnumerable<AsteroidReport> reports) =>
        Reports.AddRange(reports.Where(r => Reports.All(k => k.Id != r.Id)));
}

/// <summary>A clock that stands still until something waits on it: a delay passes at once and moves the clock on.</summary>
public sealed class InstantTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_sync)
                _now += dueTime;
            Task.Run(() => callback(state));
        }
        return new NoTimer();
    }

    private sealed class NoTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Action queues that run each group as soon as it's queued, without pauses or switching back home.</summary>
public static class TestQueues
{
    public static IActionQueues Immediate => new ActionQueues(ActionQueueSettings.Immediate, TestLicense.Valid, TimeProvider.System, NullLoggerFactory.Instance);
}
