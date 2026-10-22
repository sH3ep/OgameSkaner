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

    private Task<BuildRunResult> RunAsync(BuildStrategy strategy, BuilderMode mode = BuilderMode.Simulated, ResourceShareSettings? sharing = null) =>
        new SimulatedBuildRunner(
                new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System),
                TestQueues.Immediate,
                new BuildOrderOptimizer(TestLicense.Valid),
                Options.Create(new OgamexOptions()),
                TimeProvider.System,
                NullLogger<SimulatedBuildRunner>.Instance,
                new ResourceSharing(TestQueues.Immediate, TimeProvider.System, NullLogger<ResourceSharing>.Instance))
            .RunAsync(_client, new BuildRequest(strategy, new HashSet<PlanetId> { Colony.Id }),
                new SimulatedBuildOptions(SimulationsPerPlan: 20_000, Mode: mode, Sharing: sharing),
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
    }

    private static readonly BuildStrategy RoboticsOnly = Strategy(0, solarMax: 10,
        new BuildingRule(BuildingType.MetalMine, false, RuleMode.Absolute, 20),
        new BuildingRule(BuildingType.MetalStorage, false, RuleMode.Absolute, 5),
        new BuildingRule(BuildingType.CrystalStorage, false, RuleMode.Absolute, 5),
        new BuildingRule(BuildingType.DeuteriumTank, false, RuleMode.Absolute, 5),
        new BuildingRule(BuildingType.RoboticsFactory, true, RuleMode.Absolute, 3));

    [Fact]
    public async Task Builds_only_the_factories_when_no_mine_is_ticked_even_short_of_energy()
    {
        // The mines lack energy and the solar plant is at its max: with a mine ticked the plan would have to fix that first.
        _client.BuildStates[Colony.Id] = State(
            levels: new() { [BuildingType.MetalMine] = 20, [BuildingType.CrystalMine] = 18, [BuildingType.SolarPlant] = 10, [BuildingType.RoboticsFactory] = 1 },
            energy: -5_000,
            planet: Colony);

        await RunAsync(RoboticsOnly);

        Assert.Equal([BuildingType.RoboticsFactory, BuildingType.RoboticsFactory], _client.EnqueuedBuildings.Select(e => e.Building));
    }

    [Fact]
    public async Task A_planet_without_mines_builds_by_price_from_its_stock_when_no_order_reaches_the_targets()
    {
        // No mines, 10M of each resource (far above the storages): Robotics 24 + Nanite 19 need billions of deuterium,
        // which the planet never produces. It still queues what the stock pays, with no storage for the costs.
        _client.BuildStates[Colony.Id] = State(levels: new() { [BuildingType.RoboticsFactory] = 10 }, energy: 0, planet: Colony);
        var strategy = RoboticsOnly with
        {
            Rules = [.. RoboticsOnly.Rules.Where(r => r.Building != BuildingType.RoboticsFactory),
                new BuildingRule(BuildingType.RoboticsFactory, true, RuleMode.Absolute, 24),
                new BuildingRule(BuildingType.NaniteFactory, true, RuleMode.Absolute, 19)]
        };

        var result = await RunAsync(strategy);

        Assert.Equal(BuildOutcome.Cancelled, result.Outcome); // still running, waiting for resources
        var queued = _client.EnqueuedBuildings.Select(e => e.Building).ToList();
        Assert.Contains(BuildingType.RoboticsFactory, queued);
        Assert.Contains(BuildingType.NaniteFactory, queued);
        Assert.All(queued, b => Assert.Contains(b, (BuildingType[])[BuildingType.RoboticsFactory, BuildingType.NaniteFactory]));
        Assert.Contains(_progress, p => p.Message.Contains("no simulated order reaches the targets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Factories_at_their_targets_finish_the_run_although_energy_is_short()
    {
        _client.BuildStates[Colony.Id] = State(
            levels: new() { [BuildingType.MetalMine] = 20, [BuildingType.CrystalMine] = 18, [BuildingType.SolarPlant] = 10, [BuildingType.RoboticsFactory] = 4 },
            energy: -5_000,
            planet: Colony);

        var result = await RunAsync(RoboticsOnly);

        Assert.Equal(BuildOutcome.Completed, result.Outcome);
        Assert.Empty(_client.EnqueuedBuildings);
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

    [Fact]
    public async Task With_sharing_a_planet_waiting_for_stock_gets_its_next_items_from_another_planet()
    {
        _client.BuildStates[Colony.Id] = State(planet: Colony, resources: ResourceSet.Zero);
        _client.Fleets[Home.Id] = new FleetSnapshot(
            [new ShipStack("HEAVY_CARGO", 100)], Home.Coordinates, new FleetSlots(0, 10),
            new Dictionary<string, long> { ["HEAVY_CARGO"] = 25_000 }, Resources: new ResourceSet(1_000_000, 1_000_000, 1_000_000));

        await RunAsync(MetalAndCrystal with { StorageFillPercent = 0 }, BuilderMode.ByPrice, new ResourceShareSettings(["HEAVY_CARGO"], MinTransportAmount: 0));

        Assert.Empty(_client.EnqueuedBuildings);
        var transport = Assert.Single(_client.SentMissions);
        Assert.Equal((MissionType.Transport, Colony.Coordinates, Home.Id), (transport.Mission, transport.Target, Assert.Single(_client.SentOrigins)));
        // Its next 3 items: Metal Mine 1 (80/20), Crystal Mine 1 (64/32), Metal Mine 2 (118/30) = 262/82, rounded up to thousands.
        Assert.Equal(new ResourceSet(1_000, 1_000, 0), transport.Cargo);
    }

    [Fact]
    public async Task By_level_queues_the_lowest_levels_against_their_targets_without_a_simulation()
    {
        var strategy = Strategy(0, solarMax: 30,
            new BuildingRule(BuildingType.MetalMine, true, RuleMode.Absolute, 3),
            new BuildingRule(BuildingType.CrystalMine, true, RuleMode.Absolute, 1)) with { StorageFillPercent = 0 };

        await RunAsync(strategy, BuilderMode.ByLevel);

        // Metal is 3 below its target, Crystal 1: two Metal levels first (by price Crystal Mine 1 would come second).
        Assert.Equal([BuildingType.MetalMine, BuildingType.MetalMine, BuildingType.CrystalMine], _client.EnqueuedBuildings.Take(3).Select(e => e.Building));
        Assert.Contains(_progress, p => p.Message.Contains("(by level)", StringComparison.Ordinal));
        Assert.Equal(0, _client.TemperatureReads);
    }

    /// <summary>Runs <paramref name="cycles"/> cycles ("Check now" after each); <paramref name="between"/> sees the finished cycle's number.</summary>
    private async Task RunCyclesAsync(BuildStrategy strategy, BuilderMode mode, int cycles, Action<int> between)
    {
        var runner = new SimulatedBuildRunner(
            new OwnPlanetService(new FakeOwnPlanetStore(), TimeProvider.System),
            TestQueues.Immediate,
            new BuildOrderOptimizer(TestLicense.Valid),
            Options.Create(new OgamexOptions()),
            TimeProvider.System,
            NullLogger<SimulatedBuildRunner>.Instance);
        var done = 0;
        await runner.RunAsync(_client, new BuildRequest(strategy, new HashSet<PlanetId> { Colony.Id }),
            new SimulatedBuildOptions(SimulationsPerPlan: 20_000, Mode: mode),
            new SyncProgress(p =>
            {
                _progress.Add(p);
                if (!p.Message.StartsWith("Next check", StringComparison.Ordinal))
                    return;
                between(++done);
                if (done == cycles)
                    _cancellation.Cancel();
                else
                    runner.CheckNow();
            }),
            _cancellation.Token).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private int ColonyReads => _client.StateRequests.Count(r => r.Planet == Colony.Id);

    [Fact]
    public async Task A_planet_waiting_for_resources_is_read_again_only_when_the_empire_page_shows_enough()
    {
        // Metal Mine 1 costs 60/15: 10/10 doesn't pay it.
        _client.BuildStates[Colony.Id] = State(resources: new ResourceSet(10, 10, 0), planet: Colony);
        var reads = new List<int>();

        await RunCyclesAsync(MetalAndCrystal with { StorageFillPercent = 0 }, BuilderMode.ByPrice, cycles: 3, between: cycle =>
        {
            reads.Add(ColonyReads);
            if (cycle == 2)
                _client.BuildStates[Colony.Id] = _client.BuildStates[Colony.Id] with { Resources = new ResourceSet(100, 100, 0) };
        });

        Assert.Equal([1, 1, 2], reads); // the 2nd cycle skips it; the 3rd sees the stock on the empire page
        Assert.Equal(3, _client.EmpireReads);
        Assert.Contains(_progress, p => p.Message.StartsWith("1 of 1 planet(s) unchanged on the empire page", StringComparison.Ordinal));
        Assert.Equal(BuildingType.MetalMine, _client.EnqueuedBuildings.First().Building);
    }

    [Fact]
    public async Task A_level_change_on_the_empire_page_makes_the_planet_read()
    {
        _client.BuildStates[Colony.Id] = State(resources: new ResourceSet(10, 10, 0), planet: Colony);
        var reads = new List<int>();

        await RunCyclesAsync(MetalAndCrystal with { StorageFillPercent = 0 }, BuilderMode.ByPrice, cycles: 2, between: cycle =>
        {
            reads.Add(ColonyReads);
            _client.BuildStates[Colony.Id] = _client.BuildStates[Colony.Id] with { Levels = new Dictionary<BuildingType, int> { [BuildingType.MetalMine] = 1 } };
        });

        Assert.Equal([1, 2], reads);
    }

    [Fact]
    public async Task Without_the_empire_page_every_planet_is_read()
    {
        _client.BuildStates[Colony.Id] = State(resources: new ResourceSet(10, 10, 0), planet: Colony);
        _client.Empire = () => throw new OgamexException("markup changed");
        var reads = new List<int>();

        await RunCyclesAsync(MetalAndCrystal with { StorageFillPercent = 0 }, BuilderMode.ByPrice, cycles: 2, between: _ => reads.Add(ColonyReads));

        Assert.Equal([1, 2], reads);
        Assert.Contains(_progress, p => p.Message.StartsWith("Reading the empire page failed", StringComparison.Ordinal));
    }

    private sealed class SyncProgress(Action<BuildProgress> handler) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => handler(value);
    }
}
