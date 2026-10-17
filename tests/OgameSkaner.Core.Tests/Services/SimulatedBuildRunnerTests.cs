using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Optimizer;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class SimulatedBuildRunnerTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));

    private static readonly BuildStrategy MetalAndCrystal = Strategy(0, solarMax: 30,
        new BuildingRule(BuildingType.MetalMine, true, RuleMode.Absolute, 2),
        new BuildingRule(BuildingType.CrystalMine, true, RuleMode.Absolute, 1));

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly List<BuildProgress> _progress = [];
    private readonly CancellationTokenSource _cancellation = new();

    public SimulatedBuildRunnerTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.CurrentPlanet = Home.Id;
        _client.BuildStates[Colony.Id] = State(planet: Colony);
    }

    private Task<BuildRunResult> RunAsync(BuildStrategy strategy, BuilderMode mode = BuilderMode.Simulated) =>
        new SimulatedBuildRunner(
                new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System),
                new BuildOrderOptimizer(),
                Options.Create(new OgamexOptions()),
                TimeProvider.System,
                NullLogger<SimulatedBuildRunner>.Instance)
            .RunAsync(_client, new BuildRequest(strategy, new HashSet<PlanetId> { Colony.Id }),
                new SimulatedBuildOptions(SimulationsPerPlan: 20_000, Mode: mode),
                new SyncProgress(p =>
                {
                    _progress.Add(p);
                    // stop instead of waiting for the next cycle
                    if (p.Message.StartsWith("Next check", StringComparison.Ordinal))
                        _cancellation.Cancel();
                }),
                _cancellation.Token);

    [Fact]
    public async Task Plans_the_planet_and_queues_what_it_can_pay()
    {
        var result = await RunAsync(MetalAndCrystal);

        Assert.Equal(BuildOutcome.Cancelled, result.Outcome);
        // 10M of every resource in 11M storages (91% ≥ 70%): the storages come first.
        Assert.Equal(
            [BuildingType.MetalMine, BuildingType.MetalMine, BuildingType.CrystalMine, BuildingType.MetalStorage, BuildingType.CrystalStorage, BuildingType.DeuteriumTank],
            _client.EnqueuedBuildings.Where(e => e.Planet == Colony.Id).Select(e => e.Building).Order());
        Assert.Contains(_progress, p => p.Message.StartsWith("Colony [1:2:8]: planned 6 items to M2 C1 D0", StringComparison.Ordinal));
        Assert.Equal(1, _client.TemperatureReads);
        Assert.Equal(1, _client.BonusReads);
        Assert.Equal(Home.Id, _client.CurrentPlanet); // switched back
    }

    [Fact]
    public async Task Finishes_when_the_targets_are_built_or_queued()
    {
        _client.BuildStates[Colony.Id] = State(
            levels: new() { [BuildingType.MetalMine] = 2 },
            queue: [new QueuedBuilding(BuildingType.CrystalMine, 1, Started: true)],
            planet: Colony);

        var result = await RunAsync(MetalAndCrystal);

        Assert.Equal(BuildOutcome.Completed, result.Outcome);
        Assert.Empty(_client.EnqueuedBuildings);
        Assert.Equal(BuildStopReason.TargetsReached, Assert.Single(result.Planets).Reason);
    }

    [Fact]
    public async Task Other_buildings_are_built_by_price_after_the_simulated_targets()
    {
        _client.BuildStates[Colony.Id] = State(levels: new() { [BuildingType.MetalMine] = 2, [BuildingType.CrystalMine] = 1 }, planet: Colony);

        await RunAsync(MetalAndCrystal with { Rules = [.. MetalAndCrystal.Rules, new BuildingRule(BuildingType.ResearchLab, true, RuleMode.Absolute, 1)] });

        Assert.Equal([(Colony.Id, BuildingType.ResearchLab)], _client.EnqueuedBuildings);
    }

    [Fact]
    public async Task Other_buildings_queue_every_level_the_resources_cover_at_once()
    {
        // 10M of every resource: Research Lab 1–5 cost about 1.2M together, so all go in one cycle (they used to go one per cycle).
        _client.BuildStates[Colony.Id] = State(levels: new() { [BuildingType.MetalMine] = 2, [BuildingType.CrystalMine] = 1 }, planet: Colony);

        await RunAsync(MetalAndCrystal with { Rules = [.. MetalAndCrystal.Rules, new BuildingRule(BuildingType.ResearchLab, true, RuleMode.Absolute, 5)] });

        Assert.Equal(Enumerable.Repeat((Colony.Id, BuildingType.ResearchLab), 5), _client.EnqueuedBuildings.Take(5));
    }

    [Fact]
    public async Task By_price_queues_the_cheapest_levels_without_a_simulation()
    {
        var strategy = MetalAndCrystal with { StorageFillPercent = 0 };

        await RunAsync(strategy, BuilderMode.ByPrice);

        // Metal Mine 1 (60/15) < Crystal Mine 1 (48/24) < Metal Mine 2 (90/22) by weighted value
        Assert.Equal([BuildingType.MetalMine, BuildingType.CrystalMine, BuildingType.MetalMine], _client.EnqueuedBuildings.Select(e => e.Building));
        Assert.DoesNotContain(_progress, p => p.Message.Contains("planned", StringComparison.Ordinal));
        Assert.Equal(0, _client.TemperatureReads);
    }

    private sealed class SyncProgress(Action<BuildProgress> handler) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => handler(value);
    }
}
