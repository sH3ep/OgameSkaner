using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;

namespace OgameSkaner.Core.Services.Building;

/// <param name="Planets">The planets to build on; null means all of them.</param>
public sealed record BuildRequest(BuildAutomationMode Mode, BuildStrategy Strategy, IReadOnlyCollection<PlanetId>? Planets = null);

public sealed record BuildProgress(string Message, int TotalQueued);

public enum BuildOutcome
{
    Completed,
    Cancelled,
    Failed
}

public sealed record PlanetBuildSummary(OwnPlanet Planet, int Queued, BuildStopReason Reason);

public sealed record BuildRunResult(BuildOutcome Outcome, int TotalQueued, IReadOnlyList<PlanetBuildSummary> Planets, string? Error = null);

/// <summary>
/// Fills the build queues of the player's planets according to a <see cref="BuildStrategy"/>.
/// Every run starts by refreshing the stored planet list and ends by switching the game back to the planet
/// that was selected before (planet selection is global, so the player's browser follows the run).
/// Requests are spaced by the client's throttling handler, so no delays are added here.
/// </summary>
public sealed class BuildAutomationService(
    OwnPlanetService ownPlanets,
    IOptions<OgamexOptions> options,
    ILogger<BuildAutomationService> logger)
{
    private int QueueCapacity => options.Value.BuildQueueCapacity;

    public async Task<BuildRunResult> RunAsync(
        IOgamexClient client,
        BuildRequest request,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var run = new Run(progress);
        PlanetId? originalPlanet = null;
        try
        {
            var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
            var planets = refresh.List.Planets;
            run.Report($"Planet list refreshed: {planets.Count} planets");
            if (request.Planets is { } selected)
            {
                planets = planets.Where(p => selected.Contains(p.Id)).ToList();
                if (planets.Count == 0)
                    return run.Result(BuildOutcome.Failed, "None of the selected planets was found. Refresh the planet list and select planets.");
            }

            originalPlanet = refresh.CurrentPlanet;

            foreach (var planet in planets)
                run.Summaries[planet.Id] = new PlanetBuildSummary(planet, 0, BuildStopReason.None);

            if (request.Mode == BuildAutomationMode.Single)
                await FillOnceAsync(client, planets, request.Strategy, run, cancellationToken).ConfigureAwait(false);
            else
                await FillUntilDoneAsync(client, planets, request.Strategy, run, cancellationToken).ConfigureAwait(false);

            return run.Result(BuildOutcome.Completed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return run.Result(BuildOutcome.Cancelled);
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Build automation on {Universe} stopped after {Queued} items", client.Universe.Name, run.TotalQueued);
            return run.Result(BuildOutcome.Failed, ex.Message);
        }
        finally
        {
            if (originalPlanet is { } planet)
                await SwitchBackAsync(client, planet, run).ConfigureAwait(false);
        }
    }

    /// <summary>Selects the planet the player had before the run; also after a failure or cancellation.</summary>
    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet, Run run)
    {
        try
        {
            await client.SelectPlanetAsync(planet, CancellationToken.None).ConfigureAwait(false);
            run.Report("Switched back to the planet selected before the run");
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Could not switch {Universe} back to planet {Planet}", client.Universe.Name, planet);
            run.Report($"Could not switch back to the planet selected before the run: {ex.Message}");
        }
    }

    /// <summary>
    /// Single mode: fills every free slot of each planet once with upgrades the planet can afford right now
    /// (the server silently ignores the others), then reads the planet again to count what really got queued.
    /// </summary>
    private async Task FillOnceAsync(IOgamexClient client, IReadOnlyList<OwnPlanet> planets, BuildStrategy strategy, Run run, CancellationToken cancellationToken)
    {
        var includeFacilities = NeedsFacilities(strategy);
        foreach (var planet in planets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await client.GetPlanetBuildStateAsync(planet.Id, includeFacilities, cancellationToken).ConfigureAwait(false);
            var freeSlots = QueueCapacity - state.Queue.Count;
            if (state.VacationMode || freeSlots <= 0)
            {
                run.Finish(planet, state.VacationMode ? BuildStopReason.VacationMode : BuildStopReason.QueueFull);
                continue;
            }

            var plan = BuildPlanner.Plan(state, strategy, freeSlots, requireAffordable: true);
            var reason = plan.Reason;
            var sent = new List<PlannedUpgrade>();
            foreach (var upgrade in plan.Upgrades)
            {
                var result = await EnqueueAsync(client, planet, upgrade, run, cancellationToken).ConfigureAwait(false);
                if (result != BuildStopReason.None)
                {
                    reason = result;
                    break;
                }

                sent.Add(upgrade);
            }

            if (sent.Count > 0)
            {
                var after = await client.GetPlanetBuildStateAsync(planet.Id, includeFacilities: false, cancellationToken).ConfigureAwait(false);
                if (VerifyQueued(after, sent, run) > 0)
                    reason = BuildStopReason.NotEnoughResources;
            }

            run.Finish(planet, reason);
        }
    }

    /// <summary>
    /// Auto mode: every pass adds one affordable upgrade per active planet. A planet leaves the loop when it
    /// runs out of resources, reaches its targets, fills its queue or the server ignores an upgrade; the loop
    /// ends when no planet is left.
    /// </summary>
    private async Task FillUntilDoneAsync(IOgamexClient client, IReadOnlyList<OwnPlanet> planets, BuildStrategy strategy, Run run, CancellationToken cancellationToken)
    {
        var includeFacilities = NeedsFacilities(strategy);
        var lastSent = new Dictionary<PlanetId, PlannedUpgrade>();
        var active = planets.ToList();
        while (active.Count > 0)
        {
            foreach (var planet in active.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = await client.GetPlanetBuildStateAsync(planet.Id, includeFacilities, cancellationToken).ConfigureAwait(false);
                var reason = lastSent.Remove(planet.Id, out var previous) && VerifyQueued(state, [previous], run) > 0
                    ? BuildStopReason.NotEnoughResources
                    : await AddOneAsync(client, state, strategy, run, cancellationToken).ConfigureAwait(false);

                if (reason == BuildStopReason.None)
                {
                    lastSent[planet.Id] = run.LastSent!;
                    continue;
                }

                active.Remove(planet);
                run.Finish(planet, reason);
            }
        }
    }

    private async Task<BuildStopReason> AddOneAsync(IOgamexClient client, PlanetBuildState state, BuildStrategy strategy, Run run, CancellationToken cancellationToken)
    {
        if (state.VacationMode)
            return BuildStopReason.VacationMode;
        if (state.Queue.Count >= QueueCapacity)
            return BuildStopReason.QueueFull;

        var decision = BuildPlanner.NextUpgrade(state, strategy, requireAffordable: true);
        return decision.Upgrade is { } upgrade
            ? await EnqueueAsync(client, state.Planet, upgrade, run, cancellationToken).ConfigureAwait(false)
            : decision.Reason;
    }

    private static async Task<BuildStopReason> EnqueueAsync(IOgamexClient client, OwnPlanet planet, PlannedUpgrade upgrade, Run run, CancellationToken cancellationToken)
    {
        var name = BuildingCatalog.Get(upgrade.Building).DisplayName;
        var outcome = await client.EnqueueBuildingAsync(planet.Id, upgrade.Building, cancellationToken).ConfigureAwait(false);
        switch (outcome.Result)
        {
            case EnqueueResult.Queued:
                run.Queued(planet, upgrade, upgrade.EnergyDeficit
                    ? $"{planet}: queued {name} {upgrade.Level} (warning: energy negative, solar plant at max level)"
                    : $"{planet}: queued {name} {upgrade.Level}");
                return BuildStopReason.None;
            case EnqueueResult.NotEnoughResources:
                return BuildStopReason.NotEnoughResources;
            case EnqueueResult.QueueFull:
                return BuildStopReason.QueueFull;
            default:
                run.Report($"{planet}: server rejected {name} {upgrade.Level}: {outcome.Message ?? "no reason given"}");
                return BuildStopReason.Rejected;
        }
    }

    /// <summary>Counts (and un-counts in the run) upgrades the server answered with "success" but did not queue.</summary>
    private static int VerifyQueued(PlanetBuildState state, IEnumerable<PlannedUpgrade> sent, Run run)
    {
        var missing = sent.Where(u => !state.HasReached(u.Building, u.Level)).ToList();
        foreach (var upgrade in missing)
            run.Ignored(state.Planet, upgrade);
        return missing.Count;
    }

    private static bool NeedsFacilities(BuildStrategy strategy) =>
        strategy.Rules.Any(r => r.Enabled && BuildingCatalog.Get(r.Building).Category == BuildingCategory.Facility);

    private sealed class Run(IProgress<BuildProgress>? progress)
    {
        public Dictionary<PlanetId, PlanetBuildSummary> Summaries { get; } = new();

        public int TotalQueued { get; private set; }

        /// <summary>The upgrade most recently accepted by the server.</summary>
        public PlannedUpgrade? LastSent { get; private set; }

        public void Queued(OwnPlanet planet, PlannedUpgrade upgrade, string message)
        {
            TotalQueued++;
            LastSent = upgrade;
            Summaries[planet.Id] = Summaries[planet.Id] with { Queued = Summaries[planet.Id].Queued + 1 };
            Report(message);
        }

        public void Ignored(OwnPlanet planet, PlannedUpgrade upgrade)
        {
            TotalQueued--;
            Summaries[planet.Id] = Summaries[planet.Id] with { Queued = Summaries[planet.Id].Queued - 1 };
            Report($"{planet}: the server did not queue {BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level} (not enough resources?)");
        }

        public void Finish(OwnPlanet planet, BuildStopReason reason)
        {
            Summaries[planet.Id] = Summaries[planet.Id] with { Reason = reason };
            Report($"{planet}: done – {Describe(reason)}");
        }

        public void Report(string message) => progress?.Report(new BuildProgress(message, TotalQueued));

        public BuildRunResult Result(BuildOutcome outcome, string? error = null) =>
            new(outcome, TotalQueued, Summaries.Values.ToList(), error);
    }

    public static string Describe(BuildStopReason reason) => reason switch
    {
        BuildStopReason.TargetsReached => "all targets reached",
        BuildStopReason.NotEnoughResources => "not enough resources",
        BuildStopReason.QueueFull => "queue full",
        BuildStopReason.Rejected => "rejected by the server",
        BuildStopReason.NoFreeFields => "no free planet fields",
        BuildStopReason.VacationMode => "vacation mode",
        _ => "not finished"
    };
}
