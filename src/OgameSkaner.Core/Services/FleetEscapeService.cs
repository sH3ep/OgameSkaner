using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;

namespace OgameSkaner.Core.Services;

public sealed record FleetEscapeResult(bool Sent, string Message);

/// <summary>Sends every ship of the current planet to the moon at the same coordinates.</summary>
public sealed class FleetEscapeService
{
    public async Task<FleetEscapeResult> EscapeToMoonAsync(IOgamexClient client, CancellationToken cancellationToken = default)
    {
        var fleet = await client.GetFleetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (fleet.Ships.Count == 0)
            return new FleetEscapeResult(false, $"No ships on {fleet.CurrentPlanet} to escape.");

        var mission = new FleetMission(fleet.CurrentPlanet, PlanetType.Moon, MissionType.Deploy, fleet.Ships);
        var outcome = await client.SendFleetAsync(mission, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!outcome.Sent)
            return new FleetEscapeResult(false, $"The server refused the escape from {fleet.CurrentPlanet}: {outcome.Message}");

        var shipCount = fleet.Ships.Sum(s => s.Quantity);
        return new FleetEscapeResult(true, $"Sent {shipCount:N0} ships from {fleet.CurrentPlanet} to its moon.");
    }
}
