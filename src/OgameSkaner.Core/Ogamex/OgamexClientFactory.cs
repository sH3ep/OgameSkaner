using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex.Http;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Ogamex;

/// <summary>
/// Builds an <see cref="IOgamexClient"/> with its own cookie jar and the handler pipeline:
/// logging → throttling → browser headers → network.
/// </summary>
public sealed class OgamexClientFactory(
    ISessionStore sessions,
    IOptions<OgamexOptions> options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IOgamexClientFactory
{
    public IOgamexClient Create(UniverseProfile universe)
    {
        var settings = options.Value;
        var cookies = new CookieContainer();

        var network = new SocketsHttpHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        HttpMessageHandler pipeline = new LoggingHandler(settings, loggerFactory.CreateLogger<OgamexClient>())
        {
            InnerHandler = new ThrottlingHandler(settings, timeProvider)
            {
                InnerHandler = new BrowserHeadersHandler(settings) { InnerHandler = network }
            }
        };

        var http = new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = universe.BaseUri,
            Timeout = settings.RequestTimeout
        };

        return new OgamexClient(universe, http, cookies, sessions, settings, timeProvider);
    }
}
