using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Services.BuildPlannerTests;

namespace OgameSkaner.Core.Tests.Services;

public class BuildAutomationServiceTests
{
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(1, 1, 1));
    private static readonly OwnPlanet Colony = new(new PlanetId("2"), "Colony", new Coordinates(1, 2, 8));

    private static readonly BuildStrategy MetalTo3 =
        Strategy(3, rules: [new BuildingRule(BuildingType.MetalMine, true, RuleMode.Relative, 0)]);

    private readonly FakeOgamexClient _client = new(new UniverseProfile("tron"));
    private readonly FakeOwnPlanetStore _planetStore = new();

    public BuildAutomationServiceTests()
    {
        _client.OwnPlanets.AddRange([Home, Colony]);
        _client.BuildStates[Home.Id] = State(planet: Home);
        _client.BuildStates[Colony.Id] = State(planet: Colony);
    }

    private BuildAutomationService CreateService(int queueCapacity = 15) =>
        new(
            new OwnPlanetService(_planetStore, TimeProvider.System),
            Options.Create(new OgamexOptions { BuildQueueCapacity = queueCapacity }),
            NullLogger<BuildAutomationService>.Instance);

    private Task<BuildRunResult> RunAsync(BuildAutomationMode mode, BuildStrategy strategy, int queueCapacity = 15, PlanetId? planet = null) =>
        CreateService(queueCapacity).RunAsync(_client, new BuildRequest(mode, strategy, planet is { } id ? [id] : null));

    private static PlanetBuildSummary Summary(BuildRunResult result, OwnPlanet planet) => result.Planets.Single(p => p.Planet == planet);

    [Fact]
    public async Task Refreshes_and_stores_the_planet_list_before_building()
    {
        _planetStore.Saved["tron"] = new OwnPlanetList([new OwnPlanet(new PlanetId("old"), "Gone", new Coordinates(9, 9, 9))], null);

        await RunAsync(BuildAutomationMode.Single, MetalTo3);

        var stored = _planetStore.Saved["tron"];
        Assert.Equal([Home, Colony], stored.Planets);
        Assert.NotNull(stored.RefreshedAt);
    }

    [Fact]
    public async Task Single_fills_the_free_slots_of_every_planet()
    {
        var result = await RunAsync(BuildAutomationMode.Single, Strategy(10, rules: [new BuildingRule(BuildingType.MetalMine, true, RuleMode.Relative, 0)]), queueCapacity: 3);

        Assert.Equal(BuildOutcome.Completed, result.Outcome);
        Assert.Equal(6, result.TotalQueued);
        Assert.All(result.Planets, p => Assert.Equal(BuildStopReason.QueueFull, p.Reason));
        Assert.Equal([1, 2, 3], _client.BuildStates[Home.Id].Queue.Select(q => q.Level));
    }

    [Fact]
    public async Task Single_on_selected_planet_touches_only_that_planet()
    {
        var result = await RunAsync(BuildAutomationMode.Single, MetalTo3, planet: Colony.Id);

        Assert.Equal(3, result.TotalQueued);
        Assert.All(_client.EnqueuedBuildings, e => Assert.Equal(Colony.Id, e.Planet));
        Assert.Equal(BuildStopReason.TargetsReached, Assert.Single(result.Planets).Reason);
    }

    [Fact]
    public async Task Single_queues_only_what_the_planet_can_afford_and_continues_with_the_next()
    {
        // metal 1 (60/15) and 2 (87/22) fit, metal 3 (126/32) does not; queued items are not paid yet
        _client.BuildStates[Home.Id] = State(planet: Home, resources: new(100, 30, 0));

        var result = await RunAsync(BuildAutomationMode.Single, MetalTo3);

        Assert.Equal(new PlanetBuildSummary(Home, 2, BuildStopReason.NotEnoughResources), Summary(result, Home));
        Assert.Equal(new PlanetBuildSummary(Colony, 3, BuildStopReason.TargetsReached), Summary(result, Colony));
    }

    [Fact]
    public async Task Single_does_not_count_items_the_server_answered_with_success_but_ignored()
    {
        _client.EnqueueAnswer = (planet, _) => planet == Home.Id ? EnqueueOutcome.Queued : null;

        var result = await RunAsync(BuildAutomationMode.Single, MetalTo3);

        Assert.Equal(new PlanetBuildSummary(Home, 0, BuildStopReason.NotEnoughResources), Summary(result, Home));
        Assert.Equal(3, result.TotalQueued);
    }

    [Fact]
    public async Task Auto_drops_only_the_planet_without_resources_and_continues_with_the_others()
    {
        _client.BuildStates[Home.Id] = State(planet: Home, resources: ResourceSet.Zero);

        var result = await RunAsync(BuildAutomationMode.Auto, MetalTo3);

        Assert.Equal(BuildOutcome.Completed, result.Outcome);
        Assert.Equal(BuildStopReason.NotEnoughResources, Summary(result, Home).Reason);
        Assert.Equal(new PlanetBuildSummary(Colony, 3, BuildStopReason.TargetsReached), Summary(result, Colony));
    }

    [Fact]
    public async Task Auto_adds_one_item_per_planet_per_pass()
    {
        await RunAsync(BuildAutomationMode.Auto, MetalTo3);

        Assert.Equal([Home.Id, Colony.Id, Home.Id, Colony.Id, Home.Id, Colony.Id], _client.EnqueuedBuildings.Select(e => e.Planet));
    }

    [Fact]
    public async Task Auto_does_not_count_queued_items_against_resources()
    {
        _client.BuildStates[Home.Id] = State(planet: Home, resources: new(100, 30, 0));

        var result = await RunAsync(BuildAutomationMode.Auto, MetalTo3, planet: Home.Id);

        Assert.Equal(new PlanetBuildSummary(Home, 2, BuildStopReason.NotEnoughResources), Assert.Single(result.Planets));
    }

    [Fact]
    public async Task Auto_drops_a_planet_when_the_server_answered_success_but_did_not_queue()
    {
        _client.EnqueueAnswer = (planet, _) => planet == Home.Id ? EnqueueOutcome.Queued : null;

        var result = await RunAsync(BuildAutomationMode.Auto, MetalTo3);

        Assert.Equal(new PlanetBuildSummary(Home, 0, BuildStopReason.NotEnoughResources), Summary(result, Home));
        Assert.Equal(new PlanetBuildSummary(Colony, 3, BuildStopReason.TargetsReached), Summary(result, Colony));
    }

    [Fact]
    public async Task Auto_stops_a_planet_when_its_queue_is_full_or_the_server_refuses()
    {
        _client.EnqueueAnswer = (planet, _) => planet == Home.Id ? new EnqueueOutcome(EnqueueResult.Rejected, "First, the required technologies must be researched.") : null;
        var messages = new List<string>();

        var result = await CreateService(queueCapacity: 2).RunAsync(_client, new BuildRequest(BuildAutomationMode.Auto, MetalTo3), new SyncProgress(p => messages.Add(p.Message)));

        Assert.Equal(BuildStopReason.Rejected, Summary(result, Home).Reason);
        Assert.Contains(messages, m => m.Contains("First, the required technologies must be researched.", StringComparison.Ordinal));
        Assert.Equal(new PlanetBuildSummary(Colony, 2, BuildStopReason.QueueFull), Summary(result, Colony));
    }

    [Fact]
    public async Task Reads_the_facilities_page_only_when_a_facility_rule_is_enabled()
    {
        await RunAsync(BuildAutomationMode.Auto, MetalTo3, planet: Home.Id);
        Assert.All(_client.StateRequests, r => Assert.False(r.IncludeFacilities));

        _client.StateRequests.Clear();
        await RunAsync(BuildAutomationMode.Auto, Strategy(3, rules: [new BuildingRule(BuildingType.Shipyard, true, RuleMode.Absolute, 1)]), planet: Colony.Id);
        Assert.All(_client.StateRequests, r => Assert.True(r.IncludeFacilities));
    }

    [Theory]
    [InlineData(BuildAutomationMode.Single)]
    [InlineData(BuildAutomationMode.Auto)]
    public async Task Vacation_mode_builds_nothing(BuildAutomationMode mode)
    {
        _client.BuildStates[Home.Id] = State(planet: Home) with { VacationMode = true };

        var result = await RunAsync(mode, MetalTo3, planet: Home.Id);

        Assert.Equal(new PlanetBuildSummary(Home, 0, BuildStopReason.VacationMode), Assert.Single(result.Planets));
        Assert.Empty(_client.EnqueuedBuildings);
    }

    [Fact]
    public async Task Builds_only_on_the_selected_planets()
    {
        var third = new OwnPlanet(new PlanetId("3"), "Third", new Coordinates(2, 2, 2));
        _client.OwnPlanets.Add(third);
        _client.BuildStates[third.Id] = State(planet: third);

        var result = await CreateService().RunAsync(_client, new BuildRequest(BuildAutomationMode.Single, MetalTo3, [Home.Id, third.Id]));

        Assert.Equal([Home, third], result.Planets.Select(p => p.Planet));
        Assert.DoesNotContain(_client.StateRequests, r => r.Planet == Colony.Id);
    }

    [Theory]
    [InlineData(BuildAutomationMode.Single)]
    [InlineData(BuildAutomationMode.Auto)]
    public async Task Switches_back_to_the_planet_selected_before_the_run(BuildAutomationMode mode)
    {
        _client.CurrentPlanet = Colony.Id;

        await RunAsync(mode, MetalTo3);

        Assert.Equal(Colony.Id, _client.SelectedPlanets.Last());
        Assert.Equal(Colony.Id, _client.CurrentPlanet);
    }

    [Fact]
    public async Task Switches_back_also_after_a_failure_and_after_cancelling()
    {
        _client.CurrentPlanet = Colony.Id;
        _client.EnqueueAnswer = (_, _) => throw new OgamexException("boom");
        await RunAsync(BuildAutomationMode.Auto, MetalTo3);
        Assert.Equal(Colony.Id, _client.CurrentPlanet);

        using var cancellation = new CancellationTokenSource();
        _client.EnqueueAnswer = (_, _) =>
        {
            cancellation.Cancel();
            return null;
        };
        await CreateService().RunAsync(_client, new BuildRequest(BuildAutomationMode.Auto, MetalTo3), cancellationToken: cancellation.Token);
        Assert.Equal(Colony.Id, _client.CurrentPlanet);
    }

    [Fact]
    public async Task Unknown_selected_planet_fails()
    {
        var result = await RunAsync(BuildAutomationMode.Single, MetalTo3, planet: new PlanetId("x"));

        Assert.Equal(BuildOutcome.Failed, result.Outcome);
        Assert.Empty(_client.EnqueuedBuildings);
    }

    [Fact]
    public async Task Server_errors_fail_the_run()
    {
        _client.EnqueueAnswer = (_, _) => throw new SessionExpiredException("expired");

        var result = await RunAsync(BuildAutomationMode.Auto, MetalTo3);

        Assert.Equal(BuildOutcome.Failed, result.Outcome);
        Assert.Equal("expired", result.Error);
    }

    [Fact]
    public async Task Can_be_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        _client.EnqueueAnswer = (_, _) =>
        {
            cancellation.Cancel();
            return null;
        };

        var result = await CreateService().RunAsync(_client, new BuildRequest(BuildAutomationMode.Auto, MetalTo3), cancellationToken: cancellation.Token);

        Assert.Equal(BuildOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, result.TotalQueued);
    }

    private sealed class SyncProgress(Action<BuildProgress> handler) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => handler(value);
    }
}
