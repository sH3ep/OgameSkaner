using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Alerts;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Defense;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Services;

public class AttackMonitorTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Near = new(new PlanetId("2"), "Near", new Coordinates(8, 262, 5));
    private static readonly OwnPlanet Selected = new(new PlanetId("3"), "Selected", new Coordinates(1, 1, 1));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly FakeAlertChannel _channel = new("windows");
    private readonly FakeDefenseSettingsStore _settingsStore = new();
    private readonly InstantTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _log = [];
    private readonly List<IReadOnlyList<Threat>> _threats = [];

    public AttackMonitorTests()
    {
        _client.OwnPlanets.AddRange([Home, Near, Selected]);
        _client.CurrentPlanet = Selected.Id;
        _client.Fleets[Home.Id] = new FleetSnapshot(
            [new ShipStack("LIGHT_CARGO", 10), new ShipStack("SOLAR_SATELLITE", 50)], Home.Coordinates, new FleetSlots(0, 10),
            new Dictionary<string, long> { ["LIGHT_CARGO"] = 1_000 }, Resources: new ResourceSet(50_000, 2_000, 5_000));
        _client.Briefing = mission => new FleetBriefing(
            [MissionType.Deploy, MissionType.Transport], TimeSpan.FromMinutes(20), 100, 5, null, null, null, 0, 0, PlayerStatus.None, null);
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private AttackMonitor Monitor() => new(
        new OwnPlanetService(new FakeOwnPlanetStore(), _time),
        TestQueues.Immediate,
        new AlertDispatcher([_channel], _settingsStore, NullLogger<AlertDispatcher>.Instance),
        _time,
        NullLogger<AttackMonitor>.Instance);

    private FleetMovement Hostile(string id, MovementMission mission, TimeSpan inTime, OwnPlanet? target = null, string? targetName = null) =>
        new(id, mission, MovementRelation.Hostile, false, Now + inTime, new Coordinates(8, 250, 7), "Raider",
            (target ?? Home).Coordinates, targetName ?? (target ?? Home).Name, 500, "Evil");

    /// <summary>Runs until <paramref name="checks"/> checks are done (each ends with "Next check").</summary>
    private Task<DefenseRunResult> RunAsync(DefenseSettings settings, int checks)
    {
        var cancellation = new CancellationTokenSource();
        var done = 0;
        return Monitor().RunAsync(_client, settings, new SyncProgress(p =>
        {
            _log.Add(p.Message);
            if (p.Threats is not null)
                _threats.Add(p.Threats);
            if (p.Message.StartsWith("Next check", StringComparison.Ordinal) && ++done == checks)
                cancellation.Cancel();
        }), cancellation.Token);
    }

    [Fact]
    public async Task An_attack_is_alerted_once_and_the_fleet_escapes_to_the_moon_shortly_before_it_lands()
    {
        var attack = Hostile("a", MovementMission.Attack, TimeSpan.FromMinutes(5));
        _client.FleetMovements.Add(attack);

        var result = await RunAsync(DefenseSettings.Default with { CheckIntervalSeconds = 60, EscapeSecondsBeforeArrival = 60 }, checks: 6);

        Assert.Equal(new DefenseRunResult(DefenseOutcome.Cancelled, Detected: 1, Escapes: 1), result);
        var mission = Assert.Single(_client.SentMissions);
        Assert.Equal((Home.Coordinates, PlanetType.Moon, MissionType.Deploy, 10), (mission.Target, mission.TargetType, mission.Mission, mission.SpeedPercentage));
        Assert.Equal([new ShipStack("LIGHT_CARGO", 10)], mission.Ships); // the satellites stay
        Assert.Equal(new ResourceSet(3_100, 2_000, 4_900), mission.Cargo); // 10,000 cargo: deuterium minus 100 fuel first
        Assert.Equal(Home.Id, Assert.Single(_client.SentOrigins));

        Assert.Equal(
            [(AlertSeverity.Critical, "Attack on Home [8:260:3]"), (AlertSeverity.Info, "Fleet escaped from Home")],
            _channel.Received.Select(a => (a.Severity, a.Title)));
        Assert.Contains("Escaped to its moon", _threats[^1].Select(t => t.State));
    }

    [Fact]
    public async Task Without_a_moon_the_fleet_goes_to_the_nearest_planet()
    {
        _client.FleetMovements.Add(Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(30)));
        var normal = _client.Briefing;
        _client.Briefing = mission => mission.TargetType == PlanetType.Moon ? throw new OgamexException("No moon there.") : normal(mission);

        await RunAsync(DefenseSettings.Default, checks: 1);

        var mission = Assert.Single(_client.SentMissions);
        Assert.Equal((Near.Coordinates, PlanetType.Planet, 10), (mission.Target, mission.TargetType, mission.SpeedPercentage));
    }

    [Fact]
    public async Task A_failed_escape_is_a_critical_alert()
    {
        _client.FleetMovements.Add(Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(30)));
        _client.SendAnswer = _ => new FleetSendOutcome(false, "No free fleet slot.");

        await RunAsync(DefenseSettings.Default, checks: 1);

        var alert = _channel.Received[^1];
        Assert.Equal((AlertSeverity.Critical, "Fleet escape from Home failed"), (alert.Severity, alert.Title));
        Assert.Contains("No free fleet slot.", alert.Message);
    }

    [Fact]
    public async Task Test_mode_sends_nothing()
    {
        _client.FleetMovements.Add(Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(30)));

        await RunAsync(DefenseSettings.Default with { TestMode = true }, checks: 1);

        Assert.Empty(_client.SentMissions);
        Assert.Contains(_log, l => l.StartsWith("[Test] Would send 10 ship(s) from Home", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Spies_moons_and_own_fleets_are_alerted_but_not_escaped()
    {
        _client.FleetMovements.AddRange([
            Hostile("s", MovementMission.Espionage, TimeSpan.FromSeconds(10)),
            Hostile("m", MovementMission.Attack, TimeSpan.FromSeconds(10), targetName: "Moon"),
            Hostile("o", MovementMission.Attack, TimeSpan.FromSeconds(10)) with { Relation = MovementRelation.Own }]);

        await RunAsync(DefenseSettings.Default, checks: 2);

        Assert.Empty(_client.SentMissions);
        Assert.Equal(
            [(AlertSeverity.Warning, "Espionage on Home [8:260:3]"), (AlertSeverity.Critical, "Attack on the moon Moon [8:260:3]")],
            _channel.Received.Select(a => (a.Severity, a.Title)).OrderBy(a => a.Severity));
    }

    [Fact]
    public async Task An_expired_session_stops_the_protection_with_an_alert()
    {
        _client.MovementsError = new SessionExpiredException("Login page.");

        var result = await Monitor().RunAsync(_client, DefenseSettings.Default);

        Assert.Equal(DefenseOutcome.Failed, result.Outcome);
        Assert.Equal("Attack protection stopped", Assert.Single(_channel.Received).Title);
    }

    [Fact]
    public async Task The_dispatcher_uses_only_enabled_channels_and_survives_a_failing_one()
    {
        var broken = new FakeAlertChannel("mail", fail: true);
        var off = new FakeAlertChannel("sms");
        _settingsStore.Alerts = AlertSettings.Default with { EnabledChannels = ["windows", "mail"] };
        var dispatcher = new AlertDispatcher([broken, _channel, off], _settingsStore, NullLogger<AlertDispatcher>.Instance);

        var failed = await dispatcher.SendAsync(new Alert("tron", "Attack", "now", AlertSeverity.Critical));

        Assert.Equal(["mail"], failed);
        Assert.Single(_channel.Received);
        Assert.Empty(off.Received);
    }

    private sealed class SyncProgress(Action<DefenseProgress> handler) : IProgress<DefenseProgress>
    {
        public void Report(DefenseProgress value) => handler(value);
    }

    private sealed class FakeAlertChannel(string id, bool fail = false) : IAlertChannel
    {
        public List<Alert> Received { get; } = [];

        public string Id => id;

        public string DisplayName => id;

        public Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            if (fail)
                throw new InvalidOperationException("Mail server down.");
            Received.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDefenseSettingsStore : IDefenseSettingsStore
    {
        public AlertSettings Alerts { get; set; } = AlertSettings.Default;

        public DefenseSettings Load(string universe) => DefenseSettings.Default;

        public void Save(string universe, DefenseSettings settings)
        {
        }

        public AlertSettings LoadAlerts() => Alerts;

        public void SaveAlerts(AlertSettings settings) => Alerts = settings;
    }
}
