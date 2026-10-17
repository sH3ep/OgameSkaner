using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;

namespace OgameSkaner.Core.Services;

/// <summary>What happened to the order on one planet.</summary>
public sealed record ShipOrderResult(OwnPlanet Planet, bool Queued, string Message);

/// <summary>
/// Orders the same ships or defenses on several planets at once (the Shipyard page). Each planet's order is one request; a planet
/// the game refuses (resources, shipyard level, requirements) is reported and the others still get theirs. The game is
/// switched back to the planet that was selected before.
/// </summary>
public sealed class ShipyardService(OwnPlanetService ownPlanets, ILogger<ShipyardService> logger)
{
    /// <exception cref="OgamexException">The planet list could not be read, or the session expired.</exception>
    public async Task<IReadOnlyList<ShipOrderResult>> OrderAsync(
        IOgamexClient client,
        IReadOnlyCollection<PlanetId> planets,
        string unitType,
        int quantity,
        IProgress<ShipOrderResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        var unit = UnitCatalog.Get(unitType) ?? throw new ArgumentException($"Unknown unit {unitType}.", nameof(unitType));
        var name = unit.Name;
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        var results = new List<ShipOrderResult>();
        try
        {
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
                    logger.LogWarning(ex, "Ordering {Unit} on {Planet} failed", unitType, planet);
                    result = new ShipOrderResult(planet, false, $"{planet}: {ex.Message}");
                }

                results.Add(result);
                progress?.Report(result);
            }
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet).ConfigureAwait(false);
        }

        return results;
    }

    private async Task SwitchBackAsync(IOgamexClient client, PlanetId planet)
    {
        try
        {
            await client.SelectPlanetAsync(planet, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Could not switch {Universe} back to planet {Planet}", client.Universe.Name, planet);
        }
    }
}
