using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Ogamex;

/// <summary>Typed access to the OgameX endpoints used by the app. One instance per universe.</summary>
public interface IOgamexClient : IDisposable
{
    UniverseProfile Universe { get; }

    /// <summary>Downloads and parses one solar system from the galaxy view.</summary>
    /// <exception cref="SessionExpiredException">The server did not return a galaxy page.</exception>
    Task<IReadOnlyList<PlayerPlanet>> GetSolarSystemAsync(SolarSystemAddress address, CancellationToken cancellationToken = default);

    Task SendSpyAsync(Coordinates target, PlanetType targetType, CancellationToken cancellationToken = default);

    Task<FleetSnapshot> GetFleetAsync(CancellationToken cancellationToken = default);

    Task SendFleetAsync(FleetMission mission, CancellationToken cancellationToken = default);

    /// <summary>Returns true when the stored session cookies are accepted by the server.</summary>
    Task<bool> CheckSessionAsync(CancellationToken cancellationToken = default);
}

public interface IOgamexClientFactory
{
    IOgamexClient Create(UniverseProfile universe);
}
