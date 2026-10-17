using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Ogamex;

/// <summary>Typed access to the OgameX endpoints used by the app. One instance per universe.</summary>
public interface IOgamexClient : IDisposable
{
    UniverseProfile Universe { get; }

    /// <summary>Downloads and parses one solar system from the galaxy view.</summary>
    /// <exception cref="SessionExpiredException">The server did not return a galaxy page.</exception>
    Task<IReadOnlyList<PlayerPlanet>> GetSolarSystemAsync(SolarSystemAddress address, CancellationToken cancellationToken = default);

    Task SendSpyAsync(Coordinates target, PlanetType targetType, CancellationToken cancellationToken = default);

    /// <summary>Ships, cargo capacities and fleet slots of the current planet, or of <paramref name="planet"/> (which then becomes the current planet).</summary>
    Task<FleetSnapshot> GetFleetAsync(PlanetId? planet = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the fleet page shows before sending <paramref name="mission"/> from the current planet: flight time, possible
    /// missions and the target player's state (<c>/fleet/fleetpage3data</c>). Nothing is sent.
    /// </summary>
    Task<FleetBriefing> GetFleetBriefingAsync(FleetMission mission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a fleet from <paramref name="origin"/> (switching to it first) or from the current planet. A refusal comes
    /// back as an outcome with the server's message.
    /// </summary>
    Task<FleetSendOutcome> SendFleetAsync(FleetMission mission, PlanetId? origin = null, CancellationToken cancellationToken = default);

    /// <summary>One page of espionage reports, newest first, without ships and defenses.</summary>
    Task<IReadOnlyList<EspionageReport>> GetEspionageReportsAsync(int page = 1, CancellationToken cancellationToken = default);

    /// <summary>The report with the ships and defenses of its detail page.</summary>
    Task<EspionageReport> GetEspionageReportDetailAsync(EspionageReport report, CancellationToken cancellationToken = default);

    /// <summary>One page (1-based, 100 players) of a player ranking.</summary>
    Task<RankingPage> GetRankingPageAsync(RankingCategory category, int page, CancellationToken cancellationToken = default);

    /// <summary>Returns true when the stored session cookies are accepted by the server.</summary>
    Task<bool> CheckSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>The player's own planets (moons excluded) and the planet currently selected in the game.</summary>
    Task<OwnPlanetsSnapshot> GetOwnPlanetsAsync(CancellationToken cancellationToken = default);

    /// <summary>Solar satellites of a planet (shipyard page). Selects the planet in the game.</summary>
    Task<ShipyardState> GetShipyardStateAsync(PlanetId planet, CancellationToken cancellationToken = default);

    /// <summary>Orders ships in the planet's shipyard, e.g. <c>SOLAR_SATELLITE</c>.</summary>
    Task<EnqueueOutcome> BuildShipsAsync(PlanetId planet, string shipType, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's production and speed bonuses (research, officers, academy, premium, ranking, class) from the
    /// resource settings, officers and academy pages. Read-only; doesn't change the selected planet.
    /// </summary>
    Task<AccountBonuses> GetAccountBonusesAsync(CancellationToken cancellationToken = default);

    /// <summary>The planet's temperature range in °C (overview page). Selects the planet in the game.</summary>
    Task<(int Min, int Max)> GetPlanetTemperatureAsync(PlanetId planet, CancellationToken cancellationToken = default);

    /// <summary>Makes <paramref name="planet"/> the current planet in the game (also for the browser).</summary>
    Task SelectPlanetAsync(PlanetId planet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Building levels, resources, energy and build queue of one of the player's planets. Makes it the current
    /// planet in the game (planet selection is server-side state, also for the browser).
    /// </summary>
    /// <param name="includeFacilities">Also read the facilities page (one more request).</param>
    Task<PlanetBuildState> GetPlanetBuildStateAsync(PlanetId planet, bool includeFacilities = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the next level of <paramref name="building"/> to the planet's build queue. The server answers
    /// "success" even when it ignores an unaffordable item, so callers verify with the next state read.
    /// </summary>
    Task<EnqueueOutcome> EnqueueBuildingAsync(PlanetId planet, BuildingType building, CancellationToken cancellationToken = default);
}

public interface IOgamexClientFactory
{
    IOgamexClient Create(UniverseProfile universe);
}
