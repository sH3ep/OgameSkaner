using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OgameSkaner.Core.Domain;
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
    private long _cacheBuster;

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
            .Select(row => new PlayerPlanet(row.PlayerName, new Coordinates(address.Galaxy, address.System, row.Position), row.HasMoon, scannedAt))
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

    public async Task<FleetSnapshot> GetFleetAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "fleet", referer: "galaxy");
        var html = await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
        return FleetPageParser.Parse(html);
    }

    public async Task SendFleetAsync(FleetMission mission, CancellationToken cancellationToken = default)
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
        await SendForStringAsync(request, cancellationToken).ConfigureAwait(false);
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

    public void Dispose() => _http.Dispose();

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
