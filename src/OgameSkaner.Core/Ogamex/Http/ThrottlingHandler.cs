namespace OgameSkaner.Core.Ogamex.Http;

/// <summary>
/// Sends requests one at a time and keeps a minimum, slightly randomized gap between them,
/// so the app never hammers the server.
/// </summary>
internal sealed class ThrottlingHandler(OgamexOptions options, TimeProvider timeProvider) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long? _lastRequestTimestamp;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastRequestTimestamp is { } last)
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)options.RequestJitter.TotalMilliseconds + 1));
                var wait = options.MinRequestInterval + jitter - timeProvider.GetElapsedTime(last);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestTimestamp = timeProvider.GetTimestamp();
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _gate.Dispose();
        base.Dispose(disposing);
    }
}
