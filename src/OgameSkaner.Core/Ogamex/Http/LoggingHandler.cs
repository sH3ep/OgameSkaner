using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OgameSkaner.Core.Ogamex.Http;

/// <summary>Logs every request and optionally dumps response bodies to disk.</summary>
internal sealed class LoggingHandler(OgamexOptions options, ILogger logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Method} {Uri} failed after {Elapsed} ms", request.Method, request.RequestUri, stopwatch.ElapsedMilliseconds);
            throw;
        }

        logger.LogDebug("{Method} {Uri} -> {Status} in {Elapsed} ms",
            request.Method, request.RequestUri, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

        if (!string.IsNullOrEmpty(options.DumpResponsesDirectory))
            await DumpAsync(request, response, options.DumpResponsesDirectory, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task DumpAsync(HttpRequestMessage request, HttpResponseMessage response, string directory, CancellationToken cancellationToken)
    {
        try
        {
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var name = request.RequestUri?.AbsolutePath.Trim('/').Replace('/', '_') ?? "response";
            var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{name}.txt");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, body, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not dump response of {Uri}", request.RequestUri);
        }
    }
}
