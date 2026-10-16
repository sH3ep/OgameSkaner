using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Services.Building;

namespace OgameSkaner.Core.Services.Schemas;

/// <summary>A planet and the schema it follows.</summary>
public sealed record SchemaJob(PlanetId Planet, BuildSchema Schema);

/// <param name="Planet">Set when the message is about one planet.</param>
/// <param name="Status">The planet's current state ("waiting: …", "done"), for a status column.</param>
public sealed record SchemaProgress(string Message, PlanetId? Planet = null, string? Status = null, int TotalPushed = 0);

public enum SchemaRunOutcome
{
    Finished,
    Cancelled,
    Failed
}

public sealed record SchemaRunResult(SchemaRunOutcome Outcome, int Pushed, string? Error = null);

/// <summary>
/// Feeds the schemas of the given planets into the game as a virtual queue without limit: every cycle it pushes on
/// each planet what <see cref="SchemaPlanner"/> allows, switches the game back to the planet the player had selected,
/// and waits <c>interval</c> before the next cycle. It ends when every planet's schema is done, on cancellation, or
/// when the session expires; other errors are reported and retried in the next cycle.
/// </summary>
public sealed class SchemaRunner(
    OwnPlanetService ownPlanets,
    IOptions<OgamexOptions> options,
    TimeProvider timeProvider,
    ILogger<SchemaRunner> logger)
{
    /// <summary>Safety limit of actions per planet and cycle.</summary>
    private const int MaxActionsPerCycle = 20;

    public async Task<SchemaRunResult> RunAsync(
        IOgamexClient client,
        IReadOnlyList<SchemaJob> jobs,
        TimeSpan interval,
        IProgress<SchemaProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var run = new Run(progress);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await RunCycleAsync(client, jobs, run, cancellationToken).ConfigureAwait(false))
                {
                    run.Report("Every schema is built or queued.");
                    return new SchemaRunResult(SchemaRunOutcome.Finished, run.Pushed);
                }

                run.Report($"Next check at {timeProvider.GetLocalNow().Add(interval):T}");
                await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SchemaRunResult(SchemaRunOutcome.Cancelled, run.Pushed);
        }
        catch (SessionExpiredException ex)
        {
            return new SchemaRunResult(SchemaRunOutcome.Failed, run.Pushed, ex.Message);
        }
    }

    /// <summary>One pass over all planets; true when every schema is done.</summary>
    private async Task<bool> RunCycleAsync(IOgamexClient client, IReadOnlyList<SchemaJob> jobs, Run run, CancellationToken cancellationToken)
    {
        OwnPlanetRefresh refresh;
        try
        {
            refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OgamexException ex) when (ex is not SessionExpiredException)
        {
            run.Report($"Reading the planet list failed: {ex.Message}");
            return false;
        }

        var planets = refresh.List.Planets.ToDictionary(p => p.Id);
        var allDone = true;
        try
        {
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!planets.TryGetValue(job.Planet, out var planet))
                {
                    run.Report($"Planet {job.Planet} is not in the planet list any more.", job.Planet, "not found");
                    continue;
                }

                try
                {
                    var last = await ProcessPlanetAsync(client, planet, job.Schema, run, cancellationToken).ConfigureAwait(false);
                    allDone &= last is SchemaAction.Done;
                }
                catch (OgamexException ex) when (ex is not SessionExpiredException)
                {
                    logger.LogWarning(ex, "Schema run on {Planet} failed this cycle", planet);
                    run.Report($"{planet}: {ex.Message}", planet.Id, "error, retrying next cycle");
                    allDone = false;
                }
            }
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet, run).ConfigureAwait(false);
        }

        return allDone;
    }

    /// <summary>Applies actions on one planet until it has to wait or is done; returns the last action.</summary>
    private async Task<SchemaAction> ProcessPlanetAsync(IOgamexClient client, OwnPlanet planet, BuildSchema schema, Run run, CancellationToken cancellationToken)
    {
        for (var i = 0; i < MaxActionsPerCycle; i++)
        {
            var state = await client.GetPlanetBuildStateAsync(planet.Id, includeFacilities: true, cancellationToken).ConfigureAwait(false);

            // The game answers "success" even when it ignores an item, so check that the last one really got queued.
            if (run.LastPushed.Remove(planet.Id, out var previous) && !state.HasReached(previous.Building, previous.Level))
            {
                run.Ignored(planet, previous);
                return Report(run, planet, new SchemaAction.Wait("the game did not queue the last item"));
            }

            var (shipyard, pendingSatellites) = await ReadShipyardIfNeededAsync(client, planet, schema, state, run, cancellationToken).ConfigureAwait(false);
            var action = SchemaPlanner.Next(state, schema, shipyard, pendingSatellites, options.Value.BuildQueueCapacity);
            switch (action)
            {
                case SchemaAction.Upgrade upgrade:
                {
                    var outcome = await client.EnqueueBuildingAsync(planet.Id, upgrade.Building, cancellationToken).ConfigureAwait(false);
                    if (outcome.Result != EnqueueResult.Queued)
                        return Report(run, planet, new SchemaAction.Wait($"the game refused {BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level}: {outcome.Message ?? outcome.Result.ToString()}"));

                    run.Queued(planet, upgrade);
                    continue;
                }
                case SchemaAction.Satellites satellites:
                {
                    var outcome = await client.BuildShipsAsync(planet.Id, HangarPageParser.SolarSatellite, satellites.Count, cancellationToken).ConfigureAwait(false);
                    if (outcome.Result != EnqueueResult.Queued)
                        return Report(run, planet, new SchemaAction.Wait($"the game refused {satellites.Count} solar satellites: {outcome.Message}"));

                    var perSatellite = shipyard!.SatelliteDuration ?? TimeSpan.FromMinutes(1);
                    run.PendingSatellites[planet.Id] = new PendingSatellites(
                        shipyard.Satellites + pendingSatellites + satellites.Count,
                        timeProvider.GetUtcNow() + perSatellite * (pendingSatellites + satellites.Count) + TimeSpan.FromMinutes(5));
                    run.Report($"{planet}: ordered {satellites.Count} solar satellites – {satellites.Reason}", planet.Id, "satellites ordered");
                    continue;
                }
                default:
                    return Report(run, planet, action);
            }
        }

        return Report(run, planet, new SchemaAction.Wait("action limit for this cycle reached"));
    }

    /// <summary>The shipyard is read only when satellites may be needed: the solar plant is at its maximum or locked.</summary>
    private async Task<(ShipyardState? Shipyard, int Pending)> ReadShipyardIfNeededAsync(
        IOgamexClient client, OwnPlanet planet, BuildSchema schema, PlanetBuildState state, Run run, CancellationToken cancellationToken)
    {
        var solar = PlanetLevels.Of(PlanetLevels.Planned(state), BuildingType.SolarPlant);
        if (!schema.Rules.UseSolarSatellites || (solar < schema.Rules.SolarMaxLevel && !state.Locked.Contains(BuildingType.SolarPlant)))
            return (null, 0);

        var shipyard = await client.GetShipyardStateAsync(planet.Id, cancellationToken).ConfigureAwait(false);
        if (!run.PendingSatellites.TryGetValue(planet.Id, out var pending))
            return (shipyard, 0);

        var missing = pending.ExpectedCount - shipyard.Satellites;
        if (missing <= 0 || timeProvider.GetUtcNow() > pending.Until)
        {
            run.PendingSatellites.Remove(planet.Id);
            return (shipyard, 0);
        }

        return (shipyard, missing);
    }

    private static SchemaAction Report(Run run, OwnPlanet planet, SchemaAction action)
    {
        var status = action is SchemaAction.Done ? "done" : $"waiting: {action.Reason}";
        run.Report($"{planet}: {status}", planet.Id, status);
        return action;
    }

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

    private sealed class Run(IProgress<SchemaProgress>? progress)
    {
        public int Pushed { get; private set; }

        public Dictionary<PlanetId, SchemaAction.Upgrade> LastPushed { get; } = new();

        public Dictionary<PlanetId, PendingSatellites> PendingSatellites { get; } = new();

        public void Queued(OwnPlanet planet, SchemaAction.Upgrade upgrade)
        {
            Pushed++;
            LastPushed[planet.Id] = upgrade;
            Report($"{planet}: queued {BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level} – {upgrade.Reason}", planet.Id, "queued");
        }

        public void Ignored(OwnPlanet planet, SchemaAction.Upgrade upgrade)
        {
            Pushed--;
            Report($"{planet}: the game did not queue {BuildingCatalog.Get(upgrade.Building).DisplayName} {upgrade.Level}", planet.Id);
        }

        public void Report(string message, PlanetId? planet = null, string? status = null) =>
            progress?.Report(new SchemaProgress(message, planet, status, Pushed));
    }
}
