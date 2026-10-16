namespace OgameSkaner.Core.Ogamex.Http;

/// <summary>Adds the headers a browser sends with the game's XHR requests.</summary>
internal sealed class BrowserHeadersHandler(OgamexOptions options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers;
        headers.TryAddWithoutValidation("Accept", "*/*");
        headers.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage);
        headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        return base.SendAsync(request, cancellationToken);
    }
}
