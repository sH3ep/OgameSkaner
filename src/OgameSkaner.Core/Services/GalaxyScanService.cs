using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services;

/// <param name="PauseUntil">Set while the scan pauses (<see cref="ScanSettings.PauseEverySystems"/>) before the next system.</param>
public sealed record ScanProgress(int Completed, int Total, SolarSystemAddress Current, int PlanetsInSystem, DateTimeOffset? PauseUntil = null);

public enum ScanOutcome
{
    Completed,
    Cancelled,
    Failed
}

public sealed record ScanResult(ScanOutcome Outcome, int SystemsScanned, int Total, string? Error = null);

/// <summary>
/// Scans a range of solar systems and stores the results in the universe's planet database, paced by
/// <see cref="ScanSettings"/>. Each system is one <see cref="IActionQueue"/> group, so a long scan lets the other
/// modules act in between.
/// </summary>
public sealed class GalaxyScanService(IPlanetRepository repository, IActionQueues queues, TimeProvider timeProvider, ILogger<GalaxyScanService> logger)
{
    /// <summary>How many systems are scanned between two saves of the database.</summary>
    public int SaveInterval { get; init; } = 25;

    public async Task<ScanResult> ScanAsync(
        IOgamexClient client,
        ScanRange range,
        IProgress<ScanProgress>? progress = null,
        ScanSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        settings ??= ScanSettings.Default;
        var errors = range.Validate(client.Universe);
        if (errors.Count > 0)
            return new ScanResult(ScanOutcome.Failed, 0, range.Count, string.Join(Environment.NewLine, errors));

        var database = await repository.GetAsync(client.Universe.Name, cancellationToken).ConfigureAwait(false);
        var queue = queues.For(client);
        var completed = 0;
        var unsaved = 0;
        DateTimeOffset? lastRequest = null;
        ScanProgress? last = null;

        try
        {
            foreach (var address in range.Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (lastRequest is { } previous)
                {
                    var due = previous + settings.DelayAfter(completed, Random.Shared);
                    if (settings.PausesAfter(completed) && last is not null)
                        progress?.Report(last with { PauseUntil = due });
                    var wait = due - timeProvider.GetUtcNow();
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
                }

                var planets = await queue.RunAsync("Scanner", $"Scan system {address.Galaxy}:{address.System}", ct =>
                {
                    lastRequest = timeProvider.GetUtcNow();
                    return client.GetSolarSystemAsync(address, ct);
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                database.ReplaceSystem(address, planets, timeProvider.GetUtcNow());
                completed++;
                last = new ScanProgress(completed, range.Count, address, planets.Count);
                progress?.Report(last);

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
