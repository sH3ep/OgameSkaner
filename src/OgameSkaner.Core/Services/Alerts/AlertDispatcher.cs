using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Services.Alerts;

/// <summary>Sends alerts through the channels the player enabled (<see cref="AlertSettings.EnabledChannels"/>).</summary>
public sealed class AlertDispatcher(IEnumerable<IAlertChannel> channels, IDefenseSettingsStore settings, ILogger<AlertDispatcher> logger)
{
    public IReadOnlyList<IAlertChannel> Channels { get; } = channels.ToList();

    /// <summary>
    /// Sends the alert through every enabled channel at once. Never throws: a failing channel is logged and returned,
    /// so one broken channel (say, a mail server down) doesn't keep the others from reaching the player.
    /// </summary>
    /// <returns>The display names of the channels that failed.</returns>
    public async Task<IReadOnlyList<string>> SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        AlertSettings options;
        try
        {
            options = settings.LoadAlerts();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Reading the alert settings failed; using the defaults");
            options = AlertSettings.Default;
        }

        var enabled = Channels.Where(c => options.IsEnabled(c.Id)).ToList();
        var results = await Task.WhenAll(enabled.Select(c => TrySendAsync(c, alert, cancellationToken))).ConfigureAwait(false);
        return enabled.Where((_, i) => !results[i]).Select(c => c.DisplayName).ToList();
    }

    private async Task<bool> TrySendAsync(IAlertChannel channel, Alert alert, CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendAsync(alert, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Alert channel {Channel} failed on '{Title}'", channel.Id, alert.Title);
            return false;
        }
    }
}
