using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Building;

/// <param name="CurrentPlanet">The planet selected in the game when the list was read.</param>
public sealed record OwnPlanetRefresh(OwnPlanetList List, PlanetId CurrentPlanet);

/// <summary>Reads the player's own planets from the server and stores them for the universe.</summary>
public sealed class OwnPlanetService(IOwnPlanetStore store, TimeProvider timeProvider)
{
    public OwnPlanetList GetStored(string universe) => store.Load(universe);

    /// <exception cref="OgamexException">The planet list could not be read.</exception>
    public async Task<OwnPlanetRefresh> RefreshAsync(IOgamexClient client, CancellationToken cancellationToken = default)
    {
        var snapshot = await client.GetOwnPlanetsAsync(cancellationToken).ConfigureAwait(false);
        var list = new OwnPlanetList(snapshot.Planets, timeProvider.GetLocalNow());
        store.Save(client.Universe.Name, list);
        return new OwnPlanetRefresh(list, snapshot.CurrentPlanet);
    }

    /// <summary>
    /// Reads a planet's buildings, resources and queue. Reading a planet selects it in the game (also for the
    /// player's browser), so the planet that was selected before is selected again afterwards.
    /// </summary>
    /// <exception cref="OgamexException">The planet could not be read.</exception>
    public async Task<PlanetBuildState> InspectAsync(IOgamexClient client, PlanetId planet, CancellationToken cancellationToken = default)
    {
        var before = await client.GetOwnPlanetsAsync(cancellationToken).ConfigureAwait(false);
        var state = await client.GetPlanetBuildStateAsync(planet, includeFacilities: true, cancellationToken).ConfigureAwait(false);
        if (before.CurrentPlanet != planet)
            await client.SelectPlanetAsync(before.CurrentPlanet, CancellationToken.None).ConfigureAwait(false);
        return state;
    }
}
