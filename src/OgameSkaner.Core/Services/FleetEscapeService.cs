using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Services;

public sealed record FleetEscapeResult(bool Sent, string Message);

/// <summary>
/// Sends every ship of the player's planet (the one selected in the game before the modules got busy) to the moon at the
/// same coordinates, at once (<see cref="IActionQueue.RunUrgentAsync{T}"/>).
/// </summary>
public sealed class FleetEscapeService(IActionQueues queues)
{
    public Task<FleetEscapeResult> EscapeToMoonAsync(IOgamexClient client, CancellationToken cancellationToken = default)
    {
        var queue = queues.For(client);
        return queue.RunUrgentAsync("Utilities", "Escape to the moon", ct => EscapeAsync(client, queue.HomePlanet ?? client.CurrentPlanet, ct), cancellationToken);
    }

    private static async Task<FleetEscapeResult> EscapeAsync(IOgamexClient client, PlanetId? planet, CancellationToken cancellationToken)
    {
        var fleet = await client.GetFleetAsync(planet, cancellationToken).ConfigureAwait(false);
        if (fleet.Ships.Count == 0)
            return new FleetEscapeResult(false, $"No ships on {fleet.CurrentPlanet} to escape.");

        var mission = new FleetMission(fleet.CurrentPlanet, PlanetType.Moon, MissionType.Deploy, fleet.Ships);
        var outcome = await client.SendFleetAsync(mission, planet, cancellationToken).ConfigureAwait(false);
        if (!outcome.Sent)
            return new FleetEscapeResult(false, $"The server refused the escape from {fleet.CurrentPlanet}: {outcome.Message}");

        var shipCount = fleet.Ships.Sum(s => s.Quantity);
        return new FleetEscapeResult(true, $"Sent {shipCount:N0} ships from {fleet.CurrentPlanet} to its moon.");
    }
}
