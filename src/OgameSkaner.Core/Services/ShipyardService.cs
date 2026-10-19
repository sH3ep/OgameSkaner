using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Services;

/// <summary>What happened to the order on one planet.</summary>
public sealed record ShipOrderResult(OwnPlanet Planet, bool Queued, string Message);

/// <summary>
/// Orders the same ships or defenses on several planets at once (the Shipyard page). Each planet's order is one request; a planet
/// the game refuses (resources, shipyard level, requirements) is reported and the others still get theirs. The whole
/// order is one player group of the <see cref="IActionQueue"/>.
/// </summary>
public sealed class ShipyardService(OwnPlanetService ownPlanets, IActionQueues queues, ILogger<ShipyardService> logger)
{
    /// <exception cref="OgamexException">The planet list could not be read, or the session expired.</exception>
    public Task<IReadOnlyList<ShipOrderResult>> OrderAsync(
        IOgamexClient client,
        IReadOnlyCollection<PlanetId> planets,
        string unitType,
        int quantity,
        IProgress<ShipOrderResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        var unit = UnitCatalog.Get(unitType) ?? throw new ArgumentException($"Unknown unit {unitType}.", nameof(unitType));
        return queues.For(client).RunAsync("Shipyard", $"Order {quantity:N0} {unit.Name} on {planets.Count} planet(s)",
            ct => OrderOnPlanetsAsync(client, planets, unit, quantity, progress, ct), ActionPriority.User, cancellationToken);
    }

    private async Task<IReadOnlyList<ShipOrderResult>> OrderOnPlanetsAsync(
        IOgamexClient client, IReadOnlyCollection<PlanetId> planets, UnitInfo unit, int quantity, IProgress<ShipOrderResult>? progress, CancellationToken cancellationToken)
    {
        var name = unit.Name;
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        var results = new List<ShipOrderResult>();
        foreach (var planet in refresh.List.Planets.Where(p => planets.Contains(p.Id)))
        {
            ShipOrderResult result;
            try
            {
                var outcome = unit.Kind == UnitKind.Defense
                    ? await client.BuildDefensesAsync(planet.Id, unit.Id, quantity, cancellationToken).ConfigureAwait(false)
                    : await client.BuildShipsAsync(planet.Id, unit.Id, quantity, cancellationToken).ConfigureAwait(false);
                result = outcome.Result == EnqueueResult.Queued
                    ? new ShipOrderResult(planet, true, $"{planet}: ordered {quantity:N0} {name}")
                    : new ShipOrderResult(planet, false, $"{planet}: the game refused {quantity:N0} {name}: {outcome.Message ?? outcome.Result.ToString()}");
            }
            catch (OgamexException ex) when (ex is not SessionExpiredException)
            {
                logger.LogWarning(ex, "Ordering {Unit} on {Planet} failed", unit.Id, planet);
                result = new ShipOrderResult(planet, false, $"{planet}: {ex.Message}");
            }

            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }
}
