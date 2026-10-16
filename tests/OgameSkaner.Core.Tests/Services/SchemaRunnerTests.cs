using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Schemas;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;
using static OgameSkaner.Core.Tests.Services.SchemaPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class SchemaRunnerTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly List<SchemaProgress> _progress = [];
    private readonly CancellationTokenSource _cancellation = new();

    public SchemaRunnerTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.BuildStates[Home.Id] = State(planet: Home);
        _client.BuildStates[Colony.Id] = State(planet: Colony);
    }

    private Task<SchemaRunResult> RunAsync(params SchemaJob[] jobs) =>
        new SchemaRunner(
                new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System),
                Options.Create(new OgamexOptions()),
                TimeProvider.System,
                NullLogger<SchemaRunner>.Instance)
            .RunAsync(_client, jobs, TimeSpan.FromMinutes(5), new SyncProgress(p =>
            {
                _progress.Add(p);
                // stop instead of waiting for the next cycle
                if (p.Message.StartsWith("Next check", StringComparison.Ordinal))
                    _cancellation.Cancel();
            }), _cancellation.Token);

    private static readonly BuildSchema MetalTo3 = Schema((BuildingType.MetalMine, 1), (BuildingType.MetalMine, 2), (BuildingType.MetalMine, 3));

    [Fact]
    public async Task Pushes_every_planets_steps_in_order_and_finishes()
    {
        var result = await RunAsync(new SchemaJob(Home.Id, MetalTo3), new SchemaJob(Colony.Id, Schema((BuildingType.CrystalMine, 1))));

        Assert.Equal(new SchemaRunResult(SchemaRunOutcome.Finished, 4), result);
        Assert.Equal(
            [(Home.Id, BuildingType.MetalMine), (Home.Id, BuildingType.MetalMine), (Home.Id, BuildingType.MetalMine), (Colony.Id, BuildingType.CrystalMine)],
            _client.EnqueuedBuildings);
        Assert.Contains(_progress, p => p.Planet == Home.Id && p.Status == "done");
    }

    [Fact]
    public async Task Switches_back_to_the_planet_selected_before()
    {
        _client.CurrentPlanet = Colony.Id;

        await RunAsync(new SchemaJob(Home.Id, MetalTo3));

        Assert.Equal(Colony.Id, _client.CurrentPlanet);
    }

    [Fact]
    public async Task Waits_for_resources_and_keeps_running_until_stopped()
    {
        _client.BuildStates[Home.Id] = State(planet: Home, resources: ResourceSet.Zero);

        var result = await RunAsync(new SchemaJob(Home.Id, MetalTo3));

        Assert.Equal(SchemaRunOutcome.Cancelled, result.Outcome);
        Assert.Empty(_client.EnqueuedBuildings);
        Assert.Contains(_progress, p => p.Status?.StartsWith("waiting: Metal Mine 1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Notices_when_the_game_answers_success_but_does_not_queue()
    {
        _client.EnqueueAnswer = (_, _) => EnqueueOutcome.Queued;

        var result = await RunAsync(new SchemaJob(Home.Id, MetalTo3));

        Assert.Equal(0, result.Pushed);
        Assert.Contains(_progress, p => p.Message.Contains("did not queue Metal Mine 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Orders_satellites_once_and_counts_them_as_coming()
    {
        var rules = new SchemaRules { SolarMaxLevel = 0, MaxServerQueueDepth = 15, StorageFillPercent = 0, RoboticsMaxLevel = 0 };
        _client.BuildStates[Home.Id] = State(planet: Home, energy: 0);
        _client.Shipyards[Home.Id] = new ShipyardState(Home.Id, 0, 50, new ResourceSet(0, 2_000, 500), SatellitesLocked: false);

        var result = await RunAsync(new SchemaJob(Home.Id, Schema(rules, (BuildingType.MetalMine, 1))));

        Assert.Equal(SchemaRunOutcome.Finished, result.Outcome);
        Assert.Equal([(Home.Id, "SOLAR_SATELLITE", 1)], _client.OrderedShips);
        Assert.Equal([(Home.Id, BuildingType.MetalMine)], _client.EnqueuedBuildings);
    }

    [Fact]
    public async Task Stops_when_the_session_expires()
    {
        _client.EnqueueAnswer = (_, _) => throw new SessionExpiredException("expired");

        var result = await RunAsync(new SchemaJob(Home.Id, MetalTo3));

        Assert.Equal(new SchemaRunResult(SchemaRunOutcome.Failed, 0, "expired"), result);
    }

    [Fact]
    public async Task Other_errors_are_reported_and_retried_next_cycle()
    {
        _client.EnqueueAnswer = (_, _) => throw new OgamexException("timeout");

        var result = await RunAsync(new SchemaJob(Home.Id, MetalTo3));

        Assert.Equal(SchemaRunOutcome.Cancelled, result.Outcome); // reached the wait for the next cycle
        Assert.Contains(_progress, p => p.Status == "error, retrying next cycle");
    }

    private sealed class SyncProgress(Action<SchemaProgress> handler) : IProgress<SchemaProgress>
    {
        public void Report(SchemaProgress value) => handler(value);
    }
}
