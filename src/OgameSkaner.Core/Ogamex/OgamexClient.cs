using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex.Parsing;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Ogamex;

/// <summary>
/// HTTP client for one OgameX universe. Authentication uses the browser session cookies
/// (<c>gameAuthToken</c> and <c>SessionId</c>) stored in <see cref="ISessionStore"/>; they are
/// re-read before every request, so a newly saved session is used immediately.
/// </summary>
internal sealed class OgamexClient : IOgamexClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly ISessionStore _sessions;
    private readonly OgamexOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Held while a request switches the game's current planet (<c>?planet=</c>), and for a fleet send from its
    /// planet switch to the submit: <c>/fleet/submitfleet</c> has no origin and leaves from the current planet.
    /// </summary>
    private readonly SemaphoreSlim _planetSwitch = new(1, 1);

    private long _cacheBuster;

    /// <summary>Time zone the game shows dates in, learned from the fleet page's clock; null until then.</summary>
    private TimeSpan? _displayOffset;

    public OgamexClient(
        UniverseProfile universe,
        HttpClient http,
        CookieContainer cookies,
        ISessionStore sessions,
        OgamexOptions options,
        TimeProvider timeProvider)
    {
        Universe = universe;
        _http = http;
        _cookies = cookies;
        _sessions = sessions;
        _options = options;
        _timeProvider = timeProvider;
        _cacheBuster = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    public UniverseProfile Universe { get; }

    public async Task<IReadOnlyList<PlayerPlanet>> GetSolarSystemAsync(SolarSystemAddress address, CancellationToken cancellationToken = default)
    {
        // "_" is the jQuery-style cache buster the game itself sends.
        var uri = $"galaxy/galaxydata?x={address.Galaxy}&y={address.System}&_={Interlocked.Increment(ref _cacheBuster)}";
        using var request = CreateRequest(HttpMethod.Get, uri, referer: "galaxy");
        var html = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);

        var scannedAt = _timeProvider.GetLocalNow();
        return GalaxyPageParser.Parse(html)
            .Select(row => new PlayerPlanet(
                row.PlayerName, new Coordinates(address.Galaxy, address.System, row.Position), row.HasMoon, scannedAt, row.Status, row.PlayerId, row.Rank))
            .ToList();
    }

    public async Task SendSpyAsync(Coordinates target, PlanetType targetType, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "galaxy/sendspy", referer: $"galaxy?x={target.Galaxy}&y={target.System}");
        request.Content = JsonContent.Create(new SpyRequest(target.Galaxy, target.System, target.Position, (int)targetType), options: JsonOptions);

        var body = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
        var response = Deserialize<SpyResponse>(body);
        if (!response.IsSuccess)
            throw new OgamexException($"Spy on {target} was rejected by the server.");
    }

    public async Task<FleetSnapshot> GetFleetAsync(PlanetId? planet = null, CancellationToken cancellationToken = default)
    {
        if (planet is null)
            return await ReadFleetPageAsync(null, cancellationToken).ConfigureAwait(false);

        await _planetSwitch.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadFleetPageAsync(planet, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _planetSwitch.Release();
        }
    }

    public async Task<FleetBriefing> GetFleetBriefingAsync(FleetMission mission, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "fleet/fleetpage3data", referer: "fleet");
        request.Content = JsonContent.Create(
            new FleetBriefingRequest(
                mission.Ships.Select(s => new ShipRequest(s.ShipType, s.Quantity)).ToList(),
                mission.Target.Galaxy,
                mission.Target.System,
                mission.Target.Position,
                (int)mission.TargetType,
                mission.SpeedPercentage),
            options: JsonOptions);

        var response = Deserialize<FleetBriefingResponse>(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
        if (!response.IsSuccess)
            throw new OgamexException(response.Message ?? $"The server refused the fleet briefing for {mission.Target}.");

        var status = PlayerStatus.None;
        if (response.PlayerIsInactive7) status |= PlayerStatus.Inactive;
        if (response.PlayerIsInactive28) status |= PlayerStatus.LongInactive;
        if (response.PlayerIsVacation) status |= PlayerStatus.Vacation;
        if (response.PlayerIsBanned) status |= PlayerStatus.Banned;
        if (response.PlayerIsNoob) status |= PlayerStatus.Noob;
        if (response.PlayerIsStrong) status |= PlayerStatus.Strong;
        if (response.PlayerIsUnderProtection) status |= PlayerStatus.Protection;

        return new FleetBriefing(
            (response.AvailableMissions ?? []).Select(m => (MissionType)m).ToList(),
            TimeSpan.FromSeconds(response.FleetOneWayTimeOnSeconds),
            response.DeuteriumConsumption,
            response.Distance,
            response.PlayerId,
            response.PlayerName,
            response.PlayerRanking,
            response.PlayerPoints,
            response.PlayerFleetPoints,
            status,
            response.ShowWarning ? response.WarningMessage : null);
    }

    public async Task<FleetSendOutcome> SendFleetAsync(FleetMission mission, PlanetId? origin = null, CancellationToken cancellationToken = default)
    {
        if (origin is null)
            return await SubmitFleetAsync(mission, cancellationToken).ConfigureAwait(false);

        // The submit has no origin: the planet switch and the submit must not be split by another switch.
        await _planetSwitch.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadFleetPageAsync(origin, cancellationToken).ConfigureAwait(false);
            return await SubmitFleetAsync(mission, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _planetSwitch.Release();
        }
    }

    public async Task<IReadOnlyList<EspionageReport>> GetEspionageReportsAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"messages/messagedata?MessageCategoryType=FLEET_ESPIONAGE&page={page}", referer: "messages");
        var html = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
        return EspionageReportParser.ParseList(html, _displayOffset ?? TimeSpan.FromMinutes(_options.TimeZoneOffsetMinutes));
    }

    public async Task<EspionageReport> GetEspionageReportDetailAsync(EspionageReport report, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"messages/spyreportdetail?reportid={Uri.EscapeDataString(report.Id)}", referer: "messages");
        var (ships, defenses) = EspionageReportParser.ParseDetail(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
        return report with { Ships = ships, Defenses = defenses };
    }

    public async Task<RankingPage> GetRankingPageAsync(RankingCategory category, int page, CancellationToken cancellationToken = default)
    {
        var subCategory = category switch
        {
            RankingCategory.Defense => "DEFENSE",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        using var request = CreateRequest(HttpMethod.Get, $"statistics/statisticsdata?Category=PLAYER&SubCategory={subCategory}&Page={page}&Rel=", referer: "statistics");
        return StatisticsPageParser.Parse(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads the fleet page (switching to <paramref name="planet"/> when given); the caller holds the planet lock for a switch.</summary>
    private async Task<FleetSnapshot> ReadFleetPageAsync(PlanetId? planet, CancellationToken cancellationToken)
    {
        var uri = planet is { } id ? $"fleet?planet={Uri.EscapeDataString(id.Value)}" : "fleet";
        using var request = CreateRequest(HttpMethod.Get, uri, referer: "fleet");
        var html = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
        var fleet = FleetPageParser.Parse(html);

        // An unknown planet id is ignored and the current planet is shown instead.
        if (planet is { } expected && BuildingPageParser.TryReadPage(html, out var shown, out _) && shown != expected)
            throw new OgamexException($"Planet {expected} is not available on {Universe.Name}. Refresh the planet list.");

        if (fleet.ServerClock is { } clock)
        {
            var offset = clock - _timeProvider.GetUtcNow().UtcDateTime;
            _displayOffset = TimeSpan.FromMinutes(Math.Round(offset.TotalMinutes / 15) * 15);
        }

        return fleet;
    }

    private async Task<FleetSendOutcome> SubmitFleetAsync(FleetMission mission, CancellationToken cancellationToken)
    {
        var body = new SendFleetRequest(
            mission.Ships.Select(s => new ShipRequest(s.ShipType, s.Quantity)).ToList(),
            mission.Target.Galaxy,
            mission.Target.System,
            mission.Target.Position,
            (int)mission.TargetType,
            mission.SpeedPercentage,
            Metal: 0,
            Crystal: 0,
            Deuterium: 0,
            (int)mission.Mission);

        using var request = CreateRequest(HttpMethod.Post, "fleet/submitfleet", referer: "fleet");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = Deserialize<SubmitFleetResponse>(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
        return new FleetSendOutcome(response.IsSuccess, response.Message);
    }

    public async Task<bool> CheckSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "fleet", referer: "galaxy");
            var html = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
            return html.Contains(FleetPageParser.ShipsContainerId, StringComparison.Ordinal);
        }
        catch (SessionExpiredException)
        {
            return false;
        }
    }

    /// <summary>Message the server sends when all 15 slots (running item + 14 waiting) are taken.</summary>
    internal const string QueueFullMessage = "Construction queue is full.";

    public async Task<OwnPlanetsSnapshot> GetOwnPlanetsAsync(CancellationToken cancellationToken = default)
    {
        // Every game page has the planet list in its sidebar.
        var page = await GetBuildingPageAsync("building/resource", planet: null, cancellationToken).ConfigureAwait(false);
        return new OwnPlanetsSnapshot(page.Planets, page.CurrentPlanet);
    }

    public async Task<ShipyardState> GetShipyardStateAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"hangar?planet={Uri.EscapeDataString(planet.Value)}", referer: "hangar");
        var state = HangarPageParser.Parse(await SendSwitchingPlanetAsync(request, cancellationToken).ConfigureAwait(false));
        if (state.Planet != planet)
            throw new OgamexException($"Planet {planet} is not available on {Universe.Name}. Refresh the planet list.");
        return state;
    }

    public Task<EnqueueOutcome> BuildShipsAsync(PlanetId planet, string shipType, int quantity, CancellationToken cancellationToken = default) =>
        OrderUnitsAsync(planet, "hangar", "hangar/createship", new CreateShipRequest(shipType, quantity, planet.Value), cancellationToken);

    public Task<EnqueueOutcome> BuildDefensesAsync(PlanetId planet, string defenseType, int quantity, CancellationToken cancellationToken = default) =>
        OrderUnitsAsync(planet, "defense", "defense/createdefense", new CreateDefenseRequest(defenseType, quantity, planet.Value), cancellationToken);

    private async Task<EnqueueOutcome> OrderUnitsAsync<TBody>(PlanetId planet, string page, string action, TBody body, CancellationToken cancellationToken)
    {
        // As the game does it: the planet's page first, so the order can't go to another (server-side current) planet,
        // and no other switch between the two.
        await _planetSwitch.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var pageRequest = CreateRequest(HttpMethod.Get, $"{page}?planet={Uri.EscapeDataString(planet.Value)}", referer: page);
            if (!BuildingPageParser.TryReadPage(await SendForStringAsync(pageRequest, cancellationToken).ConfigureAwait(false), out var shown, out _))
                throw new SessionExpiredException($"The server did not return the {page} page. Check the token and session id.");
            if (shown != planet)
                throw new OgamexException($"Planet {planet} is not available on {Universe.Name}. Refresh the planet list.");

            using var request = CreateRequest(HttpMethod.Post, action, referer: page);
            request.Content = JsonContent.Create(body, options: JsonOptions);
            var response = Deserialize<BuildingResponse>(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
            return response.IsSuccess ? EnqueueOutcome.Queued : new EnqueueOutcome(EnqueueResult.Rejected, response.Message);
        }
        finally
        {
            _planetSwitch.Release();
        }
    }

    public async Task<AccountBonuses> GetAccountBonusesAsync(CancellationToken cancellationToken = default)
    {
        using var resourceRequest = CreateRequest(HttpMethod.Get, "resource", referer: "home");
        var production = AccountBonusParser.ParseResourceSettings(await SendForStringAsync(resourceRequest, cancellationToken).ConfigureAwait(false));
        using var officerRequest = CreateRequest(HttpMethod.Get, "officer", referer: "home");
        var officers = AccountBonusParser.ParseOfficers(await SendForStringAsync(officerRequest, cancellationToken).ConfigureAwait(false));
        using var academyRequest = CreateRequest(HttpMethod.Get, "academy", referer: "home");
        var (className, skills) = AccountBonusParser.ParseAcademy(await SendForStringAsync(academyRequest, cancellationToken).ConfigureAwait(false));
        return AccountBonusParser.Combine(production, officers, className, skills, _timeProvider.GetLocalNow());
    }

    public async Task<(int Min, int Max)> GetPlanetTemperatureAsync(PlanetId planet, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"home?planet={Uri.EscapeDataString(planet.Value)}", referer: "home");
        var html = await SendSwitchingPlanetAsync(request, cancellationToken).ConfigureAwait(false);
        var temperature = OverviewPageParser.ParseTemperature(html);
        if (BuildingPageParser.TryReadPage(html, out var shown, out _) && shown != planet)
            throw new OgamexException($"Planet {planet} is not available on {Universe.Name}. Refresh the planet list.");
        return temperature;
    }

    public Task SelectPlanetAsync(PlanetId planet, CancellationToken cancellationToken = default) =>
        GetBuildingPageAsync("building/resource", planet, cancellationToken);

    public async Task<PlanetBuildState> GetPlanetBuildStateAsync(PlanetId planet, bool includeFacilities = true, CancellationToken cancellationToken = default)
    {
        var pages = new List<BuildingPage>
        {
            await GetBuildingPageAsync("building/resource", planet, cancellationToken).ConfigureAwait(false)
        };

        // Moons have no mines; their buildings are left to a separate automation.
        if (pages[0].Buildings.All(b => b.Building != BuildingType.MetalMine))
            throw new OgamexException($"{planet} is not a planet (a moon?). Only planets are automated.");
        if (includeFacilities)
            pages.Add(await GetBuildingPageAsync("building/facility", planet, cancellationToken).ConfigureAwait(false));

        // The latest page has the freshest resources and queue; buildings come from both.
        var latest = pages[^1];
        var buildings = pages.SelectMany(p => p.Buildings).GroupBy(b => b.Building).Select(g => g.Last()).ToList();
        var levels = buildings.ToDictionary(b => b.Building, b => b.Level);

        return new PlanetBuildState(
            latest.Planets.FirstOrDefault(p => p.Id == planet) ?? new OwnPlanet(planet, planet.Value, default),
            levels,
            latest.Resources,
            latest.Queue,
            EnergyBalance: (long)Math.Floor(latest.EnergyProduction - latest.EnergyConsumption))
        {
            NextLevelCosts = buildings.Where(b => b.NextLevelCost is not null).ToDictionary(b => b.Building, b => b.NextLevelCost!.Value),
            Locked = buildings.Where(b => b.Locked).Select(b => b.Building).ToHashSet(),
            StorageCapacity = latest.StorageCapacity,
            ProductionPerHour = latest.ProductionPerHour,
            NextLevelDurations = buildings.Where(b => b.Duration is not null).ToDictionary(b => b.Building, b => b.Duration!.Value),
            FreeFields = pages.Select(p => p.FreeFields).LastOrDefault(f => f is not null),
            RunningRemaining = latest.Queue.Count > 0 ? latest.RunningRemaining : null,
            VacationMode = latest.VacationMode
        };
    }

    public async Task<EnqueueOutcome> EnqueueBuildingAsync(PlanetId planet, BuildingType building, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "building/increasebuilding", referer: "building/resource");
        request.Content = JsonContent.Create(new IncreaseBuildingRequest(BuildingCatalog.Get(building).ServerId, planet.Value), options: JsonOptions);

        var response = Deserialize<BuildingResponse>(await SendForStringAsync(request, cancellationToken).ConfigureAwait(false));
        if (response.IsSuccess)
            return EnqueueOutcome.Queued;

        return string.Equals(response.Message?.Trim(), QueueFullMessage, StringComparison.OrdinalIgnoreCase)
            ? new EnqueueOutcome(EnqueueResult.QueueFull, response.Message)
            : new EnqueueOutcome(EnqueueResult.Rejected, response.Message);
    }

    /// <summary>Loads a building page, switching to <paramref name="planet"/> first when given.</summary>
    /// <exception cref="OgamexException">The server returned another planet (unknown or lost planet id).</exception>
    private async Task<BuildingPage> GetBuildingPageAsync(string path, PlanetId? planet, CancellationToken cancellationToken)
    {
        var uri = planet is { } id ? $"{path}?planet={Uri.EscapeDataString(id.Value)}" : path;
        using var request = CreateRequest(HttpMethod.Get, uri, referer: path);
        var html = planet is null
            ? await SendForStringAsync(request, cancellationToken).ConfigureAwait(false)
            : await SendSwitchingPlanetAsync(request, cancellationToken).ConfigureAwait(false);
        var page = BuildingPageParser.Parse(html);

        // An unknown planet id is silently ignored and the current planet is returned instead.
        if (planet is { } expected && page.CurrentPlanet != expected)
            throw new OgamexException($"Planet {expected} is not available on {Universe.Name}. Refresh the planet list.");

        return page;
    }

    public void Dispose()
    {
        _http.Dispose();
        _planetSwitch.Dispose();
    }

    /// <summary>Sends a request that switches the current planet, so it can't split another caller's switch and fleet send.</summary>
    private async Task<string> SendSwitchingPlanetAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _planetSwitch.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _planetSwitch.Release();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUri, string referer)
    {
        ApplySessionCookies();

        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Referrer = new Uri(Universe.BaseUri, referer);
        return request;
    }

    private void ApplySessionCookies()
    {
        var session = _sessions.Get(Universe.Name);
        if (session is not { IsComplete: true })
            throw new SessionExpiredException($"No session saved for universe '{Universe.Name}'. Paste the token and session id first.");

        var baseUri = Universe.BaseUri;
        _cookies.Add(baseUri, new Cookie("lang", _options.Language));
        _cookies.Add(baseUri, new Cookie("timeZoneOffset", _options.TimeZoneOffsetMinutes.ToString()));
        _cookies.Add(baseUri, new Cookie("SessionId", session.SessionId));
        _cookies.Add(baseUri, new Cookie("gameAuthToken", session.AuthToken));
    }

    private async Task<string> SendForStringAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OgamexException($"Could not reach {Universe.BaseUri.Host}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OgamexException($"Request to {Universe.BaseUri.Host} timed out.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new SessionExpiredException("The server rejected the session. Paste a fresh token and session id.");

            if (!response.IsSuccessStatusCode)
                throw new OgamexException($"{request.Method} {request.RequestUri} returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static T Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new OgamexException("The server returned an empty response.");
        }
        catch (JsonException)
        {
            // HTML instead of JSON means we were redirected to the login page.
            throw new SessionExpiredException("The server returned an unexpected response. Check the token and session id.");
        }
    }
}
