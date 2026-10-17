using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.Core.Services;

public sealed record ColonizeResult(bool Sent, string Message);

/// <summary>
/// Sends one Colony Ship to a free slot from the nearest own planet that has one. The briefing is checked first, so a
/// slot taken since the scan (or out of reach of Astrophysics) is refused without sending. The game is switched back
/// to the planet that was selected before.
/// </summary>
public sealed class ColonizationService(OwnPlanetService ownPlanets, ILogger<ColonizationService> logger)
{
    public const string ColonyShipType = "COLONY_SHIP";

    /// <exception cref="OgamexException">The planet list or a fleet page could not be read.</exception>
    public async Task<ColonizeResult> ColonizeAsync(IOgamexClient client, Coordinates target, CancellationToken cancellationToken = default)
    {
        var refresh = await ownPlanets.RefreshAsync(client, cancellationToken).ConfigureAwait(false);
        try
        {
            // Nearest first, so usually only one fleet page is read.
            var origin = default(OwnPlanet);
            foreach (var planet in refresh.List.Planets.OrderBy(p => RaidPlanner.Distance(p.Coordinates, target, client.Universe)))
            {
                var fleet = await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false);
                if (fleet.QuantityOf(ColonyShipType) > 0)
                {
                    origin = planet;
                    break;
                }
            }

            if (origin is null)
                return new ColonizeResult(false, "No Colony Ship on any planet.");

            // The fleet page read above made the origin the current planet, which the briefing flies from.
            var mission = new FleetMission(target, PlanetType.Planet, MissionType.Colonize, [new ShipStack(ColonyShipType, 1)]);
            var briefing = await client.GetFleetBriefingAsync(mission, cancellationToken).ConfigureAwait(false);
            if (!briefing.AvailableMissions.Contains(MissionType.Colonize))
            {
                var reason = briefing.Warning
                    ?? (briefing.PlayerName is { } owner ? $"the slot now belongs to {owner}" : "the game doesn't offer colonizing it (Astrophysics level?)");
                return new ColonizeResult(false, $"Can't colonize {target}: {reason}.");
            }

            var outcome = await client.SendFleetAsync(mission, origin.Id, cancellationToken).ConfigureAwait(false);
            return outcome.Sent
                ? new ColonizeResult(true, $"Colony Ship sent from {origin} at {DateTime.Now:T}, arrives in {briefing.OneWayFlight:g}.")
                : new ColonizeResult(false, $"The server refused the colonization of {target}: {outcome.Message}");
        }
        finally
        {
            await SwitchBackAsync(client, refresh.CurrentPlanet).ConfigureAwait(false);
        }
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
