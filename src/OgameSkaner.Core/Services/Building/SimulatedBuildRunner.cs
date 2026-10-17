using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services.Optimizer;

namespace OgameSkaner.Core.Services.Building;

/// <summary>
/// The Builder's Simulated mode: a continuous loop over the selected planets. Each cycle reads a planet, lets
/// <see cref="BuildOrderOptimizer"/> find the fastest order from its real state to the strategy's targets (about
/// <see cref="SimulatedBuildOptions.SimulationsPerPlan"/> simulated orders) and pushes the beginning of that order that
/// the planet can pay now (<see cref="SimulatedPlanner.Push"/>). Then it switches the game back to the player's planet
/// and sleeps until the next planned item is due (at most <see cref="SimulatedBuildOptions.CheckInterval"/>).
/// </summary>
/// <remarks>
/// A planet is planned again whenever it changed (something got built, queued, or the player did something by hand);
/// otherwise the previous plan is reused. The previous plan also seeds the next search. The account's bonuses are read
/// at the start and every hour; each planet's temperature once per run. Targets of buildings the simulation doesn't
/// know are built by price (<see cref="BuildPlanner"/>) once the simulated targets are queued.
/// </remarks>
public sealed class SimulatedBuildRunner(
    OwnPlanetService ownPlanets,
    BuildOrderOptimizer optimizer,
    IOptions<OgamexOptions> options,
    TimeProvider timeProvider,
    ILogger<SimulatedBuildRunner> logger)
{
    private static readonly TimeSpan BonusRefresh = TimeSpan.FromHours(1);

    /// <summary>A plan is redone after this long even when the planet didn't change (production drifts from the model).</summary>
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);

    private int QueueCapacity => options.Value.BuildQueueCapacity;

    public async Task<BuildRunResult> RunAsync(
        IOgamexClient client,
        BuildRequest request,
        SimulatedBuildOptions settings,
        IProgress<BuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var run = new Run(progress);
        try
        {
            var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
            var planets = refresh.List.Planets.Where(p => request.Planets?.Contains(p.Id) ?? true).ToList();
            if (planets.Count == 0)
                return run.Result(BuildOutcome.Failed, "None of the selected planets was found. Refresh the planet list and select planets.");
            foreach (var planet in planets)
                run.Summaries[planet.Id] = new PlanetBuildSummary(planet, 0, BuildStopReason.None);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (allDone, nextIn) = await RunCycleAsync(client, planets, request.Strategy, settings, run, cancellationToken).ConfigureAwait(false);
                if (allDone)
                {
                    run.Report("Every planet has its targets built or queued.");
                    return run.Result(BuildOutcome.Completed);
                }

                var wait = nextIn is { } due
                    ? TimeSpan.FromTicks(Math.Clamp(due.Ticks, SimulatedBuildOptions.MinCheckInterval.Ticks, settings.CheckInterval.Ticks))
                    : settings.CheckInterval;
                run.Report($"Next check at {timeProvider.GetLocalNow().Add(wait):T}");
                await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return run.Result(BuildOutcome.Cancelled);
        }
        catch (SessionExpiredException ex)
        {
            return run.Result(BuildOutcome.Failed, ex.Message);
        }
    }

    /// <summary>One pass over the planets; whether all are done and how soon the next item is due.</summary>
    private async Task<(bool AllDone, TimeSpan? NextIn)> RunCycleAsync(
        IOgamexClient client, IReadOnlyList<OwnPlanet> planets, BuildStrategy strategy, SimulatedBuildOptions settings, Run run, CancellationToken cancellationToken)
    {
        OwnPlanetRefresh refresh;
        try
        {
            refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OgamexException ex) when (ex is not SessionExpiredException)
        {
            run.Report($"Reading the planet list failed: {ex.Message}");
            return (false, null);
        }

        var allDone = true;
        TimeSpan? nextIn = null;
        try
        {
            if (run.Bonuses is null || timeProvider.GetUtcNow() - run.BonusesReadAt > BonusRefresh)
            {
                run.Bonuses = await client.GetAccountBonusesAsync(cancellationToken).ConfigureAwait(false);
                run.BonusesReadAt = timeProvider.GetUtcNow();
                run.Report($"Account bonuses read: {run.Bonuses.ClassName}, construction speed +{run.Bonuses.ConstructionSpeed:P0}");
            }

            foreach (var planet in planets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var (done, due) = await ProcessPlanetAsync(client, planet, strategy, settings, run, cancellationToken).ConfigureAwait(false);
                    allDone &= done;
                    if (due is { } d && (nextIn is null || d < nextIn))
                        nextIn = d;
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Simulated build on {Planet} failed this cycle", planet);
                    run.Report($"{planet}: {ex.Message} (retrying next cycle)");
                    allDone = false;
                }
            }
        }
        catch (OgamexException ex) when (ex is not SessionExpiredException)
        {
            run.Report($"Reading the account bonuses failed: {ex.Message}");
            allDone = false;
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, run).ConfigureAwait(false);
        }

        return (allDone, nextIn);
    }

    /// <summary>Plans one planet (or reuses its plan) and pushes what it can pay; returns whether it's done and when to come back.</summary>
    private async Task<(bool Done, TimeSpan? NextIn)> ProcessPlanetAsync(
        IOgamexClient client, OwnPlanet planet, BuildStrategy strategy, SimulatedBuildOptions settings, Run run, CancellationToken cancellationToken)
    {
        var state = await client.GetPlanetBuildStateAsync(planet.Id, includeFacilities: true, cancellationToken).ConfigureAwait(false);
        var memory = run.Planet(planet.Id);

        // The game answers "success" even when it ignores an item, so check that the last pushed ones really got queued.
        var ignored = memory.LastPushed.Where(u => !state.HasReached(u.Building, u.Level)).ToList();
        memory.LastPushed.Clear();
        foreach (var upgrade in ignored)
            run.Ignored(planet, upgrade);
        if (ignored.Count > 0)
            memory.Plan = null;

        if (state.VacationMode)
        {
            run.Finish(planet, BuildStopReason.VacationMode);
            return (false, null);
        }

        if (settings.Mode == BuilderMode.ByPrice)
            return await BuildByPriceAsync(client, state, strategy, "", run, cancellationToken).ConfigureAwait(false);

        var shipyard = settings.UseSolarSatellites && state.LevelOf(BuildingType.Shipyard) > 0
            ? await client.GetShipyardStateAsync(planet.Id, cancellationToken).ConfigureAwait(false)
            : null;
        var pendingSatellites = memory.PendingSatellites(shipyard, timeProvider.GetUtcNow());
        memory.Temperature ??= await client.GetPlanetTemperatureAsync(planet.Id, cancellationToken).ConfigureAwait(false);

        var setup = SimulatedPlanner.SetupFor(state, strategy, settings, run.Bonuses!, memory.Temperature.Value, shipyard?.Satellites ?? 0, pendingSatellites);
        var signature = Signature(state, setup, shipyard?.Satellites ?? 0, pendingSatellites);
        var now = timeProvider.GetUtcNow();
        if (memory.Plan is null || memory.Signature != signature || now - memory.PlannedAt > PlanLifetime)
        {
            var plan = await PlanAsync(planet, setup, settings, memory, run, cancellationToken).ConfigureAwait(false);
            if (plan is null)
                return (false, settings.CheckInterval);
            memory.Plan = plan;
            memory.Signature = signature;
            memory.PlannedAt = now;
        }

        var steps = memory.Plan.Steps;
        if (steps.Count == 0)
        {
            // The simulated targets are built or queued: the strategy's other buildings (Research Lab, …) by price.
            var others = strategy with
            {
                Rules = strategy.Rules.Where(r => !SimulatedPlanner.IsSimulated(r.Building)).ToList(),
                StorageFillPercent = 0
            };
            return await BuildByPriceAsync(client, state, others, "simulated targets queued; ", run, cancellationToken).ConfigureAwait(false);
        }

        var push = SimulatedPlanner.Push(state, steps, shipyard, QueueCapacity);
        foreach (var action in push.Actions)
        {
            if (!await SendAsync(client, planet, action, shipyard, pendingSatellites, memory, run, cancellationToken).ConfigureAwait(false))
            {
                memory.Plan = null; // plan again with the game's answer in mind
                return (false, settings.CheckInterval);
            }
        }

        if (push.Actions.Count > 0)
            memory.Plan = null; // the planet changed; the next cycle plans from the new state
        run.Report($"{planet}: {push.Actions.Count} sent, {steps.Count - push.StepsPushed} to go – {push.Waiting}");
        return (false, push.NextIn ?? TimeSpan.FromSeconds(steps[^1].FinishSeconds));
    }

    private async Task<OptimizedOrder?> PlanAsync(OwnPlanet planet, OptimizerSetup setup, SimulatedBuildOptions settings, PlanetMemory memory, Run run, CancellationToken cancellationToken)
    {
        var simulator = new BuildSimulator(new GameModel(setup.Bonuses, setup.Planet), setup);
        if (simulator.TargetsReached)
            return new OptimizedOrder { Setup = setup, SetupKey = setup.Key, TotalSeconds = simulator.Time, FoundAt = timeProvider.GetLocalNow() };

        var watch = Stopwatch.StartNew();
        var seed = Environment.TickCount;
        var order = await optimizer.RunAsync(setup, settings.OptimizerOptions(seed), progress: null, cancellationToken, memory.SeedFor(setup)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (order is null)
        {
            run.Report($"{planet}: no order reaches the targets ({setup.ShortName}); check the strategy (solar plant max, locked buildings)");
            return null;
        }

        var start = setup.Start!;
        run.Report($"{planet}: planned {order.Steps.Count} items to {setup.ShortName}"
                   + (setup.RoboticsTarget + setup.NaniteTarget + setup.ShipyardTarget > 0 ? $" + factories {setup.RoboticsTarget}/{setup.NaniteTarget}/{setup.ShipyardTarget}" : "")
                   + $", done in {OptimizedOrder.FormatDuration(order.TotalSeconds)} ({order.Evaluations / 1e6:0.0}M orders in {watch.Elapsed.TotalSeconds:0.0} s;"
                   + $" model × {start.MetalCorrection:0.00}/{start.CrystalCorrection:0.00}/{start.DeuteriumCorrection:0.00}, extra energy {start.ExtraEnergy:0})");
        return order;
    }

    private async Task<bool> SendAsync(
        IOgamexClient client, OwnPlanet planet, SimulatedAction action, ShipyardState? shipyard, int pendingSatellites, PlanetMemory memory, Run run, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case SimulatedAction.Upgrade upgrade:
            {
                var name = $"{BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level}";
                var outcome = await client.EnqueueBuildingAsync(planet.Id, upgrade.Building, cancellationToken).ConfigureAwait(false);
                if (outcome.Result != EnqueueResult.Queued)
                {
                    run.Report($"{planet}: the game refused {name}: {outcome.Message ?? outcome.Result.ToString()}");
                    return false;
                }

                memory.LastPushed.Add(new PlannedUpgrade(upgrade.Building, upgrade.Level, upgrade.Cost));
                run.Queued(planet, $"{planet}: queued {name}");
                return true;
            }
            case SimulatedAction.Satellites satellites:
            {
                var outcome = await client.BuildShipsAsync(planet.Id, HangarPageParser.SolarSatellite, satellites.Count, cancellationToken).ConfigureAwait(false);
                if (outcome.Result != EnqueueResult.Queued)
                {
                    run.Report($"{planet}: the game refused {satellites.Count} solar satellites: {outcome.Message}");
                    return false;
                }

                var perSatellite = shipyard?.SatelliteDuration ?? TimeSpan.FromMinutes(1);
                memory.Satellites = new PendingSatellites(
                    (shipyard?.Satellites ?? 0) + pendingSatellites + satellites.Count,
                    timeProvider.GetUtcNow() + perSatellite * (pendingSatellites + satellites.Count) + TimeSpan.FromMinutes(5));
                run.Report($"{planet}: ordered {satellites.Count} solar satellites");
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Queues the strategy's buildings in the price planner's order (cheapest next level first), as many levels as the
    /// resources cover on top of the waiting queue items.
    /// </summary>
    private async Task<(bool Done, TimeSpan? NextIn)> BuildByPriceAsync(
        IOgamexClient client, PlanetBuildState state, BuildStrategy strategy, string waitingPrefix, Run run, CancellationToken cancellationToken)
    {
        var plan = BuildPlanner.Plan(state, strategy, Math.Max(1, QueueCapacity - state.Queue.Count), requireAffordable: false);
        if (plan.Upgrades.Count == 0)
        {
            run.Finish(state.Planet, plan.Reason);
            return (true, null);
        }

        var runningLeft = state.RunningRemaining ?? TimeSpan.Zero;
        TimeSpan? nextIn = runningLeft > TimeSpan.Zero ? runningLeft : null;
        var push = state.Queue.Count >= QueueCapacity ? [] : SimulatedPlanner.AffordablePrefix(state, plan.Upgrades);
        foreach (var upgrade in push)
        {
            var name = $"{BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level}";
            var outcome = await client.EnqueueBuildingAsync(state.Planet.Id, upgrade.Building, cancellationToken).ConfigureAwait(false);
            if (outcome.Result != EnqueueResult.Queued)
            {
                run.Report($"{state.Planet}: the game refused {name}: {outcome.Message ?? outcome.Result.ToString()}");
                return (false, nextIn);
            }
            run.Planet(state.Planet.Id).LastPushed.Add(upgrade);
            run.Queued(state.Planet, $"{state.Planet}: queued {name} (by price)");
        }

        if (push.Count < plan.Upgrades.Count)
        {
            var next = plan.Upgrades[push.Count];
            run.Report($"{state.Planet}: {waitingPrefix}waiting for {BuildingCatalog.Get(next.Building).DisplayName} {next.Level}"
                       + (state.Queue.Count + push.Count >= QueueCapacity ? " (queue full)" : " (resources, queued items reserved)"));
        }
        return (false, nextIn);
    }

    /// <summary>What the plan depends on: levels, queue, satellites and the setup's limits (resources only drift).</summary>
    private static string Signature(PlanetBuildState state, OptimizerSetup setup, int satellites, int pendingSatellites) =>
        string.Join(",", state.Levels.OrderBy(l => l.Key).Select(l => $"{l.Key}={l.Value}"))
        + "|" + string.Join(",", state.Queue.Select(q => $"{q.Building}{q.Level}{(q.Started ? "*" : "")}"))
        + $"|{satellites}+{pendingSatellites}|{setup.TargetKey}";

    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet, Run run)
    {
        try
        {
            await client.SelectPlanetAsync(planet, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Could not switch {Universe} back to planet {Planet}", client.Universe.Name, planet);
            run.Report($"Could not switch back to the planet selected before: {ex.Message}");
        }
    }

    private sealed record PendingSatellites(int ExpectedCount, DateTimeOffset Until);

    /// <summary>What the runner remembers about a planet between cycles.</summary>
    private sealed class PlanetMemory
    {
        public (int Min, int Max)? Temperature { get; set; }

        /// <summary>The current plan; null = plan again. The last one is kept to seed the next search.</summary>
        public OptimizedOrder? Plan
        {
            get => _plan;
            set
            {
                _previous = value ?? _previous;
                _plan = value;
            }
        }

        private OptimizedOrder? _plan;

        public string? Signature { get; set; }

        public DateTimeOffset PlannedAt { get; set; }

        /// <summary>The previous plan, for seeding the next search.</summary>
        private OptimizedOrder? _previous;

        public List<PlannedUpgrade> LastPushed { get; } = [];

        public PendingSatellites? Satellites { get; set; }

        /// <summary>Satellites ordered by this run and not in the shipyard's count yet.</summary>
        public int PendingSatellites(ShipyardState? shipyard, DateTimeOffset now)
        {
            if (Satellites is not { } pending || shipyard is null)
                return 0;
            var missing = pending.ExpectedCount - shipyard.Satellites;
            if (missing > 0 && now <= pending.Until)
                return missing;
            Satellites = null;
            return 0;
        }

        /// <summary>
        /// The previous plan without what has been built or queued since: a good start for the next search. Building
        /// steps at or below the planet's level are gone; satellite steps before the first remaining building step too.
        /// </summary>
        public Gene[]? SeedFor(OptimizerSetup setup)
        {
            if (_previous is null || setup.Start is not { } start)
                return null;

            var levels = new Dictionary<Gene, int>(start.Levels);
            foreach (var item in start.Queue)
            {
                if (item.Gene is { } gene)
                    levels[gene] = Math.Max(levels.GetValueOrDefault(gene), item.Level);
            }

            var genes = new List<Gene>();
            foreach (var step in _previous.Steps)
            {
                if (step.IsSatellites)
                {
                    if (genes.Count > 0)
                        genes.Add(Gene.SolarSatellites);
                }
                else if (step.Level > levels.GetValueOrDefault(step.Gene))
                    genes.Add(step.Gene);
            }
            return genes.Count > 0 ? genes.ToArray() : null;
        }
    }

    private sealed class Run(IProgress<BuildProgress>? progress)
    {
        private readonly Dictionary<PlanetId, PlanetMemory> _planets = new();

        public Dictionary<PlanetId, PlanetBuildSummary> Summaries { get; } = new();

        public int TotalQueued { get; private set; }

        public AccountBonuses? Bonuses { get; set; }

        public DateTimeOffset BonusesReadAt { get; set; }

        public PlanetMemory Planet(PlanetId id) =>
            _planets.TryGetValue(id, out var memory) ? memory : _planets[id] = new PlanetMemory();

        public void Queued(OwnPlanet planet, string message)
        {
            TotalQueued++;
            Summaries[planet.Id] = Summaries[planet.Id] with { Queued = Summaries[planet.Id].Queued + 1 };
            Report(message);
        }

        public void Ignored(OwnPlanet planet, PlannedUpgrade upgrade)
        {
            TotalQueued--;
            Summaries[planet.Id] = Summaries[planet.Id] with { Queued = Summaries[planet.Id].Queued - 1 };
            Report($"{planet}: the game did not queue {BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level} (not enough resources?)");
        }

        public void Finish(OwnPlanet planet, BuildStopReason reason)
        {
            if (Summaries[planet.Id].Reason == reason)
                return;
            Summaries[planet.Id] = Summaries[planet.Id] with { Reason = reason };
            Report($"{planet}: {BuildStopReasons.Describe(reason)}");
        }

        public void Report(string message) => progress?.Report(new BuildProgress(message, TotalQueued));

        public BuildRunResult Result(BuildOutcome outcome, string? error = null) =>
            new(outcome, TotalQueued, Summaries.Values.ToList(), error);
    }
}
