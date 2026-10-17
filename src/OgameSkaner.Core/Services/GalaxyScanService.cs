using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services;

public sealed record ScanProgress(int Completed, int Total, SolarSystemAddress Current, int PlanetsInSystem);

public enum ScanOutcome
{
    Completed,
    Cancelled,
    Failed
}

public sealed record ScanResult(ScanOutcome Outcome, int SystemsScanned, int Total, string? Error = null);

/// <summary>Scans a range of solar systems and stores the results in the universe's planet database.</summary>
public sealed class GalaxyScanService(IPlanetRepository repository, ILogger<GalaxyScanService> logger)
{
    /// <summary>How many systems are scanned between two saves of the database.</summary>
    public int SaveInterval { get; init; } = 25;

    public async Task<ScanResult> ScanAsync(
        IOgamexClient client,
        ScanRange range,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = range.Validate(client.Universe);
        if (errors.Count > 0)
            return new ScanResult(ScanOutcome.Failed, 0, range.Count, string.Join(Environment.NewLine, errors));

        var database = await repository.GetAsync(client.Universe.Name, cancellationToken).ConfigureAwait(false);
        var completed = 0;
        var unsaved = 0;

        try
        {
            foreach (var address in range.Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var planets = await client.GetSolarSystemAsync(address, cancellationToken).ConfigureAwait(false);
                database.ReplaceSystem(address, planets, DateTimeOffset.UtcNow);
                completed++;
                progress?.Report(new ScanProgress(completed, range.Count, address, planets.Count));

                if (++unsaved >= SaveInterval)
                {
                    await repository.SaveAsync(database, CancellationToken.None).ConfigureAwait(false);
                    unsaved = 0;
                }
            }

            return new ScanResult(ScanOutcome.Completed, completed, range.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ScanResult(ScanOutcome.Cancelled, completed, range.Count);
        }
        catch (OgamexException ex)
        {
            logger.LogWarning(ex, "Scan of {Universe} stopped after {Completed} systems", client.Universe.Name, completed);
            return new ScanResult(ScanOutcome.Failed, completed, range.Count, ex.Message);
        }
        finally
        {
            if (unsaved > 0)
                await repository.SaveAsync(database, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
